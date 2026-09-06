from __future__ import annotations

import json
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

from agents import Agent, function_tool

from ps_to_unity_agents.evidence import prepare_case, qa_manifest
from ps_to_unity_agents.models import AgentDecision, PipelineDecision, PipelineRequest, PsdAgentDecision
from ps_to_unity_agents.psd_analysis import analyze_psd_hierarchy, analyze_psd_structure
from ps_to_unity_agents.shared_assets import match_shared_assets


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

    def ensure_inspection(self) -> dict:
        output = self.inspection_path()
        if output.is_file() and output.stat().st_mtime >= self.request.psd_path.stat().st_mtime:
            return {"status": "PASS", "source": "cache"}
        script = WORKSPACE_ROOT / "Tools" / "Invoke-PhotoshopPsdInspect.ps1"
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
        if completed.returncode != 0:
            return {"status": "BLOCKED", "source": "photoshop", "error": (completed.stderr or completed.stdout).strip()}
        return {"status": "PASS", "source": "photoshop"}

    def ensure_export(self) -> dict:
        layout = self.request.layout_json_path
        if layout.is_file() and layout.stat().st_mtime >= self.request.psd_path.stat().st_mtime:
            return {"status": "PASS", "source": "cache"}
        if self.request.execution_mode != "execute":
            return {"status": "BLOCKED", "source": "missing_or_stale", "error": "Photoshop export is missing or older than the PSD."}
        script = WORKSPACE_ROOT / "Tools" / "Invoke-PhotoshopUiExport.ps1"
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
        if completed.returncode != 0:
            return {"status": "BLOCKED", "source": "photoshop", "error": (completed.stderr or completed.stdout).strip()}
        return {"status": "PASS", "source": "photoshop"}

    def inspect_psd_for_planning(self) -> dict:
        if self.planning_evidence is not None:
            return self.planning_evidence
        inspection = self.ensure_inspection()
        if inspection["status"] != "PASS":
            return {
                "status": "BLOCKED",
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
            self.psd_evidence = {
                "status": "BLOCKED",
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
    hidden_keys = {"manifestPath", "layoutPath", "assetFolder", "inspectionPath"}
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


def build_director(request: PipelineRequest) -> Agent:
    tools = CaseTools(request)

    @function_tool
    def validate_current_unity_gate() -> str:
        """Check whether Unity generation is allowed for this request. It does not launch Unity."""
        evidence = tools.inspect()
        allowed = (
            request.execution_mode == "execute"
            and request.semantics_approved
            and evidence["status"] == "PASS"
            and request.unity_executable is not None
            and request.unity_project_path is not None
        )
        return json.dumps({
            "status": "PASS" if allowed else "NEEDS_REVIEW",
            "generationAllowed": allowed,
            "executionMode": request.execution_mode,
            "semanticsApproved": request.semantics_approved,
            "semanticStatus": evidence["status"],
            "reason": "Generation remains gated until semantic evidence is approved." if not allowed else "Unity generation gate passed.",
        }, ensure_ascii=False)

    @function_tool
    def run_current_qa() -> str:
        """Run local semantic/structural QA. Size mismatches are evaluated by render semantics and never fail by themselves."""
        tools.inspect()
        return json.dumps(_model_safe_summary(qa_manifest(request)), ensure_ascii=False)

    @function_tool
    def inspect_project_layout_rules() -> str:
        """Return curated project UI layout rules. Holdouts must never be used as generation references."""
        return json.dumps(load_unity_layout_knowledge(), ensure_ascii=False)

    psd_agent = build_psd_agent(request, tools)
    unity_agent = Agent(
        name="Unity Agent",
        model=MODEL,
        instructions=(
            "Validate the Unity execution gate. Do not claim that a prefab was generated because this tool only checks the gate. "
            "Font, TMP, Sprite, Atlas, and output dependencies must be explicit. If semantic evidence is not approved, return NEEDS_REVIEW."
        ),
        tools=[validate_current_unity_gate, inspect_project_layout_rules],
        output_type=AgentDecision,
    )
    qa_agent = Agent(
        name="QA Agent",
        model=MODEL,
        instructions=(
            "Run local QA and compare PS design intent with Unity semantics. Simple, Sliced, Mask, Scroll, and LayoutGroup have "
            "different valid size relationships. Use the curated project layout rules, but never learn from holdouts. Never fail only because "
            "dimensions differ. Unresolved visual intent must be NEEDS_REVIEW, not guessed."
        ),
        tools=[run_current_qa, inspect_project_layout_rules],
        output_type=AgentDecision,
    )
    return Agent(
        name="Director Agent",
        model=MODEL,
        instructions=(
            "You own the final workflow result. Call PSD Agent first, Unity Agent second, and QA Agent last. "
            "This request is evidence-first: stop at NEEDS_REVIEW when semantic intent is not approved. "
            "Use at most one retry for a specialist and only for a retryable tool failure. Never turn a proposed border into an approved border. "
            "PASS is allowed only when every required stage passes. Return concise structured evidence and name the responsible role."
        ),
        tools=[
            psd_agent.as_tool(tool_name="inspect_psd_semantics", tool_description="Inspect PSD hierarchy and artist asset intent."),
            unity_agent.as_tool(tool_name="validate_unity_stage", tool_description="Validate Unity dependencies and execution gate."),
            qa_agent.as_tool(tool_name="run_qa_stage", tool_description="Run semantic and structural QA."),
        ],
        output_type=PipelineDecision,
    )
