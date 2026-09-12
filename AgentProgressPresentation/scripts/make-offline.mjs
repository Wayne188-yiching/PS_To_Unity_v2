import { readFile, writeFile } from "node:fs/promises";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const outputDirectory = resolve(scriptDirectory, "..", "dist");
const entryPath = join(outputDirectory, "index.html");
const offlinePath = join(outputDirectory, "PS_To_Unity_Agent_Report_Offline.html");

let html = await readFile(entryPath, "utf8");

const stylesheetMatch = html.match(/<link rel="stylesheet"[^>]*href="([^"]+)"[^>]*>/);
const scriptMatch = html.match(/<script type="module"[^>]*src="([^"]+)"[^>]*><\/script>/);

if (!stylesheetMatch || !scriptMatch) {
  throw new Error("Unable to locate Vite CSS or JavaScript output in dist/index.html.");
}

const resolveAsset = (reference) => resolve(outputDirectory, reference.replace(/^\.\//, ""));
const stylesheetPath = resolveAsset(stylesheetMatch[1]);
let css = await readFile(stylesheetPath, "utf8");

css = await replaceAsync(css, /url\((['"]?)(\.\/[^)'\"]+)\1\)/g, async (match, quote, reference) => {
  const assetPath = resolve(dirname(stylesheetPath), reference);
  const extension = extname(assetPath).toLowerCase();
  const mime = extension === ".woff2" ? "font/woff2" : "application/octet-stream";
  const data = await readFile(assetPath);
  return `url(data:${mime};base64,${data.toString("base64")})`;
});

const javascript = (await readFile(resolveAsset(scriptMatch[1]), "utf8"))
  .replaceAll("</script", "<\\/script");

html = html
  .replace(stylesheetMatch[0], () => `<style>${css}</style>`)
  .replace(scriptMatch[0], () => `<script type="module">${javascript}</script>`);

const imageMime = {
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".gif": "image/gif",
  ".svg": "image/svg+xml",
};

// <img src="./assets/x.png"> has to become a data URI too, or the single-file build
// ships broken images. Anything already inline or remote is left alone.
html = await replaceAsync(html, /(<img\b[^>]*?\ssrc=")([^"]+)(")/g, async (match, before, reference, after) => {
  if (/^(data:|https?:|\/\/)/i.test(reference)) return match;
  const assetPath = resolveAsset(reference);
  const extension = extname(assetPath).toLowerCase();
  const mime = imageMime[extension];
  if (!mime) throw new Error(`Unsupported image type in offline build: ${reference}`);
  const data = await readFile(assetPath);
  return `${before}data:${mime};base64,${data.toString("base64")}${after}`;
});

await writeFile(offlinePath, html, "utf8");
console.log(`Offline presentation: ${offlinePath}`);

async function replaceAsync(source, pattern, replacer) {
  const matches = [...source.matchAll(pattern)];
  const replacements = await Promise.all(matches.map((match) => replacer(...match)));
  let result = source;
  for (let index = matches.length - 1; index >= 0; index -= 1) {
    const match = matches[index];
    result = `${result.slice(0, match.index)}${replacements[index]}${result.slice(match.index + match[0].length)}`;
  }
  return result;
}
