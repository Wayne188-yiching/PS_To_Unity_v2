from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

from .models import PsdStructurePlan


REF_NAME = re.compile(r"^[a-z][a-z0-9_]*$")
ASCII_NAME = re.compile(r"^[\x20-\x7e]+$")


def _walk(nodes: list[dict[str, Any]], parent_visible: bool = True, parent_id: int | None = None):
    for node in nodes or []:
        visible = parent_visible and bool(node.get("visible"))
        yield node, visible, parent_id
        yield from _walk(node.get("children") or [], visible, node.get("id"))


def _issue(code: str, message: str, action_index: int | None = None) -> dict[str, Any]:
    result = {"code": code, "severity": "error", "message": message}
    if action_index is not None:
        result["actionIndex"] = action_index
    return result


def validate_structure_plan(plan_path: Path, inspection_path: Path) -> dict[str, Any]:
    if not plan_path.is_file() or not inspection_path.is_file():
        missing = [str(path) for path in (plan_path, inspection_path) if not path.is_file()]
        return {
            "status": "BLOCKED",
            "readyToApply": False,
            "issues": [_issue("STRUCTURE_PLAN_EVIDENCE_MISSING", path) for path in missing],
        }

    try:
        plan = PsdStructurePlan.model_validate_json(plan_path.read_text(encoding="utf-8-sig"))
        inspection = json.loads(inspection_path.read_text(encoding="utf-8-sig"))
    except (ValueError, json.JSONDecodeError) as error:
        return {
            "status": "BLOCKED",
            "readyToApply": False,
            "issues": [_issue("STRUCTURE_PLAN_INVALID_JSON", str(error))],
        }

    issues: list[dict[str, Any]] = []
    document = inspection.get("document") or {}
    if str(document.get("name") or "") != plan.source_document:
        issues.append(_issue("STRUCTURE_PLAN_DOCUMENT_MISMATCH", f"Plan targets {plan.source_document}; inspection is {document.get('name')}."))
    if round(float(document.get("width") or 0)) != plan.canvas_width or round(float(document.get("height") or 0)) != plan.canvas_height:
        issues.append(_issue("STRUCTURE_PLAN_CANVAS_MISMATCH", "Plan canvas does not match the inspected PSD."))

    walked = list(_walk(inspection.get("layers") or []))
    layers = {int(node["id"]): (node, visible, parent_id) for node, visible, parent_id in walked if node.get("id") is not None}
    prior_refs: set[str] = set()
    renamed_ids: set[int] = set()
    moved_ids: set[int] = set()
    final_names = {layer_id: str(node.get("name") or "") for layer_id, (node, _, _) in layers.items()}
    rename_action_indices: dict[int, int] = {}

    for index, action in enumerate(plan.actions):
        if action.action == "create_group":
            if not action.ref or not REF_NAME.fullmatch(action.ref):
                issues.append(_issue("STRUCTURE_PLAN_INVALID_REF", "create_group requires a lowercase ASCII ref.", index))
            elif action.ref in prior_refs:
                issues.append(_issue("STRUCTURE_PLAN_DUPLICATE_REF", f"Duplicate group ref: {action.ref}.", index))
            if not action.new_name or not ASCII_NAME.fullmatch(action.new_name):
                issues.append(_issue("STRUCTURE_PLAN_INVALID_NAME", "Created group names must be printable ASCII.", index))
            if action.parent_ref and action.parent_ref not in prior_refs:
                issues.append(_issue("STRUCTURE_PLAN_PARENT_REF_ORDER", f"Parent ref must be created earlier: {action.parent_ref}.", index))
            if action.parent_layer_id is not None:
                target = layers.get(action.parent_layer_id)
                if target is None or target[0].get("nodeType") != "group":
                    issues.append(_issue("STRUCTURE_PLAN_PARENT_NOT_GROUP", f"Layer {action.parent_layer_id} is not a group.", index))
                elif not target[1]:
                    issues.append(_issue("STRUCTURE_PLAN_HIDDEN_TARGET", f"Parent group {action.parent_layer_id} is effectively hidden.", index))
            if action.parent_ref and action.parent_layer_id is not None:
                issues.append(_issue("STRUCTURE_PLAN_MULTIPLE_PARENTS", "Use parent_ref or parent_layer_id, not both.", index))
            if action.ref:
                prior_refs.add(action.ref)
            continue

        if action.layer_id is None or action.layer_id not in layers:
            issues.append(_issue("STRUCTURE_PLAN_LAYER_MISSING", f"Layer ID {action.layer_id} was not found.", index))
            continue
        node, effective_visible, _ = layers[action.layer_id]
        if not effective_visible:
            issues.append(_issue("STRUCTURE_PLAN_HIDDEN_LAYER", f"Layer {action.layer_id} is effectively hidden and must remain unchanged.", index))

        if action.action == "rename":
            if action.layer_id in renamed_ids:
                issues.append(_issue("STRUCTURE_PLAN_DUPLICATE_RENAME", f"Layer {action.layer_id} is renamed more than once.", index))
            renamed_ids.add(action.layer_id)
            rename_action_indices[action.layer_id] = index
            if not action.new_name or not ASCII_NAME.fullmatch(action.new_name):
                issues.append(_issue("STRUCTURE_PLAN_INVALID_NAME", "Renamed layer names must be printable ASCII.", index))
            elif action.new_name:
                final_names[action.layer_id] = action.new_name
        elif action.action == "move":
            if action.layer_id in moved_ids:
                issues.append(_issue("STRUCTURE_PLAN_DUPLICATE_MOVE", f"Layer {action.layer_id} is moved more than once.", index))
            moved_ids.add(action.layer_id)
            if bool(action.parent_ref) == bool(action.parent_layer_id is not None):
                issues.append(_issue("STRUCTURE_PLAN_MOVE_PARENT", "move requires exactly one parent_ref or parent_layer_id.", index))
            elif action.parent_ref and action.parent_ref not in prior_refs:
                issues.append(_issue("STRUCTURE_PLAN_PARENT_REF_ORDER", f"Parent ref must be created earlier: {action.parent_ref}.", index))
            elif action.parent_layer_id is not None:
                target = layers.get(action.parent_layer_id)
                if target is None or target[0].get("nodeType") != "group":
                    issues.append(_issue("STRUCTURE_PLAN_PARENT_NOT_GROUP", f"Layer {action.parent_layer_id} is not a group.", index))
                elif not target[1]:
                    issues.append(_issue("STRUCTURE_PLAN_HIDDEN_TARGET", f"Parent group {action.parent_layer_id} is effectively hidden.", index))

    for layer_id, (node, effective_visible, parent_id) in layers.items():
        if not effective_visible or str(node.get("layerKind") or "").casefold() not in {"layerkind.text", "text"}:
            continue
        ancestor_id = parent_id
        inside_merge = False
        while ancestor_id is not None and ancestor_id in layers:
            if final_names.get(ancestor_id, "").startswith("[MERGE]"):
                inside_merge = True
                break
            ancestor_id = layers[ancestor_id][2]
        if inside_merge or final_names.get(layer_id, "").startswith("TMP_"):
            continue
        issues.append(_issue(
            "STRUCTURE_PLAN_TEXT_NAME_MISSING_TMP",
            f"Visible text layer {layer_id} must use a TMP_ name unless it is inside a [MERGE] group.",
            rename_action_indices.get(layer_id),
        ))

    status = "BLOCKED" if issues else "PASS" if plan.approved else "NEEDS_REVIEW"
    return {
        "status": status,
        "readyToApply": not issues and plan.approved,
        "approved": plan.approved,
        "actionCount": len(plan.actions),
        "renameCount": sum(action.action == "rename" for action in plan.actions),
        "createGroupCount": sum(action.action == "create_group" for action in plan.actions),
        "moveCount": sum(action.action == "move" for action in plan.actions),
        "issues": issues,
        "nextAction": "Apply with the deterministic Photoshop tool." if not issues and plan.approved else "Approve a valid plan before applying." if not issues else "Repair the structure plan.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate a PSD structure plan against deterministic inspection evidence.")
    parser.add_argument("plan", type=Path)
    parser.add_argument("inspection", type=Path)
    args = parser.parse_args()
    result = validate_structure_plan(args.plan.resolve(), args.inspection.resolve())
    print(json.dumps(result, ensure_ascii=True))
    return 1 if result["status"] == "BLOCKED" else 0


if __name__ == "__main__":
    raise SystemExit(main())
