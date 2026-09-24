// Jev acceptance for the SkinTheme Inspector / Importer_v2 reskin UI (layer 2).
//
// Layer 1 runs in Unity (SkinThemeUiCapture.cs, -Stage ui): it records what the editor actually drew
// (GUIViewDebugger draw instructions, or the default Inspector's field names/tooltips) and checks
// truncation / overlap in code. Jev reads only that text: no pixels, no screenshots.
//
//   node jev_skintheme_ui_acceptance.mjs --before BEFORE/ui_dump.json --after AFTER/ui_dump.json --out result.json
//
// SDK: @typesafe-ai/sdk from JEV_SDK_DIR (default D:\Wayne\JevTest). Key: TYPESAFE_API_KEY env var only;
// this script never reads or writes .env files and never prints the key.

import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const sdkDir = process.env.JEV_SDK_DIR || "D:\\Wayne\\JevTest";
const sdk = await import(pathToFileURL(join(sdkDir, "node_modules", "@typesafe-ai", "sdk", "dist", "index.mjs")).href);
const { TypeSafeClient, choice, noul, score } = sdk;

if (!process.env.TYPESAFE_API_KEY) {
  console.error("TYPESAFE_API_KEY is not set (environment variable). Aborting.");
  process.exit(2);
}
const client = new TypeSafeClient();

const args = {};
const argv = process.argv.slice(2);
for (let i = 0; i < argv.length; i += 2) args[argv[i].replace(/^--/, "")] = argv[i + 1];
const readJson = (p) => JSON.parse(readFileSync(p, "utf8").replace(/^\uFEFF/, ""));

// ── ground truth: what the tool really does (from PsUiSkinApplier / PsUiSkinTheme) ─────────────
const BEHAVIOR_FACTS = [
  "targetPrefabFolderAsset (the folder to reskin) is REQUIRED for preview and is the ONLY folder that preview/apply writes to.",
  "sourcePrefabFolderAsset (the old version) is OPTIONAL and READ-ONLY: only the pairing button reads it, comparing the same node paths against the target to fill in new sprites. It is never written. If it is the same folder as the target, or they contain each other, preview is refused.",
  "sourceArtFolder is OPTIONAL and used only for entries whose new sprite is empty: a PNG with the same file name in that folder overwrites the old sprite file. When the new art is already imported into Unity it should stay empty; entries with no new sprite and no art folder are blocked (not executed).",
  "entries: each row maps an old sprite to a new sprite. The Importer_v2 'scan' button fills old sprites, the pairing button fills new sprites, or the user fills them by hand.",
  "excludedPrefabs: prefabs inside the target folder that the reskin skips. Optional.",
  "A legacy asset (made before v2.19: it has the old 'reference folder' but no source folder) cannot be previewed or paired until converted. Converting moves the old target to the source slot and the old reference folder to the target slot.",
  "Scan, pair, preview and execute are buttons in the Importer_v2 window's reskin section. The SkinTheme Inspector only edits the asset's data.",
];
const USER_TASK =
  "A Traditional-Chinese-speaking UI artist/engineer copied the CatAct page prefabs into Assets/Temp/LiveRoom and wants to reskin LiveRoom " +
  "with new sprites that are already imported into Unity. The original Assets/ThemeActivity/CatAct must stay untouched. " +
  "They just created a new SkinTheme asset and are looking at it for the first time.";

// ── what the user sees, as text ─────────────────────────────────────────────────────────────
const CHROME = new Set(["Open", "Asset Labels", "AssetBundle"]);

function visibleRows(scenario, seenTooltips) {
  const tip = (t) => {
    if (!t) return "";
    if (seenTooltips.has(t)) return " (hover tooltip: same as shown earlier)";
    seenTooltips.add(t);
    return ` (hover tooltip: ${t})`;
  };
  if (scenario.editorType === "GenericInspector") {
    // Unity 6 default Inspector (UI Toolkit): one row per visible serialized field, label = displayName.
    return scenario.serializedFields.map((f) => `field "${f.displayName}" = ${f.value}${tip(f.tooltip)}`);
  }
  const rows = [];
  for (const i of scenario.items) {
    if (i.text === "Asset Labels") break; // Unity's footer below every Inspector
    if (i.style === "LargeLabel" || CHROME.has(i.text)) continue;
    rows.push(`[${i.style}] ${i.text}${tip(i.tooltip)}`);
  }
  return rows;
}

function uiText(dump) {
  const seen = new Set();
  const out = {};
  for (const s of dump.scenarios) {
    if (s.kind === "inspector" && s.width !== 520) continue; // 360 draws the same content; geometry is checked in code
    const key = `${s.kind}:${s.scenario}`;
    out[key] = {
      what: s.kind === "window" ? "Importer_v2 window, reskin section, with the configured SkinTheme selected"
        : s.editorType === "GenericInspector" ? "SkinTheme Inspector (Unity default Inspector)" : "SkinTheme Inspector (custom)",
      rows: visibleRows(s, seen),
    };
  }
  return out;
}

// Known-bad perturbation: swap the two Prefab folder descriptions so the text contradicts behavior.
function swapFolderDescriptions(dump) {
  const d = JSON.parse(JSON.stringify(dump));
  const all = d.scenarios.flatMap((s) => s.items);
  const targetNote = all.find((i) => i.text.startsWith("必填。預覽與執行只寫這裡"))?.text;
  const sourceNote = all.find((i) => i.text.startsWith("選填、唯讀。"))?.text;
  if (!targetNote || !sourceNote) throw new Error("folder notes not found in the AFTER dump; update the perturbation");
  const targetTip = all.find((i) => i.text === "要換皮的 Prefab 資料夾" && i.tooltip)?.tooltip;
  const sourceTip = all.find((i) => i.text === "舊版 Prefab 資料夾" && i.tooltip)?.tooltip;
  for (const i of all) {
    if (i.text === targetNote) i.text = sourceNote;
    else if (i.text === sourceNote) i.text = targetNote;
    if (i.text === "要換皮的 Prefab 資料夾") i.tooltip = sourceTip;
    else if (i.text === "舊版 Prefab 資料夾") i.tooltip = targetTip;
  }
  return d;
}

// ── questions ───────────────────────────────────────────────────────────────────────────────
const questions = {
  // Diagnostic only (not part of the verdict): which mix-up is still the most plausible, to direct the next fix.
  most_plausible_mixup: choice(
    "Diagnostic. Using only state.ui and state.userTask: which mistake is a first-time user MOST likely to make with the folder inputs?",
    {
      none: "None of these is plausible; the inputs are unambiguous.",
      swap_old_and_copy: "Putting CatAct into the folder-to-reskin input and LiveRoom into the old-version input (swapped).",
      art_folder_gets_new_sprites: "Putting the folder that holds the already-imported new sprites (e.g. LiveRoom/Atlas) into the PNG / art folder input.",
      art_folder_gets_prefabs: "Putting a Prefab folder into the PNG / art folder input, or the art folder into a Prefab folder input.",
      skip_required_folder: "Leaving the required folder-to-reskin input empty, or thinking both Prefab folders are required.",
    },
  ),
  folder_roles_clear: score(
    "Use only state.ui (what the user sees) and state.userTask. For every folder input in the SkinTheme UI, can a first-time user tell " +
    "(a) which of their folders goes there, (b) whether the tool writes into it or only reads it, and (c) whether it is required?",
    [
      "0 - Folder inputs show raw internal names with no description; the user cannot tell what goes where.",
      "1 - Names hint at a purpose, but it is ambiguous which of the old/new folders goes where; nothing about writing or required/optional.",
      "2 - The user can probably guess the correct placement, but write behavior or required/optional is not stated.",
      "3 - Placement is clear, and for every folder input at least one of write behavior or required/optional is stated.",
      "4 - Placement, write behavior (which folder is written, which is read-only) and required/optional are all explicit for every folder input.",
    ],
  ),
  field_confusion: noul(
    "Use only state.ui and state.userTask. Would a first-time user plausibly put CatAct and LiveRoom into the wrong folder inputs, " +
    "or put the new-art folder into a Prefab folder input (or a Prefab folder into the art-folder input)?",
    {
      true: "Yes: names are similar or ambiguous (e.g. two inputs both called 'Source ...'), or descriptions are missing, so such a mix-up is plausible.",
      false: "No: each folder input's visible name and description clearly rule out these mix-ups.",
    },
  ),
  text_contradicts_behavior: noul(
    "Compare every visible label, note, hover tooltip and message in state.ui with state.behaviorFacts. Does any of them contradict the facts " +
    "(e.g. says a folder is written when it is read-only, calls an optional input required or vice versa, or sends the user to a button in a place " +
    "where it does not exist)? Missing information is NOT a contradiction.",
    {
      true: "Yes: at least one visible text states something that the behavior facts contradict.",
      false: "No: nothing visible contradicts the behavior facts (some information may simply be absent).",
    },
  ),
  layout_order: score(
    "Look at the configured Inspector scenarios in state.ui (collapsed and expanded). Do the order and grouping support the user's workflow?",
    [
      "0 - A flat list of internal fields; no grouping, no meaningful order.",
      "1 - Some order, but required inputs are mixed with optional/advanced ones.",
      "2 - Required inputs come first, but optional inputs are not separated, or a long list sits above short settings and buries them.",
      "3 - Required inputs first, optional inputs grouped/separated, the long mapping list last; but it is unclear where the operations are run.",
      "4 - Everything in level 3, plus a clear pointer (text or button) to where scan / pair / preview / execute are run.",
    ],
  ),
  state_guidance: score(
    "Look at the legacy and conflict Inspector scenarios in state.ui (legacy: an asset made before v2.19; conflict: the old-version folder and " +
    "the folder to reskin are the same folder). Does the UI tell the user what is wrong, what would happen if ignored, and what to do next?",
    [
      "0 - Nothing indicates a problem.",
      "1 - The problem is only implicit (e.g. visible only by reading folder values).",
      "2 - The problem is stated, but there is no next step.",
      "3 - The problem and the next step are stated.",
      "4 - The problem, its consequence and the next step are stated, and the fixing action (e.g. a convert button) is offered right there.",
    ],
  ),
};

async function evaluate(label, dump) {
  const state = {
    note: "Text recorded from what the Unity editor actually drew (labels, notes, buttons, hover tooltips, messages), in on-screen order. " +
      "Style names in brackets are Unity GUIStyles (e.g. BoldLabel = section heading, HelpBox = message box, MiniLabel = small note).",
    userTask: USER_TASK,
    behaviorFacts: BEHAVIOR_FACTS,
    ui: uiText(dump),
  };
  const r = await client.systemOne({ state, questions });
  const a = r.answers;
  const sc = (q) => ({ score: a[q].score, confidence: a[q].confidence, probabilities: a[q].probabilities });
  return {
    label, model: r.model, usage: r.usage, stateChars: JSON.stringify(state).length,
    most_plausible_mixup: { choice: a.most_plausible_mixup.choice, confidence: a.most_plausible_mixup.confidence, probabilities: a.most_plausible_mixup.probabilities },
    folder_roles_clear: sc("folder_roles_clear"),
    field_confusion: a.field_confusion.noul,
    text_contradicts_behavior: a.text_contradicts_behavior.noul,
    layout_order: sc("layout_order"),
    state_guidance: sc("state_guidance"),
  };
}

// Thresholds are fixed before looking at AFTER: noul pass <= 0.35, fail >= 0.65, manual in between
// (the 0.5 +- 0.15 band). Score pass >= 3 (rubric level). A question only yields an automatic verdict if its
// known-bad run lands on the bad side; otherwise it does not discriminate here and AFTER goes to manual review.
const NOUL_PASS = 0.35, NOUL_FAIL = 0.65, SCORE_PASS = 3;

function judge(after, knownBad) {
  const verdicts = [], manual = [];
  const noulQ = { field_confusion: "before", text_contradicts_behavior: "after_swapped_descriptions" };
  for (const [q, badLabel] of Object.entries(noulQ)) {
    const v = after[q], bad = knownBad[badLabel][q];
    const discriminates = bad >= NOUL_FAIL;
    let verdict = !discriminates ? "manual" : v <= NOUL_PASS ? "pass" : v >= NOUL_FAIL ? "fail" : "manual";
    verdicts.push({ question: q, raw: v, verdict, knownBad: { label: badLabel, raw: bad, discriminates } });
    if (verdict === "manual") manual.push({ question: q, raw: v, why: discriminates ? "noul in the 0.35-0.65 band" : `known-bad (${badLabel}) scored ${bad} < ${NOUL_FAIL}: question does not discriminate` });
  }
  for (const q of ["folder_roles_clear", "layout_order", "state_guidance"]) {
    const v = after[q], bad = knownBad.before[q];
    const discriminates = bad.score < SCORE_PASS;
    let verdict = !discriminates ? "manual" : v.score >= SCORE_PASS ? "pass" : "fail";
    if (verdict === "pass" && v.confidence < 0.6) verdict = "manual";
    verdicts.push({ question: q, raw: v.score, confidence: v.confidence, verdict, knownBad: { label: "before", raw: bad.score, discriminates } });
    if (verdict === "manual") manual.push({ question: q, raw: v.score, confidence: v.confidence, why: discriminates ? "confidence < 0.6" : "BEFORE already scores >= 3: rubric does not discriminate" });
  }
  const failed = verdicts.filter((v) => v.verdict === "fail");
  return { passed: failed.length === 0 && manual.length === 0, failed, manualReview: manual, verdicts };
}

const before = readJson(args.before), after = readJson(args.after);
const runs = {
  before: await evaluate("before", before),
  after: await evaluate("after", after),
  after_swapped_descriptions: await evaluate("after_swapped_descriptions", swapFolderDescriptions(after)),
};
const result = {
  generatedAt: new Date().toISOString(), jevModel: runs.after.model,
  thresholds: { noulPassAtMost: NOUL_PASS, noulFailAtLeast: NOUL_FAIL, scorePassAtLeast: SCORE_PASS, scoreMinConfidence: 0.6 },
  runs, verdict: judge(runs.after, runs),
};
writeFileSync(args.out, JSON.stringify(result, null, 1));
for (const [k, r] of Object.entries(runs))
  console.log(`${k.padEnd(28)} mixup=${r.most_plausible_mixup.choice}(${r.most_plausible_mixup.confidence}) roles=${r.folder_roles_clear.score}(${r.folder_roles_clear.confidence}) confusion=${r.field_confusion} ` +
    `contradicts=${r.text_contradicts_behavior} layout=${r.layout_order.score}(${r.layout_order.confidence}) guidance=${r.state_guidance.score}(${r.state_guidance.confidence}) chars=${r.stateChars}`);
console.log("verdict:", JSON.stringify({ passed: result.verdict.passed, failed: result.verdict.failed.map((f) => f.question), manual: result.verdict.manualReview }));
