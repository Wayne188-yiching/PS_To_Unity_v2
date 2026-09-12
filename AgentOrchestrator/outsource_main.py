from __future__ import annotations

import argparse
import asyncio
import json
import os
from pathlib import Path

from agents import Runner

from agent_roles.ui_outsourcing_producer import (
    AGENT_DISPLAY_NAME,
    OutsourcingCaseTools,
    build_outsourcing_agent,
    enforce_approval_gate,
    require_api_transmission_approval,
    render_package_markdown,
    render_qc_markdown,
)
from agent_roles.ui_outsourcing_models import OutsourcingRequest
from main import ROOT, configure_console_encoding, load_local_key


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
    evidence = OutsourcingCaseTools(request).inspect()
    result = {"agent": AGENT_DISPLAY_NAME, "mode": "local_preflight", "case": evidence}
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


def main() -> int:
    configure_console_encoding()
    parser = argparse.ArgumentParser(description="Independent UI outsourcing assistant")
    parser.add_argument("mode", choices=("outsource-preflight", "outsource"))
    parser.add_argument("--request", type=Path, required=True)
    args = parser.parse_args()
    request = load_outsourcing_request(args.request.resolve())
    if args.mode == "outsource-preflight":
        return run_outsourcing_preflight(request)
    return asyncio.run(run_outsourcing_agent(request))


if __name__ == "__main__":
    raise SystemExit(main())
