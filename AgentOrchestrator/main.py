from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path

from agents import Runner

from agent_roles.pipeline_agents import build_director, build_psd_agent
from agent_roles.ui_outsourcing_producer import (
    AGENT_DISPLAY_NAME,
    OutsourcingCaseTools,
    build_outsourcing_agent,
    enforce_approval_gate,
    require_api_transmission_approval,
    render_package_markdown,
    render_qc_markdown,
)
from ps_to_unity_agents.evidence import prepare_case, qa_manifest
from ps_to_unity_agents.models import OutsourcingRequest, PipelineRequest
from ps_to_unity_agents.psd_controller import PsdAgentController
from ps_to_unity_agents.psd_structure_plan import validate_structure_plan
from ps_to_unity_agents.reviewer import serve_review


ROOT = Path(__file__).resolve().parents[1]


def configure_console_encoding() -> None:
    """Keep agent evidence printable on Windows when it contains multilingual UI text."""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure:
            reconfigure(encoding="utf-8", errors="replace")


def load_local_key() -> None:
    env_file = ROOT / ".env.local"
    if os.environ.get("OPENAI_API_KEY") or not env_file.is_file():
        return
    for line in env_file.read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("OPENAI_API_KEY=") and line.partition("=")[2].strip():
            os.environ["OPENAI_API_KEY"] = line.partition("=")[2].strip()
            return


def load_request(path: Path) -> PipelineRequest:
    payload = json.loads(path.read_text(encoding="utf-8-sig"))
    for key in (
        "psd_path", "psd_inspection_path", "layout_json_path", "artist_asset_folder", "exporter_asset_folder", "output_folder",
        "unity_executable", "unity_project_path",
    ):
        if payload.get(key) and not Path(payload[key]).is_absolute():
            payload[key] = str((ROOT / payload[key]).resolve())
    return PipelineRequest.model_validate(payload)


def load_outsourcing_request(path: Path) -> OutsourcingRequest:
    payload = json.loads(path.read_text(encoding="utf-8-sig"))
    for key in ("spec_paths", "qc_evidence_paths"):
        payload[key] = [
            str((ROOT / item).resolve()) if not Path(item).is_absolute() else item
            for item in (payload.get(key) or [])
        ]
    if payload.get("output_folder") and not Path(payload["output_folder"]).is_absolute():
        payload["output_folder"] = str((ROOT / payload["output_folder"]).resolve())
    return OutsourcingRequest.model_validate(payload)


async def run_live(request: PipelineRequest) -> int:
    load_local_key()
    if not os.environ.get("OPENAI_API_KEY"):
        raise RuntimeError("OPENAI_API_KEY is not available.")
    director = build_director(request)
    input_text = json.dumps({
        "caseId": request.case_id,
        "executionMode": request.execution_mode,
        "semanticsApproved": request.semantics_approved,
        "privacy": "Do not request or emit PSD/image bytes; use structured tool evidence only.",
    }, ensure_ascii=False)
    result = await Runner.run(director, input_text, max_turns=12)
    decision = enforce_approval_gate(result.final_output, request)
    output_path = request.output_folder / "director_result.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(decision.model_dump_json(indent=2), encoding="utf-8")
    print(decision.model_dump_json(indent=2))
    return 0 if decision.status.value in {"PASS", "NEEDS_REVIEW"} else 1


async def run_psd_agent(request: PipelineRequest) -> int:
    load_local_key()
    if not os.environ.get("OPENAI_API_KEY"):
        raise RuntimeError("OPENAI_API_KEY is not available.")
    agent = build_psd_agent(request)
    result = await Runner.run(agent, json.dumps({
        "caseId": request.case_id,
        "executionMode": request.execution_mode,
        "task": (
            "Produce an unapproved partial structure plan now for every action made safe by confirmedSemantics. "
            "Propose the learned <PageName>/UIWindow/Animation production-root template and move the confirmed "
            "visible page roots under it while preserving stacking order. Omit unresolved scroll viewport, mask, "
            "component, descendant, and rename actions. Do not wait for approval and keep approved=false."
        ),
        "privacy": "PSD and image bytes stay local; use the structured deterministic tool evidence.",
    }, ensure_ascii=False), max_turns=4)
    decision = result.final_output
    output_path = request.output_folder / "psd_agent_result.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(decision.model_dump_json(indent=2), encoding="utf-8")
    if decision.structure_plan is not None:
        plan_path = request.output_folder / "psd_structure_plan.json"
        plan_path.write_text(decision.structure_plan.model_dump_json(indent=2), encoding="utf-8")
        inspection_path = request.psd_inspection_path or request.output_folder / "psd_inspection.json"
        validation = validate_structure_plan(plan_path, inspection_path)
        (request.output_folder / "psd_structure_plan_validation.json").write_text(
            json.dumps(validation, ensure_ascii=False, indent=2), encoding="utf-8"
        )
    print(decision.model_dump_json(indent=2))
    return 0 if decision.status.value in {"PASS", "NEEDS_REVIEW"} else 1


async def run_outsourcing_agent(request: OutsourcingRequest) -> int:
    require_api_transmission_approval(request)
    load_local_key()
    if not os.environ.get("OPENAI_API_KEY"):
        raise RuntimeError("OPENAI_API_KEY is not available.")
    agent = build_outsourcing_agent(request)
    result = await Runner.run(agent, json.dumps({
        "caseId": request.case_id,
        "projectName": request.project_name,
        "taskTitle": request.task_title,
        "workflow": request.workflow,
        "task": "Prepare the local outsourcing brief or QC discussion draft from the approved tool evidence.",
    }, ensure_ascii=False), max_turns=4)
    decision = enforce_approval_gate(result.final_output, request)
    request.output_folder.mkdir(parents=True, exist_ok=True)
    (request.output_folder / "outsourcing_agent_result.json").write_text(
        decision.model_dump_json(indent=2), encoding="utf-8"
    )
    (request.output_folder / "outsourcing_package.md").write_text(
        render_package_markdown(decision), encoding="utf-8"
    )
    (request.output_folder / "qc_feedback_draft.md").write_text(
        render_qc_markdown(decision), encoding="utf-8"
    )
    print(decision.model_dump_json(indent=2))
    return 0 if decision.status.value in {"PASS", "NEEDS_REVIEW"} else 1


def run_outsourcing_preflight(request: OutsourcingRequest) -> int:
    """Read the case and discover Unity context locally without calling the API."""
    evidence = OutsourcingCaseTools(request).inspect()
    result = {
        "agent": AGENT_DISPLAY_NAME,
        "mode": "local_preflight",
        "case": evidence,
    }
    request.output_folder.mkdir(parents=True, exist_ok=True)
    (request.output_folder / "outsourcing_preflight.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps({
        "agent": AGENT_DISPLAY_NAME,
        "status": evidence["status"],
        "sources": [item["name"] for item in evidence["sources"]],
        "projectDiscovery": evidence["projectDiscovery"],
        "issues": evidence["issues"],
        "output": str(request.output_folder / "outsourcing_preflight.json"),
    }, ensure_ascii=False, indent=2))
    return 0 if evidence["status"] == "PASS" else 1


async def run_psd_controller(request: PipelineRequest, approve_plan: bool) -> int:
    controller = PsdAgentController(request)
    if approve_plan:
        result = controller.approve_apply_and_export()
    else:
        load_local_key()
        if not os.environ.get("OPENAI_API_KEY"):
            raise RuntimeError("OPENAI_API_KEY is not available.")
        agent = build_psd_agent(request, planning_only=True)
        run = await Runner.run(agent, json.dumps({
            "caseId": request.case_id,
            "task": (
                "Create the most complete safe unapproved structure and rename plan for the visible PSD. Structure first, then names. "
                "Include rename actions only when the layer role is supported by hierarchy, geometry, text, case knowledge, or confirmed "
                "semantics. Keep ambiguous numeric or generic artwork unchanged and report it for review. Include approved project semantic "
                "tags only when their requirements are proven. Never include hidden layers, never duplicate layers, and keep approved=false."
            ),
            "privacy": "PSD and image bytes stay local; use structured deterministic evidence only.",
        }, ensure_ascii=False), max_turns=4)
        result = controller.record_plan(run.final_output)
    print(json.dumps(result, ensure_ascii=False, indent=2, default=str))
    return 0 if result["status"] in {"PASS", "NEEDS_REVIEW"} else 1


def run_offline(request: PipelineRequest) -> int:
    prepared = prepare_case(request)
    qa = qa_manifest(request)
    result = {"mode": "offline", "prepared": prepared, "qa": qa}
    output_path = request.output_folder / "offline_result.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if qa["status"] in {"PASS", "NEEDS_REVIEW"} else 1


def main() -> int:
    configure_console_encoding()
    parser = argparse.ArgumentParser(description="PS_To_Unity_v2 multi-agent MVP")
    parser.add_argument(
        "mode",
        choices=("offline", "psd", "psd-controller", "run", "review", "outsource-preflight", "outsource"),
    )
    parser.add_argument("--request", type=Path, required=True)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument(
        "--approve-plan",
        action="store_true",
        help="Approve the existing on-disk PSD plan, apply it, then export and validate the package.",
    )
    args = parser.parse_args()
    if args.mode in {"outsource-preflight", "outsource"}:
        request = load_outsourcing_request(args.request.resolve())
        if args.mode == "outsource-preflight":
            return run_outsourcing_preflight(request)
        return asyncio.run(run_outsourcing_agent(request))
    request = load_request(args.request.resolve())
    if args.mode == "offline":
        return run_offline(request)
    if args.mode == "review":
        serve_review(request, args.port)
        return 0
    if args.mode == "psd":
        return asyncio.run(run_psd_agent(request))
    if args.mode == "psd-controller":
        return asyncio.run(run_psd_controller(request, args.approve_plan))
    return asyncio.run(run_live(request))


if __name__ == "__main__":
    raise SystemExit(main())
