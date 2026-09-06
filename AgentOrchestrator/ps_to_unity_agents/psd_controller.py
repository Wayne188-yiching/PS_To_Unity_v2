from __future__ import annotations

import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable

from agent_roles.pipeline_agents import CaseTools
from .models import PipelineRequest, PsdAgentDecision, PsdStructurePlan
from .psd_structure_plan import validate_structure_plan


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

        plan = decision.structure_plan.model_copy(update={"approved": False})
        self._write_json(self.plan_path, plan.model_dump(mode="json"))
        validation = validate_structure_plan(self.plan_path, self.inspection_path)
        self._write_json(self.request.output_folder / "psd_structure_plan_validation.json", validation)
        stage = "PLAN_BLOCKED" if validation["status"] == "BLOCKED" else "AWAITING_PLAN_APPROVAL"
        state = {
            "schemaVersion": "1.0",
            "caseId": self.request.case_id,
            "stage": stage,
            "planApproved": False,
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
            return self._result("BLOCKED", "INSPECTION_BLOCKED", "Could not refresh PSD evidence before approval.", inspection=inspection)
        validation = validate_structure_plan(self.plan_path, self.inspection_path)
        if validation["status"] == "BLOCKED":
            return self._result("BLOCKED", "PLAN_BLOCKED", "The plan no longer matches the PSD.", validation=validation)
        if not self.plan_path.is_file():
            return self._result("BLOCKED", "PLAN_MISSING", "No structure plan exists to approve.")
        plan = PsdStructurePlan.model_validate_json(self.plan_path.read_text(encoding="utf-8-sig"))
        approved = plan.model_copy(update={"approved": True})
        self._write_json(self.plan_path, approved.model_dump(mode="json"))
        validation = validate_structure_plan(self.plan_path, self.inspection_path)
        if not validation.get("readyToApply"):
            return self._result("BLOCKED", "PLAN_BLOCKED", "Approved plan failed the deterministic gate.", validation=validation)
        state = self._load_state()
        state.update({"stage": "PLAN_APPROVED", "planApproved": True})
        self._write_json(self.state_path, state)
        return {"status": "PASS", "stage": "PLAN_APPROVED", "validation": validation}

    def _apply_structure(self) -> dict[str, Any]:
        report = self.request.output_folder / "photoshop_structure_report.json"
        script = Path(__file__).resolve().parents[2] / "Tools" / "Invoke-PhotoshopStructurePlan.ps1"
        completed = self.command_runner(
            [
                "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                "-PsdPath", str(self.request.psd_path),
                "-PlanFile", str(self.plan_path),
                "-InspectionFile", str(self.inspection_path),
                "-ReportFile", str(report),
                "-Mode", "Apply",
            ],
            cwd=Path(__file__).resolve().parents[2],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=900,
            check=False,
        )
        if completed.returncode != 0:
            return {"status": "BLOCKED", "error": (completed.stderr or completed.stdout).strip()}
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
            return {
                "status": "BLOCKED",
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
            current_mtime = self.request.psd_path.stat().st_mtime_ns
            if state.get("psdMtimeNs") == current_mtime:
                return json.loads(self.result_path.read_text(encoding="utf-8-sig"))
            state["stage"] = "STRUCTURE_APPLIED"
            self._write_json(self.state_path, state)

        if state.get("stage") == "PLAN_NOT_REQUIRED":
            state.update({"stage": "STRUCTURE_APPLIED", "structureApplied": False})
            self._write_json(self.state_path, state)
        elif state.get("stage") not in {"PLAN_APPROVED", "STRUCTURE_APPLIED"}:
            approval = self.approve_existing_plan()
            if approval["status"] != "PASS":
                return approval
            state = self._load_state()

        if state.get("stage") != "STRUCTURE_APPLIED":
            applied = self._apply_structure()
            if applied.get("status") != "PASS":
                return self._result("BLOCKED", "STRUCTURE_APPLY_BLOCKED", "Photoshop could not apply the approved plan.", apply=applied)
            state.update({
                "stage": "STRUCTURE_APPLIED",
                "structureApplied": True,
                "applyReport": applied,
                "psdMtimeNs": self.request.psd_path.stat().st_mtime_ns,
            })
            self._write_json(self.state_path, state)

        finalized = self._finalize_package()
        if finalized["status"] == "BLOCKED":
            details = {key: value for key, value in finalized.items() if key != "status"}
            return self._result("BLOCKED", "PACKAGE_EXPORT_BLOCKED", "Structure was applied, but package export or validation failed.", **details)

        state.update({
            "stage": "PACKAGE_READY",
            "packageStatus": finalized["status"],
            "psdMtimeNs": self.request.psd_path.stat().st_mtime_ns,
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
