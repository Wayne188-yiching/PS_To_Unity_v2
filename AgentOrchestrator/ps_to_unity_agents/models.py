from __future__ import annotations

from enum import Enum
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field


class Status(str, Enum):
    PASS = "PASS"
    FAIL_RETRYABLE = "FAIL_RETRYABLE"
    NEEDS_REVIEW = "NEEDS_REVIEW"
    BLOCKED = "BLOCKED"


class PipelineRequest(BaseModel):
    case_id: str
    psd_path: Path
    psd_inspection_path: Path | None = None
    layout_json_path: Path
    artist_asset_folder: Path
    exporter_asset_folder: Path
    output_folder: Path
    execution_mode: Literal["analyze", "execute"] = "analyze"
    semantics_approved: bool = False
    confirmed_semantics: list[str] = Field(default_factory=list)
    unity_executable: Path | None = None
    unity_project_path: Path | None = None
    unity_import_folder: str = "Assets/Temp/AgentPeakPower/Atlas/SpriteAtlas/Base"
    unity_prefab_folder: str = "Assets/Temp/AgentPeakPower/Prefab"
    unity_default_tmp_font_asset: str | None = None
    shared_asset_folders: list[Path] = Field(default_factory=list)


class Issue(BaseModel):
    code: str
    owner: Literal["PSD", "UNITY", "QA", "OUTSOURCE", "HUMAN"]
    severity: Literal["info", "warning", "error"]
    message: str
    node_path: str | None = None
    retryable: bool = False


class AgentDecision(BaseModel):
    status: Status
    summary: str
    issues: list[Issue] = Field(default_factory=list)
    evidence: list[str] = Field(default_factory=list)
    next_action: str


class PsdStructureAction(BaseModel):
    action: Literal["rename", "create_group", "move"]
    layer_id: int | None = None
    ref: str | None = None
    parent_ref: str | None = None
    parent_layer_id: int | None = None
    new_name: str | None = None
    reason: str


class PsdStructurePlan(BaseModel):
    schema_version: Literal["1.0"] = "1.0"
    case_id: str
    source_document: str
    canvas_width: int
    canvas_height: int
    approved: bool = False
    actions: list[PsdStructureAction] = Field(default_factory=list)


class PsdAgentDecision(AgentDecision):
    structure_plan: PsdStructurePlan | None = None


class PipelineDecision(BaseModel):
    status: Status
    summary: str
    responsible_agent: Literal["PSD", "UNITY", "QA", "HUMAN", "NONE"]
    retries_used: int = 0
    issues: list[Issue] = Field(default_factory=list)
    evidence: list[str] = Field(default_factory=list)
    next_action: str


class OutsourcingRequest(BaseModel):
    case_id: str
    project_name: str = "DemoGame"
    task_title: str
    workflow: Literal["brief", "qc"] = "brief"
    requirement_text: str = ""
    spec_paths: list[Path] = Field(default_factory=list)
    qc_evidence_paths: list[Path] = Field(default_factory=list)
    user_context: list[str] = Field(default_factory=list)
    confirmed_decisions: list[str] = Field(default_factory=list)
    api_transmission_approved: bool = False
    user_approved_output: bool = False
    output_folder: Path


class OutsourcingQcFinding(BaseModel):
    category: Literal["must_fix", "recommended", "acceptable_difference", "discuss_with_user"]
    area: Literal["requirements", "visual", "layout", "motion", "unity", "delivery"]
    finding: str
    evidence: str
    requested_action: str


class OutsourcingAgentDecision(AgentDecision):
    workflow: Literal["brief", "qc"]
    requirements_summary: list[str] = Field(default_factory=list)
    assumptions: list[str] = Field(default_factory=list)
    questions_for_user: list[str] = Field(default_factory=list)
    package_document: str = ""
    qc_findings: list[OutsourcingQcFinding] = Field(default_factory=list)
    vendor_feedback_draft: str = ""
    user_discussion_required: bool = True
    ready_for_vendor: bool = False
