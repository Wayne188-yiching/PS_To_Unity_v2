from __future__ import annotations

import json
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import main
from agent_roles.pipeline_agents import CaseTools, enforce_pipeline_gate
from ps_to_unity_agents.fingerprints import sha256_file
from ps_to_unity_agents.models import PipelineDecision, PipelineRequest, Status


class PipelineEntryTests(unittest.IsolatedAsyncioTestCase):
    def make_request(self, root: Path, **updates) -> PipelineRequest:
        psd = root / "Screen.psd"
        psd.write_bytes(b"psd")
        values = dict(
            case_id="screen",
            psd_path=psd,
            layout_json_path=root / "export" / "layout.json",
            artist_asset_folder=root / "artist",
            exporter_asset_folder=root / "export" / "Images",
            output_folder=root / "run",
        )
        values.update(updates)
        return PipelineRequest(**values)

    def decision(self) -> PipelineDecision:
        return PipelineDecision(
            status=Status.PASS,
            summary="Model claimed pass.",
            responsible_agent="NONE",
            next_action="Done.",
        )

    def test_pipeline_gate_never_uses_outsourcing_fields(self):
        with tempfile.TemporaryDirectory() as raw:
            request = self.make_request(Path(raw), execution_mode="execute", semantics_approved=True)
            gated = enforce_pipeline_gate(self.decision(), request)
            self.assertEqual(Status.NEEDS_REVIEW, gated.status)
            self.assertEqual("PIPELINE_VALIDATOR", gated.responsible_agent)

    async def test_run_live_writes_pipeline_decision_without_outsourcing_gate(self):
        with tempfile.TemporaryDirectory() as raw:
            request = self.make_request(Path(raw), execution_mode="execute", semantics_approved=True)
            with patch.dict(os.environ, {"OPENAI_API_KEY": "test"}), \
                    patch("main.build_director", return_value=object()), \
                    patch("main.Runner.run", new=AsyncMock(return_value=SimpleNamespace(final_output=self.decision()))):
                exit_code = await main.run_live(request)
            saved = json.loads((request.output_folder / "director_result.json").read_text(encoding="utf-8"))
            self.assertEqual(0, exit_code)
            self.assertEqual("NEEDS_REVIEW", saved["status"])
            self.assertEqual("PIPELINE_VALIDATOR", saved["responsible_agent"])

    def test_cached_evidence_requires_matching_psd_fingerprint(self):
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            request = self.make_request(root)
            request.layout_json_path.parent.mkdir(parents=True)
            fingerprint = sha256_file(request.psd_path)
            inspection = root / "inspection.json"
            inspection.write_text(json.dumps({"runId": "inspection-test", "sourceFingerprint": fingerprint}), encoding="utf-8")
            request = request.model_copy(update={"psd_inspection_path": inspection})
            request.layout_json_path.write_text("{}", encoding="utf-8")
            (request.layout_json_path.parent / "photoshop_result.json").write_text(
                json.dumps({"status": "PASS", "runId": "export-test", "psdSha256": fingerprint,
                            "layoutSha256": sha256_file(request.layout_json_path), "imageSha256": {}}), encoding="utf-8"
            )
            tools = CaseTools(request)
            self.assertEqual("cache", tools.ensure_inspection()["source"])
            self.assertEqual("cache", tools.ensure_export()["source"])
            request.layout_json_path.write_text('{"tampered":true}', encoding='utf-8')
            self.assertEqual('BLOCKED', CaseTools(request).ensure_export()['status'])

    def test_cache_without_run_id_is_rejected(self):
        import subprocess
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            request = self.make_request(root)
            inspection = root / 'inspection.json'
            inspection.write_text(json.dumps({'sourceFingerprint': sha256_file(request.psd_path)}), encoding='utf-8')
            request = request.model_copy(update={'psd_inspection_path': inspection})
            request.layout_json_path.parent.mkdir(parents=True)
            request.layout_json_path.write_text('{}', encoding='utf-8')
            (request.layout_json_path.parent / 'photoshop_result.json').write_text(json.dumps({
                'status': 'PASS', 'psdSha256': sha256_file(request.psd_path),
            }), encoding='utf-8')
            with patch('agent_roles.pipeline_agents.subprocess.run', return_value=subprocess.CompletedProcess([], 1, '', 'unavailable')) as run:
                self.assertEqual('BLOCKED', CaseTools(request).ensure_inspection()['status'])
                run.assert_called_once()
            self.assertEqual('BLOCKED', CaseTools(request).ensure_export()['status'])


if __name__ == "__main__":
    unittest.main()
