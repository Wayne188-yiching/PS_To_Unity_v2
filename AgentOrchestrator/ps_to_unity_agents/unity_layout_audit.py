from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path
from typing import Any


BLOCK = re.compile(r"^--- !u!(\d+) &(\-?\d+)", re.MULTILINE)
PAGE_NAME = re.compile(
    r"(panel|page|window|dialog|popup|login|loading|lobby|room|setting|activity|shop|rank|menu|optional)",
    re.IGNORECASE,
)
EXCLUDED_PARTS = {
    "plugins", "samples", "sample", "examples", "example", "thirdparty", "tools",
    "temp", "vfx", "fx", "effect", "effects", "guneffect", "audio", "spine",
    "commoneffects", "fishes",
}
UGUI_SCRIPTS = (
    "Image", "ScrollRect", "Mask", "RectMask2D", "HorizontalLayoutGroup",
    "VerticalLayoutGroup", "GridLayoutGroup", "ContentSizeFitter",
    "AspectRatioFitter", "LayoutElement",
)


def _blocks(text: str) -> list[tuple[int, int, str]]:
    matches = list(BLOCK.finditer(text))
    return [
        (int(match.group(1)), int(match.group(2)), text[match.end(): matches[index + 1].start() if index + 1 < len(matches) else len(text)])
        for index, match in enumerate(matches)
    ]


def _vector(block: str, name: str) -> tuple[float, ...] | None:
    match = re.search(rf"{re.escape(name)}: \{{([^}}]+)\}}", block)
    if not match:
        return None
    values = re.findall(r"(?:x|y|z): ([\-\d.eE]+)", match.group(1))
    return tuple(map(float, values)) if values else None


def _reference(block: str, name: str) -> int | None:
    match = re.search(rf"{re.escape(name)}: \{{fileID: (\-?\d+)", block)
    return int(match.group(1)) if match else None


def ugui_script_guids(project_root: Path) -> dict[str, str]:
    package_cache = project_root / "Library" / "PackageCache"
    mapping: dict[str, str] = {}
    for script_name in UGUI_SCRIPTS:
        metas = list(package_cache.glob(f"com.unity.ugui@*/Runtime/UGUI/UI/Core/**/{script_name}.cs.meta"))
        if not metas:
            continue
        match = re.search(r"^guid: ([0-9a-f]+)$", metas[0].read_text(encoding="utf-8"), re.MULTILINE)
        if match:
            mapping[match.group(1)] = script_name
    return mapping


def audit_prefab(prefab: Path, assets_root: Path, guid_to_type: dict[str, str]) -> dict[str, Any]:
    text = prefab.read_text(encoding="utf-8", errors="ignore")
    component_counts: Counter[str] = Counter()
    image_types: Counter[str] = Counter()
    object_names: list[str] = []
    scales: list[tuple[float, ...]] = []
    anchors: list[tuple[tuple[float, ...], tuple[float, ...]]] = []
    scroll_missing_links = 0
    scroll_evidence = []
    canvas_count = 0
    parsed = _blocks(text)
    names_by_id = {}
    for class_id, file_id, block in parsed:
        if class_id == 1:
            name = re.search(r"\n  m_Name: (.*)", block)
            if name:
                names_by_id[file_id] = name.group(1).strip()
    scale_evidence = []

    for class_id, _, block in parsed:
        if class_id == 1:
            name = re.search(r"\n  m_Name: (.*)", block)
            if name:
                object_names.append(name.group(1).strip())
        elif class_id == 223:
            canvas_count += 1
        elif class_id == 224:
            scale = _vector(block, "m_LocalScale")
            anchor_min = _vector(block, "m_AnchorMin")
            anchor_max = _vector(block, "m_AnchorMax")
            if scale:
                scales.append(scale)
                if any(abs(value - 1) > 0.001 for value in scale):
                    game_object = _reference(block, "m_GameObject")
                    scale_evidence.append({"name": names_by_id.get(game_object, str(game_object)), "scale": scale})
            if anchor_min and anchor_max:
                anchors.append((anchor_min, anchor_max))
        elif class_id == 114:
            script = re.search(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]+)", block)
            component = guid_to_type.get(script.group(1)) if script else None
            if not component:
                continue
            component_counts[component] += 1
            if component == "Image":
                image_type = re.search(r"\n  m_Type: (\d+)", block)
                image_types[{"0": "Simple", "1": "Sliced", "2": "Tiled", "3": "Filled"}.get(
                    image_type.group(1) if image_type else "", "Unknown"
                )] += 1
            elif component == "ScrollRect":
                if _reference(block, "m_Content") in (None, 0) or _reference(block, "m_Viewport") in (None, 0):
                    scroll_missing_links += 1
                    game_object = _reference(block, "m_GameObject")
                    scroll_evidence.append(names_by_id.get(game_object, str(game_object)))

    non_unit = sum(any(abs(value - 1) > 0.001 for value in scale) for scale in scales)
    zero_or_negative = sum(any(value <= 0 for value in scale) for scale in scales)
    stretch_both = sum(anchor_min[:2] == (0.0, 0.0) and anchor_max[:2] == (1.0, 1.0) for anchor_min, anchor_max in anchors)
    generic = sum(bool(re.fullmatch(r"(gameobject|image|text(?: \(tmp\))?|button|bg|panel)(?: \(\d+\))?", name, re.I)) for name in object_names)
    masks = component_counts["Mask"] + component_counts["RectMask2D"]
    flags = []
    if zero_or_negative:
        flags.append("ZERO_OR_NEGATIVE_SCALE")
    if scales and non_unit / len(scales) > 0.08:
        flags.append("MANY_NON_UNIT_SCALES")
    if component_counts["ScrollRect"] and not masks:
        flags.append("SCROLL_WITHOUT_MASK")
    if scroll_missing_links:
        flags.append("SCROLL_LINKS_INCOMPLETE")
    if len(object_names) > 20 and generic / len(object_names) > 0.45:
        flags.append("GENERIC_NAMING")

    semantic_components = sum(component_counts[name] for name in UGUI_SCRIPTS if name != "Image")
    status = "REFERENCE_CANDIDATE" if not flags and (semantic_components or image_types["Sliced"]) else "MANUAL_REVIEW"
    return {
        "path": prefab.relative_to(assets_root).as_posix(),
        "status": status,
        "flags": flags,
        "objects": len(object_names),
        "canvasCount": canvas_count,
        "rectTransforms": len(scales),
        "nonUnitScaleCount": non_unit,
        "zeroOrNegativeScaleCount": zero_or_negative,
        "scaleEvidence": scale_evidence[:12],
        "fullStretchAnchorCount": stretch_both,
        "genericNameRatio": round(generic / max(1, len(object_names)), 3),
        "components": dict(component_counts),
        "imageTypes": dict(image_types),
        "incompleteScrollEvidence": scroll_evidence[:12],
    }


def audit_unity_layout_project(project_root: Path) -> dict[str, Any]:
    assets_root = project_root / "Assets"
    guid_to_type = ugui_script_guids(project_root)
    audited = []
    excluded = 0
    for prefab in assets_root.rglob("*.prefab"):
        relative = prefab.relative_to(assets_root)
        lowered_parts = {part.casefold() for part in relative.parts}
        if lowered_parts & EXCLUDED_PARTS or relative.name.casefold() == "demo.prefab":
            excluded += 1
            continue
        if not PAGE_NAME.search(prefab.stem) and "ui" not in lowered_parts and "prefab_common" not in lowered_parts:
            continue
        result = audit_prefab(prefab, assets_root, guid_to_type)
        if result["objects"] >= 8:
            audited.append(result)
    return {
        "schemaVersion": "unity-layout-audit@1",
        "project": str(project_root),
        "policy": "REFERENCE_CANDIDATE is evidence for human curation, never automatic training approval.",
        "candidateCount": len(audited),
        "excludedPrefabCount": excluded,
        "referenceCandidateCount": sum(item["status"] == "REFERENCE_CANDIDATE" for item in audited),
        "manualReviewCount": sum(item["status"] == "MANUAL_REVIEW" for item in audited),
        "items": audited,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Read-only Unity UI layout audit")
    parser.add_argument("project", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = audit_unity_layout_project(args.project.resolve())
    payload = json.dumps(report, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(payload, encoding="utf-8")
    print(payload)


if __name__ == "__main__":
    main()
