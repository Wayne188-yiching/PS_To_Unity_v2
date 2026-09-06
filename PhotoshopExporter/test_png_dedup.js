// Self-check for PhotoshopUiPackageExporter's PNG visual-content deduplication.
//
//   node PhotoshopExporter/test_png_dedup.js

// Photoshop Save for Web can change iTXt/XMP while leaving the decoded image
// unchanged. The exporter must ignore that metadata but keep color-profile and
// image-data differences distinct.

const fs = require("fs");
const path = require("path");
const assert = require("assert");
const os = require("os");

const here = __dirname;
const src = fs
  .readFileSync(path.join(here, "PhotoshopUiPackageExporter.jsx"), "utf8")
  .replace(/^#target photoshop/, "");

function FakeFile(filePath) {
  this.fsName = filePath;
  this.encoding = "BINARY";
  this._content = "";
  this._offset = 0;
}

Object.defineProperties(FakeFile.prototype, {
  exists: { get() { return fs.existsSync(this.fsName); } },
  length: { get() { return this.exists ? fs.statSync(this.fsName).size : 0; } },
});

FakeFile.prototype.open = function () {
  if (!this.exists) return false;
  this._content = fs.readFileSync(this.fsName).toString("latin1");
  this._offset = 0;
  return true;
};
FakeFile.prototype.read = function (count) {
  const end = count === undefined ? this._content.length : this._offset + count;
  const result = this._content.substring(this._offset, end);
  this._offset = end;
  return result;
};
FakeFile.prototype.close = function () { return true; };
FakeFile.prototype.remove = function () {
  if (this.exists) fs.unlinkSync(this.fsName);
  return true;
};

const sandbox = {
  app: { documents: { length: 0 } },
  alert: () => {},
  confirm: () => false,
  File: FakeFile,
  Folder: { temp: { fsName: "" } },
  Window: function () {},
  LayerKind: { TEXT: "TEXT" },
  $: { fileName: "", os: "windows" },
};

const names = Object.keys(sandbox);
const X = new Function(
  ...names,
  src + "\nreturn {computePngVisualHashFromBinary,dedupPngsByHash};"
)(...names.map((name) => sandbox[name]));

function chunk(type, data) {
  const payload = Buffer.from(data || []);
  const result = Buffer.alloc(12 + payload.length);
  result.writeUInt32BE(payload.length, 0);
  result.write(type, 4, 4, "ascii");
  payload.copy(result, 8);
  // CRC is intentionally left zero: the hash parser validates structure, not CRC.
  return result;
}

function ihdr(width, height) {
  const data = Buffer.alloc(13);
  data.writeUInt32BE(width, 0);
  data.writeUInt32BE(height, 4);
  data[8] = 8; // bit depth
  data[9] = 6; // RGBA
  return data;
}

function png({ metadata, pixels, gamma }) {
  const parts = [
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk("IHDR", ihdr(32, 16)),
  ];
  if (gamma) parts.push(chunk("gAMA", gamma));
  parts.push(chunk("iTXt", Buffer.from(metadata, "utf8")));
  parts.push(chunk("IDAT", pixels));
  parts.push(chunk("IEND", []));
  return Buffer.concat(parts).toString("latin1");
}

const pixels = Buffer.from([1, 2, 3, 4, 5, 6, 7, 8]);
const samePixelsA = png({ metadata: "layer=A", pixels });
const samePixelsB = png({ metadata: "a much longer XMP record for layer B", pixels });

assert.strictEqual(
  X.computePngVisualHashFromBinary(samePixelsA),
  X.computePngVisualHashFromBinary(samePixelsB),
  "different iTXt length/content must not hide identical PNG pixels"
);

assert.notStrictEqual(
  X.computePngVisualHashFromBinary(samePixelsA),
  X.computePngVisualHashFromBinary(png({ metadata: "layer=C", pixels: Buffer.from([1, 2, 3, 9]) })),
  "different IDAT content must not be merged"
);

assert.notStrictEqual(
  X.computePngVisualHashFromBinary(samePixelsA),
  X.computePngVisualHashFromBinary(png({ metadata: "layer=D", pixels, gamma: Buffer.from([0, 0, 177, 143]) })),
  "color interpretation chunks must remain part of the visual hash"
);

assert.strictEqual(X.computePngVisualHashFromBinary("not a png"), null,
  "invalid files must not participate in deduplication");

const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "ps-ui-png-dedup-"));
try {
  fs.writeFileSync(path.join(tempRoot, "a.png"), Buffer.from(samePixelsA, "latin1"));
  fs.writeFileSync(path.join(tempRoot, "b.png"), Buffer.from(samePixelsB, "latin1"));
  fs.writeFileSync(path.join(tempRoot, "different.png"), Buffer.from(
    png({ metadata: "layer=C", pixels: Buffer.from([8, 7, 6, 5]) }), "latin1"));

  const pending = ["a.png", "b.png", "different.png"].map((imagePath) => ({
    node: { imagePath },
  }));
  const stats = X.dedupPngsByHash(pending, { fsName: tempRoot }, { exportCache: null });

  assert.strictEqual(stats.dedupedCount, 1, "one metadata-only alias must be removed");
  assert.strictEqual(stats.uniqueCount, 2, "one canonical and one different image must remain");
  assert.strictEqual(pending[1].node.imagePath, "a.png",
    "duplicate layout entries must point at the canonical PNG");
  assert.strictEqual(fs.existsSync(path.join(tempRoot, "b.png")), false,
    "duplicate PNG must be removed from the export folder");
  assert.strictEqual(fs.existsSync(path.join(tempRoot, "different.png")), true,
    "visually different PNG must remain");
} finally {
  fs.rmSync(tempRoot, { recursive: true, force: true });
}

console.log("ok - PNG visual dedup ignores metadata and preserves visual differences");
