from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

from pydantic import BaseModel


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical_json_sha256(payload: Any) -> str:
    if isinstance(payload, BaseModel):
        payload = payload.model_dump(mode="json")
    encoded = json.dumps(
        payload,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def inspection_fingerprint(payload: dict[str, Any]) -> str:
    normalized = dict(payload)
    normalized.pop("runId", None)
    return canonical_json_sha256(normalized)


def plan_fingerprint(payload: BaseModel | dict[str, Any]) -> str:
    if isinstance(payload, BaseModel):
        normalized = payload.model_dump(mode="json")
    else:
        normalized = dict(payload)
    normalized.pop("approved", None)
    return canonical_json_sha256(normalized)
