from __future__ import annotations

import copy
import json
import math
import re
import shutil
from pathlib import Path
from typing import Any, Iterable

from PIL import Image

from .models import PipelineRequest, Status


SUPPORTED_IMAGES = {".png", ".jpg", ".jpeg"}


def _image_index(folder: Path) -> dict[str, Path]:
    if not folder.is_dir():
        return {}
    return {
        path.stem.casefold(): path
        for path in folder.rglob("*")
        if path.is_file() and path.suffix.casefold() in SUPPORTED_IMAGES
    }


def _walk_nodes(nodes: Iterable[dict[str, Any]], parent: str = ""):
    for node in nodes or []:
        name = str(node.get("name") or "unnamed")
        path = f"{parent}/{name}" if parent else name
        yield node, path
        yield from _walk_nodes(node.get("children") or [], path)


def _size(path: Path) -> tuple[int, int]:
    with Image.open(path) as image:
        return image.size


def _premultiplied_thumbnail(path: Path, size: tuple[int, int]) -> list[tuple[float, float, float, float]]:
    with Image.open(path) as image:
        rgba = image.convert("RGBA").resize(size, Image.Resampling.BILINEAR)
        result = []
        for red, green, blue, alpha in rgba.get_flattened_data():
            a = alpha / 255.0
            result.append((red / 255.0 * a, green / 255.0 * a, blue / 255.0 * a, a))
        return result


def _rmse(first: list[tuple[float, ...]], second: list[tuple[float, ...]]) -> float:
    if len(first) != len(second) or not first:
        return 1.0
    total = 0.0
    count = 0
    for left, right in zip(first, second):
        for a, b in zip(left, right):
            total += (a - b) ** 2
            count += 1
    return math.sqrt(total / max(1, count))


def _simple_stretch_rmse(asset: Path, reference: Path) -> float:
    with Image.open(reference) as target:
        width, height = target.size
    scale = min(1.0, 256.0 / max(width, height))
    sample = (max(1, round(width * scale)), max(1, round(height * scale)))
    return _rmse(_premultiplied_thumbnail(asset, sample), _premultiplied_thumbnail(reference, sample))


def _horizontal_slice_candidate(asset: Path, reference: Path) -> dict[str, Any] | None:
    with Image.open(asset) as source_image, Image.open(reference) as target_image:
        if source_image.height != target_image.height or source_image.width >= target_image.width:
            return None
        scale = min(1.0, 256.0 / max(target_image.size))
        source_size = (max(3, round(source_image.width * scale)), max(3, round(source_image.height * scale)))
        target_size = (max(3, round(target_image.width * scale)), max(3, round(target_image.height * scale)))
        source = source_image.convert("RGBA").resize(source_size, Image.Resampling.BILINEAR)
        target_pixels = _premultiplied_thumbnail(reference, target_size)
        simple_pixels = _premultiplied_thumbnail(asset, target_size)
        simple_error = _rmse(simple_pixels, target_pixels)
        best_error = simple_error
        best_border = 0
        maximum = max(0, source.width // 2 - 1)
        for border in range(1, maximum + 1):
            center_width = target_size[0] - border * 2
            if center_width <= 0:
                break
            center = source.crop((border, 0, source.width - border, source.height)).resize(
                (center_width, target_size[1]), Image.Resampling.BILINEAR
            )
            rendered = Image.new("RGBA", target_size)
            rendered.paste(source.crop((0, 0, border, source.height)), (0, 0))
            rendered.paste(center, (border, 0))
            rendered.paste(
                source.crop((source.width - border, 0, source.width, source.height)),
                (target_size[0] - border, 0),
            )
            temporary = []
            for red, green, blue, alpha in rendered.get_flattened_data():
                a = alpha / 255.0
                temporary.append((red / 255.0 * a, green / 255.0 * a, blue / 255.0 * a, a))
            error = _rmse(temporary, target_pixels)
            if error < best_error:
                best_error = error
                best_border = border
        if best_border == 0 or simple_error <= 0:
            return None
        improvement = (simple_error - best_error) / simple_error
        return {
            "border": round(best_border / scale),
            "simpleRmse": round(simple_error, 5),
            "slicedRmse": round(best_error, 5),
            "improvement": round(improvement, 4),
        }


def prepare_case(request: PipelineRequest) -> dict[str, Any]:
    layout = json.loads(request.layout_json_path.read_text(encoding="utf-8-sig"))
    prepared_layout = copy.deepcopy(layout)
    artist_index = _image_index(request.artist_asset_folder)
    exporter_index = _image_index(request.exporter_asset_folder)
    package_folder = request.output_folder / "package"
    asset_folder = package_folder / "assets"
    asset_folder.mkdir(parents=True, exist_ok=True)

    entries: list[dict[str, Any]] = []
    issues: list[dict[str, Any]] = []
    for node, node_path in _walk_nodes(prepared_layout.get("nodes") or []):
        if str(node.get("type", "")).casefold() != "image":
            continue
        original_image_path = str(node.get("imagePath") or "")
        stem = Path(original_image_path).stem.casefold()
        node_stem = Path(str(node.get("name") or "")).stem.casefold()
        collision_base = re.sub(r"_\d{3}$", "", node_stem)
        artist_asset = artist_index.get(stem) or artist_index.get(node_stem) or artist_index.get(collision_base)
        exporter_asset = exporter_index.get(stem)
        selected = artist_asset or exporter_asset
        if selected is None:
            issues.append({
                "code": "ASSET_UNRESOLVED",
                "owner": "PSD",
                "severity": "error",
                "nodePath": node_path,
                "message": f"No artist or exporter asset matches {original_image_path}",
            })
            continue

        destination_stem = selected.stem.casefold() if artist_asset else stem
        destination_name = f"{destination_stem}{selected.suffix.casefold()}"
        destination = asset_folder / destination_name
        shutil.copy2(selected, destination)
        node["imagePath"] = destination_name
        asset_width, asset_height = _size(selected)
        target_width = round(float(node.get("width") or 0))
        target_height = round(float(node.get("height") or 0))
        explicit_sliced = str(node.get("imageType") or "").casefold() == "sliced"
        mode = "sliced" if explicit_sliced else "simple"
        confidence = "explicit" if explicit_sliced else "deterministic"
        proposal = None
        status = "PASS"

        if not explicit_sliced and (asset_width != target_width or asset_height != target_height):
            status = "NEEDS_REVIEW"
            confidence = "proposal"
            simple_error = _simple_stretch_rmse(selected, exporter_asset) if exporter_asset else None
            slice_candidate = _horizontal_slice_candidate(selected, exporter_asset) if exporter_asset else None
            if slice_candidate and slice_candidate["improvement"] >= 0.75 and slice_candidate["slicedRmse"] <= 0.04:
                proposal = {
                    "mode": "sliced",
                    "border": {
                        "left": slice_candidate["border"],
                        "top": 0,
                        "right": slice_candidate["border"],
                        "bottom": 0,
                    },
                    "evidence": slice_candidate,
                }
            elif simple_error is not None and simple_error <= 0.12:
                proposal = {"mode": "simple_stretch", "evidence": {"rmse": round(simple_error, 5)}}
            else:
                proposal = {"mode": "unresolved", "evidence": {"simpleRmse": simple_error}}
            issues.append({
                "code": "RENDER_INTENT_REVIEW_REQUIRED",
                "owner": "HUMAN",
                "severity": "warning",
                "nodePath": node_path,
                "message": (
                    f"Asset {asset_width}x{asset_height} targets PS/Unity rect "
                    f"{target_width}x{target_height}; size mismatch is allowed, but render intent needs approval."
                ),
            })

        entries.append({
            "nodePath": node_path,
            "assetPath": destination_name,
            "assetSource": "artist" if artist_asset else "exporter_fallback",
            "assetPixelSize": {"width": asset_width, "height": asset_height},
            "psLayoutTargetSize": {"width": target_width, "height": target_height},
            "expectedUnityRectSize": {"width": target_width, "height": target_height},
            "renderMode": mode,
            "spriteBorder": {
                "left": float(node.get("spriteBorderLeft") or 0),
                "top": float(node.get("spriteBorderTop") or 0),
                "right": float(node.get("spriteBorderRight") or 0),
                "bottom": float(node.get("spriteBorderBottom") or 0),
            },
            "confidence": confidence,
            "status": status,
            "proposal": proposal,
        })

    layout_output = package_folder / "layout.json"
    layout_output.write_text(json.dumps(prepared_layout, ensure_ascii=False, indent=2), encoding="utf-8")
    manifest = {
        "schemaVersion": "1.0",
        "caseId": request.case_id,
        "privacy": "Images remain local; only structured evidence may be sent to the model.",
        "layoutPath": str(layout_output),
        "assetFolder": str(asset_folder),
        "entries": entries,
        "issues": issues,
    }
    manifest_path = request.output_folder / "semantic_manifest.json"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    return summarize_manifest(manifest, manifest_path)


def summarize_manifest(manifest: dict[str, Any], manifest_path: Path) -> dict[str, Any]:
    entries = manifest.get("entries") or []
    issues = manifest.get("issues") or []
    blocked = any(issue.get("severity") == "error" for issue in issues)
    review = any(entry.get("status") == "NEEDS_REVIEW" for entry in entries)
    status = Status.BLOCKED if blocked else Status.NEEDS_REVIEW if review else Status.PASS
    proposals = [
        {
            "nodePath": entry["nodePath"],
            "assetPixelSize": entry["assetPixelSize"],
            "targetSize": entry["expectedUnityRectSize"],
            "proposal": entry.get("proposal"),
        }
        for entry in entries
        if entry.get("proposal")
    ]
    return {
        "status": status.value,
        "manifestPath": str(manifest_path),
        "layoutPath": manifest["layoutPath"],
        "assetFolder": manifest["assetFolder"],
        "imageCount": len(entries),
        "artistAssetCount": sum(entry.get("assetSource") == "artist" for entry in entries),
        "fallbackAssetCount": sum(entry.get("assetSource") == "exporter_fallback" for entry in entries),
        "issueCount": len(issues),
        "issues": issues,
        "proposals": proposals,
    }


def qa_manifest(request: PipelineRequest) -> dict[str, Any]:
    manifest_path = request.output_folder / "semantic_manifest.json"
    if not manifest_path.is_file():
        return {"status": Status.BLOCKED.value, "issues": [{"code": "MANIFEST_MISSING", "owner": "PSD"}]}
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    issues = list(manifest.get("issues") or [])
    for entry in manifest.get("entries") or []:
        if entry.get("renderMode") != "sliced":
            continue
        size = entry["assetPixelSize"]
        border = entry["spriteBorder"]
        if border["left"] + border["right"] > size["width"] or border["top"] + border["bottom"] > size["height"]:
            issues.append({
                "code": "SPRITE_BORDER_EXCEEDS_ASSET",
                "owner": "PSD",
                "severity": "error",
                "nodePath": entry["nodePath"],
                "message": "9-slice border exceeds the original asset pixels.",
            })
    blocked = any(issue.get("severity") == "error" for issue in issues)
    review = any(entry.get("status") == "NEEDS_REVIEW" for entry in manifest.get("entries") or [])
    status = Status.BLOCKED if blocked else Status.NEEDS_REVIEW if review else Status.PASS
    return {"status": status.value, "issues": issues, "manifestPath": str(manifest_path)}
