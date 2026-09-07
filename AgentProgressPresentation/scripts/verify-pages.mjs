// Faithful GitHub Pages simulation.
//
// verify.mjs checks dist/ served at a domain root and the file:// offline build.
// Neither covers how Pages actually serves this repo: a *project* site, where the
// repo root sits under /PS_To_Unity_v2/ and the public hostname is not localhost.
// That gap is what let an absolute "/src/main.js" reference ship a 404.
//
// This script serves the repo straight from disk via request interception, so the
// page sees the real production origin and base path with no network access.
//
// Usage: node scripts/verify-pages.mjs [repoRoot]

import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, resolve } from "node:path";
import { chromium } from "playwright-core";

const repoRoot = resolve(process.argv[2] || join(import.meta.dirname, "..", ".."));
const ORIGIN = "https://wayne188-yiching.github.io";
const BASE = "/PS_To_Unity_v2";
const chromePath = "C:/Program Files/Google/Chrome/Application/chrome.exe";

const mime = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".woff2": "font/woff2",
  ".woff": "font/woff",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".json": "application/json",
  ".ico": "image/x-icon",
};

// Entries a visitor can realistically land on.
const ENTRIES = [
  `${BASE}/AgentProgressPresentation/`,
  `${BASE}/AgentProgressPresentation/index.html`,
  `${BASE}/AgentProgressPresentation/dist/`,
];

async function resolveFile(urlPath) {
  if (!urlPath.startsWith(BASE)) return null;
  const rel = urlPath.slice(BASE.length).replace(/^\/+/, "");
  let filePath = normalize(join(repoRoot, rel));
  if (!filePath.startsWith(repoRoot)) return null;
  try {
    let st = await stat(filePath);
    if (st.isDirectory()) {
      filePath = join(filePath, "index.html");
      st = await stat(filePath);
    }
    if (!st.isFile()) return null;
    return filePath;
  } catch {
    return null;
  }
}

const browser = await chromium.launch({ executablePath: chromePath, headless: true });
const results = [];

for (const entry of ENTRIES) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });

  await ctx.route("**/*", async (route, request) => {
    const url = new URL(request.url());
    if (url.origin !== ORIGIN) {
      // Anything leaving the site is a packaging defect for an offline deck.
      await route.abort("blockedbyclient");
      return;
    }
    const filePath = await resolveFile(decodeURIComponent(url.pathname));
    if (!filePath) {
      await route.fulfill({ status: 404, contentType: "text/html", body: "<h1>404</h1>" });
      return;
    }
    await route.fulfill({
      status: 200,
      contentType: mime[extname(filePath)] || "application/octet-stream",
      body: await readFile(filePath),
    });
  });

  const page = await ctx.newPage();
  const served = [];
  const notFound = [];
  const failures = [];
  const consoleErrors = [];
  const offSite = [];

  page.on("response", (r) => {
    const path = r.url().replace(ORIGIN, "");
    served.push({ status: r.status(), path });
    if (r.status() >= 400) notFound.push({ status: r.status(), path });
  });
  page.on("requestfailed", (r) => {
    const path = r.url().replace(ORIGIN, "");
    if (!r.url().startsWith(ORIGIN)) offSite.push(path);
    else failures.push({ path, type: r.resourceType(), err: r.failure()?.errorText });
  });
  page.on("pageerror", (e) => consoleErrors.push(`pageerror: ${e.message}`));
  page.on("console", (m) => {
    if (m.type() === "error") consoleErrors.push(`console: ${m.text()}`);
  });

  await page.goto(ORIGIN + entry, { waitUntil: "networkidle" });
  await page.waitForTimeout(1200);

  const state = await page.evaluate(() => ({
    landedOn: location.pathname,
    title: document.title,
    chapter: document.querySelector("#currentIndex")?.textContent ?? null,
    scrollHeight: document.documentElement.scrollHeight,
    background: getComputedStyle(document.body).backgroundColor,
    fonts: document.fonts.status,
    chapters: document.querySelectorAll("main section").length,
    horizontalOverflow:
      document.documentElement.scrollWidth > document.documentElement.clientWidth,
  }));

  // The deck is only "loaded" if the built CSS and markup actually applied.
  const rendered =
    state.background === "rgb(7, 19, 26)" &&
    state.chapter === "01" &&
    state.scrollHeight > 5000 &&
    !state.horizontalOverflow;

  const passed =
    notFound.length === 0 &&
    failures.length === 0 &&
    consoleErrors.length === 0 &&
    offSite.length === 0 &&
    rendered;

  results.push({
    entry,
    state,
    rendered,
    requestCount: served.length,
    notFound,
    networkFailures: failures,
    offSiteRequests: offSite,
    consoleErrors,
    passed,
  });

  await ctx.close();
}

await browser.close();

const allPassed = results.every((r) => r.passed);
console.log(JSON.stringify({ origin: ORIGIN, base: BASE, results, passed: allPassed }, null, 2));
process.exitCode = allPassed ? 0 : 1;
