// Guards the pinned scenes against collapsing at smaller viewports.
//
// The multi-agent graph pins its three rows to top 0 / 36% / bottom 0 inside a fixed
// height, so the gaps are whatever is left after three content-sized cards. On 1366x768
// that left -6px and the cards overlapped; nothing in the existing checks noticed,
// because they only looked at horizontal overflow and scroll behaviour.
//
// For every group below this asserts that no two cards overlap, and that cards stacked
// above one another keep a minimum vertical gap.
//
// Usage: node scripts/verify-layout.mjs [--report]

import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";
import { chromium } from "playwright-core";

const distRoot = resolve(join(import.meta.dirname, "..", "dist"));
const chromePath = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const reportOnly = process.argv.includes("--report");
const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".woff2": "font/woff2",
};

// Scroll offset puts each pinned scene into its settled state before measuring.
const GROUPS = [
  { chapter: "previous", selector: ".trace-node", offset: 1400, minGap: 12 },
  // Flush-stacked list rows: they share a divider border, so a zero gap is the design.
  // Only the overlap check is meaningful for these two.
  { chapter: "limitation", selector: ".ambiguity-item", offset: 2000, minGap: 0 },
  { chapter: "agent-layer", selector: ".stack-layer", offset: 1500, minGap: 8 },
  { chapter: "psd-agent", selector: ".flow-node", offset: 5800, minGap: 12 },
  { chapter: "real-case", selector: ".case-column", offset: 1300, minGap: 8 },
  { chapter: "progress", selector: ".roadmap-item", offset: 2000, minGap: 0 },
  { chapter: "direction", selector: ".graph-node", offset: 2300, minGap: 16 },
];

// Above 900px the deck runs its pinned scroll scenes; below it falls back to static
// reading mode, which is a different layout with its own rules.
const VIEWPORTS = [
  [1920, 1080],
  [1910, 911],
  [1600, 900],
  [1440, 900],
  [1366, 768],
  [1024, 768],
];

const browser = await chromium.launch({ executablePath: chromePath, headless: true });
const results = [];

for (const [width, height] of VIEWPORTS) {
  const ctx = await browser.newContext({ viewport: { width, height } });
  await ctx.route("**/*", async (route, request) => {
    const rel =
      decodeURIComponent(new URL(request.url()).pathname).replace(/^\/+/, "") ||
      "index.html";
    let filePath = normalize(join(distRoot, rel));
    if (!filePath.startsWith(distRoot)) return route.fulfill({ status: 403, body: "no" });
    try {
      const st = await stat(filePath);
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
  await page.goto("https://wayne188-yiching.github.io/", { waitUntil: "networkidle" });
  await page.waitForTimeout(900);

  const groups = [];
  for (const group of GROUPS) {
    await page.evaluate(
      ([id, px]) => {
        const chapter = document.getElementById(id);
        window.scrollTo(0, chapter.getBoundingClientRect().top + window.scrollY + px);
      },
      [group.chapter, group.offset]
    );
    await page.waitForTimeout(500);

    const measured = await page.evaluate(
      ([id, selector]) => {
        const nodes = [...document.querySelectorAll(`#${id} ${selector}`)];
        const boxes = nodes
          .map((node) => node.getBoundingClientRect())
          .filter((r) => r.width > 1 && r.height > 1)
          .map((r) => ({
            top: r.top,
            bottom: r.bottom,
            left: r.left,
            right: r.right,
          }));
        const overlaps = [];
        const verticalGaps = [];
        for (let a = 0; a < boxes.length; a += 1) {
          for (let b = a + 1; b < boxes.length; b += 1) {
            const x = Math.min(boxes[a].right, boxes[b].right) -
              Math.max(boxes[a].left, boxes[b].left);
            const y = Math.min(boxes[a].bottom, boxes[b].bottom) -
              Math.max(boxes[a].top, boxes[b].top);
            if (x > 1 && y > 1) {
              overlaps.push({ a, b, overlapX: Math.round(x), overlapY: Math.round(y) });
            } else if (x > 1) {
              // Stacked in the same column: the gap between them is what can collapse.
              const gap =
                boxes[a].top < boxes[b].top
                  ? boxes[b].top - boxes[a].bottom
                  : boxes[a].top - boxes[b].bottom;
              verticalGaps.push(Math.round(gap));
            }
          }
        }
        return { count: boxes.length, overlaps, minVerticalGap: verticalGaps.length ? Math.min(...verticalGaps) : null };
      },
      [group.chapter, group.selector]
    );

    const gapOk =
      measured.minVerticalGap === null || measured.minVerticalGap >= group.minGap;
    groups.push({
      chapter: group.chapter,
      selector: group.selector,
      ...measured,
      requiredGap: group.minGap,
      passed: measured.count > 0 && measured.overlaps.length === 0 && gapOk,
    });
  }

  const overflow = await page.evaluate(() => ({
    horizontal:
      document.documentElement.scrollWidth > document.documentElement.clientWidth,
  }));

  results.push({
    viewport: `${width}x${height}`,
    horizontalOverflow: overflow.horizontal,
    groups,
    passed: groups.every((g) => g.passed) && !overflow.horizontal,
  });
  await ctx.close();
}

await browser.close();

const passed = results.every((r) => r.passed);
console.log(JSON.stringify({ results, passed }, null, 2));
process.exitCode = reportOnly || passed ? 0 : 1;
