from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.models import PsdStructurePlan
from ps_to_unity_agents.psd_structure_plan import bind_structure_plan_preconditions, validate_structure_plan


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class PsdStructurePlanTests(unittest.TestCase):
    def write_case(self, plan: dict, inspection: dict, bind: bool = True):
        temporary = tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT)
        root = Path(temporary.name)
        plan_path = root / "plan.json"
        inspection_path = root / "inspection.json"
        if bind:
            plan = bind_structure_plan_preconditions(PsdStructurePlan.model_validate(plan), inspection).model_dump(mode="json")
            plan["approved"] = True
        plan_path.write_text(json.dumps(plan, ensure_ascii=False), encoding="utf-8")
        inspection_path.write_text(json.dumps(inspection, ensure_ascii=False), encoding="utf-8")
        return temporary, plan_path, inspection_path

    def base_inspection(self):
        return {
            "document": {"name": "Screen.psd", "width": 100, "height": 100},
            "layers": [
                {"id": 1, "name": "可見", "nodeType": "layer", "visible": True, "children": []},
                {"id": 2, "name": "Hidden", "nodeType": "group", "visible": False, "children": [
                    {"id": 3, "name": "Child", "nodeType": "layer", "visible": True, "children": []},
                ]},
            ],
        }

    def base_plan(self):
        return {
            "schema_version": "1.0",
            "case_id": "screen",
            "source_document": "Screen.psd",
            "canvas_width": 100,
            "canvas_height": 100,
            "approved": True,
            "actions": [
                {"action": "create_group", "ref": "screen_root", "new_name": "Screen", "reason": "Production root"},
                {"action": "rename", "layer_id": 1, "new_name": "Btn_Confirm", "reason": "Approved button"},
                {"action": "move", "layer_id": 1, "parent_ref": "screen_root", "reason": "Place in production root"},
            ],
        }

    def test_approved_visible_plan_is_ready(self):
        temporary, plan_path, inspection_path = self.write_case(self.base_plan(), self.base_inspection())
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("PASS", result["status"])
            self.assertTrue(result["readyToApply"])
        finally:
            temporary.cleanup()

    def test_hidden_descendant_cannot_be_changed(self):
        plan = self.base_plan()
        plan["actions"] = [{"action": "rename", "layer_id": 3, "new_name": "Icon_Hidden", "reason": "Should fail"}]
        temporary, plan_path, inspection_path = self.write_case(plan, self.base_inspection())
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("BLOCKED", result["status"])
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_HIDDEN_LAYER" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_non_ascii_output_name_is_blocked(self):
        plan = self.base_plan()
        plan["actions"] = [{"action": "rename", "layer_id": 1, "new_name": "按鈕", "reason": "Should fail"}]
        temporary, plan_path, inspection_path = self.write_case(plan, self.base_inspection())
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("BLOCKED", result["status"])
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_INVALID_NAME" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_unapproved_plan_needs_review(self):
        plan = self.base_plan()
        plan["approved"] = False
        temporary, plan_path, inspection_path = self.write_case(plan, self.base_inspection())
        saved = json.loads(plan_path.read_text(encoding="utf-8"))
        saved["approved"] = False
        plan_path.write_text(json.dumps(saved), encoding="utf-8")
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("NEEDS_REVIEW", result["status"])
            self.assertFalse(result["readyToApply"])
        finally:
            temporary.cleanup()

    def test_visible_numeric_text_requires_tmp_prefix(self):
        inspection = self.base_inspection()
        inspection["layers"][0].update({"name": "999,130", "layerKind": "LayerKind.TEXT"})
        plan = self.base_plan()
        plan["actions"] = []
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("BLOCKED", result["status"])
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_TEXT_NAME_MISSING_TMP" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_visible_numeric_text_with_tmp_prefix_is_valid(self):
        inspection = self.base_inspection()
        inspection["layers"][0].update({"name": "999,130", "layerKind": "LayerKind.TEXT"})
        plan = self.base_plan()
        plan["actions"] = [{"action": "rename", "layer_id": 1, "new_name": "TMP_Price", "reason": "Runtime price"}]
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("PASS", result["status"])
        finally:
            temporary.cleanup()

    def test_text_inside_merge_group_is_exempt(self):
        inspection = self.base_inspection()
        inspection["layers"] = [{
            "id": 10,
            "name": "Artwork",
            "nodeType": "group",
            "visible": True,
            "children": [{"id": 11, "name": "Decorative 10", "nodeType": "layer", "layerKind": "LayerKind.TEXT", "visible": True, "children": []}],
        }]
        plan = self.base_plan()
        plan["actions"] = [{"action": "rename", "layer_id": 10, "new_name": "[MERGE]Artwork", "reason": "Bake decoration"}]
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertEqual("PASS", result["status"])
        finally:
            temporary.cleanup()

    def test_combined_moves_and_new_groups_cannot_create_cycle(self):
        inspection = self.base_inspection()
        inspection["layers"][0]["nodeType"] = "group"
        plan = self.base_plan()
        plan["actions"] = [
            {"action": "create_group", "ref": "child", "new_name": "Child", "parent_layer_id": 1, "reason": "test"},
            {"action": "move", "layer_id": 1, "parent_ref": "child", "reason": "test"},
        ]
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertIn("STRUCTURE_PLAN_MOVE_CYCLE", [issue["code"] for issue in result["issues"]])
        finally:
            temporary.cleanup()

    def test_duplicate_inspection_ids_are_blocked(self):
        inspection = self.base_inspection()
        inspection["layers"].append(dict(inspection["layers"][0]))
        temporary, plan_path, inspection_path = self.write_case(self.base_plan(), inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertIn("STRUCTURE_PLAN_DUPLICATE_LAYER_ID", [issue["code"] for issue in result["issues"]])
        finally:
            temporary.cleanup()

    def test_renaming_to_existing_sibling_name_is_blocked(self):
        plan = self.base_plan()
        plan["actions"] = [{"action": "rename", "layer_id": 1, "new_name": "Hidden", "reason": "test"}]
        temporary, plan_path, inspection_path = self.write_case(plan, self.base_inspection())
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertIn("STRUCTURE_PLAN_FINAL_NAME_CONFLICT", [issue["code"] for issue in result["issues"]])
        finally:
            temporary.cleanup()

    def test_existing_group_name_is_blocked(self):
        inspection = self.base_inspection()
        inspection["layers"].append({
            "id": 4, "name": "Screen", "nodeType": "group", "layerKind": "group", "visible": True, "children": [],
        })
        plan = self.base_plan()
        plan["actions"] = [{"action": "create_group", "ref": "screen", "new_name": "Screen", "reason": "Duplicate"}]
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_GROUP_NAME_CONFLICT" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_move_into_descendant_is_blocked(self):
        inspection = {
            "document": {"name": "Screen.psd", "width": 100, "height": 100},
            "layers": [{
                "id": 10, "name": "Parent", "nodeType": "group", "layerKind": "group", "visible": True,
                "children": [{"id": 11, "name": "Child", "nodeType": "group", "layerKind": "group", "visible": True, "children": []}],
            }],
        }
        plan = self.base_plan()
        plan["actions"] = [{"action": "move", "layer_id": 10, "parent_layer_id": 11, "reason": "Invalid cycle"}]
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_MOVE_CYCLE" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_sibling_moves_must_be_bottom_to_top(self):
        inspection = self.base_inspection()
        inspection["layers"] = [
            {"id": 1, "name": "Top", "nodeType": "layer", "layerKind": "LayerKind.NORMAL", "visible": True, "children": []},
            {"id": 2, "name": "Bottom", "nodeType": "layer", "layerKind": "LayerKind.NORMAL", "visible": True, "children": []},
        ]
        plan = self.base_plan()
        plan["actions"] = [
            {"action": "create_group", "ref": "target", "new_name": "Target", "reason": "Group"},
            {"action": "move", "layer_id": 1, "parent_ref": "target", "reason": "Wrong order"},
            {"action": "move", "layer_id": 2, "parent_ref": "target", "reason": "Wrong order"},
        ]
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_MOVE_ORDER" for issue in result["issues"]))
        finally:
            temporary.cleanup()

    def test_changed_name_breaks_bound_precondition(self):
        plan = self.base_plan()
        inspection = self.base_inspection()
        temporary, plan_path, inspection_path = self.write_case(plan, inspection)
        try:
            inspection["layers"][0]["name"] = "ChangedAfterPlanning"
            inspection_path.write_text(json.dumps(inspection), encoding="utf-8")
            result = validate_structure_plan(plan_path, inspection_path)
            self.assertTrue(any(issue["code"] == "STRUCTURE_PLAN_PRECONDITION_MISMATCH" for issue in result["issues"]))
        finally:
            temporary.cleanup()


if __name__ == "__main__":
    unittest.main()
