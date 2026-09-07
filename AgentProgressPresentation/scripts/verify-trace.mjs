// Guards the scroll-driven trace lines.
//
// Each trace uses vector-effect: non-scaling-stroke, so stroke-dasharray is resolved in
// screen pixels while getTotalLength() reports viewBox units. When the two are mixed the
// dash is too short to cover the line: the pattern repeats, lighting a stray segment near
// the right edge before the scroll starts, and the trace never reaches its last node.
//
// This asserts the dash unit equals the line's real rendered length, that nothing is lit
// at rest, that the fill advances with scroll, and that it survives a resize.
//
// Usage: node scripts/verify-trace.mjs

import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";
import { chromium } from "playwright-core";

const distRoot = resolve(join(import.meta.dirname, "..", "dist"));
const ORIGIN = "https://wayne188-yiching.github.io";
const chromePath = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".woff2": "font/woff2",
};

const TRACES = [
  // scan spans each ScrollTrigger's own end distance.
  { id: "previousTrace", chapter: "previous", scan: 1600 },
  { id: "psdFlowPath", chapter: "psd-agent", scan: 6400 },
  { id: "multiAgentPath", chapter: "direction", scan: 2900 },
];

const browser = await chromium.launch({ executablePath: chromePath, headless: true });
const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });

await ctx.route("**/*", async (route, request) => {
  const url = new URL(request.url());
  const rel = decodeURIComponent(url.pathname).replace(/^\/+/, "") || "index.html";
  let filePath = normalize(join(distRoot, rel));
  if (!filePath.startsWith(distRoot)) return route.fulfill({ status: 403, body: "no" });
  try {
    let st = await stat(filePath);
    if (st.isDirectory()) filePath = join(filePath, "index.html");
    await route.fulfill({
      status: 200,
      contentType: mime[extname(filePath)] || "application/octet-stream",
      body: await readFile(filePath),
    });
  } catch {
    await route.fulfill({ status: 404, contentType: "text/html", body: "<h1>404</h1>" });
  }
});

const page = await ctx.newPage();
const errors = [];
page.on("pageerror", (e) => errors.push(`pageerror: ${e.message}`));
page.on("console", (m) => {
  if (m.type() === "error") errors.push(`console: ${m.text()}`);
});

await page.goto(`${ORIGIN}/`, { waitUntil: "networkidle" });
await page.waitForTimeout(900);

// Measure a path the way the browser resolves a non-scaling stroke: in screen space.
const measure = async (id) =>
  page.evaluate((pathId) => {
    const path = document.getElementById(pathId);
    if (!path) return null;
    const userLength = path.getTotalLength();
    const matrix = path.getScreenCTM();
    const point = path.ownerSVGElement.createSVGPoint();
    let rendered = 0;
    let previous = null;
    for (let i = 0; i <= 240; i += 1) {
      const at = path.getPointAtLength((userLength * i) / 240);
      point.x = at.x;
      point.y = at.y;
      const s = point.matrixTransform(matrix);
      if (previous) rendered += Math.hypot(s.x - previous.x, s.y - previous.y);
      previous = s;
    }
    const style = getComputedStyle(path);
    const dashParts = style.strokeDasharray.split(/[ ,]+/).map(parseFloat);
    const dashArray = dashParts[0];
    const dashGap = dashParts.length > 1 ? dashParts[1] : dashParts[0];
    const dashOffset = parseFloat(style.strokeDashoffset);
    return {
      userLength,
      rendered,
      dashArray,
      dashGap,
      dashOffset,
      litFraction: dashArray > 0 ? 1 - dashOffset / dashArray : null,
    };
  }, id);

// ScrollTrigger is module-scoped, so step through real scroll positions instead of
// reading its internals.
const scrollPastChapter = async (chapterId, offset) => {
  await page.evaluate(
    ([id, px]) => {
      const chapter = document.getElementById(id);
      const top = chapter.getBoundingClientRect().top + window.scrollY;
      window.scrollTo(0, top + px);
    },
    [chapterId, offset]
  );
  await page.waitForTimeout(420);
};

const report = { traces: [], errors, passed: false };

for (const trace of TRACES) {
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(500);

  const atRest = await measure(trace.id);
  if (!atRest) {
    report.traces.push({ ...trace, error: "path not found" });
    continue;
  }

  const samples = [];
  const step = Math.round(trace.scan / 8);
  for (let offset = step; offset <= trace.scan; offset += step) {
    await scrollPastChapter(trace.chapter, offset);
    samples.push({ offset, ...(await measure(trace.id)) });
  }

  // Resize, then confirm the dash unit was re-measured rather than left stale.
  await page.setViewportSize({ width: 1366, height: 768 });
  await page.waitForTimeout(900);
  await scrollPastChapter(trace.chapter, Math.round(trace.scan * 0.6));
  const afterResize = await measure(trace.id);
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.waitForTimeout(900);

  // A gap at least as long as the line makes a second lit band impossible.
  const noRepeat = atRest.dashGap >= atRest.rendered - 1;
  // Scenes that scale their container mid-scroll drift a few percent from the measurement.
  const dashNearRendered = Math.abs(atRest.dashArray - atRest.rendered) / atRest.rendered < 0.06;
  const hiddenAtRest = atRest.litFraction !== null && atRest.litFraction < 0.02;
  const advances = samples.every(
    (s, i) => i === 0 || s.litFraction >= samples[i - 1].litFraction - 0.01
  );
  const completes = Math.max(...samples.map((s) => s.litFraction)) > 0.98;
  const resizeStaysCorrect =
    Math.abs(afterResize.dashArray - afterResize.rendered) / afterResize.rendered < 0.06 &&
    afterResize.dashGap >= afterResize.rendered - 1;

  report.traces.push({
    ...trace,
    userLength: Math.round(atRest.userLength),
    renderedLength: Math.round(atRest.rendered),
    dashArray: Math.round(atRest.dashArray),
    dashGap: Math.round(atRest.dashGap),
    litAtRest: Number(atRest.litFraction.toFixed(4)),
    litByScroll: samples.map((s) => Number(s.litFraction.toFixed(3))),
    afterResize: {
      rendered: Math.round(afterResize.rendered),
      dashArray: Math.round(afterResize.dashArray),
    },
    checks: { noRepeat, dashNearRendered, hiddenAtRest, advances, completes, resizeStaysCorrect },
    passed:
      noRepeat && dashNearRendered && hiddenAtRest && advances && completes && resizeStaysCorrect,
  });
}

// The flow trace must arrive at a node as that node lights, not before it. Nodes sit at
// uneven intervals along the snaking path, so an even reveal cadence silently desynced
// them: the line ran up to 15% of the path ahead of the node it was drawing towards.
await page.evaluate(() => window.scrollTo(0, 0));
await page.waitForTimeout(500);

const nodeFractions = await page.evaluate(() => {
  const path = document.getElementById("psdFlowPath");
  const nodes = [...document.querySelectorAll("#psd-agent [data-flow-step]")];
  const total = path.getTotalLength();
  const matrix = path.getScreenCTM();
  const point = path.ownerSVGElement.createSVGPoint();
  return nodes.map((node) => {
    const rect = node.getBoundingClientRect();
    const cx = rect.left + rect.width / 2;
    const cy = rect.top + rect.height / 2;
    let best = { d: Infinity, f: 0 };
    for (let i = 0; i <= 1200; i += 1) {
      const p = path.getPointAtLength((total * i) / 1200);
      point.x = p.x;
      point.y = p.y;
      const s = point.matrixTransform(matrix);
      const d = Math.hypot(s.x - cx, s.y - cy);
      if (d < best.d) best = { d, f: i / 1200 };
    }
    return best.f;
  });
});

const syncSamples = [];
for (let offset = 500; offset <= 6200; offset += 475) {
  await scrollPastChapter("psd-agent", offset);
  const sample = await page.evaluate(() => {
    const path = document.getElementById("psdFlowPath");
    const style = getComputedStyle(path);
    const dash = parseFloat(style.strokeDasharray.split(/[ ,]+/)[0]);
    const lit = 1 - parseFloat(style.strokeDashoffset) / dash;
    const nodes = [...document.querySelectorAll("#psd-agent [data-flow-step]")];
    let lastLit = -1;
    nodes.forEach((node, index) => {
      if (parseFloat(getComputedStyle(node).opacity) > 0.95) lastLit = index;
    });
    const readout = document.getElementById("evidenceCounter").textContent;
    return { lit, lastLit, readout };
  });
  syncSamples.push({ offset, ...sample });
}

// The invariant is not that the line sits on the last lit node — between two nodes it
// legitimately runs on, and the connector between Human Approval and PSD Controller is a
// fifth of the path with no node on it. What must never happen is the line sweeping past
// a node that has not lit yet.
const TOLERANCE = 0.08;
const overruns = syncSamples
  .filter((s) => s.lastLit >= 0 && s.lastLit + 1 < nodeFractions.length)
  .map((s) => ({
    offset: s.offset,
    lastLitNode: s.lastLit,
    lineAt: Number(s.lit.toFixed(3)),
    nextNodeAt: Number(nodeFractions[s.lastLit + 1].toFixed(3)),
    overrun: Number(Math.max(0, s.lit - nodeFractions[s.lastLit + 1]).toFixed(3)),
    readout: s.readout,
    // Never behind the lit nodes (the original defect); one ahead is the node still
    // fading in, which is what the crossfading panel shows too.
    readoutMatchesNodes:
      parseInt(s.readout, 10) >= s.lastLit + 1 && parseInt(s.readout, 10) <= s.lastLit + 2,
  }));
const worstOverrun = overruns.reduce((max, o) => Math.max(max, o.overrun), 0);

report.flowSync = {
  nodeFractions: nodeFractions.map((f) => Number(f.toFixed(3))),
  worstOverrun: Number(worstOverrun.toFixed(3)),
  tolerance: TOLERANCE,
  samples: overruns,
  readoutMismatches: overruns.filter((o) => !o.readoutMatchesNodes).length,
  passed: worstOverrun <= TOLERANCE && overruns.every((o) => o.readoutMatchesNodes),
};

report.passed =
  report.traces.every((t) => t.passed) && report.flowSync.passed && errors.length === 0;
console.log(JSON.stringify(report, null, 2));

await browser.close();
process.exitCode = report.passed ? 0 : 1;
