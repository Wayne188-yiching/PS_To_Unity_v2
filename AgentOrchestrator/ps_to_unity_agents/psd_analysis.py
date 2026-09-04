from __future__ import annotations

import json
import re
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

from .models import Status


GUIDE_ROOT = re.compile(r"^\d+\s*[*xX×]\s*\d+.*(?:操作區|safe|guide)", re.IGNORECASE)
KNOWN_TAG = re.compile(
    r"\[(SLICED|9SLICE|9S|H|HLAYOUT|V|VLAYOUT|GRID|GLAYOUT|CG|CANVASGROUP|"
    r"SCROLL_V|SCROLL_H|SCROLLBAR_V|SCROLLBAR_H|TRACK|HANDLE|MASK|MERGE|"
    r"SOFTMASK_BOTTOM|SOFTMASK_Y|FADE_BOTTOM|THICK)(?:\s*[:=][^\]]+)?\]",
    re.IGNORECASE,
)
SAFE_FALLBACKS = {"LAYOUT_CROSS_AXIS_DEGRADED", "GRID_DEGRADED", "GRID_OUTLIER"}
NON_ASCII = re.compile(r"[^\x00-\x7f]")
STATE_SUFFIX = re.compile(r"^(.*?)[_-]?(\d{1,3})$")
EXPLICIT_SLICED_TAG = re.compile(
    r"\[(?:SLICED|9SLICE|9S)\s*(?::|=)\s*"
    r"(?:\d+(?:\.\d+)?|\d+(?:\.\d+)?\s*,\s*\d+(?:\.\d+)?\s*,\s*\d+(?:\.\d+)?\s*,\s*\d+(?:\.\d+)?)\s*\]",
    re.IGNORECASE,
)


def _walk(nodes: Iterable[dict[str, Any]], depth: int = 0):
    for node in nodes or []:
        yield node, depth
        yield from _walk(node.get("children") or [], depth + 1)


def _walk_effective(
    nodes: Iterable[dict[str, Any]],
    parent_visible: bool = True,
    parent_path: str = "",
    inside_merge: bool = False,
):
    for node in nodes or []:
        name = str(node.get("name") or "")
        path = f"{parent_path}/{name}" if parent_path else name
        visible = parent_visible and bool(node.get("visible"))
        yield node, visible, path, inside_merge
        child_inside_merge = inside_merge or _has_tag(name, "MERGE")
        yield from _walk_effective(node.get("children") or [], visible, path, child_inside_merge)


def _state_family_candidates(nodes: Iterable[dict[str, Any]], parent_path: str = "") -> list[dict[str, Any]]:
    candidates = []
    families: dict[str, list[str]] = {}
    for node in nodes or []:
        name = str(node.get("name") or "")
        match = STATE_SUFFIX.match(name)
        if match and match.group(1).casefold().startswith(("icon_", "state_")):
            families.setdefault(match.group(1), []).append(name)
    for stem, members in families.items():
        if len(members) >= 2:
            candidates.append({"parentPath": parent_path or None, "stem": stem, "members": members, "status": "NEEDS_REVIEW"})
    for node in nodes or []:
        name = str(node.get("name") or "")
        path = f"{parent_path}/{name}" if parent_path else name
        candidates.extend(_state_family_candidates(node.get("children") or [], path))
    return candidates


def _visible_layer_index(nodes: Iterable[dict[str, Any]], parent_visible: bool = True, parent_path: str = "", parent_id: int | None = None):
    for node in nodes or []:
        name = str(node.get("name") or "")
        path = f"{parent_path}/{name}" if parent_path else name
        visible = parent_visible and bool(node.get("visible"))
        if visible:
            yield {
                "id": node.get("id"),
                "parentId": parent_id,
                "path": path,
                "name": name,
                "nodeType": node.get("nodeType"),
                "childCount": len(node.get("children") or []),
                "bounds": node.get("bounds"),
            }
        yield from _visible_layer_index(node.get("children") or [], visible, path, node.get("id"))


def _slug(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.casefold())


def _has_tag(name: str, tag: str) -> bool:
    return any(match.group(1).casefold() == tag.casefold() for match in KNOWN_TAG.finditer(name))


def _base_name(name: str) -> str:
    return KNOWN_TAG.sub("", name).strip()


def _matching_visible_paths(effective_hierarchy: list[tuple[dict[str, Any], bool, str, bool]], name: str) -> list[str]:
    target = _slug(name)
    return [
        path
        for node, visible, path, inside_merge in effective_hierarchy
        if visible and not inside_merge and _slug(_base_name(str(node.get("name") or ""))) == target
    ]


def _layout_index(nodes: Iterable[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    result: dict[str, list[dict[str, Any]]] = {}
    for node, _ in _walk(nodes):
        result.setdefault(_slug(str(node.get("name") or "")), []).append(node)
    return result


def _case_semantic_evidence(
    case_knowledge: dict[str, Any],
    effective_hierarchy: list[tuple[dict[str, Any], bool, str, bool]],
    layout: dict[str, Any],
) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    if not case_knowledge:
        return {}, []

    issues: list[dict[str, Any]] = []
    confirmed_states = []
    for state in case_knowledge.get("stateSemantics") or []:
        layer_name = str(state.get("layerName") or "")
        paths = _matching_visible_paths(effective_hierarchy, layer_name)
        confirmed_states.append({
            "layerName": layer_name,
            "paths": paths,
            "meaning": state.get("meaning"),
            "exclusiveWith": state.get("exclusiveWith") or [],
        })

    runtime = case_knowledge.get("runtimeStructure") or {}
    scroll = runtime.get("horizontalScroll") or {}
    scroll_name = str(scroll.get("groupName") or "")
    expected_tag = str(scroll.get("tag") or "[SCROLL_H]")
    scroll_paths = [
        path
        for node, visible, path, inside_merge in effective_hierarchy
        if visible
        and not inside_merge
        and _slug(_base_name(str(node.get("name") or ""))) == _slug(scroll_name)
        and _has_tag(str(node.get("name") or ""), expected_tag.strip("[]"))
    ]
    moving_regions = {
        region: _matching_visible_paths(effective_hierarchy, str(region))
        for region in scroll.get("movingRegions") or []
    }
    scroll_result = {
        "groupName": scroll_name,
        "tag": expected_tag,
        "paths": scroll_paths,
        "movingRegions": moving_regions,
        "unityGeneratedChildren": scroll.get("unityGeneratedChildren") or [],
    }
    if scroll_name and not scroll_paths:
        issues.append({
            "code": "SCROLL_STRUCTURE_MISSING",
            "owner": "PSD",
            "severity": "warning",
            "message": f"Expected {expected_tag}{scroll_name} containing {', '.join(moving_regions)}; do not infer or create it automatically.",
        })
    elif scroll_paths:
        missing_regions = [name for name, paths in moving_regions.items() if not any(path.startswith(scroll_paths[0] + "/") for path in paths)]
        if missing_regions:
            issues.append({
                "code": "SCROLL_CONTENT_INCOMPLETE",
                "owner": "PSD",
                "severity": "warning",
                "message": f"Scroll group is missing confirmed moving regions: {', '.join(missing_regions)}.",
                "node_path": scroll_paths[0],
            })

    layout_by_name = _layout_index(layout.get("nodes") or [])
    sliced_results = []
    for intent in case_knowledge.get("slicedIntents") or []:
        layer_name = str(intent.get("layerName") or "")
        paths = _matching_visible_paths(effective_hierarchy, layer_name)
        matching_nodes = layout_by_name.get(_slug(layer_name), [])
        tagged = any(
            EXPLICIT_SLICED_TAG.search(str(node.get("name") or ""))
            for node, visible, _, inside_merge in effective_hierarchy
            if visible and not inside_merge and _slug(_base_name(str(node.get("name") or ""))) == _slug(layer_name)
        )
        exported_border = any(
            any(float(node.get(key) or 0) > 0 for key in ("spriteBorderLeft", "spriteBorderTop", "spriteBorderRight", "spriteBorderBottom"))
            for node in matching_nodes
        )
        metadata_present = tagged and exported_border
        sliced_results.append({
            "layerName": layer_name,
            "paths": paths,
            "renderMode": intent.get("renderMode") or "sliced",
            "metadataPresent": metadata_present,
            "borderStatus": "APPROVED" if metadata_present else "NEEDS_REVIEW",
        })
        if paths and not metadata_present:
            issues.append({
                "code": "SLICED_METADATA_MISSING",
                "owner": "PSD",
                "severity": "warning",
                "message": f"{layer_name} has confirmed Sliced intent but no explicit valid border. Border values must not be guessed.",
                "node_path": paths[0],
            })

    return {
        "caseId": case_knowledge.get("caseId"),
        "confirmedStateNodes": confirmed_states,
        "confirmedScrollIntent": scroll_result,
        "confirmedSlicedIntents": sliced_results,
    }, issues


def _main_chain(root: dict[str, Any]) -> tuple[list[str], dict[str, Any]]:
    names = [str(root.get("name") or "")]
    current = root
    while True:
        groups = [child for child in current.get("children") or [] if child.get("nodeType") == "group" and child.get("visible")]
        if len(groups) != 1:
            return names, current
        current = groups[0]
        names.append(str(current.get("name") or ""))


def analyze_psd_structure(
    inspection_path: Path,
    layout_path: Path | None,
    case_knowledge: dict[str, Any] | None = None,
    *,
    require_layout: bool = True,
) -> dict[str, Any]:
    missing = []
    if not inspection_path.is_file():
        missing.append(str(inspection_path))
    if require_layout and (layout_path is None or not layout_path.is_file()):
        missing.append(str(layout_path))
    if missing:
        return {
            "status": Status.BLOCKED.value,
            "summary": "PSD inspection or exporter layout is missing.",
            "issues": [{"code": "PSD_EVIDENCE_MISSING", "owner": "PSD", "severity": "error", "message": path} for path in missing],
            "evidence": [],
            "nextAction": "Run the Photoshop inspector and exporter deterministic tools.",
        }

    inspection = json.loads(inspection_path.read_text(encoding="utf-8-sig"))
    layout_available = layout_path is not None and layout_path.is_file()
    layout = json.loads(layout_path.read_text(encoding="utf-8-sig")) if layout_available else {}
    top = inspection.get("layers") or []
    guide_roots = [node for node in top if GUIDE_ROOT.search(str(node.get("name") or ""))]
    primary_roots = [node for node in top if node.get("visible") and node not in guide_roots]
    issues = []
    review_required = False
    if len(primary_roots) != 1:
        issues.append({
            "code": "PSD_PRIMARY_ROOT_AMBIGUOUS",
            "owner": "PSD",
            "severity": "error",
            "message": f"Expected one visible production root, found {len(primary_roots)}.",
        })

    document = inspection.get("document") or {}
    canvas = layout.get("canvas") or document
    if layout_available and (round(float(document.get("width") or 0)) != round(float(canvas.get("width") or 0)) or round(float(document.get("height") or 0)) != round(float(canvas.get("height") or 0))):
        issues.append({
            "code": "PSD_LAYOUT_CANVAS_MISMATCH",
            "owner": "PSD",
            "severity": "error",
            "message": f"PSD is {document.get('width')}x{document.get('height')}; layout is {canvas.get('width')}x{canvas.get('height')}.",
        })

    hierarchy = list(_walk(top))
    effective_hierarchy = list(_walk_effective(top))
    names = [str(node.get("name") or "") for node, _ in hierarchy]
    tags = Counter(match.group(1).upper() for name in names for match in KNOWN_TAG.finditer(name))
    buttons = [name for name in names if name.casefold().startswith("btn_")]
    functional_roles = {
        prefix: [name for name in names if name.casefold().startswith(prefix.casefold())]
        for prefix in ("Btn_", "TMP_", "Frame_", "Icon_", "Bar_", "State_", "REF_")
    }
    visible_non_ascii = [
        path for node, visible, path, inside_merge in effective_hierarchy
        if visible and not inside_merge and NON_ASCII.search(str(node.get("name") or ""))
    ]
    merge_localized_exemptions = [
        path for node, visible, path, inside_merge in effective_hierarchy
        if visible and inside_merge and NON_ASCII.search(str(node.get("name") or ""))
    ]
    hidden_non_ascii = [
        path for node, visible, path, _ in effective_hierarchy
        if not visible and NON_ASCII.search(str(node.get("name") or ""))
    ]
    if visible_non_ascii:
        review_required = True
        issues.append({
            "code": "VISIBLE_LAYER_RENAME_REQUIRED",
            "owner": "PSD",
            "severity": "warning",
            "message": f"{len(visible_non_ascii)} visible runtime layer names require an approved ASCII rename plan.",
        })
    layout_nodes = list(_walk(layout.get("nodes") or []))
    exporter_warnings = layout.get("warnings") or []
    for warning in exporter_warnings:
        code = str(warning.get("code") or "PSD_EXPORT_WARNING")
        severity = "info" if code in SAFE_FALLBACKS else "warning"
        if severity == "warning":
            review_required = True
        issues.append({
            "code": code,
            "owner": "PSD",
            "severity": severity,
            "message": str(warning.get("message") or ""),
            "node_path": str(warning.get("node") or "") or None,
        })

    case_evidence, case_issues = _case_semantic_evidence(case_knowledge or {}, effective_hierarchy, layout)
    if case_issues:
        review_required = True
        issues.extend(case_issues)

    chain = []
    region_root = None
    if len(primary_roots) == 1:
        chain, region_root = _main_chain(primary_roots[0])
        layout_roots = [str(node.get("name") or "") for node in layout.get("nodes") or []]
        if layout_available and len(layout_roots) == 1 and _slug(chain[0]) != _slug(layout_roots[0]):
            issues.append({
                "code": "PSD_LAYOUT_ROOT_NAME_MISMATCH",
                "owner": "PSD",
                "severity": "warning",
                "message": f"PSD root '{chain[0]}' exported as '{layout_roots[0]}'.",
            })
            review_required = True

    root_template_conforms = (
        len(chain) >= 3
        and chain[1].casefold() == "uiwindow"
        and chain[2].casefold() == "animation"
    )

    status = Status.BLOCKED if any(issue["severity"] == "error" for issue in issues) else Status.NEEDS_REVIEW if review_required else Status.PASS
    type_counts = Counter(str(node.get("type") or "unknown") for node, _ in layout_nodes)
    return {
        "status": status.value,
        "summary": (
            f"Inspected {len(hierarchy)} PSD layers and {len(layout_nodes)} exported layout nodes."
            if layout_available
            else f"Inspected {len(hierarchy)} PSD layers before package export."
        ),
        "document": document,
        "primaryRootChain": chain,
        "regions": [
            {"name": child.get("name"), "visible": bool(child.get("visible")), "childCount": len(child.get("children") or [])}
            for child in (region_root or {}).get("children") or []
        ],
        "guideRoots": [node.get("name") for node in guide_roots],
        "semanticTags": dict(tags),
        "buttonLayers": buttons,
        "functionalRoleCounts": {prefix: len(items) for prefix, items in functional_roles.items()},
        "visibleRenameCandidates": visible_non_ascii,
        "mergeLocalizedNameExemptions": merge_localized_exemptions,
        "mergeLocalizedNameExemptionCount": len(merge_localized_exemptions),
        "hiddenRenameSkippedCount": len(hidden_non_ascii),
        "rootTemplateConforms": root_template_conforms,
        "stateFamilyCandidates": _state_family_candidates(top),
        "visibleLayerIndex": list(_visible_layer_index(top)),
        "hiddenLayerCount": sum(not bool(node.get("visible")) for node, _ in hierarchy),
        "maxDepth": max((depth for _, depth in hierarchy), default=0),
        "layoutNodeTypes": dict(type_counts),
        "layoutEvidenceAvailable": layout_available,
        "deterministicFallbackCount": sum(str(warning.get("code") or "") in SAFE_FALLBACKS for warning in exporter_warnings),
        "caseKnowledge": case_evidence,
        "issues": issues,
        "evidence": [
            f"PSD canvas {document.get('width')}x{document.get('height')}",
            f"Production root {'/'.join(chain) if chain else 'unresolved'}",
            f"Hidden guide roots: {', '.join(str(name) for name in [node.get('name') for node in guide_roots]) or 'none'}",
            f"Semantic tags: {dict(tags)}",
            f"Localized names exempt inside [MERGE]: {len(merge_localized_exemptions)}",
        ],
        "nextAction": "Resolve error and warning evidence." if status != Status.PASS else "PSD structure is ready for package preparation.",
    }


def analyze_psd_hierarchy(inspection_path: Path, case_knowledge: dict[str, Any] | None = None) -> dict[str, Any]:
    """Build planning evidence without requiring the PSD to be export-ready yet."""
    return analyze_psd_structure(inspection_path, None, case_knowledge, require_layout=False)
