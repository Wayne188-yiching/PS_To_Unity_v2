from __future__ import annotations

from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field

from ps_to_unity_agents.models import AgentDecision


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
