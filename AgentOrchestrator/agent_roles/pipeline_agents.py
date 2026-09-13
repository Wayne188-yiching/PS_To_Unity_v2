from __future__ import annotations

import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

from agents import Agent, function_tool

from ps_to_unity_agents.evidence import prepare_case, validate_psd_package_manifest
from ps_to_unity_agents.fingerprints import sha256_file
from ps_to_unity_agents.models import AgentDecision, Issue, PipelineDecision, PipelineRequest, PsdAgentDecision, Status
from ps_to_unity_agents.psd_analysis import analyze_psd_hierarchy, analyze_psd_structure
from ps_to_unity_agents.shared_assets import match_shared_assets
from ps_to_unity_agents.tool_failures import classify_tool_failure


MODEL = "gpt-5.6-terra"
APP_ROOT = Path(__file__).resolve().parents[1]
WORKSPACE_ROOT = APP_ROOT.parent
LAYOUT_KNOWLEDGE_PATH = APP_ROOT / "data" / "unity_layout_learning.json"
LAYOUT_KNOWLEDGE_EXAMPLE_PATH = APP_ROOT / "data" / "unity_layout_learning.example.json"
PSD_STRUCTURE_KNOWLEDGE_PATH = APP_ROOT / "data" / "psd_structure_learning.json"
PSD_STRUCTURE_KNOWLEDGE_EXAMPLE_PATH = APP_ROOT / "data" / "psd_structure_learning.example.json"
PSD_CASE_KNOWLEDGE_ROOT = APP_ROOT / "data" / "psd_case_learning"


def load_unity_layout_knowledge() -> dict:
    path = LAYOUT_KNOWLEDGE_PATH if LAYOUT_KNOWLEDGE_PATH.is_file() else LAYOUT_KNOWLEDGE_EXAMPLE_PATH
    return json.loads(path.read_text(encoding="utf-8"))


def load_psd_structure_knowledge() -> dict:
    path = PSD_STRUCTURE_KNOWLEDGE_PATH if PSD_STRUCTURE_KNOWLEDGE_PATH.is_file() else PSD_STRUCTURE_KNOWLEDGE_EXAMPLE_PATH
    return json.loads(path.read_text(encoding="utf-8"))


def load_psd_case_knowledge(case_id: str) -> dict:
    safe_case_id = re.sub(r"[^a-z0-9_-]", "", case_id.casefold())
    path = PSD_CASE_KNOWLEDGE_ROOT / f"{safe_case_id}.json"
    if not safe_case_id or not path.is_file():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


@dataclass
class CaseTools:
    request: PipelineRequest
    prepared: dict | None = None
    psd_evidence: dict | None = None
    planning_evidence: dict | None = None

    def inspect(self) -> dict:
        if self.prepared is None:
            self.prepared = prepare_case(self.request)
        return self.prepared

    def inspection_path(self) -> Path:
        return self.request.psd_inspection_path or self.request.output_folder / "psd_inspection.json"

    def _export_receipt_matches(self, receipt: dict, source_fingerprint: str) -> bool:
        layout = self.request.layout_json_path
        if not (receipt.get("runId") and receipt.get("status") == "PASS"
                and receipt.get("psdSha256") == source_fingerprint and layout.is_file()):
            return False
        if receipt.get("layoutSha256") != sha256_file(layout):
            return False
        image_hashes = {path.name: sha256_file(path) for path in (layout.parent / "Images").glob("*.png")}
        return receipt.get("imageSha256") == image_hashes

    def ensure_inspection(self) -> dict:
        output = self.inspection_path()
        source_fingerprint = sha256_file(self.request.psd_path)
        if output.is_file():
            try:
                cached = json.loads(output.read_text(encoding="utf-8-sig"))
                if cached.get("runId") and cached.get("sourceFingerprint") == source_fingerprint:
                    return {"status": "PASS", "source": "cache", "sourceFingerprint": source_fingerprint}
            except (json.JSONDecodeError, UnicodeError):
                pass
        script = WORKSPACE_ROOT / "Tools" / "Invoke-PhotoshopPsdInspect.ps1"
        try:
            completed = subprocess.run(
                [
                    "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                    "-PsdPath", str(self.request.psd_path), "-OutputFile", str(output),
                ],
                cwd=WORKSPACE_ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=180,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            return {"status": "FAIL_RETRYABLE", "source": "photoshop", "error": f"Inspector timed out: {error}"}
        if completed.returncode != 0:
            message = (completed.stderr or completed.stdout).strip()
            return {"status": classify_tool_failure(message), "source": "photoshop", "error": message}
        try:
            refreshed = json.loads(output.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError, UnicodeError) as error:
            return {"status": "BLOCKED", "source": "photoshop", "error": f"Inspector output is invalid: {error}"}
        if not refreshed.get("runId") or refreshed.get("sourceFingerprint") != source_fingerprint:
            return {"status": "BLOCKED", "source": "photoshop", "error": "Inspector output does not match the source PSD fingerprint."}
        return {"status": "PASS", "source": "photoshop", "sourceFingerprint": source_fingerprint}

    def ensure_export(self) -> dict:
        layout = self.request.layout_json_path
        result_path = layout.parent / "photoshop_result.json"
        source_fingerprint = sha256_file(self.request.psd_path)
        if layout.is_file() and result_path.is_file():
            try:
                cached = json.loads(result_path.read_text(encoding="utf-8-sig"))
                if self._export_receipt_matches(cached, source_fingerprint):
                    return {"status": "PASS", "source": "cache", "sourceFingerprint": source_fingerprint}
            except (json.JSONDecodeError, UnicodeError):
                pass
        if self.request.execution_mode != "execute":
            return {"status": "BLOCKED", "source": "missing_or_stale", "error": "Photoshop export is missing or older than the PSD."}
        script = WORKSPACE_ROOT / "Tools" / "Invoke-PhotoshopUiExport.ps1"
        try:
            completed = subprocess.run(
                [
                    "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script),
                    "-PsdPath", str(self.request.psd_path), "-OutputFolder", str(layout.parent),
                ],
                cwd=WORKSPACE_ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=900,
                check=False,
            )
        except subprocess.TimeoutExpired as error:
            return {"status": "FAIL_RETRYABLE", "source": "photoshop", "error": f"Exporter timed out: {error}"}
        if completed.returncode != 0:
            message = (completed.stderr or completed.stdout).strip()
            return {"status": classify_tool_failure(message), "source": "photoshop", "error": message}
        try:
            exported = json.loads(result_path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError, UnicodeError) as error:
            return {"status": "BLOCKED", "source": "photoshop", "error": f"Exporter result is invalid: {error}"}
        if not self._export_receipt_matches(exported, source_fingerprint):
            return {"status": "BLOCKED", "source": "photoshop", "error": "Exporter result does not match the source PSD fingerprint."}
        return {"status": "PASS", "source": "photoshop", "sourceFingerprint": source_fingerprint}

    def inspect_psd_for_planning(self) -> dict:
        if self.planning_evidence is not None:
            return self.planning_evidence
        inspection = self.ensure_inspection()
        if inspection["status"] != "PASS":
            return {
                "status": inspection["status"] if inspection["status"] == "FAIL_RETRYABLE" else "BLOCKED",
                "summary": "PSD hierarchy evidence could not be refreshed.",
                "inspection": inspection,
                "issues": [{
                    "code": "PSD_INSPECTION_BLOCKED",
                    "owner": "PSD",
                    "severity": "error",
                    "message": inspection.get("error") or "Unknown Photoshop inspector failure.",
                }],
                "evidence": [],
                "nextAction": "Repair the Photoshop inspector before planning structure or names.",
            }
        case_knowledge = load_psd_case_knowledge(self.request.case_id)
        structure = analyze_psd_hierarchy(self.inspection_path(), case_knowledge)
        shared_assets = (
            match_shared_assets(self.inspection_path(), self.request.shared_asset_folders)
            if self.request.shared_asset_folders else None
        )
        self.planning_evidence = {
            "status": structure["status"],
            "summary": structure["summary"],
            "inspectionSource": inspection["source"],
            "structure": structure,
            "structurePolicy": load_psd_structure_knowledge(),
            "caseKnowledge": case_knowledge,
            "confirmedSemantics": self.request.confirmed_semantics,
            "sharedAssets": shared_assets,
            "issues": structure.get("issues") or [],
            "evidence": structure.get("evidence") or [],
            "nextAction": "Produce an unapproved structure and rename plan using stable visible layer IDs.",
        }
        output = self.request.output_folder / "psd_planning_evidence.json"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(self.planning_evidence, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
        return self.planning_evidence

    def inspect_psd(self) -> dict:
        if self.psd_evidence is not None:
            return self.psd_evidence
        inspection = self.ensure_inspection()
        export = self.ensure_export()
        if inspection["status"] != "PASS" or export["status"] != "PASS":
            tool_statuses = {inspection["status"], export["status"]}
            self.psd_evidence = {
                "status": "FAIL_RETRYABLE" if "FAIL_RETRYABLE" in tool_statuses else "BLOCKED",
                "summary": "PSD deterministic evidence could not be refreshed.",
                "inspection": inspection,
                "export": export,
                "issues": [{"code": "PSD_TOOL_BLOCKED", "owner": "PSD", "severity": "error", "message": inspection.get("error") or export.get("error") or "Unknown tool failure."}],
                "evidence": [],
                "nextAction": "Repair or approve the Photoshop deterministic tool step.",
            }
            return self.psd_evidence
        case_knowledge = load_psd_case_knowledge(self.request.case_id)
        structure = analyze_psd_structure(self.inspection_path(), self.request.layout_json_path, case_knowledge)
        package = self.inspect()
        statuses = {structure["status"], package["status"]}
        status = "BLOCKED" if "BLOCKED" in statuses else "NEEDS_REVIEW" if "NEEDS_REVIEW" in statuses else "PASS"
        self.psd_evidence = {
            "status": status,
            "summary": f"PSD structure: {structure['status']}; export package: {package['status']}.",
            "inspectionSource": inspection["source"],
            "exportSource": export["source"],
            "structure": structure,
            "structurePolicy": load_psd_structure_knowledge(),
            "caseKnowledge": case_knowledge,
            "confirmedSemantics": self.request.confirmed_semantics,
            "package": package,
            "issues": list(structure.get("issues") or []) + list(package.get("issues") or []),
            "evidence": list(structure.get("evidence") or []) + [f"Semantic manifest contains {package.get('imageCount', 0)} image entries."],
            "nextAction": "Human approval is required for unresolved render intent." if status == "NEEDS_REVIEW" else structure.get("nextAction"),
        }
        output = self.request.output_folder / "psd_agent_evidence.json"
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(self.psd_evidence, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
        return self.psd_evidence


def _model_safe_summary(payload):
    """Recursively remove local paths before structured evidence reaches the model."""
    hidden_keys = {"manifestPath", "layoutPath", "assetFolder", "inspectionPath", "runFolder"}
    if isinstance(payload, dict):
        return {
            key: _model_safe_summary(value)
            for key, value in payload.items()
            if key not in hidden_keys
        }
    if isinstance(payload, list):
        return [_model_safe_summary(value) for value in payload]
    if isinstance(payload, Path):
        return payload.name
    return payload


def build_psd_agent(request: PipelineRequest, tools: CaseTools | None = None, *, planning_only: bool = False) -> Agent:
    case_tools = tools or CaseTools(request)

    @function_tool
    def inspect_current_psd_case() -> str:
        """Inspect PSD hierarchy, validate exporter semantics, and build the local evidence package without returning image bytes."""
        evidence = case_tools.inspect_psd_for_planning() if planning_only else case_tools.inspect_psd()
        return json.dumps(_model_safe_summary(evidence), ensure_ascii=False)

    return Agent(
        name="PSD Agent",
        model=MODEL,
        instructions=(
            "Call inspect_current_psd_case exactly once, then judge only from its structured evidence and structurePolicy. First classify "
            "reference layers, the production root, semantic regions, Unity roles, and state families; only then propose names. Layer Auto "
            "Namer is lexical normalization only and must never decide Unity hierarchy or component semantics. Any approved rename changes "
            "the existing layer identified by stable layer ID in place; never duplicate a layer to preserve its Chinese name. Effectively "
            "hidden layers stay unchanged and are skipped by automatic rename and export unless the user explicitly includes them. "
            "A CONFIRMED_EXACT sharedAssets match overrides generic Icon_ or translated artwork naming: use the exact project asset basename, "
            "record it as an existing Unity dependency, and do not request duplicate export. Duplicate basenames or visual-only similarity stay NEEDS_REVIEW. "
            "Localized descendant names inside an explicit [MERGE] group are implementation-private artwork and are not rename candidates; "
            "the [MERGE] container itself still needs a stable export name. Case knowledge contains user-confirmed product semantics and may "
            "override English intuition for that matching case only. An explicit [SCROLL_H] or [SCROLL_V] Photoshop group maps to a Unity "
            "ScrollView whose Viewport and Content children are generated by the deterministic importer; do not require artists to recreate "
            "those generated children in Photoshop. "
            "Understand that Photoshop visual "
            "groups and Unity implementation containers are related but not identical. Hidden safe-area or guide roots are references, not "
            "production roots. Deterministic exporter fallbacks that preserve Photoshop coordinates are evidence, not failures. Distinguish "
            "assetPixelSize, psLayoutTargetSize, expectedUnityRectSize, and spriteBorder. A size mismatch is never an error by itself. "
            "Respect explicit Simple, Sliced, Mask, Scroll, and LayoutGroup semantics. H/V tags authorize a LayoutGroup only when geometry "
            "validation is safe; otherwise preserve Photoshop coordinates. Never invent a 9-slice border, missing tag, layer role, "
            "or asset match. Numbered or overlapping Icon_/State_ siblings are state-family candidates, not automatically simultaneous "
            "runtime nodes. Treat confirmedSemantics as user-confirmed design facts that may resolve matching ambiguities, but not as approval "
            "to apply a structure plan. Proposed or remaining ambiguous intent must return NEEDS_REVIEW. BLOCKED is reserved for missing/stale evidence, invalid "
            "structure, missing assets, or deterministic tool failure. If the PSD needs restructuring, include an unapproved structure_plan "
            "using only stable IDs from visibleLayerIndex. Create parent groups before child groups. List move actions bottom-to-top so Photoshop "
            "stacking order is preserved. A proposal does not require prior approval: when confirmedSemantics makes only part of the restructure "
            "safe, emit a partial unapproved plan for those safe actions and explicitly omit unresolved descendant, viewport, mask, component, "
            "or rename actions. Never include effectively hidden nodes and never set approved=true yourself. Return concise evidence, "
            "assign the responsible owner, and state the next safe action."
        ),
        tools=[inspect_current_psd_case],
        output_type=PsdAgentDecision,
    )


def build_unity_agent(request: PipelineRequest, controller=None) -> Agent:
    from ps_to_unity_agents.unity_controller import UnityAgentController
    controller = controller or UnityAgentController(request)

    @function_tool
    def inspect_unity_prerequisites() -> str:
        """Inspect approval, package and explicit project dependencies without launching Unity."""
        return json.dumps(_model_safe_summary(controller.inspect()), ensure_ascii=False)

    @function_tool
    def generate_unity_prefab() -> str:
        """Run the guarded existing Unity batch importer and verify current-run artifact evidence."""
        return json.dumps(_model_safe_summary(controller.generate()), ensure_ascii=False)

    return Agent(
        name="Unity Agent", model=MODEL,
        instructions=(
            "Inspect prerequisites, then select generation only when generationAllowed is true. Use the existing deterministic "
            "importer; never invent dependencies, font mappings, borders or geometry. Diagnose structured evidence and escalate "
            "missing dependencies as BLOCKED and ambiguous intent as NEEDS_REVIEW. Only a FAIL_RETRYABLE result permits one retry; "
            "never retry a live or locked project, never alter approval or delete locks. A prerequisite PASS is not generation PASS. "
            "Claim generation PASS only from a fresh successful generate_unity_prefab result. This is UNITY_GENERATION_ONLY, "
            "not full pipeline PASS or live acceptance. Preserve warnings and explain the next safe action."
        ), tools=[inspect_unity_prerequisites, generate_unity_prefab], output_type=AgentDecision,
    )


def build_pipeline_validator(request: PipelineRequest, controller=None) -> Agent:
    from ps_to_unity_agents.pipeline_validator import PipelineValidatorController
    controller = controller or PipelineValidatorController(request)

    @function_tool
    def validate_generated_prefab() -> str:
        """Compare the current Unity receipt and Prefab snapshot against the current PSD package IR."""
        return json.dumps(_model_safe_summary(controller.validate()), ensure_ascii=False)

    @function_tool
    def inspect_project_layout_rules() -> str:
        """Return curated project UI layout rules. Holdouts are never generation references."""
        return json.dumps(load_unity_layout_knowledge(), ensure_ascii=False)

    return Agent(
        name="Pipeline Validator",
        model=MODEL,
        instructions=(
            "Validate only PSD semantic intent -> layout IR -> Unity import -> generated Prefab. Never perform external vendor QC. "
            "Call validate_generated_prefab and use project rules only to interpret ambiguity; deterministic mismatches cannot be "
            "softened. Simple, Sliced, Mask, Scroll, Scrollbar, TMP and LayoutGroup have different valid relationships. A structural "
            "PASS is not final Photoshop/Unity visual acceptance: preserve validationScope and liveAcceptance. Unresolved intent or "
            "duplicate identity is NEEDS_REVIEW; missing components, hidden exports, stale evidence or geometry drift is BLOCKED."
        ),
        tools=[validate_generated_prefab, inspect_project_layout_rules],
        output_type=AgentDecision,
    )


def build_director(request: PipelineRequest, unity_controller=None, pipeline_controller=None) -> Agent:
    tools = CaseTools(request)

    from ps_to_unity_agents.pipeline_validator import PipelineValidatorController
    from ps_to_unity_agents.unity_controller import UnityAgentController
    unity_controller = unity_controller or UnityAgentController(request)
    pipeline_controller = pipeline_controller or PipelineValidatorController(request, unity_controller)

    psd_agent = build_psd_agent(request, tools)
    unity_agent = build_unity_agent(request, unity_controller)
    pipeline_validator = build_pipeline_validator(request, pipeline_controller)
    return Agent(
        name="Director Agent",
        model=MODEL,
        instructions=(
            "You own the final workflow result. Call PSD Agent first, Unity Agent second, and Pipeline Validator last. "
            "This request is evidence-first: stop at NEEDS_REVIEW when semantic intent is not approved. "
            "Use at most one retry for a specialist and only for a retryable tool failure. Never turn a proposed border into an approved border. "
            "PASS is allowed only when every required stage passes. Return concise structured evidence and name the responsible role."
        ),
        tools=[
            psd_agent.as_tool(tool_name="inspect_psd_semantics", tool_description="Inspect PSD hierarchy and artist asset intent."),
            unity_agent.as_tool(tool_name="validate_unity_stage", tool_description="Validate Unity dependencies and execution gate."),
            pipeline_validator.as_tool(
                tool_name="run_pipeline_validation_stage",
                tool_description="Compare current PSD/IR evidence with the current-run generated Prefab snapshot.",
            ),
        ],
        output_type=PipelineDecision,
    )


def enforce_pipeline_gate(
    decision: PipelineDecision,
    request: PipelineRequest,
    validation_result=None,
) -> PipelineDecision:
    """A model cannot bypass approval or deterministic pipeline validation evidence."""
    if request.execution_mode != "execute" or not request.semantics_approved:
        enforced_status = Status.NEEDS_REVIEW
        owner = "HUMAN"
        code = "PIPELINE_APPROVAL_REQUIRED"
        message = "Pipeline execution or semantic approval gate is incomplete."
        next_action = "Approve semantic intent and use execute mode before generation."
    elif not validation_result:
        enforced_status = Status.NEEDS_REVIEW
        owner = "PIPELINE_VALIDATOR"
        code = "PIPELINE_VALIDATION_REQUIRED"
        message = "No current deterministic pipeline result backs the Director decision."
        next_action = "Run Pipeline Validator on current-run Unity evidence."
    else:
        enforced_status = Status(validation_result.get("status", "BLOCKED"))
        owner = "PIPELINE_VALIDATOR"
        code = (validation_result.get("issues") or [{}])[0].get("code") or "PIPELINE_VALIDATION_REQUIRED"
        message = "The Director decision is softer than the deterministic Pipeline Validator result."
        next_action = "Resolve and rerun Pipeline Validator."

    rank = {Status.PASS: 0, Status.NEEDS_REVIEW: 1, Status.FAIL_RETRYABLE: 2, Status.BLOCKED: 3}
    if rank[decision.status] >= rank[enforced_status]:
        return decision
    issue_severity = "warning" if enforced_status == Status.NEEDS_REVIEW else "error"
    return decision.model_copy(update={
        "status": enforced_status,
        "responsible_agent": owner,
        "summary": message,
        "issues": [
            *decision.issues,
            Issue(
                code=code,
                owner=owner if owner in {"HUMAN", "PIPELINE_VALIDATOR"} else "PIPELINE_VALIDATOR",
                severity=issue_severity,
                phase="PIPELINE",
                message=message,
                suggested_action=next_action,
                retryable=enforced_status == Status.FAIL_RETRYABLE,
            ),
        ],
        "next_action": next_action,
    })
