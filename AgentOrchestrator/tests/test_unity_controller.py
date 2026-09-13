import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock

from ps_to_unity_agents.fingerprints import sha256_file
from ps_to_unity_agents.models import AgentDecision, PipelineRequest, Status
from ps_to_unity_agents.unity_controller import UnityAgentController


class UnityControllerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        project = self.root / "project"
        for folder in ("Assets/Editor/PhotoshopUiImporter", "ProjectSettings"):
            (project / folder).mkdir(parents=True)
        (project / "ProjectSettings/ProjectVersion.txt").write_text("test")
        (project / "Assets/Editor/PhotoshopUiImporter/PhotoshopUiBatchEntryPoint.cs").write_text("test")
        executable = self.root / "Unity.exe"
        executable.write_text("fake; never execute")
        layout = self.root / "source.json"
        layout.write_text(json.dumps({"canvas": {"width": 100, "height": 100}, "nodes": []}))
        self.request = PipelineRequest(case_id="test", psd_path=self.root / "test.psd", layout_json_path=layout,
            artist_asset_folder=self.root / "artist", exporter_asset_folder=self.root / "Images", output_folder=self.root / "run",
            unity_executable=executable, unity_project_path=project, execution_mode="execute", semantics_approved=True)

    def factory(self, *, mutate=None, code=0, timeout=False):
        def launch(command, **kwargs):
            self.command = command
            payload = json.loads(Path(command[command.index("-psToUnityRequest") + 1]).read_text())
            self.payload = payload
            prefab = self.request.unity_project_path / payload["prefabFolder"] / (payload["prefabName"] + ".prefab")
            prefab.parent.mkdir(parents=True, exist_ok=True)
            prefab.write_text("generated prefab")
            receipt = {"status": "PASS", "runId": payload["runId"], "requestFingerprint": payload["requestFingerprint"],
                       "layoutSha256": sha256_file(Path(payload["layoutJsonPath"])), "prefabSha256": sha256_file(prefab),
                       "prefabAssetPath": prefab.relative_to(self.request.unity_project_path).as_posix(), "errors": []}
            if mutate:
                mutate(receipt, prefab)
            if receipt:
                Path(command[command.index("-psToUnityResult") + 1]).write_text(json.dumps(receipt))
            process = Mock(pid=1234)
            process.poll.return_value = None if timeout else code
            process.wait.side_effect = subprocess.TimeoutExpired(command, 1) if timeout else None
            process.wait.return_value = code
            self.process = process
            return process
        return Mock(side_effect=launch)

    def test_fresh_generation_and_bounded_duplicate(self):
        factory = self.factory()
        controller = UnityAgentController(self.request, process_factory=factory)
        result = controller.generate()
        self.assertEqual("PASS", result["status"])
        self.assertEqual("UNITY_GENERATION_ONLY", result["validationScope"])
        self.assertEqual("NOT_RUN", result["liveAcceptance"])
        self.assertEqual(result, controller.generate())
        factory.assert_called_once()
        self.assertIn("-batchmode", self.command)
        self.assertFalse((self.request.unity_project_path / ".ps_to_unity_agent.lock").exists())

    def test_approval_and_analyze_never_launch(self):
        for updates in ({"execution_mode": "analyze"}, {"semantics_approved": False}):
            factory = Mock()
            result = UnityAgentController(self.request.model_copy(update=updates), process_factory=factory).generate()
            self.assertEqual("NEEDS_REVIEW", result["status"])
            factory.assert_not_called()

    def test_missing_dependency_and_path_escape(self):
        for updates in ({"unity_default_tmp_font_asset": "Assets/Missing.asset"}, {"unity_prefab_folder": "Assets/../../escape"}):
            factory = Mock()
            self.assertEqual("BLOCKED", UnityAgentController(self.request.model_copy(update=updates), process_factory=factory).generate()["status"])
            factory.assert_not_called()

    def test_open_project_never_launch(self):
        lock = self.request.unity_project_path / "Temp/UnityLockfile"
        lock.parent.mkdir()
        lock.touch()
        factory = Mock()
        self.assertEqual("NEEDS_REVIEW", UnityAgentController(self.request, process_factory=factory).generate()["status"])
        factory.assert_not_called()

    def test_stale_missing_forged_receipts_and_prefab(self):
        mutations = [lambda r, p: r.update(runId="old"), lambda r, p: r.update(requestFingerprint="forged"),
                     lambda r, p: r.update(layoutSha256="old"), lambda r, p: r.update(prefabSha256="forged"),
                     lambda r, p: p.unlink(), lambda r, p: r.clear()]
        for mutate in mutations:
            with self.subTest(mutate=mutate):
                result = UnityAgentController(self.request, process_factory=self.factory(mutate=mutate)).generate()
                self.assertEqual("BLOCKED", result["status"])

    def test_timeout_retains_lock_and_prevents_new_controller(self):
        factory = self.factory(timeout=True)
        controller = UnityAgentController(self.request, process_factory=factory)
        self.assertEqual("FAIL_RETRYABLE", controller.generate()["status"])
        self.assertEqual("FAIL_RETRYABLE", controller.generate()["status"])
        self.assertEqual("NEEDS_REVIEW", UnityAgentController(self.request, process_factory=factory).generate()["status"])
        factory.assert_called_once()

    def test_completed_timed_out_process_is_consumed_without_restart(self):
        factory = self.factory(timeout=True)
        controller = UnityAgentController(self.request, process_factory=factory)
        self.assertEqual("FAIL_RETRYABLE", controller.generate()["status"])
        self.process.poll.return_value = 0
        self.assertEqual("PASS", controller.generate()["status"])
        factory.assert_called_once()
        self.assertFalse((self.request.unity_project_path / ".ps_to_unity_agent.lock").exists())

    def test_retry_at_most_once_after_exited_process(self):
        factory = self.factory(code=1, mutate=lambda r, p: r.clear())
        controller = UnityAgentController(self.request, process_factory=factory)
        for _ in range(3):
            self.assertEqual("FAIL_RETRYABLE", controller.generate()["status"])
        self.assertEqual(2, factory.call_count)

    def test_nonzero_dependency_receipt_is_blocked(self):
        factory = self.factory(code=1, mutate=lambda r, p: r.update(status="BLOCKED", errors=["Missing TMP"]))
        self.assertEqual("BLOCKED", UnityAgentController(self.request, process_factory=factory).generate()["status"])

    def test_changed_layout_during_generation_is_blocked(self):
        def mutate(_receipt, _prefab):
            Path(self.payload["layoutJsonPath"]).write_text('{"changed":true}')
        result = UnityAgentController(self.request, process_factory=self.factory(mutate=mutate)).generate()
        self.assertEqual("BLOCKED", result["status"])

    def test_changed_material_library_during_generation_is_blocked(self):
        library = self.request.unity_project_path / "Assets/Fonts/Materials"
        library.mkdir(parents=True)
        material = library / "Outline.mat"
        material.write_text("before")
        request = self.request.model_copy(update={"unity_material_library_folder": "Assets/Fonts/Materials"})

        def mutate(_receipt, _prefab):
            material.write_text("after")

        result = UnityAgentController(request, process_factory=self.factory(mutate=mutate)).generate()
        self.assertEqual("BLOCKED", result["status"])
        self.assertEqual("UNITY_STALE_RESULT", result["issues"][0]["code"])

    def test_full_mapping(self):
        request = self.request.model_copy(update={"unity_prefab_name": "Screen", "unity_project_folder": "Screen",
            "unity_reference_resolution_x": 1920, "unity_reference_resolution_y": 1080,
            "unity_outline_thickness_multiplier": 2, "unity_use_responsive_anchor": True, "unity_create_sprite_atlases": False})
        self.assertEqual("PASS", UnityAgentController(request, process_factory=self.factory()).generate()["status"])
        self.assertEqual(1920, self.payload["referenceResolutionX"])
        self.assertEqual(1080, self.payload["referenceResolutionY"])
        self.assertEqual(2, self.payload["outlineThicknessMultiplier"])
        self.assertTrue(self.payload["useResponsiveAnchor"])
        self.assertFalse(self.payload["createSpriteAtlases"])

    def test_model_cannot_claim_generation_pass(self):
        controller = UnityAgentController(self.request)
        decision = AgentDecision(status=Status.PASS, summary="Invented", next_action="Done")
        self.assertEqual(Status.NEEDS_REVIEW, controller.enforce_decision(decision).status)

    def test_model_cannot_downgrade_deterministic_block(self):
        controller = UnityAgentController(self.request)
        controller.last_result = {"status": "BLOCKED"}
        decision = AgentDecision(status=Status.NEEDS_REVIEW, summary="Too soft", next_action="Review")
        self.assertEqual(Status.BLOCKED, controller.enforce_decision(decision).status)

    def test_unmapped_font_warning_requires_review(self):
        def warning(receipt, _prefab):
            receipt["diagnostics"] = [{"code": "FONT_TOKEN_UNMAPPED", "severity": "warning"}]
        result = UnityAgentController(self.request, process_factory=self.factory(mutate=warning)).generate()
        self.assertEqual("NEEDS_REVIEW", result["status"])


if __name__ == "__main__":
    unittest.main()
