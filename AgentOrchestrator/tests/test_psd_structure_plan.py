from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.psd_structure_plan import validate_structure_plan


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class PsdStructurePlanTests(unittest.TestCase):
    def write_case(self, plan: dict, inspection: dict):
        temporary = tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT)
        root = Path(temporary.name)
        plan_path = root / "plan.json"
        inspection_path = root / "inspection.json"
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


if __name__ == "__main__":
    unittest.main()
