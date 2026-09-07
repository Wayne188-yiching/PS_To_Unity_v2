import { createServer } from "node:http";
import { mkdir, readFile, stat } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { pathToFileURL } from "node:url";
import { chromium } from "playwright-core";

const root = normalize(join(import.meta.dirname, "..", "dist"));
const sourceEntry = normalize(join(import.meta.dirname, "..", "index.html"));
const reviewDir = normalize(join(import.meta.dirname, "..", ".impeccable", "review"));
const executablePath = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".woff2": "font/woff2",
};

await mkdir(reviewDir, { recursive: true });

const server = createServer(async (request, response) => {
  const requestPath = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
  const safePath = requestPath === "/" ? "index.html" : requestPath.replace(/^\/+/, "");
  const filePath = normalize(join(root, safePath));
  if (!filePath.startsWith(root)) {
    response.writeHead(403).end("Forbidden");
    return;
  }
  try {
    const fileStat = await stat(filePath);
    if (!fileStat.isFile()) throw new Error("Not a file");
    response.writeHead(200, { "Content-Type": mimeTypes[extname(filePath)] || "application/octet-stream" });
    response.end(await readFile(filePath));
  } catch {
    response.writeHead(404).end("Not found");
  }
});

await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
const address = server.address();
const url = `http://127.0.0.1:${address.port}/`;
const browser = await chromium.launch({ executablePath, headless: true });
const report = { url, viewports: [], reducedMotion: null, fileOffline: null, errors: [], externalRequests: [] };

async function inspectViewport(name, width, height) {
  const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 });
  const page = await context.newPage();
  page.on("pageerror", (error) => report.errors.push(`${name}: pageerror: ${error.message}`));
  page.on("console", (message) => {
    if (message.type() === "error") report.errors.push(`${name}: console: ${message.text()}`);
  });
  page.on("request", (request) => {
    if (!request.url().startsWith(url)) report.externalRequests.push(request.url());
  });

  await page.goto(url, { waitUntil: "networkidle" });
  await page.waitForTimeout(500);
  const initial = await page.evaluate(() => ({
    width: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    height: document.documentElement.clientHeight,
    scrollHeight: document.documentElement.scrollHeight,
    fontReady: document.fonts.status,
    chapter: document.querySelector("#currentIndex")?.textContent,
    evidencePanels: document.querySelectorAll("[data-evidence-step]").length,
  }));

  await page.screenshot({ path: join(reviewDir, `${name}-opening.png`) });
  if (name === "wide-2560x1187") {
    for (const id of ["limitation", "agent-layer", "real-case"]) {
      await page.evaluate((chapterId) => {
        const chapter = document.getElementById(chapterId);
        if (!chapter) return;
        const top = chapter.getBoundingClientRect().top + window.scrollY;
        window.scrollTo(0, top + Math.min(760, window.innerHeight * 0.66));
      }, id);
      await page.waitForTimeout(400);
      await page.screenshot({ path: join(reviewDir, `${name}-${id}.png`) });
    }
  }
  await page.locator("#psd-agent").scrollIntoViewIfNeeded();
  await page.waitForTimeout(350);
  await page.keyboard.press("PageDown");
  await page.waitForTimeout(350);
  const psdAgent = await page.evaluate(() => ({
    scrollY: window.scrollY,
    chapter: document.querySelector("#currentIndex")?.textContent,
    counter: document.querySelector("#evidenceCounter")?.textContent,
    overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
  }));
  await page.screenshot({ path: join(reviewDir, `${name}-psd-agent.png`) });

  const temporaryViewport = {
    width: Math.max(1024, width - 160),
    height: Math.max(680, height - 80),
  };
  await page.setViewportSize(temporaryViewport);
  await page.waitForTimeout(450);
  const resizeState = await page.evaluate(() => ({
    pinnedTop: Math.round(document.querySelector("#psd-agent .scene")?.getBoundingClientRect().top ?? 9999),
    overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    chapter: document.querySelector("#currentIndex")?.textContent,
  }));
  await page.setViewportSize({ width, height });
  await page.waitForTimeout(450);

  await page.locator("#progress").scrollIntoViewIfNeeded();
  await page.waitForTimeout(450);
  const progressState = await page.evaluate(() => {
    const rail = document.querySelector(".chapter-rail");
    const railVisible = rail && getComputedStyle(rail).display !== "none";
    const statuses = [...document.querySelectorAll("#progress .roadmap-item em")];
    const railItems = railVisible ? [...rail.querySelectorAll("a")] : [];
    const intersects = (a, b) => a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;
    return {
      chapter: document.querySelector("#currentIndex")?.textContent,
      railVisible,
      overlapCount: statuses.flatMap((status) => railItems.filter((item) => intersects(status.getBoundingClientRect(), item.getBoundingClientRect()))).length,
      overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
    };
  });
  await page.screenshot({ path: join(reviewDir, `${name}-progress.png`) });

  await page.keyboard.press("End");
  await page.waitForTimeout(450);
  const endState = await page.evaluate(() => ({
    scrollY: window.scrollY,
    maxScroll: document.documentElement.scrollHeight - window.innerHeight,
    chapter: document.querySelector("#currentIndex")?.textContent,
    overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
  }));
  await page.screenshot({ path: join(reviewDir, `${name}-direction.png`) });

  await page.keyboard.press("Home");
  await page.waitForTimeout(450);
  await page.keyboard.press("ArrowDown");
  await page.waitForTimeout(350);
  const keyboard = await page.evaluate(() => ({ scrollY: window.scrollY }));

  report.viewports.push({ name, initial, psdAgent, resizeState, progressState, endState, keyboard });
  await context.close();
}

await inspectViewport("wide-2560x1187", 2560, 1187);
await inspectViewport("desktop-1920x1080", 1920, 1080);
await inspectViewport("zoomed-1707x870", 1707, 870);
await inspectViewport("projector-1366x768", 1366, 768);

const reducedContext = await browser.newContext({
  viewport: { width: 2560, height: 1187 },
  reducedMotion: "reduce",
});
const reducedPage = await reducedContext.newPage();
reducedPage.on("pageerror", (error) => report.errors.push(`reduced: pageerror: ${error.message}`));
reducedPage.on("console", (message) => {
  if (message.type() === "error") report.errors.push(`reduced: console: ${message.text()}`);
});
await reducedPage.goto(url, { waitUntil: "networkidle" });
await reducedPage.waitForTimeout(350);
await reducedPage.evaluate(() => document.querySelector("#psd-agent")?.scrollIntoView());
await reducedPage.waitForTimeout(150);
report.reducedMotion = await reducedPage.evaluate(() => ({
  bodyClass: document.body.className,
  visibleEvidencePanels: [...document.querySelectorAll("[data-evidence-step]")]
    .filter((element) => getComputedStyle(element).visibility !== "hidden").length,
  overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
  flowColumns: getComputedStyle(document.querySelector(".psd-flow-layout")).gridTemplateColumns,
}));
await reducedPage.screenshot({ path: join(reviewDir, "reduced-2560x1187-psd-agent.png"), fullPage: false });

await reducedContext.close();

const fileContext = await browser.newContext({ viewport: { width: 1366, height: 768 } });
const filePage = await fileContext.newPage();
filePage.on("pageerror", (error) => report.errors.push(`file-offline: pageerror: ${error.message}`));
filePage.on("console", (message) => {
  if (message.type() === "error") report.errors.push(`file-offline: console: ${message.text()}`);
});
filePage.on("request", (request) => {
  if (/^https?:/i.test(request.url())) report.externalRequests.push(request.url());
});
await filePage.goto(pathToFileURL(sourceEntry).href, { waitUntil: "load" });
await filePage.waitForTimeout(900);
report.fileOffline = await filePage.evaluate(() => ({
  url: location.href,
  background: getComputedStyle(document.body).backgroundColor,
  chapter: document.querySelector("#currentIndex")?.textContent,
  scrollHeight: document.documentElement.scrollHeight,
  fontReady: document.fonts.status,
  overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
}));
await filePage.screenshot({ path: join(reviewDir, "file-offline-1366x768.png") });
await fileContext.close();

await browser.close();
server.close();

const failed = report.errors.length > 0
  || report.externalRequests.length > 0
  || report.viewports.some((entry) => entry.initial.scrollWidth > entry.initial.width || entry.psdAgent.overflow || entry.endState.overflow)
  || report.viewports.some((entry) => entry.resizeState.overflow || Math.abs(entry.resizeState.pinnedTop) > 2)
  || report.viewports.some((entry) => entry.progressState.overflow || entry.progressState.overlapCount > 0)
  || report.viewports.some((entry) => entry.endState.chapter !== "07" || entry.keyboard.scrollY <= 0)
  || report.reducedMotion?.visibleEvidencePanels !== 10
  || report.reducedMotion?.overflow
  || !report.fileOffline?.url.endsWith("PS_To_Unity_Agent_Report_Offline.html")
  || report.fileOffline?.chapter !== "01"
  || report.fileOffline?.background !== "rgb(7, 19, 26)"
  || report.fileOffline?.scrollHeight < 10000
  || report.fileOffline?.overflow;

console.log(JSON.stringify({ ...report, passed: !failed }, null, 2));
process.exitCode = failed ? 1 : 0;
