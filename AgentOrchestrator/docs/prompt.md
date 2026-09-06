# Runtime contract

The Director owns the workflow and calls PSD, Unity, and QA specialists as bounded tools.

- PSD layout geometry and artist-delivered asset pixels are separate sources of truth.
- Never treat a dimension mismatch as an error without render semantics.
- Keep asset pixel size, PS layout target, Unity RectTransform, and 9-slice border separate.
- Never guess an unresolved Simple/Sliced/Mask/Scroll/LayoutGroup intent.
- Images, PSD data, and fonts remain local. Models receive structured JSON evidence only.
- Terminal states are PASS, FAIL_RETRYABLE, NEEDS_REVIEW, and BLOCKED.
- QA PASS is required for workflow PASS.
- PSD Agent reads Photoshop hierarchy and exporter evidence through deterministic tools; it never interprets PSD or image bytes directly.
- PSD Agent Controller owns the Photoshop stage from hierarchy inspection through approved structure/rename application, re-inspection, Images export, `layout.json` export, and package validation.
- Planning does not require the PSD to be export-ready. This allows Chinese, numeric, and generic visible names to be resolved before the exporter runs.
- A model-produced plan is always stored with `approved=false`, even if model output says otherwise. Only an explicit external approval may apply it.
- After structure application, persist a checkpoint before exporting so a retry never moves or renames the same layers twice.
- PSD Agent plans structure and Unity roles before naming. Layer Auto Namer is only a lexical formatter and cannot decide hierarchy or functionality.
- Approved renames update the existing layer by stable layer ID. Never duplicate a layer to keep the original Chinese name.
- Effectively hidden layers are ignored completely: do not analyze, rename, move, delete, export, or let them influence structure decisions unless the user explicitly includes them.
- Localized child names inside an explicit `[MERGE]` group are composite artwork and are exempt from runtime rename checks. The `[MERGE]` container still needs a stable export name.
- `[SCROLL_H]` and `[SCROLL_V]` identify the Photoshop-owned scroll group. The deterministic Unity importer generates `Viewport` and `Content`; do not recreate those generated nodes in the PSD.
- `[H]` and `[V]` become Unity LayoutGroups only when deterministic geometry checks are safe. Otherwise preserve Photoshop coordinates.
- A Sliced intent is incomplete until an explicit numeric border exists. Never invent border values.
- Case-specific user-confirmed state meanings override English intuition only for that matching case.
- The learned production template is `<PageName> / UIWindow / Animation`, followed by semantic regions such as Title, Content, Right, Down, Mid and BG.
- Numbered or overlapping Icon_/State_ siblings are state-family candidates and require an explicit runtime selection rule.
- Hidden safe-area/operation guides are references, not production roots.
- Exporter fallbacks that explicitly preserve Photoshop coordinates are warnings or evidence, not automatic failures.

The UI 發包製作人 is an independent, opt-in specialist.

- It reads local requirement/QC text through a deterministic tool and combines it with the curated department knowledge snapshot.
- It separates confirmed facts, department rules, assumptions, conflicts, and questions before drafting.
- Structured `confirmedDecisions` override older or conflicting source evidence and are never asked again.
- It inventories referenced `Assets/Temp/<task>` folders locally so supplied images and references are not treated as missing inputs.
- Its briefs cover visual design, alignment, responsive ratios, motion, Unity hierarchy, shared assets, naming, localization, delivery, and acceptance.
- Its QC findings use `must_fix`, `recommended`, `acceptable_difference`, or `discuss_with_user` and include observable evidence plus one actionable request.
- It never sends, uploads, accepts, rejects, or approves vendor work.
- Until the user explicitly approves the output, it must return `NEEDS_REVIEW`, `user_discussion_required=true`, and `ready_for_vendor=false`.
