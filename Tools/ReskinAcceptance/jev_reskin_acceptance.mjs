// Jev semantic acceptance for the reskin report (layer 2).
//
// Layer 1 (mechanical checks: GUID, protected-field fingerprints, summary recount, hashes, console)
// runs inside Unity. Only when layer 1 is green does this script ask Jev the semantic questions.
// Jev reads the report TEXT, not the Unity scene: pixels, sizes, GUIDs and hashes stay in code.
// Typed output guarantees the answer's shape, not its correctness.
//
//   node jev_reskin_acceptance.mjs calibrate --before B.json --after A.json --folder F.json --diff code.diff --out calib.json
//   node jev_reskin_acceptance.mjs judge --report A.json --diff code.diff --calibration calib.json --out result.json
//
// SDK: @typesafe-ai/sdk from JEV_SDK_DIR (default D:\Wayne\JevTest). Key: TYPESAFE_API_KEY env var only;
// this script never reads or writes .env files and never prints the key.

import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const sdkDir = process.env.JEV_SDK_DIR || "D:\\Wayne\\JevTest";
const sdk = await import(pathToFileURL(join(sdkDir, "node_modules", "@typesafe-ai", "sdk", "dist", "index.mjs")).href);
const { TypeSafeClient, choice, noul, score, APIError } = sdk;

if (!process.env.TYPESAFE_API_KEY) {
  console.error("TYPESAFE_API_KEY is not set (environment variable). Aborting.");
  process.exit(2);
}
const client = new TypeSafeClient();

// ── arguments ───────────────────────────────────────────────────────────────
const [mode, ...rest] = process.argv.slice(2);
const args = {};
for (let i = 0; i < rest.length; i += 2) args[rest[i].replace(/^--/, "")] = rest[i + 1];
const readJson = (p) => JSON.parse(readFileSync(p, "utf8"));

// ── state: what Jev is allowed to see ───────────────────────────────────────
const PROTECTED = [
  "CanvasGroup", "GridLayoutGroup", "HorizontalLayoutGroup/VerticalLayoutGroup parameters (padding, spacing, childControl...)",
  "ContentSizeFitter", "ScrollRect (incl. scrollSensitivity, movementType)", "synthetic Viewport node (RectMask2D + raycast Image)",
  "synthetic Content node (its LayoutGroup / ContentSizeFitter)", "RectTransform parameters of the three scroll layers",
  "hierarchy, RectTransform of any node, adding/removing components",
];

function nameOf(key) {
  if (!key) return "";
  const hash = key.lastIndexOf("#");
  if (hash >= 0) return key.slice(hash + 1);
  return key.split("/").pop();
}

// The artist-facing missing list is exactly what the tool shows the artist:
// v2.16+: every status=missing item with its reason; v2.15 legacy: the raw missingFiles strings.
function artistFacingMissingList(report) {
  if (Array.isArray(report.legacyMissingFiles)) return report.legacyMissingFiles;
  return (report.items || []).filter((i) => i.status === "missing").map((i) => `${nameOf(i.oldSprite)}: ${i.reason}`);
}

function trimReport(report) {
  const items = (report.items || []).map((i) => ({
    oldSprite: i.oldSprite, action: i.action, status: i.status, reasonCode: i.reasonCode ?? null, reason: i.reason,
    newSource: i.newSource, oldSize: i.oldSize, newSize: i.newSize, border: i.border, newBorder: i.newBorder ?? null,
    borderValidAfter: i.borderValidAfter, warnings: i.warnings ?? [],
    usedBy: (i.usedBy || []).map((u) => ({
      prefab: u.prefab, path: u.path, component: u.component, relation: u.relation ?? null,
      writtenHere: u.writtenHere ?? null, applied: u.applied ?? null,
    })),
  }));
  return {
    toolVersion: report.toolVersion, mode: report.mode, flow: report.flow, reportOrigin: report.reportOrigin ?? null,
    targetFolder: report.targetFolder, sourceArtFolder: report.sourceArtFolder, errors: report.errors ?? [],
    items, summary: report.summary, execution: report.execution ?? null,
    protectedContractViolations: report.protectedContract?.violations ?? [],
  };
}

// Jev accepts ~32k input tokens per call and all questions must go in ONE call, so the diff is compacted
// in steps. Every step is recorded in diffMode; nothing is dropped silently.
const PROTECTED_API = /ScrollRect|LayoutGroup|ContentSizeFitter|CanvasGroup|RectTransform|RectMask2D|sizeDelta|anchoredPosition|anchorMin|anchorMax|pivot|AddComponent|DestroyImmediate|SetParent|SetSiblingIndex|scrollSensitivity|movementType|padding|spacing/;

function splitDiffByFile(diff) {
  const files = [];
  for (const chunk of diff.split(/^(?=diff --git )/m)) {
    if (!chunk.trim()) continue;
    const m = /^diff --git a\/(\S+)/.exec(chunk);
    files.push({ path: m ? m[1] : "?", text: chunk });
  }
  return files;
}

function compactDiff(diff, addedOnly) {
  const kept = [];
  const omitted = [];
  for (const f of splitDiffByFile(diff)) {
    const changed = f.text.split("\n").filter((l) => /^[+-]/.test(l) && !/^(\+\+\+|---)/.test(l));
    const touchesProtected = changed.some((l) => PROTECTED_API.test(l));
    const isApplier = /PsUiSkinApplier\.cs$/.test(f.path);
    if (!isApplier && !touchesProtected) {
      omitted.push(`${f.path}: ${changed.length} changed lines, none mention a protected type/API (checked by regex ${PROTECTED_API.source})`);
      continue;
    }
    const lines = changed
      .filter((l) => !addedOnly || l.startsWith("+"))
      .filter((l) => !/^[+-]\s*\/\//.test(l) && l.slice(1).trim().length > 0)
      .map((l) => l[0] + l.slice(1).trim());
    kept.push(`diff ${f.path}\n${lines.join("\n")}`);
  }
  return { text: kept.join("\n"), omitted };
}

// Mapping questions only for refSwap pairs whose old and new names differ (one question per unique pair).
function mappingPairs(report) {
  const pairs = new Map();
  for (const i of report.items || []) {
    if (!(i.action === "refSwap" || (i.action === "skipped" && i.newSource && i.newSource.includes("#")))) continue;
    const oldName = nameOf(i.oldSprite), newName = nameOf(i.newSource);
    if (!newName || oldName.toLowerCase() === newName.toLowerCase()) continue;
    const key = `${i.oldSprite} -> ${i.newSource}`;
    const p = pairs.get(key) || { key, oldName, newName, oldSize: i.oldSize, newSize: i.newSize, border: i.border, newBorder: i.newBorder ?? null, statuses: [], components: new Set(), nodes: new Set() };
    p.statuses.push(`${i.status}${i.reasonCode ? `(${i.reasonCode})` : ""}`);
    for (const u of i.usedBy || []) { p.components.add(u.component); p.nodes.add(u.path); }
    pairs.set(key, p);
  }
  return [...pairs.values()].map((p, idx) => ({ ...p, id: `mapping_quality_${idx + 1}`, components: [...p.components], nodes: [...p.nodes] }));
}

function buildQuestions(pairs, report) {
  // Diagnostic only (does not decide pass/fail): which counted-as-success item looks most suspicious.
  const suspects = { none: "No item counted as success looks failed or broken." };
  (report.items || []).forEach((i, idx) => {
    if (i.status === "ok") suspects[`item_${idx + 1}`] = `${nameOf(i.oldSprite)} (${i.action}${i.reasonCode ? ", " + i.reasonCode : ""})`;
  });
  const q = {
    silent_failure_suspect: choice(
      "Diagnostic: among the items counted as success (status ok), which one most looks like it actually failed or will render broken? " +
      "Answer none if none does.",
      suspects,
    ),
    silent_failure: noul(
      "Look at state.report.items and state.report.summary. Is there any item that actually failed, or will render broken, " +
      "but is still reported as success (status \"ok\", or counted in summary.filesOverwritten / spritesReplaced / prefabsChanged)?",
      {
        true: "Yes: at least one item is status ok or counted as done while its own fields contradict success - e.g. borderValidAfter=false, " +
          "a 9-slice sprite (non-zero border) whose newSize differs from oldSize, a usedBy entry with applied=false, a reason or warning that " +
          "describes breakage, or summary numbers larger than what the ok items support.",
        false: "No: every item counted as success is internally consistent (all usages applied, border still valid, sizes compatible), and every " +
          "real problem is reported as missing / blocked / skipped with a reason instead of being counted as done.",
      },
    ),
    contract_violation: noul(
      "Look at state.codeDiff (a git diff of the Unity reskin tool) and state.protectedList. Does the diff make the reskin flow WRITE to any " +
      "protected item - assign fields of CanvasGroup / GridLayoutGroup / Horizontal- or VerticalLayoutGroup / ContentSizeFitter / ScrollRect, " +
      "change the synthetic Viewport or Content nodes, change any RectTransform, reparent, or add/remove components?",
      {
        true: "Yes: the diff adds or changes code in the reskin path that assigns, adds, removes or reparents one of the protected items " +
          "(e.g. sets scrollSensitivity, padding, spacing, sizeDelta or anchoredPosition; AddComponent; DestroyImmediate; SetParent).",
        false: "No: the diff only READS protected items (to detect, skip, fingerprint or compare them) and only writes Image.m_Sprite, " +
          "Selectable.m_SpriteState.* and RawImage.m_Texture, plus reports, backups and editor UI.",
      },
    ),
    report_actionability: score(
      "Look at state.artistFacingMissingList: the missing-art list this tool gives to an artist. How actionable is it for an artist " +
      "who does not open Unity?",
      [
        "0 - No list, or only generic messages; the artist cannot tell which files are missing.",
        "1 - Bare file names only; the artist cannot tell what pixel size to draw, whether 9-slice margins matter, or where the image is used.",
        "2 - File names plus partial information (e.g. some sizes) but no 9-slice constraints and no usage location.",
        "3 - File name and exact pixel size and 9-slice constraints, but usage (which prefab / node) is missing or incomplete.",
        "4 - File name, exact pixel size, 9-slice border constraints and every prefab / node where it is used are all present; " +
          "the artist can produce the file without opening Unity.",
      ],
    ),
  };
  for (const p of pairs) {
    q[p.id] = choice(
      `Reskin mapping check. Old sprite "${p.oldName}" (size ${JSON.stringify(p.oldSize)}, 9-slice border ${JSON.stringify(p.border)}) ` +
      `is mapped to new sprite "${p.newName}" (size ${JSON.stringify(p.newSize)}, border ${JSON.stringify(p.newBorder)}). ` +
      `It is used by components ${JSON.stringify(p.components)} on nodes ${JSON.stringify(p.nodes)}. Tool status: ${JSON.stringify(p.statuses)}. ` +
      "Judging from names, sizes and where it is used, is this a sensible reskin mapping?",
      {
        correct_match: "The new sprite plays the same UI role as the old one (same kind of element: button to button, frame to frame, line to line), " +
          "and size / 9-slice usage are compatible or the tool already handles the incompatibility.",
        wrong_target: "The new sprite is a different kind of element than the old one (e.g. a close button mapped to a gem icon, a frame mapped to an " +
          "icon); applying it would put the wrong art in that place.",
        should_not_swap: "The old sprite should not be swapped at all (e.g. shared or system art, protected scroll infrastructure), regardless of the target.",
        cannot_tell: "Names, sizes and usages do not give enough information to judge.",
      },
    );
  }
  return q;
}

const renameText = ({ text, omitted }) => ({ diff: text, omitted });

async function evaluate(report, diffText, label) {
  const pairs = mappingPairs(report);
  const questions = buildQuestions(pairs, report);
  const baseState = {
    note: "Machine-generated report of a Unity UI reskin run. Judge only from this text.",
    protectedList: PROTECTED,
    artistFacingMissingList: artistFacingMissingList(report),
    report: trimReport(report),
  };
  const attempts = [
    { mode: "full diff", diff: diffText, omitted: [] },
    { mode: "compact: changed lines only, comment-only lines and indentation removed", ...renameText(compactDiff(diffText, false)) },
    { mode: "compact, added lines only (removed lines dropped)", ...renameText(compactDiff(diffText, true)) },
  ];
  let diffMode = null;
  let result;
  for (const a of attempts) {
    try {
      const state = { ...baseState, codeDiff: a.diff, codeDiffNote: { mode: a.mode, omittedFiles: a.omitted } };
      result = await client.systemOne({ state, questions });
      diffMode = { mode: a.mode, omittedFiles: a.omitted, chars: a.diff.length };
      break;
    } catch (err) {
      if (!(err instanceof APIError) || err.status !== 400 || !String(err.message).includes("max_tokens_exceeded")) throw err;
    }
  }
  if (!result) throw new Error("Even the most compact diff exceeds Jev's input limit; contract_violation cannot be asked in one call.");
  const a = result.answers;
  return {
    label, model: result.model, usage: result.usage, diffMode,
    silent_failure: a.silent_failure.noul,
    silent_failure_suspect: { choice: a.silent_failure_suspect.choice, confidence: a.silent_failure_suspect.confidence, probabilities: a.silent_failure_suspect.probabilities },
    contract_violation: a.contract_violation.noul,
    report_actionability: { score: a.report_actionability.score, confidence: a.report_actionability.confidence, probabilities: a.report_actionability.probabilities },
    mapping_quality: pairs.map((p) => ({
      id: p.id, pair: p.key, statuses: p.statuses,
      choice: a[p.id].choice, confidence: a[p.id].confidence, probabilities: a[p.id].probabilities,
    })),
  };
}

// ── perturbations used only for calibration ────────────────────────────────
function injectSilentFailure(report) {
  const r = JSON.parse(JSON.stringify(report));
  const victim = r.items.find((i) => i.reasonCode === "borderInvalidAfterResize") || r.items.find((i) => i.status === "blocked");
  victim.status = "ok";
  victim.reasonCode = "ready";
  victim.reason = "同名檔案覆蓋；.meta（GUID、importer 設定、9-slice）保留不動。";
  r.summary.filesOverwritten += 1;
  r.summary.blocked -= 1;
  return r;
}

const VIOLATING_DIFF = `diff --git a/Assets/Editor/PhotoshopUiImporter/PsUiSkinApplier.cs b/Assets/Editor/PhotoshopUiImporter/PsUiSkinApplier.cs
@@ -612,6 +612,12 @@ public static Report Execute(Report dryRun)
                     var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(group.Key);
+                    foreach (var scroll in prefab.GetComponentsInChildren<ScrollRect>(true))
+                    {
+                        scroll.scrollSensitivity = 20f;          // normalise feel after reskin
+                        scroll.movementType = ScrollRect.MovementType.Clamped;
+                    }
+                    foreach (var fitter in prefab.GetComponentsInChildren<ContentSizeFitter>(true)) Object.DestroyImmediate(fitter, true);
                     foreach (var u in group)
`;

// ── thresholds from the project's own calibration data ──────────────────────
// Positive = known-bad cases, negative = known-good cases. A noul question gets automatic
// pass/fail bands only if the two groups separate; between them (and always within 0.5 +- 0.15)
// the verdict is "manual". Score gets a pass floor below the known-good reports.
function deriveThresholds(runs) {
  const noulBands = (q, badLabels, goodLabels) => {
    const bad = runs.filter((r) => badLabels.includes(r.label)).map((r) => r[q]);
    const good = runs.filter((r) => goodLabels.includes(r.label)).map((r) => r[q]);
    const minBad = Math.min(...bad), maxGood = Math.max(...good);
    if (!(minBad > maxGood)) return { q, separable: false, minBad, maxGood, note: "known-good and known-bad overlap: every verdict is manual" };
    const gap = minBad - maxGood;
    return {
      q, separable: true, minBad, maxGood,
      passBelow: +(maxGood + gap / 4).toFixed(4),
      failAbove: +(minBad - gap / 4).toFixed(4),
      ambiguousBand: [0.35, 0.65],
    };
  };
  const good = runs.filter((r) => ["after_theme", "after_folder"].includes(r.label)).map((r) => r.report_actionability.score);
  const bad = runs.filter((r) => r.label === "before_theme").map((r) => r.report_actionability.score);
  return {
    silent_failure: noulBands("silent_failure", ["before_theme", "after_theme_injected_silent_failure"], ["after_theme", "after_folder"]),
    contract_violation: noulBands("contract_violation", ["after_theme_violating_diff"], ["after_theme", "after_folder", "before_theme"]),
    // Rubric level 3 = exact size + 9-slice constraint present. The calibration data must place every
    // known-bad report below it; otherwise the rubric does not discriminate and the floor is flagged.
    report_actionability: {
      knownGood: good, knownBad: bad, passAtLeast: 3,
      discriminates: Math.max(...bad) < 3,
      note: "levels 0-4; floor = rubric level 3 (size + 9-slice), not a fitted number",
    },
    mapping_quality: { autoAcceptOnly: "correct_match", minConfidence: 0.8, note: "cannot_tell, wrong_target, should_not_swap or confidence < 0.8 go to the manual list" },
  };
}

function judge(evaluation, thresholds) {
  const verdicts = [];
  const manual = [];
  for (const q of ["silent_failure", "contract_violation"]) {
    const t = thresholds[q];
    const v = evaluation[q];
    let verdict;
    if (!t.separable) verdict = "manual";
    else if (v >= t.ambiguousBand[0] && v <= t.ambiguousBand[1]) verdict = "manual";
    else if (v <= t.passBelow) verdict = "pass";
    else if (v >= t.failAbove) verdict = "fail";
    else verdict = "manual";
    verdicts.push({ question: q, raw: v, verdict, thresholds: t });
    if (verdict === "manual") manual.push({ question: q, raw: v, why: "noul in the ambiguous band or between calibrated pass/fail bands" });
  }
  const s = evaluation.report_actionability.score;
  const sv = s >= thresholds.report_actionability.passAtLeast ? "pass" : "fail";
  verdicts.push({ question: "report_actionability", raw: s, confidence: evaluation.report_actionability.confidence, verdict: sv, thresholds: thresholds.report_actionability });
  for (const m of evaluation.mapping_quality) {
    const ok = m.choice === "correct_match" && m.confidence >= thresholds.mapping_quality.minConfidence;
    verdicts.push({ question: m.id, pair: m.pair, raw: m.choice, confidence: m.confidence, verdict: ok ? "pass" : "manual" });
    if (!ok) manual.push({ question: m.id, pair: m.pair, statuses: m.statuses, raw: m.choice, confidence: m.confidence, why: "mapping not auto-accepted (choice or confidence)" });
  }
  const failed = verdicts.filter((v) => v.verdict === "fail");
  return { passed: failed.length === 0 && manual.length === 0, failed, manualReview: manual, verdicts };
}

// ── main ───────────────────────────────────────────────────────────────────
try {
  if (mode === "calibrate") {
    const diff = readFileSync(args.diff, "utf8");
    const before = readJson(args.before), after = readJson(args.after), folder = readJson(args.folder);
    const runs = [];
    runs.push(await evaluate(before, diff, "before_theme"));
    runs.push(await evaluate(after, diff, "after_theme"));
    runs.push(await evaluate(folder, diff, "after_folder"));
    runs.push(await evaluate(injectSilentFailure(after), diff, "after_theme_injected_silent_failure"));
    runs.push(await evaluate(after, VIOLATING_DIFF, "after_theme_violating_diff"));
    const thresholds = deriveThresholds(runs);
    writeFileSync(args.out, JSON.stringify({ generatedAt: new Date().toISOString(), runs, thresholds }, null, 2));
    for (const r of runs)
      console.log(`${r.label.padEnd(40)} silent=${r.silent_failure.toFixed(4)} contract=${r.contract_violation.toFixed(4)} ` +
        `actionability=${r.report_actionability.score.toFixed(3)} mapping=${r.mapping_quality.map((m) => `${m.choice}@${m.confidence.toFixed(2)}`).join(",")} diff=${r.diffMode.mode} (${r.diffMode.chars} chars)`);
    console.log(JSON.stringify(thresholds, null, 2));
  } else if (mode === "judge") {
    const diff = readFileSync(args.diff, "utf8");
    const report = readJson(args.report);
    const { thresholds } = readJson(args.calibration);
    const evaluation = await evaluate(report, diff, args.label || "judge");
    const verdict = judge(evaluation, thresholds);
    writeFileSync(args.out, JSON.stringify({ generatedAt: new Date().toISOString(), evaluation, verdict }, null, 2));
    console.log(JSON.stringify({ evaluation, verdict }, null, 2));
  } else {
    console.error("usage: calibrate|judge (see header)");
    process.exit(2);
  }
} catch (err) {
  if (err instanceof APIError) console.error(`API error ${err.status}: ${err.message}${err.requestId ? ` (request ${err.requestId})` : ""}`);
  else console.error(err);
  process.exit(1);
}
