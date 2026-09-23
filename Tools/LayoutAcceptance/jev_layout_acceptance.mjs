// Jev semantic acceptance for a PS -> Unity layout run (layer 2).
//
// Layer 1 is mechanical and decides on its own: layout_vs_composite.py (per-element shift vs the Photoshop
// composite), recomposite.py (full-scene diff of the image re-composite and of a Unity render), the Unity
// import result and the Pipeline Validator. build_layout_state.py turns those into text; Jev only reads
// that text. Typed answers guarantee the shape, not the truth.
//
//   node jev_layout_acceptance.mjs calibrate --before B.json --after A.json --out calib.json
//   node jev_layout_acceptance.mjs judge --state A.json --calibration calib.json --out result.json
//
// SDK from JEV_SDK_DIR (default D:\Wayne\JevTest); key from TYPESAFE_API_KEY only.

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

const [mode, ...rest] = process.argv.slice(2);
const args = {};
for (let i = 0; i < rest.length; i += 2) args[rest[i].replace(/^--/, "")] = rest[i + 1];
const readJson = (p) => JSON.parse(readFileSync(p, "utf8"));

const RISKS = {
  none_significant: "No remaining risk worth a follow-up in this run.",
  text_not_verified: "Text (TMP) placement or appearance was not verified by the pixel checks and could still differ from Photoshop.",
  nine_slice_border_semantics: "Auto 9-slice borders differ from the artist's own borders, which can surprise later reskins or edits.",
  unmeasured_elements: "Elements that could not be measured one by one might still be misplaced.",
  structure_not_production_like: "The generated hierarchy/naming does not match the production prefab structure, so the page still needs manual restructuring.",
  cannot_tell: "The report does not give enough information to name the main risk.",
};

function questions() {
  return {
    layout_still_misaligned: noul(
      "Using state.geometryCheck, state.imagesRecompositeVsPhotoshop and state.unityRenderVsPhotoshop: is any image element still placed " +
      "differently from Photoshop by more than 1 px?",
      {
        true: "Yes: a geometryCheck failure with a shift, a 220 px block where some shift matches Photoshop better, or an element whose position " +
          "is neither measured one by one nor covered by a full-scene diff without local offsets.",
        false: "No: every measurable element passes, and the full-scene diffs of both the re-composite and the Unity render show no block where a " +
          "shift would match better, so the unmeasurable elements are covered too.",
      },
    ),
    root_cause_explains_all: noul(
      "Compare state.measuredBeforeOffsets with state.rootCause. Does the stated root cause account for every measured offset, including why " +
      "different elements moved by different amounts (23 px, 41/32 px, 4 px)?",
      {
        true: "Yes: each listed offset is covered by a mechanism in the root cause, and the remaining uncertain part is explicitly marked as inferred.",
        false: "No, or there is no root cause: at least one offset or magnitude is left unexplained, or no explanation is given at all.",
      },
    ),
    nine_slice_practice_alignment: score(
      "Compare state.unityImport (what the tool produced) with state.handMadeReference (how the production prefab of the same page is built). " +
      "How closely does the tool's image output follow the hand-made 9-slice practice?",
      [
        "0 - No 9-slice at all: every frame, pill and panel is a full-size Simple image.",
        "1 - A few images are sliced, but the frames, pills and panels that the hand-made prefab slices are mostly full-size.",
        "2 - About half of the elements the reference slices are sliced; others that clearly should be are not.",
        "3 - Most frames, pills, panels and the scroll track are small sliced sprites like the reference; minor gaps or different border values.",
        "4 - Matches the reference practice: sliced where the reference slices, Simple where the art is a gradient, with evidence of equal pixels.",
      ],
    ),
    main_remaining_risk: choice(
      "Diagnostic: given everything in the state, what is the main remaining risk before this output can replace hand-made work?",
      RISKS,
    ),
    evidence_sufficiency: score(
      "How sufficient is the evidence in the state for accepting the layout as matching Photoshop?",
      [
        "0 - Claims only, no measurements.",
        "1 - Measurements only against the tool's own layout.json (cannot catch errors made during export).",
        "2 - Some comparison against the Photoshop picture, but partial or without a baseline.",
        "3 - Per-element and full-scene comparison against the Photoshop composite, with a before/after baseline.",
        "4 - As 3, plus a real Unity render and structural validation of the imported prefab, with uncertain parts stated.",
      ],
    ),
  };
}

async function evaluate(state, label) {
  const result = await client.systemOne({ state, questions: questions() });
  const a = result.answers;
  return {
    label, model: result.model, usage: result.usage,
    layout_still_misaligned: a.layout_still_misaligned.noul,
    root_cause_explains_all: a.root_cause_explains_all.noul,
    nine_slice_practice_alignment: { score: a.nine_slice_practice_alignment.score, confidence: a.nine_slice_practice_alignment.confidence },
    main_remaining_risk: { choice: a.main_remaining_risk.choice, confidence: a.main_remaining_risk.confidence, probabilities: a.main_remaining_risk.probabilities },
    evidence_sufficiency: { score: a.evidence_sufficiency.score, confidence: a.evidence_sufficiency.confidence },
  };
}

// Known-bad variants built from the real AFTER state, used only for calibration.
function injectMisalignment(after) {
  const s = JSON.parse(JSON.stringify(after));
  s.label = "after_injected_misalignment";
  s.geometryCheck.summary.FAIL += 1;
  s.geometryCheck.summary.PASS -= 1;
  s.geometryCheck.failures.push({ node: "1_003", rect: [1255, 411, 348, 82], shift: [0, 4] });
  s.unityRenderVsPhotoshop.blocksWhereAShiftMatchesBetter.push({ x: 1100, y: 440, shift: [0, -4] });
  return s;
}

function dropRootCausePart(after) {
  const s = JSON.parse(JSON.stringify(after));
  s.label = "after_incomplete_root_cause";
  s.rootCause = "Exporter fast-duplicate path: layers extending past the canvas top/left were aligned to their unclamped origin while the " +
    "layout rect was clamped, so they moved by the clamped amount (title +23, board +41/+32). Fix: align relative to the clamped export rect.";
  return s;
}

function band(values, badHigh) {
  const bad = values.bad, good = values.good;
  const separable = badHigh ? Math.min(...bad) > Math.max(...good) : Math.max(...bad) < Math.min(...good);
  if (!separable) return { separable: false, bad, good, note: "known-good and known-bad overlap: every verdict is manual" };
  const lo = badHigh ? Math.max(...good) : Math.max(...bad);
  const hi = badHigh ? Math.min(...bad) : Math.min(...good);
  const gap = hi - lo;
  return { separable: true, bad, good, lowerBand: +(lo + gap / 4).toFixed(4), upperBand: +(hi - gap / 4).toFixed(4), ambiguousBand: [0.35, 0.65] };
}

function judge(e, t) {
  const verdicts = [], manual = [];
  const noulVerdict = (q, badHigh) => {
    const th = t[q], v = e[q];
    let verdict;
    if (!th.separable || (v >= th.ambiguousBand[0] && v <= th.ambiguousBand[1])) verdict = "manual";
    else if (badHigh) verdict = v <= th.lowerBand ? "pass" : v >= th.upperBand ? "fail" : "manual";
    else verdict = v >= th.upperBand ? "pass" : v <= th.lowerBand ? "fail" : "manual";
    verdicts.push({ question: q, raw: v, verdict, thresholds: th });
    if (verdict === "manual") manual.push({ question: q, raw: v, why: "noul in the ambiguous band, between calibrated bands, or not separable" });
  };
  noulVerdict("layout_still_misaligned", true);
  noulVerdict("root_cause_explains_all", false);
  for (const q of ["nine_slice_practice_alignment", "evidence_sufficiency"]) {
    const v = e[q].score, floor = t[q].passAtLeast;
    verdicts.push({ question: q, raw: v, confidence: e[q].confidence, verdict: v >= floor ? "pass" : "fail", floor });
  }
  const r = e.main_remaining_risk;
  verdicts.push({ question: "main_remaining_risk", raw: r.choice, confidence: r.confidence, verdict: "info" });
  if (r.choice !== "none_significant") manual.push({ question: "main_remaining_risk", raw: r.choice, confidence: r.confidence, why: "a named risk must be addressed or explained by a person" });
  const failed = verdicts.filter((v) => v.verdict === "fail");
  return { passed: failed.length === 0 && manual.length === 0, failed, manualReview: manual, verdicts };
}

try {
  if (mode === "calibrate") {
    const before = readJson(args.before), after = readJson(args.after);
    const runs = [];
    runs.push(await evaluate(before, "before"));
    runs.push(await evaluate(after, "after"));
    runs.push(await evaluate(injectMisalignment(after), "after_injected_misalignment"));
    runs.push(await evaluate(dropRootCausePart(after), "after_incomplete_root_cause"));
    const pick = (label, q) => runs.find((r) => r.label === label)[q];
    const thresholds = {
      layout_still_misaligned: band({ bad: [pick("before", "layout_still_misaligned"), pick("after_injected_misalignment", "layout_still_misaligned")],
        good: [pick("after", "layout_still_misaligned")] }, true),
      root_cause_explains_all: band({ bad: [pick("before", "root_cause_explains_all"), pick("after_incomplete_root_cause", "root_cause_explains_all")],
        good: [pick("after", "root_cause_explains_all")] }, false),
      // Rubric floors (level 3 = the practice / evidence is essentially there), checked against the data.
      nine_slice_practice_alignment: { passAtLeast: 3, before: pick("before", "nine_slice_practice_alignment").score, after: pick("after", "nine_slice_practice_alignment").score },
      evidence_sufficiency: { passAtLeast: 3, before: pick("before", "evidence_sufficiency").score, after: pick("after", "evidence_sufficiency").score },
    };
    writeFileSync(args.out, JSON.stringify({ generatedAt: new Date().toISOString(), runs, thresholds }, null, 2));
    for (const r of runs)
      console.log(`${r.label.padEnd(30)} misaligned=${r.layout_still_misaligned.toFixed(2)} rootCause=${r.root_cause_explains_all.toFixed(2)} ` +
        `9slice=${r.nine_slice_practice_alignment.score.toFixed(2)} evidence=${r.evidence_sufficiency.score.toFixed(2)} risk=${r.main_remaining_risk.choice}@${r.main_remaining_risk.confidence.toFixed(2)} tokens=${r.usage.input_tokens}`);
    console.log(JSON.stringify(thresholds, null, 1));
  } else if (mode === "judge") {
    const state = readJson(args.state);
    const { thresholds } = readJson(args.calibration);
    const evaluation = await evaluate(state, state.label || "judge");
    const verdict = judge(evaluation, thresholds);
    writeFileSync(args.out, JSON.stringify({ generatedAt: new Date().toISOString(), evaluation, verdict }, null, 2));
    console.log(JSON.stringify({ evaluation, verdict }, null, 1));
  } else {
    console.error("usage: calibrate|judge");
    process.exit(2);
  }
} catch (err) {
  if (err instanceof APIError) console.error(`API error ${err.status}: ${err.message}${err.requestId ? ` (request ${err.requestId})` : ""}`);
  else console.error(err);
  process.exit(1);
}
