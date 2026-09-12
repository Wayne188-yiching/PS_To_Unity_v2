from __future__ import annotations


TRANSIENT_TOOL_ERRORS = (
    "timeout",
    "timed out",
    "busy",
    "rpc server",
    "call was rejected",
    "temporarily unavailable",
    "逾時",
    "超時",
    "伺服器忙碌",
    "呼叫被拒",
)


def classify_tool_failure(message: str) -> str:
    normalized = message.casefold()
    return "FAIL_RETRYABLE" if any(marker in normalized for marker in TRANSIENT_TOOL_ERRORS) else "BLOCKED"
