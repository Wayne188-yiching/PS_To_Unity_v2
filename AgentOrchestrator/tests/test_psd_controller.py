from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from ps_to_unity_agents.fingerprints import plan_fingerprint, sha256_file
from ps_to_unity_agents.models import PipelineRequest, PsdAgentDecision, PsdStructurePlan, Status
from ps_to_unity_agents.psd_controller import PsdAgentController, classify_tool_failure, validate_export_package


TEST_TEMP_ROOT = Path(__file__).resolve().parents[1] / ".test_tmp"
TEST_TEMP_ROOT.mkdir(exist_ok=True)


class FakeController(PsdAgentController):
    apply_count = 0
    finalize_count = 0

    def _apply_structure(self):
        self.apply_count += 1
        plan = PsdStructurePlan.model_validate_json(self.plan_path.read_text(encoding="utf-8"))
        return {
            "status": "PASS",
            "saved": True,
            "backupPath": "Screen.pre_structure.psd",
            "planFingerprint": plan_fingerprint(plan),
        }

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
        inspection = {
            "runId": "test-inspection",
            "sourceFingerprint": sha256_file(self.psd),
            "document": {"name": "Screen.psd", "width": 100, "height": 100},
            "layers": [{
                "id": 1, "name": "中文", "nodeType": "layer", "layerKind": "LayerKind.NORMAL", "visible": True, "children": [],
            }],
        }
        self.inspection.write_text(json.dumps(inspection, ensure_ascii=False), encoding="utf-8")
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

    def test_approval_updates_persisted_validation_and_result(self):
        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        result = controller.approve_existing_plan()
        validation = json.loads((self.request.output_folder / 'psd_structure_plan_validation.json').read_text(encoding='utf-8'))
        saved_result = json.loads(controller.result_path.read_text(encoding='utf-8'))
        self.assertTrue(validation['readyToApply'])
        self.assertTrue(validation['approved'])
        self.assertEqual(result, saved_result)

    def test_explicit_approval_runs_apply_once_then_reuses_checkpoint(self):
        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        first = controller.approve_apply_and_export()
        second = controller.approve_apply_and_export()
        self.assertEqual("PACKAGE_READY", first["stage"])
        self.assertEqual("PASS", first["status"])
        self.assertEqual(first, second)
        self.assertEqual(1, controller.apply_count)
        self.assertEqual(2, controller.finalize_count)

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

    def test_psd_change_before_approval_invalidates_plan(self):
        from unittest.mock import patch

        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        self.psd.write_bytes(b"changed after planning")
        with patch("ps_to_unity_agents.psd_controller.CaseTools.ensure_inspection", return_value={"status": "PASS"}):
            result = controller.approve_apply_and_export()
        self.assertEqual("NEEDS_REVIEW", result["status"])
        self.assertEqual("APPROVAL_INVALIDATED", result["stage"])
        self.assertEqual(0, controller.apply_count)

    def test_psd_change_after_approval_before_apply_invalidates_plan(self):
        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        self.assertEqual("PASS", controller.approve_existing_plan()["status"])
        self.psd.write_bytes(b"edited after human approval")
        result = controller.approve_apply_and_export()
        self.assertEqual("NEEDS_REVIEW", result["status"])
        self.assertEqual(0, controller.apply_count)

    def test_corrupt_approved_plan_fails_closed(self):
        controller = FakeController(self.request)
        controller.record_plan(self.decision())
        controller.approve_existing_plan()
        controller.plan_path.write_text('{broken', encoding='utf-8')
        result = controller.approve_apply_and_export()
        self.assertEqual('NEEDS_REVIEW', result['status'])
        self.assertEqual(0, controller.apply_count)

    def test_ready_package_is_revalidated_after_asset_deletion(self):
        images = self.root / 'export' / 'Images'
        images.mkdir(parents=True)
        icon = images / 'icon.png'
        icon.write_bytes(b'fixture')
        self.request.layout_json_path.write_text(json.dumps({
            'canvas': {'width': 100, 'height': 100},
            'nodes': [{'name': 'icon', 'type': 'image', 'imagePath': 'icon.png'}],
        }), encoding='utf-8')

        class ValidatingController(FakeController):
            def _finalize_package(inner):
                return validate_export_package(inner.request.layout_json_path, images)

        controller = ValidatingController(self.request)
        controller.record_plan(self.decision())
        self.assertEqual('PASS', controller.approve_apply_and_export()['status'])
        icon.unlink()
        self.assertEqual('BLOCKED', controller.approve_apply_and_export()['status'])
        self.assertEqual(1, controller.apply_count)

    def test_retry_from_applying_checkpoint_does_not_reapprove(self):
        class CrashOnceController(FakeController):
            crashed = False

            def _apply_structure(self):
                self.apply_count += 1
                if not self.crashed:
                    self.crashed = True
                    self.request.psd_path.write_bytes(b"saved by Photoshop")
                    raise RuntimeError("host stopped after Photoshop save")
                plan = PsdStructurePlan.model_validate_json(self.plan_path.read_text(encoding="utf-8"))
                return {
                    "status": "PASS",
                    "saved": True,
                    "alreadyAppliedCount": len(plan.actions),
                    "planFingerprint": plan_fingerprint(plan),
                }

        controller = CrashOnceController(self.request)
        controller.record_plan(self.decision())
        with self.assertRaises(RuntimeError):
            controller.approve_apply_and_export()
        state = json.loads(controller.state_path.read_text(encoding="utf-8"))
        self.assertEqual("APPLYING", state["stage"])
        recovered = controller.approve_apply_and_export()
        self.assertEqual("PACKAGE_READY", recovered["stage"])
        self.assertEqual(2, controller.apply_count)

    def test_transient_apply_failure_keeps_retryable_checkpoint(self):
        class BusyOnceController(FakeController):
            busy = True

            def _apply_structure(self):
                self.apply_count += 1
                if self.busy:
                    self.busy = False
                    return {"status": "FAIL_RETRYABLE", "error": "RPC server is busy"}
                return super()._apply_structure()

        controller = BusyOnceController(self.request)
        controller.record_plan(self.decision())
        first = controller.approve_apply_and_export()
        self.assertEqual("FAIL_RETRYABLE", first["status"])
        self.assertEqual("APPLYING", first["stage"])
        second = controller.approve_apply_and_export()
        self.assertEqual("PACKAGE_READY", second["stage"])

    def test_tool_failure_classification_is_rule_based(self):
        self.assertEqual("FAIL_RETRYABLE", classify_tool_failure("The RPC server is busy"))
        self.assertEqual("FAIL_RETRYABLE", classify_tool_failure("伺服器忙碌中，呼叫被拒"))
        self.assertEqual("BLOCKED", classify_tool_failure("Plan precondition failed"))


if __name__ == "__main__":
    unittest.main()
