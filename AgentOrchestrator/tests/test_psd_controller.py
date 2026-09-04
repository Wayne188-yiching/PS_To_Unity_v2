from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.models import PipelineRequest, PsdAgentDecision, PsdStructurePlan, Status
from ps_to_unity_agents.psd_controller import PsdAgentController, validate_export_package


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class FakeController(PsdAgentController):
    apply_count = 0
    finalize_count = 0

    def _apply_structure(self):
        self.apply_count += 1
        return {"status": "PASS", "saved": True, "backupPath": "Screen.pre_structure.psd"}

    def _finalize_package(self):
        self.finalize_count += 1
        return {
            "status": "PASS",
            "packageValidation": {"status": "PASS", "referencedImageCount": 1},
            "evidence": {"status": "PASS"},
            "issues": [],
        }


class PsdControllerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(dir=TEST_TEMP_ROOT)
        self.root = Path(self.temporary.name)
        self.psd = self.root / "Screen.psd"
        self.psd.write_bytes(b"test")
        self.inspection = self.root / "inspection.json"
        self.inspection.write_text(json.dumps({
            "document": {"name": "Screen.psd", "width": 100, "height": 100},
            "layers": [{"id": 1, "name": "中文", "nodeType": "layer", "visible": True, "children": []}],
        }, ensure_ascii=False), encoding="utf-8")
        self.request = PipelineRequest(
            case_id="screen",
            psd_path=self.psd,
            psd_inspection_path=self.inspection,
            layout_json_path=self.root / "export" / "layout.json",
            artist_asset_folder=self.root / "artist",
            exporter_asset_folder=self.root / "export" / "Images",
            output_folder=self.root / "run",
        )

    def tearDown(self):
        self.temporary.cleanup()

    def decision(self) -> PsdAgentDecision:
        return PsdAgentDecision(
            status=Status.NEEDS_REVIEW,
            summary="Plan ready.",
            next_action="Approve the plan.",
            structure_plan=PsdStructurePlan(
                case_id="screen",
                source_document="Screen.psd",
                canvas_width=100,
                canvas_height=100,
                approved=True,
                actions=[{
                    "action": "rename",
                    "layer_id": 1,
                    "new_name": "Frame_Main",
                    "reason": "Visible production artwork.",
                }],
            ),
        )

    def test_recorded_agent_plan_is_forced_back_to_unapproved(self):
        controller = PsdAgentController(self.request)
        result = controller.record_plan(self.decision())
        saved = json.loads(controller.plan_path.read_text(encoding="utf-8"))
        self.assertEqual("NEEDS_REVIEW", result["status"])
        self.assertEqual("AWAITING_PLAN_APPROVAL", result["stage"])
        self.assertFalse(saved["approved"])

    def test_explicit_approval_runs_apply_once_then_reuses_checkpoint(self):
        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        first = controller.approve_apply_and_export()
        second = controller.approve_apply_and_export()
        self.assertEqual("PACKAGE_READY", first["stage"])
        self.assertEqual("PASS", first["status"])
        self.assertEqual(first, second)
        self.assertEqual(1, controller.apply_count)
        self.assertEqual(1, controller.finalize_count)

    def test_psd_change_after_package_ready_reexports_without_reapplying_plan(self):
        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        controller.approve_apply_and_export()
        self.psd.write_bytes(b"artist updated sliced metadata")
        refreshed = controller.approve_apply_and_export()
        self.assertEqual("PACKAGE_READY", refreshed["stage"])
        self.assertEqual(1, controller.apply_count)
        self.assertEqual(2, controller.finalize_count)

    def test_export_package_requires_every_referenced_image(self):
        export = self.root / "export"
        images = export / "Images"
        images.mkdir(parents=True)
        layout = export / "layout.json"
        layout.write_text(json.dumps({
            "schemaVersion": "2.11",
            "canvas": {"width": 100, "height": 100},
            "nodes": [{"name": "icon", "type": "image", "imagePath": "icon.png", "children": []}],
        }), encoding="utf-8")
        missing = validate_export_package(layout, images)
        self.assertEqual("BLOCKED", missing["status"])
        self.assertTrue(any(issue["code"] == "LAYOUT_IMAGE_MISSING" for issue in missing["issues"]))
        (images / "icon.png").write_bytes(b"png")
        valid = validate_export_package(layout, images)
        self.assertEqual("PASS", valid["status"])


if __name__ == "__main__":
    unittest.main()
