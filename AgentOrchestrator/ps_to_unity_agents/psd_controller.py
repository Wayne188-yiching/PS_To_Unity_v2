from __future__ import annotations

import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable

from agent_roles.pipeline_agents import CaseTools
from .fingerprints import inspection_fingerprint, plan_fingerprint, sha256_file
from .models import PipelineRequest, PsdAgentDecision, PsdStructurePlan
from .psd_structure_plan import bind_structure_plan_preconditions, load_inspection, validate_structure_plan
from .tool_failures import classify_tool_failure


NON_ASCII = re.compile(r"[^\x00-\x7f]")


def _walk_layout(nodes: Iterable[dict[str, Any]], parent: str = ""):
    for node in nodes or []:
        name = str(node.get("name") or "")
        path = f"{parent}/{name}" if parent else name
        yield node, path
        yield from _walk_layout(node.get("children") or [], path)


def validate_export_package(layout_path: Path, image_folder: Path) -> dict[str, Any]:
    if not layout_path.is_file():
        return {
            "status": "BLOCKED",
            "issues": [{"code": "LAYOUT_JSON_MISSING", "severity": "error", "message": str(layout_path)}],
        }
    try:
        layout = json.loads(layout_path.read_text(encoding="utf-8-sig"))
    except (json.JSONDecodeError, UnicodeError) as error:
        return {
            "status": "BLOCKED",
            "issues": [{"code": "LAYOUT_JSON_INVALID", "severity": "error", "message": str(error)}],
        }

    issues: list[dict[str, Any]] = []
    canvas = layout.get("canvas") or {}
    if float(canvas.get("width") or 0) <= 0 or float(canvas.get("height") or 0) <= 0:
        issues.append({"code": "LAYOUT_CANVAS_INVALID", "severity": "error", "message": "Canvas size must be positive."})

    referenced_images: set[str] = set()
    node_paths: set[str] = set()
    for node, node_path in _walk_layout(layout.get("nodes") or []):
        if node_path in node_paths:
            issues.append({"code": "LAYOUT_NODE_PATH_DUPLICATE", "severity": "error", "message": node_path})
        node_paths.add(node_path)
        if NON_ASCII.search(str(node.get("name") or "")):
            issues.append({"code": "LAYOUT_NODE_NAME_NON_ASCII", "severity": "error", "message": node_path})
        if str(node.get("type") or "").casefold() != "image":
            continue
        image_name = str(node.get("imagePath") or "")
        referenced_images.add(image_name.casefold())
        if not image_name or Path(image_name).name != image_name or NON_ASCII.search(image_name):
            issues.append({"code": "LAYOUT_IMAGE_PATH_INVALID", "severity": "error", "message": f"{node_path}: {image_name}"})
        elif not (image_folder / image_name).is_file():
            issues.append({"code": "LAYOUT_IMAGE_MISSING", "severity": "error", "message": f"{node_path}: {image_name}"})

    exported_images = {
        path.name.casefold()
        for path in image_folder.glob("*.png")
        if path.is_file()
    } if image_folder.is_dir() else set()
    if referenced_images and not image_folder.is_dir():
        issues.append({"code": "IMAGE_FOLDER_MISSING", "severity": "error", "message": str(image_folder)})

    return {
        "status": "BLOCKED" if issues else "PASS",
        "schemaVersion": layout.get("schemaVersion"),
        "layoutNodeCount": len(node_paths),
        "referencedImageCount": len(referenced_images),
        "exportedImageCount": len(exported_images),
        "unreferencedImageCount": len(exported_images - referenced_images),
        "issues": issues,
    }


@dataclass
class PsdAgentController:
    request: PipelineRequest
    command_runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run

    @property
    def plan_path(self) -> Path:
        return self.request.output_folder / "psd_structure_plan.json"

    @property
    def inspection_path(self) -> Path:
        return self.request.psd_inspection_path or self.request.output_folder / "psd_inspection.json"

    @property
    def state_path(self) -> Path:
        return self.request.output_folder / "psd_controller_state.json"

    @property
    def result_path(self) -> Path:
        return self.request.output_folder / "psd_controller_result.json"

    def _write_json(self, path: Path, payload: dict[str, Any]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix(path.suffix + ".tmp")
        temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
        temporary.replace(path)

    def _load_state(self) -> dict[str, Any]:
        if not self.state_path.is_file():
            return {"schemaVersion": "1.0", "caseId": self.request.case_id, "stage": "NEW"}
        return json.loads(self.state_path.read_text(encoding="utf-8-sig"))

    def _current_fingerprints(self, plan: PsdStructurePlan | None = None) -> dict[str, str]:
        fingerprints = {"psdFingerprint": sha256_file(self.request.psd_path)}
        if self.inspection_path.is_file():
            fingerprints["inspectionFingerprint"] = inspection_fingerprint(load_inspection(self.inspection_path))
        if plan is not None:
            fingerprints["planFingerprint"] = plan_fingerprint(plan)
        return fingerprints

    def _validate_approval_binding(self, state: dict[str, Any]) -> dict[str, Any] | None:
        if not self.plan_path.is_file() or not self.inspection_path.is_file():
            return self._result("BLOCKED", "APPROVAL_INVALIDATED", "Approved PSD evidence is missing.")
        try:
            plan = PsdStructurePlan.model_validate_json(self.plan_path.read_text(encoding="utf-8-sig"))
            current = self._current_fingerprints(plan)
        except (ValueError, OSError) as error:
            return self._result("NEEDS_REVIEW", "APPROVAL_INVALIDATED", "Approved evidence is unreadable.", error=str(error))
        mismatched = [key for key in ("planFingerprint", "inspectionFingerprint") if state.get(key) != current.get(key)]
        if state.get("stage") == "PLAN_APPROVED" and state.get("approvedPsdFingerprint") != current["psdFingerprint"]:
            mismatched.append("psdFingerprint")
        if mismatched:
            return self._result(
                "NEEDS_REVIEW",
                "APPROVAL_INVALIDATED",
                "The approved plan or inspection evidence changed after review.",
                changed=mismatched,
                nextAction="Generate and approve a new plan from fresh PSD evidence.",
            )
        return None

    def _result(self, status: str, stage: str, summary: str, **extra: Any) -> dict[str, Any]:
        payload = {"status": status, "stage": stage, "summary": summary, **extra}
        self._write_json(self.result_path, payload)
        return payload

    def record_plan(self, decision: PsdAgentDecision) -> dict[str, Any]:
        decision_path = self.request.output_folder / "psd_agent_result.json"
        self._write_json(decision_path, decision.model_dump(mode="json"))
        if decision.structure_plan is None:
            state = {
                "schemaVersion": "1.0",
                "caseId": self.request.case_id,
                "stage": "PLAN_NOT_REQUIRED" if decision.status.value == "PASS" else "PLANNING_REVIEW_REQUIRED",
            }
            self._write_json(self.state_path, state)
            return self._result(
                decision.status.value,
                state["stage"],
                decision.summary,
                issues=[issue.model_dump(mode="json") for issue in decision.issues],
                nextAction=decision.next_action,
            )

        inspection = load_inspection(self.inspection_path)
        plan = bind_structure_plan_preconditions(decision.structure_plan, inspection)
        self._write_json(self.plan_path, plan.model_dump(mode="json"))
        validation = validate_structure_plan(self.plan_path, self.inspection_path)
        self._write_json(self.request.output_folder / "psd_structure_plan_validation.json", validation)
        stage = "PLAN_BLOCKED" if validation["status"] == "BLOCKED" else "AWAITING_PLAN_APPROVAL"
        state = {
            "schemaVersion": "1.1",
            "caseId": self.request.case_id,
            "stage": stage,
            "planApproved": False,
            **self._current_fingerprints(plan),
        }
        self._write_json(self.state_path, state)
        status = "BLOCKED" if stage == "PLAN_BLOCKED" else "NEEDS_REVIEW"
        return self._result(
            status,
            stage,
            "PSD structure and rename plan is ready for review." if status != "BLOCKED" else "PSD plan validation failed.",
            planPath=str(self.plan_path),
            validation=validation,
            issues=[issue.model_dump(mode="json") for issue in decision.issues],
            nextAction="Review the plan, then rerun with --approve-plan." if status != "BLOCKED" else "Repair the invalid plan.",
        )

    def approve_existing_plan(self) -> dict[str, Any]:
        inspection = CaseTools(self.request).ensure_inspection()
        if inspection["status"] != "PASS":
            status = inspection["status"] if inspection["status"] == "FAIL_RETRYABLE" else "BLOCKED"
            return self._result(
                status,
                "INSPECTION_RETRYABLE" if status == "FAIL_RETRYABLE" else "INSPECTION_BLOCKED",
                "Could not refresh PSD evidence before approval.",
                inspection=inspection,
            )
        state = self._load_state()
        if not self.plan_path.is_file():
            return self._result("BLOCKED", "PLAN_MISSING", "No structure plan exists to approve.")
        plan = PsdStructurePlan.model_validate_json(self.plan_path.read_text(encoding="utf-8-sig"))
        current = self._current_fingerprints(plan)
        changed = [
            key for key in ("psdFingerprint", "inspectionFingerprint", "planFingerprint")
            if state.get(key) != current.get(key)
        ]
        if changed:
            return self._result(
                "NEEDS_REVIEW",
                "APPROVAL_INVALIDATED",
                "PSD, inspection evidence, or plan changed after planning.",
                changed=changed,
                nextAction="Generate a new plan from fresh PSD evidence.",
            )
        validation = validate_structure_plan(self.plan_path, self.inspection_path)
        if validation["status"] == "BLOCKED":
            return self._result("BLOCKED", "PLAN_BLOCKED", "The plan no longer matches the PSD.", validation=validation)
        approved = plan.model_copy(update={"approved": True})
        self._write_json(self.plan_path, approved.model_dump(mode="json"))
        validation = validate_structure_plan(self.plan_path, self.inspection_path)
        if not validation.get("readyToApply"):
            return self._result("BLOCKED", "PLAN_BLOCKED", "Approved plan failed the deterministic gate.", validation=validation)
        state.update({
            "stage": "PLAN_APPROVED",
            "planApproved": True,
            "planFingerprint": plan_fingerprint(approved),
            "approvedPsdFingerprint": current["psdFingerprint"],
        })
        self._write_json(self.state_path, state)
        self._write_json(self.request.output_folder / "psd_structure_plan_validation.json", validation)
        return self._result("PASS", "PLAN_APPROVED", "The reviewed plan is approved for application.", validation=validation)

    def _apply_structure(self) -> dict[str, Any]:
        report = self.request.output_folder / "photoshop_structure_report.json"
        script = Path(__file__).resolve().parents[2] / "Tools" / "Invoke-PhotoshopStructurePlan.ps1"
        try:
            completed = self.command_runner(
                [
                    "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                    "-PsdPath", str(self.request.psd_path),
                    "-PlanFile", str(self.plan_path),
                    "-InspectionFile", str(self.inspection_path),
                    "-ReportFile", str(report),
                    "-Mode", "Apply",
                    "-PlanFingerprint", plan_fingerprint(
                        PsdStructurePlan.model_validate_json(self.plan_path.read_text(encoding="utf-8-sig"))
                    ),
                ],
                cwd=Path(__file__).resolve().parents[2],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=900,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            return {"status": "FAIL_RETRYABLE", "error": f"Photoshop structure apply timed out: {error}"}
        if completed.returncode != 0:
            message = (completed.stderr or completed.stdout).strip()
            return {"status": classify_tool_failure(message), "error": message}
        try:
            return json.loads(completed.stdout.strip().splitlines()[-1])
        except (json.JSONDecodeError, IndexError) as error:
            return {"status": "BLOCKED", "error": f"Structure tool returned invalid output: {error}"}

    def _finalize_package(self) -> dict[str, Any]:
        execution_request = self.request.model_copy(update={
            "execution_mode": "execute",
            "exporter_asset_folder": self.request.layout_json_path.parent / "Images",
        })
        tools = CaseTools(execution_request)
        inspection = tools.ensure_inspection()
        export = tools.ensure_export()
        if inspection["status"] != "PASS" or export["status"] != "PASS":
            tool_statuses = {inspection["status"], export["status"]}
            return {
                "status": "FAIL_RETRYABLE" if "FAIL_RETRYABLE" in tool_statuses else "BLOCKED",
                "inspection": inspection,
                "export": export,
                "issues": [{
                    "code": "PSD_FINALIZE_TOOL_BLOCKED",
                    "severity": "error",
                    "message": inspection.get("error") or export.get("error") or "Unknown Photoshop tool failure.",
                }],
            }
        package_validation = validate_export_package(
            execution_request.layout_json_path,
            execution_request.layout_json_path.parent / "Images",
        )
        if package_validation["status"] == "BLOCKED":
            return {"status": "BLOCKED", "packageValidation": package_validation, "issues": package_validation["issues"]}
        evidence = tools.inspect_psd()
        return {
            "status": evidence["status"],
            "packageValidation": package_validation,
            "evidence": evidence,
            "issues": evidence.get("issues") or [],
        }

    def approve_apply_and_export(self) -> dict[str, Any]:
        state = self._load_state()
        if state.get("stage") == "PACKAGE_READY" and self.result_path.is_file():
            # PSD bytes alone cannot prove exported layout/assets remain intact.
            # Always revalidate the package; the approved structure stays applied.
            state["stage"] = "STRUCTURE_APPLIED"
            self._write_json(self.state_path, state)

        if state.get("stage") == "PLAN_NOT_REQUIRED":
            state.update({"stage": "STRUCTURE_APPLIED", "structureApplied": False})
            self._write_json(self.state_path, state)
        elif state.get("stage") not in {"PLAN_APPROVED", "APPLYING", "STRUCTURE_APPLIED"}:
            approval = self.approve_existing_plan()
            if approval["status"] != "PASS":
                return approval
            state = self._load_state()

        if state.get("stage") in {"PLAN_APPROVED", "APPLYING"}:
            invalidated = self._validate_approval_binding(state)
            if invalidated is not None:
                return invalidated
            state.update({"stage": "APPLYING", "applyAttempted": True})
            self._write_json(self.state_path, state)
            applied = self._apply_structure()
            if applied.get("status") != "PASS":
                status = applied.get("status") if applied.get("status") == "FAIL_RETRYABLE" else "BLOCKED"
                stage = "APPLYING" if status == "FAIL_RETRYABLE" else "STRUCTURE_APPLY_BLOCKED"
                return self._result(
                    status,
                    stage,
                    "Photoshop temporarily could not apply the approved plan; the APPLYING checkpoint is safe to retry."
                    if status == "FAIL_RETRYABLE" else "Photoshop could not apply the approved plan.",
                    apply=applied,
                    nextAction="Retry the same approved request." if status == "FAIL_RETRYABLE" else "Review the Photoshop error.",
                )
            if applied.get("planFingerprint") != state.get("planFingerprint"):
                return self._result(
                    "BLOCKED",
                    "STRUCTURE_APPLY_EVIDENCE_MISMATCH",
                    "Photoshop apply report does not match the approved plan.",
                    apply=applied,
                )
            state.update({
                "stage": "STRUCTURE_APPLIED",
                "structureApplied": True,
                "applyReport": applied,
                "psdFingerprint": sha256_file(self.request.psd_path),
            })
            self._write_json(self.state_path, state)

        finalized = self._finalize_package()
        if finalized["status"] in {"BLOCKED", "FAIL_RETRYABLE"}:
            details = {key: value for key, value in finalized.items() if key != "status"}
            retryable = finalized["status"] == "FAIL_RETRYABLE"
            return self._result(
                finalized["status"],
                "PACKAGE_EXPORT_RETRYABLE" if retryable else "PACKAGE_EXPORT_BLOCKED",
                "Structure was applied; Photoshop package export can be retried safely."
                if retryable else "Structure was applied, but package export or validation failed.",
                **details,
            )

        state.update({
            "stage": "PACKAGE_READY",
            "packageStatus": finalized["status"],
            "psdFingerprint": sha256_file(self.request.psd_path),
        })
        self._write_json(self.state_path, state)
        details = {key: value for key, value in finalized.items() if key != "status"}
        return self._result(
            finalized["status"],
            "PACKAGE_READY",
            "PSD structure, names, Images, and layout.json completed deterministic validation.",
            **details,
            nextAction=(
                "Hand the package to Unity Agent."
                if finalized["status"] == "PASS"
                else "Review unresolved render semantics before Unity generation."
            ),
        )
