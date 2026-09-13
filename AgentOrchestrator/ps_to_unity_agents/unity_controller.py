from __future__ import annotations

import json
import os
import subprocess
import uuid
from pathlib import Path

from .evidence import prepare_case, validate_psd_package_manifest
from .fingerprints import canonical_json_sha256, sha256_file
from .models import AgentDecision, Issue, PipelineRequest, Status


class UnityAgentController:
    """Bounded launcher for the existing importer, not a replacement importer."""

    def __init__(self, request: PipelineRequest, *, process_factory=None):
        self.request = request
        self.process_factory = process_factory or subprocess.Popen
        self.last_result = None
        self.attempts = 0
        self.pending = None

    def _result(self, status, code, message, **evidence):
        result = {
            "status": status, "summary": message, "generationAllowed": False,
            "validationScope": "UNITY_GENERATION_ONLY", "liveAcceptance": "NOT_RUN",
            "issues": [] if status == "PASS" else [{
                "code": code, "owner": "UNITY", "severity": "warning" if status == "NEEDS_REVIEW" else "error",
                "message": message, "retryable": status == "FAIL_RETRYABLE",
            }], **evidence,
        }
        return result

    def _asset_path(self, value):
        path = Path(value.replace("\\", "/"))
        project = self.request.unity_project_path.resolve()
        if path.is_absolute() or not path.parts or path.parts[0] != "Assets" or ".." in path.parts:
            raise ValueError(f"Expected a project-relative Assets path: {value}")
        resolved = (project / path).resolve()
        if not resolved.is_relative_to(project / "Assets"):
            raise ValueError("Asset path escapes the project's Assets folder.")
        return resolved

    def inspect(self):
        request = self.request
        if request.execution_mode != "execute" or not request.semantics_approved:
            return self._result("NEEDS_REVIEW", "UNITY_APPROVAL_REQUIRED", "Execute mode and human semantic approval are required.")
        if not request.unity_executable or not request.unity_executable.is_file():
            return self._result("BLOCKED", "UNITY_EXECUTABLE_MISSING", "Provide an installed Unity executable.")
        project = request.unity_project_path
        if not project or not (project / "Assets").is_dir() or not (project / "ProjectSettings/ProjectVersion.txt").is_file():
            return self._result("BLOCKED", "UNITY_PROJECT_MISSING", "Provide an existing Unity project.")
        if (project / "Temp/UnityLockfile").exists() or (project / ".ps_to_unity_agent.lock").exists():
            return self._result("NEEDS_REVIEW", "UNITY_PROJECT_BUSY", "Project lock exists (not proof of a live process). Check Editor/process ownership and local run logs; only manually remove a stale controller lock after confirming no owner is running. Do not launch another Editor.")
        if not (project / "Assets/Editor/PhotoshopUiImporter/PhotoshopUiBatchEntryPoint.cs").is_file():
            return self._result("BLOCKED", "UNITY_IMPORTER_MISSING", "Install the existing Photoshop UI importer in the target project.")
        try:
            self._asset_path(request.unity_import_folder)
            self._asset_path(request.unity_prefab_folder)
            if not request.unity_project_folder or any(char in request.unity_project_folder for char in '/\\:') or request.unity_project_folder in {".", ".."}:
                raise ValueError("unity_project_folder must be a single folder name.")
            name = request.unity_prefab_name
            if name and (any(char in name for char in '/\\:*?"<>|') or name in {".", ".."}):
                raise ValueError("unity_prefab_name must be a filename, not a path.")
            for value in (request.unity_default_tmp_font_asset, request.unity_default_tmp_material_preset,
                          request.unity_tmp_font_map, request.unity_skin_map, request.unity_material_library_folder):
                if value and not self._asset_path(value).exists():
                    return self._result("BLOCKED", "UNITY_DEPENDENCY_MISSING", f"Declared dependency is missing: {value}")
            package = prepare_case(request)  # Rebuild; never trust an old manifest PASS.
            validation = validate_psd_package_manifest(request)
            if package["status"] != "PASS" or validation["status"] != "PASS":
                status = "BLOCKED" if "BLOCKED" in {package["status"], validation["status"]} else "NEEDS_REVIEW"
                return self._result(status, "UNITY_PACKAGE_GATE", "PSD package is not ready for generation.", package=package, validation=validation)
            layout = Path(package["layoutPath"])
            payload = json.loads(layout.read_text(encoding="utf-8-sig"))
            def has_text(nodes):
                return any(str(node.get("type", "")).lower() == "text" or has_text(node.get("children") or []) for node in nodes)
            if has_text(payload.get("nodes") or []) and not request.unity_default_tmp_font_asset:
                return self._result("BLOCKED", "TMP_DEFAULT_FONT_REQUIRED", "Text nodes require an explicit default TMP font asset.")
            return self._result("PASS", "", "Generation prerequisites passed; no Prefab generated yet.", generationAllowed=True, package=package)
        except (OSError, ValueError, TypeError, KeyError) as error:
            return self._result("BLOCKED", "UNITY_INPUT_INVALID", str(error))

    def _payload(self, package, run_id):
        r = self.request
        return {
            "runId": run_id, "layoutJsonPath": str(Path(package["layoutPath"]).resolve()),
            "sourceImageFolder": str(Path(package["assetFolder"]).resolve()),
            "importFolder": r.unity_import_folder, "prefabFolder": r.unity_prefab_folder,
            "prefabName": r.unity_prefab_name or "layout", "projectFolder": r.unity_project_folder,
            "defaultTmpFontAssetPath": r.unity_default_tmp_font_asset or "",
            "defaultTmpMaterialPresetPath": r.unity_default_tmp_material_preset or "",
            "tmpFontMapPath": r.unity_tmp_font_map or "", "skinMapPath": r.unity_skin_map or "",
            "materialLibraryFolder": r.unity_material_library_folder or "",
            "referenceResolutionX": r.unity_reference_resolution_x, "referenceResolutionY": r.unity_reference_resolution_y,
            "outlineThicknessMultiplier": r.unity_outline_thickness_multiplier,
            "useResponsiveAnchor": r.unity_use_responsive_anchor, "createSpriteAtlases": r.unity_create_sprite_atlases,
        }

    def generate(self):
        if self.pending:
            process, payload, layout_hash, inputs, run_folder = self.pending
            if process.poll() is None:
                return self._result("FAIL_RETRYABLE", "UNITY_STILL_RUNNING", "Previous Unity process is still running; poll again, never restart.", runId=payload["runId"])
            try:
                result = self._read_result(process.poll(), payload, layout_hash, inputs, run_folder)
            except (OSError, ValueError, TypeError) as error:
                result = self._result("BLOCKED", "UNITY_EXECUTION_EVIDENCE_INVALID", str(error))
            self.pending = None
            (self.request.unity_project_path / ".ps_to_unity_agent.lock").unlink(missing_ok=True)
            return self._save(result, payload["runId"], run_folder)
        if self.last_result and (self.last_result["status"] != "FAIL_RETRYABLE" or self.attempts >= 2):
            return self.last_result
        gate = self.inspect()
        if not gate["generationAllowed"]:
            self.last_result = gate
            return gate
        project = self.request.unity_project_path.resolve()
        lock = project / ".ps_to_unity_agent.lock"
        try:
            descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except FileExistsError:
            return self._result("NEEDS_REVIEW", "UNITY_PROJECT_BUSY", "Another controller owns this project.")
        os.close(descriptor)
        process = None
        run_id = uuid.uuid4().hex
        run_folder = self.request.output_folder / "unity_runs" / run_id
        try:
            run_folder.mkdir(parents=True)
            payload = self._payload(gate["package"], run_id)
            layout_hash = sha256_file(Path(payload["layoutJsonPath"]))
            inputs = self._input_hashes(payload)
            payload["requestFingerprint"] = canonical_json_sha256({"request": payload, "layoutSha256": layout_hash, "inputs": inputs})
            request_path, result_path = run_folder / "request.json", run_folder / "result.json"
            request_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
            command = [str(self.request.unity_executable.resolve()), "-batchmode", "-projectPath", str(project),
                       "-executeMethod", "PhotoshopToUnity.EditorImporter.PhotoshopUiBatchEntryPoint.Run",
                       "-psToUnityRequest", str(request_path.resolve()), "-psToUnityResult", str(result_path.resolve()),
                       "-logFile", str((run_folder / "editor.log").resolve())]
            self.attempts += 1
            with (run_folder / "stdout.log").open("w", encoding="utf-8") as stdout, (run_folder / "stderr.log").open("w", encoding="utf-8") as stderr:
                process = self.process_factory(command, cwd=project, stdout=stdout, stderr=stderr,
                                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
                lock.write_text(json.dumps({"runId": run_id, "pid": process.pid}), encoding="utf-8")
                return_code = process.wait(timeout=self.request.unity_timeout_seconds)
            result = self._read_result(return_code, payload, layout_hash, inputs, run_folder)
        except subprocess.TimeoutExpired:
            self.pending = (process, payload, layout_hash, inputs, run_folder)
            result = self._result("FAIL_RETRYABLE", "UNITY_TIMEOUT", "Unity timed out. A live process retains its project lock; do not restart it.")
        except (OSError, ValueError, TypeError) as error:
            result = self._result("BLOCKED", "UNITY_EXECUTION_EVIDENCE_INVALID", str(error))
        finally:
            if process is None or process.poll() is not None:
                lock.unlink(missing_ok=True)
        return self._save(result, run_id, run_folder)

    def _input_hashes(self, payload):
        folder = Path(payload["sourceImageFolder"])
        inputs = {str(path.resolve()): sha256_file(path) for path in folder.rglob("*") if path.is_file()}
        for key in ("defaultTmpFontAssetPath", "defaultTmpMaterialPresetPath", "tmpFontMapPath", "skinMapPath",
                    "materialLibraryFolder"):
            if payload[key]:
                path = self._asset_path(payload[key])
                candidates = path.rglob("*") if path.is_dir() else (path,)
                for candidate in candidates:
                    if candidate.is_file():
                        inputs[str(candidate.resolve())] = sha256_file(candidate)
                if path.is_file():
                    meta = Path(str(path) + ".meta")
                    if meta.is_file():
                        inputs[str(meta.resolve())] = sha256_file(meta)
        return inputs

    def _read_result(self, return_code, payload, layout_hash, inputs, run_folder):
        result_path = run_folder / "result.json"
        if return_code != 0 and not result_path.is_file():
            log = "\n".join(path.read_text(encoding="utf-8", errors="replace")[-32000:] for path in run_folder.glob("*.log")).lower()
            permanent = any(token in log for token in ("error cs", "compilation failed", "license", "licensing", "execute method", "could not load", "not found"))
            return self._result("BLOCKED" if permanent else "FAIL_RETRYABLE", "UNITY_PROCESS_FAILED", f"Unity exited with code {return_code}; inspect local logs before retry.")
        receipt = json.loads(result_path.read_text(encoding="utf-8-sig"))
        if not isinstance(receipt, dict):
            raise ValueError("Unity result must be an object.")
        if any(receipt.get(key) != payload[key] for key in ("runId", "requestFingerprint")) or receipt.get("layoutSha256") != layout_hash or sha256_file(Path(payload["layoutJsonPath"])) != layout_hash or self._input_hashes(payload) != inputs:
            return self._result("BLOCKED", "UNITY_STALE_RESULT", "Unity receipt or current inputs do not match this run, request, layout, and dependency snapshot.")
        if receipt.get("status") != "PASS" or receipt.get("errors"):
            status = receipt.get("status") if receipt.get("status") in {"BLOCKED", "NEEDS_REVIEW", "FAIL_RETRYABLE"} else "BLOCKED"
            return self._result(status, "UNITY_IMPORT_FAILED", "Importer did not pass.", importResult=receipt)
        if return_code != 0:
            return self._result("FAIL_RETRYABLE", "UNITY_PROCESS_FAILED", f"Unity exited with code {return_code} despite a PASS receipt.")
        prefab = self._asset_path(receipt.get("prefabAssetPath") or "")
        expected = self._asset_path(payload["prefabFolder"]) / (payload["prefabName"] + ".prefab")
        if prefab != expected or not prefab.is_file() or not prefab.stat().st_size or receipt.get("prefabSha256") != sha256_file(prefab):
            return self._result("BLOCKED", "UNITY_PREFAB_EVIDENCE_INVALID", "Generated Prefab path or content evidence is missing/mismatched.")
        diagnostics = receipt.get("diagnostics") or []
        if any(item.get("severity", "").lower() == "error" for item in diagnostics):
            return self._result("BLOCKED", "UNITY_DIAGNOSTIC_ERROR", "Importer reported an error despite PASS.", importResult=receipt)
        warnings = " ".join(receipt.get("warnings") or []) + " " + " ".join(item.get("code", "") for item in diagnostics)
        if any(token in warnings.upper() for token in (
            "UNRESOLVED", "DEPENDENCY_NOT_FOUND", "FONT_TOKEN_UNMAPPED",
            "TMP_FONT_TOKEN", "FONT_FALLBACK", "MISSING_FONT",
        )):
            return self._result("NEEDS_REVIEW", "UNITY_SEMANTIC_REVIEW", "Prefab exists, but unresolved semantic/font diagnostics need review.", importResult=receipt)
        return self._result("PASS", "", "Unity generation passed basic artifact checks; full pipeline validation remains unimplemented.", importResult=receipt)

    def _save(self, result, run_id, run_folder):
        result.update(runId=run_id, runFolder=str(run_folder), attempts=self.attempts)
        self.last_result = result
        (run_folder / "controller_result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        return result

    def enforce_decision(self, decision: AgentDecision) -> AgentDecision:
        result = self.last_result
        if result is None:
            if decision.status != Status.PASS:
                return decision
            enforced_status = Status.NEEDS_REVIEW
        else:
            deterministic_status = Status(result["status"])
            severity = {
                Status.PASS: 0,
                Status.NEEDS_REVIEW: 1,
                Status.FAIL_RETRYABLE: 2,
                Status.BLOCKED: 3,
            }
            if severity[decision.status] >= severity[deterministic_status]:
                return decision
            enforced_status = deterministic_status
        return decision.model_copy(update={
            "status": enforced_status,
            "summary": "Model status is not backed by the deterministic Unity controller evidence.",
            "issues": [
                *decision.issues,
                Issue(
                    code="UNITY_GENERATION_UNVERIFIED",
                    owner="UNITY",
                    severity="warning" if enforced_status == Status.NEEDS_REVIEW else "error",
                    message="Run or resolve the guarded deterministic Unity controller before changing this status.",
                    retryable=enforced_status == Status.FAIL_RETRYABLE,
                ),
            ],
            "next_action": "Resolve the deterministic Unity controller gate.",
        })
