// Self-check for the layer-naming contract shared by the Auto Namer and the Exporter.
//
//   node PhotoshopExporter/test_layer_auto_namer.js
//
// The .jsx files cannot run outside Photoshop, but their naming logic is pure string
// work. This harness stubs the Photoshop globals, loads the real sources, and exercises
// the real functions against the real glossary -- no reimplementation, so it fails if
// the shipped logic breaks.

const fs = require("fs");
const path = require("path");
const assert = require("assert");

const here = __dirname;
const src = fs.readFileSync(path.join(here, "PhotoshopLayerAutoNamer.jsx"), "utf8");

// Photoshop globals. documents.length = 0 makes the top-level IIFE alert and return,
// leaving the hoisted function declarations available for testing.
const sandbox = {
  app: { documents: { length: 0 } },
  alert: () => {},
  confirm: () => false,
  File: function () {},
  Window: function () {},
  LayerKind: { TEXT: "TEXT" },
  $: { fileName: path.join(here, "PhotoshopLayerAutoNamer.jsx") },
};

const names = Object.keys(sandbox);
const values = names.map((k) => sandbox[k]);
const exported = [
  "segment",
  "parseName",
  "composeName",
  "stripGeneratedIndex",
  "extractBracketTags",
];

const load = new Function(
  ...names,
  src.replace(/^#target photoshop/, "") +
    "\nreturn {" + exported.map((n) => `${n}:${n}`).join(",") + "};"
);
const M = load(...values);

// Build the glossary the same way loadGlossary does, from the real .tsv.
function loadGlossary() {
  const text = fs.readFileSync(path.join(here, "naming_glossary.tsv"), "utf8");
  const map = {};
  const keys = [];
  for (const line of text.split(/\r\n|\r|\n/)) {
    if (!line || line.charAt(0) === "#") continue;
    const parts = line.split("\t");
    if (parts.length < 2) continue;
    const source = parts[0].trim();
    const english = parts[1].trim();
    if (!source || !english) continue;
    if (map["k_" + source] === undefined) {
      map["k_" + source] = english;
      keys.push(source);
    }
  }
  keys.sort((a, b) => b.length - a.length);
  return { map, keys, missing: false };
}

const g = loadGlossary();
let checks = 0;
const eq = (actual, expected, label) => {
  assert.deepStrictEqual(actual, expected, `${label}\n  got:      ${JSON.stringify(actual)}\n  expected: ${JSON.stringify(expected)}`);
  checks++;
};

// -- segmentation ------------------------------------------------------------

eq(M.segment("排行榜底框", g).words, ["ranking", "frame"],
   "CJK name maps through the glossary");

// The glossary holds both 排行榜 and 排行; longest-first must win.
eq(M.segment("排行榜", g).words, ["ranking"],
   "longest match wins over the shorter prefix term");

eq(M.segment("ranking_ui_frame", g).words, ["ranking", "ui", "frame"],
   "ASCII names split on separators and pass through");

eq(M.segment("金色按鈕", g).words, ["gold", "btn"],
   "multiple CJK terms concatenate in order");

// 神秘 is not in the glossary; 底框 is. The unknown run is reported, not guessed.
const mixed = M.segment("神秘底框", g);
eq(mixed.unknown, ["神秘"], "unmapped CJK run is reported verbatim");

// -- bracket tags ------------------------------------------------------------

eq(M.extractBracketTags("list[SCROLL_V][SLICED=32]"), ["[SCROLL_V]", "[SLICED=32]"],
   "every bracket tag is captured, not just a known list");

const tagged = M.parseName("列表[SCROLL_V]", g);
eq(tagged.tags, ["[SCROLL_V]"], "tag survives parsing");
eq(tagged.base, "list", "tag text does not leak into the base name");
eq(tagged.resolved, true, "tagged CJK name still resolves");

// -- re-run idempotency ------------------------------------------------------

eq(M.stripGeneratedIndex("ranking_ui_frame01"), "ranking_ui_frame",
   "family index is stripped before re-segmenting");
eq(M.stripGeneratedIndex("ranking_ui_frame01_1"), "ranking_ui_frame",
   "family + variant index is stripped");
eq(M.parseName("IMG_ranking_ui_frame01", g).base, "ranking_ui_frame",
   "an already-generated name round-trips to the same base");

// -- family / variant numbering ----------------------------------------------

function compose(entries) {
  const plan = { families: {}, familyNext: {}, variants: {} };
  return entries.map((e) =>
    M.composeName(
      { marker: "", prefix: "IMG", base: "ranking_ui_frame", tags: [], parentPath: e },
      plan
    )
  );
}

// Same parent group + same base = variants of one family.
eq(compose(["/rank1", "/rank1"]), ["IMG_ranking_ui_frame01", "IMG_ranking_ui_frame01_1"],
   "same parent -> family 01 with _1 variant");

// Different parent groups = new family index each.
eq(compose(["/rank1", "/rank1", "/rank2", "/rank2"]),
   ["IMG_ranking_ui_frame01", "IMG_ranking_ui_frame01_1",
    "IMG_ranking_ui_frame02", "IMG_ranking_ui_frame02_1"],
   "different parents -> 01/01_1 then 02/02_1");

// -- exporter guard against non-ASCII layer names -----------------------------
//
// normalizeAsciiSlug drops every non-[a-z0-9] character, so a Chinese-only layer name
// collapses to "" and degrades to layer / layer_002. Those names are still unique, so
// Unity's LayoutReader accepts them -- the damage is silent. uniqueFileName must warn.

const exporterSrc = fs
  .readFileSync(path.join(here, "PhotoshopUiPackageExporter.jsx"), "utf8")
  .replace(/^#target photoshop/, "");

const exporterStub = {
  app: { documents: { length: 0 } },
  alert: () => {},
  confirm: () => false,
  File: function () {},
  Folder: { temp: { fsName: "" } },
  Window: function () {},
  LayerKind: { TEXT: "TEXT" },
  $: { fileName: "", os: "windows" },
};
const exporterNames = Object.keys(exporterStub);
const X = new Function(
  ...exporterNames,
  exporterSrc +
    "\nreturn {uniqueFileName,uniqueNodeName,normalizeAsciiSlug,applyMaskMetadata," +
    "stripKnownTags,warnOrphanMaskLayers};"
)(...exporterNames.map((k) => exporterStub[k]));

function exportNames(layerNames) {
  const ctx = { counters: {}, warnings: [] };
  const files = layerNames.map((n) => X.uniqueFileName(n, ctx.counters, ctx) + ".png");
  return { files, warnings: ctx.warnings };
}

const cjk = exportNames(["排行榜底框", "金色按鈕", "背景"]);
eq(cjk.files, ["layer.png", "layer_002.png", "layer_003.png"],
   "CJK-only names still collapse (documents the failure being warned about)");
eq(cjk.warnings.length, 3, "every collapsed name raises a warning");
eq(cjk.warnings.map((w) => w.code), ["NON_ASCII_LAYER_NAME", "NON_ASCII_LAYER_NAME", "NON_ASCII_LAYER_NAME"],
   "warning code is NON_ASCII_LAYER_NAME");
eq(cjk.warnings[0].node, "排行榜底框",
   "warning carries the original layer name so the layer can be found in Photoshop");

// Names produced by the Auto Namer must pass through clean and raise nothing.
const clean = exportNames([
  "IMG_ranking_ui_frame01",
  "IMG_ranking_ui_frame01_1",
  "IMG_ranking_ui_frame02",
]);
eq(clean.files,
   ["ranking_ui_frame01.png", "ranking_ui_frame01_1.png", "ranking_ui_frame02.png"],
   "Auto Namer output survives the exporter and matches the target file naming");
eq(clean.warnings.length, 0, "well-named layers raise no warning");

// -- [MASK] shape-mask tag ----------------------------------------------------

const maskNode = {};
X.applyMaskMetadata(maskNode, "shape_round[MASK]");
eq(maskNode.maskRole, "mask", "[MASK] sets maskRole on the layer node");

const plainNode = {};
X.applyMaskMetadata(plainNode, "shape_round");
eq(plainNode.maskRole, undefined, "untagged layers get no maskRole");

// [MASK] must be in the tag registry, or it would leak into node/file names.
eq(X.stripKnownTags("shape_round[MASK]").trim(), "shape_round",
   "[MASK] is stripped from the exported name");
eq(X.uniqueFileName("shape_round[MASK]", {}, { warnings: [] }) + ".png", "shape_round.png",
   "mask layer still exports as a normal PNG through the existing image path");

// A [MASK] layer nobody adopted would silently vanish in Unity -- it must be reported.
const orphanCtx = { warnings: [] };
X.warnOrphanMaskLayers(
  [
    { name: "loose_mask", maskRole: "mask", children: [] },
    { name: "adopted_mask", maskRole: "mask", _maskConsumed: true, children: [] },
    { name: "plain", children: [{ name: "nested_loose", maskRole: "mask", children: [] }] },
  ],
  orphanCtx
);
eq(orphanCtx.warnings.map((w) => w.node), ["loose_mask", "nested_loose"],
   "unadopted [MASK] layers are reported, adopted ones are not");
eq(orphanCtx.warnings[0].code, "MASK_ORPHAN", "orphan warning code is MASK_ORPHAN");

console.log(`ok - ${checks} checks passed`);
