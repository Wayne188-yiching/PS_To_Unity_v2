from __future__ import annotations

import json
from collections import defaultdict
from pathlib import Path
from typing import Any, Iterable


IMAGE_SUFFIXES = {".png", ".jpg", ".jpeg", ".tga", ".psd"}


def build_shared_asset_index(roots: Iterable[Path]) -> dict[str, list[dict[str, str]]]:
    index: dict[str, list[dict[str, str]]] = defaultdict(list)
    for root_index, root in enumerate(roots):
        if not root.is_dir():
            continue
        for path in root.rglob("*"):
            if not path.is_file() or path.suffix.casefold() not in IMAGE_SUFFIXES:
                continue
            index[path.stem.casefold()].append({
                "assetName": path.stem,
                "assetType": "image_file",
                "relativePath": path.relative_to(root).as_posix(),
                "rootIndex": str(root_index),
            })
        for sheet in root.rglob("*.tpsheet"):
            texture_name = ""
            for raw_line in sheet.read_text(encoding="utf-8-sig", errors="replace").splitlines():
                line = raw_line.strip()
                if line.startswith(":texture="):
                    texture_name = line.split("=", 1)[1].strip()
                    continue
                if not line or line.startswith(("#", ":")):
                    continue
                fields = [field.strip() for field in line.split(";")]
                if len(fields) < 5 or not fields[0]:
                    continue
                try:
                    x, y, width, height = (int(fields[index]) for index in range(1, 5))
                except ValueError:
                    continue
                index[fields[0].casefold()].append({
                    "assetName": fields[0],
                    "assetType": "texture_packer_sprite",
                    "relativePath": f"{sheet.parent.relative_to(root).as_posix()}/{texture_name}#{fields[0]}",
                    "descriptorPath": sheet.relative_to(root).as_posix(),
                    "sourceRect": f"{x},{y},{width},{height}",
                    "rootIndex": str(root_index),
                })
    return dict(index)


def _walk_visible(nodes: list[dict[str, Any]], parent_visible: bool = True, inside_merge: bool = False):
    for node in nodes or []:
        visible = parent_visible and bool(node.get("visible"))
        merged = inside_merge or str(node.get("name") or "").startswith("[MERGE]")
        yield node, visible, merged
        yield from _walk_visible(node.get("children") or [], visible, merged)


def _source_names(node: dict[str, Any]) -> list[tuple[str, str]]:
    values = [("layerName", str(node.get("name") or ""))]
    smart_object = node.get("smartObject") or {}
    for key in ("fileReference", "link"):
        value = str(smart_object.get(key) or "")
        if value:
            values.insert(0, (f"smartObject.{key}", Path(value).stem))
    return values


def match_shared_assets(inspection_path: Path, roots: Iterable[Path]) -> dict[str, Any]:
    root_list = list(roots)
    inspection = json.loads(inspection_path.read_text(encoding="utf-8-sig"))
    index = build_shared_asset_index(root_list)
    matches: list[dict[str, Any]] = []
    unmatched_count = 0

    for node, visible, merged in _walk_visible(inspection.get("layers") or []):
        if not visible or merged or node.get("nodeType") == "group":
            continue
        if str(node.get("layerKind") or "").casefold() in {"layerkind.text", "text"}:
            continue
        matched = False
        for source, source_name in _source_names(node):
            candidates = index.get(source_name.casefold()) or []
            if not candidates:
                continue
            matched = True
            status = "CONFIRMED_EXACT" if len(candidates) == 1 else "NEEDS_REVIEW_DUPLICATE_BASENAME"
            matches.append({
                "layerId": node.get("id"),
                "layerName": node.get("name"),
                "source": source,
                "targetName": candidates[0]["assetName"] if len(candidates) == 1 else None,
                "status": status,
                "candidates": candidates,
            })
            break
        if not matched:
            unmatched_count += 1

    return {
        "status": "NEEDS_REVIEW" if any(item["status"].startswith("NEEDS_REVIEW") for item in matches) else "PASS",
        "rootCount": len(root_list),
        "indexedBasenameCount": len(index),
        "indexedAssetCount": sum(len(items) for items in index.values()),
        "confirmedMatchCount": sum(item["status"] == "CONFIRMED_EXACT" for item in matches),
        "ambiguousMatchCount": sum(item["status"].startswith("NEEDS_REVIEW") for item in matches),
        "unmatchedVisibleImageCount": unmatched_count,
        "matches": matches,
    }
