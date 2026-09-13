# Runtime contract

The Director owns the PS_To_Unity workflow and calls PSD Agent, Unity Agent, and Pipeline Validator as bounded tools. The first implementation of all three roles is present, but full live/model/end-to-end acceptance is intentionally deferred until Agent implementation is complete. Never describe implementation or regression evidence as live acceptance.

## Responsibility boundary

Development priority updated by the user on 2026-09-13: complete Agent implementation first, then perform full live/model/end-to-end acceptance. PSD acceptance is no longer a prerequisite for developing Unity Agent. This scheduling change does not bypass runtime human approval, semantic intent, execution authorization, dependency checks, or evidence gates. Keep implemented and accepted statuses separate; run basic regression checks during development.

- Agents own ambiguity, semantic judgment, planning, deterministic tool selection, diagnostics, recovery decisions, and human escalation.
- Deterministic Core owns Sprite import, 9-slice, TMP mapping, ScrollRect construction, Mask, Atlas, Prefab generation, pixel deduplication, and all exact geometry calculations.
- Rule-based checks stay deterministic. Agents must not reimplement the existing Photoshop exporter or Unity importer.
- PSD geometry, artist asset pixels, Unity RectTransform, and 9-slice border are separate evidence fields.
- Never guess unresolved Simple/Sliced/Mask/Scroll/LayoutGroup intent.
- Terminal states are `PASS`, `FAIL_RETRYABLE`, `NEEDS_REVIEW`, and `BLOCKED`.

## PSD Agent contract

- PSD and image bytes remain local. The model receives structured JSON evidence only.
- The Controller owns inspection → unapproved structure plan → human approval → apply → re-inspection → Images/layout export → PSD package validation.
- Every model plan is persisted with `approved=false`, regardless of model output.
- Approval is bound to PSD, inspection, and plan fingerprints. Changed evidence invalidates approval.
- Mechanical actions carry expected name, parent, and layer-kind preconditions and target stable Photoshop layer IDs.
- Persist `APPLYING` before mutation. Re-running an interrupted approved plan must detect already-applied final state without duplicating groups, renames, or moves.
- Photoshop busy, RPC rejection, and timeout are retryable; semantic or precondition conflicts are blocked.
- Hidden layers never influence plans unless explicitly included by a future human-authored contract.
- Structure comes before lexical naming. Layer Auto Namer cannot decide hierarchy or functionality.
- `[SCROLL_H]` and `[SCROLL_V]` identify Photoshop-owned scroll groups. Unity's deterministic importer creates `Viewport` and `Content`.
- `[H]` and `[V]` become Unity LayoutGroups only when deterministic geometry checks are safe; otherwise preserve Photoshop coordinates.
- A Sliced intent requires an explicit numeric border. Never invent border values.
- Review decisions are local, explicit, and bound to the current review fingerprint.

## Pipeline Validator contract

Pipeline Validator is the internal consistency role formerly labelled QA. It only validates:

`PSD semantic intent → layout.json / IR → Unity import → generated Prefab`

It does not perform external vendor QC. Diagnostics should converge on `severity`, `code`, `phase`, `node`, `message`, `evidence`, `suggestedAction`, and `safeToAutoFix`.

The deterministic validator requires a current Unity run receipt and Prefab structure snapshot. It checks current input freshness, authored node presence/hierarchy, hidden-node exclusion, non-layout geometry, TMP Font Asset/material/content/size, Sprite/9-slice/Button, Mask, ScrollRect, draggable Scrollbar wiring, and LayoutGroup components. Duplicate authored identity or other ambiguity remains NEEDS_REVIEW. Structural PASS is not final Photoshop/Unity visual acceptance.

## Independent outsourcing workflow

The UI outsourcing producer and future standalone `QC_Agent` use `outsource_main.py`. Their vendor brief, discussion, transmission approval, and acceptance responsibilities must not be imported into the PS_To_Unity Director or Pipeline Validator.
