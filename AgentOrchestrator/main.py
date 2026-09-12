from __future__ import annotations

import argparse
import asyncio
import json
import os
import sys
from pathlib import Path

from agents import Runner

from agent_roles.pipeline_agents import build_director, build_psd_agent, enforce_pipeline_gate
from ps_to_unity_agents.evidence import prepare_case, validate_psd_package_manifest
from ps_to_unity_agents.models import PipelineRequest
from ps_to_unity_agents.psd_controller import PsdAgentController
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
    decision = enforce_pipeline_gate(result.final_output, request)
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
    controller_result = PsdAgentController(request).record_plan(result.final_output)
    print(json.dumps(controller_result, ensure_ascii=False, indent=2, default=str))
    return 0 if controller_result["status"] in {"PASS", "NEEDS_REVIEW"} else 1


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
    validation = validate_psd_package_manifest(request)
    result = {"mode": "offline", "prepared": prepared, "validation": validation}
    output_path = request.output_folder / "offline_result.json"
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if validation["status"] in {"PASS", "NEEDS_REVIEW"} else 1


def main() -> int:
    configure_console_encoding()
    parser = argparse.ArgumentParser(description="PS_To_Unity_v2 multi-agent MVP")
    parser.add_argument(
        "mode",
        choices=("offline", "psd", "psd-controller", "run", "review"),
    )
    parser.add_argument("--request", type=Path, required=True)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument(
        "--approve-plan",
        action="store_true",
        help="Approve the existing on-disk PSD plan, apply it, then export and validate the package.",
    )
    args = parser.parse_args()
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
