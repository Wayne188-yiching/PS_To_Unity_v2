"""Layout-vs-Photoshop geometry check (mechanical, no model involved).

The Pipeline Validator compares the Unity prefab with layout.json; both come from the same export, so it
cannot see a rect that was wrong from the start. This check closes that gap: for every image node it takes
the exported PNG's opaque pixels and searches where those pixels actually sit in the Photoshop composite
of the same PSD. The offset from the node's rect is the layout error Unity will reproduce.

    python layout_vs_composite.py <PhotoshopExport dir> <ps_composite.png> [--out result.json] [--tolerance 1]

Only the node's pixels that stay visible in the composite are used: pixels covered by any later (upper) node,
by a text rect, or clipped away by a clipToBounds ancestor are removed from the template, so partly covered
boards and frames can still be measured.

Verdict per node:
    PASS          matched, |dx|,|dy| <= tolerance
    FAIL          matched confidently somewhere else
    UNMEASURABLE  too few opaque pixels, or no confident match (occluded / blended / flat art)
Exit code 1 if any node FAILs.
"""
import argparse
import json
import sys

import numpy as np
from PIL import Image

MIN_OPAQUE = 200          # opaque pixels needed to trust a template
OPAQUE_ALPHA = 230        # template pixels must be essentially opaque to equal the composite
MATCH_MEDIAN = 6          # median per-pixel max-channel difference (0-255) that counts as "the same pixels"
SEARCH = 60               # +- px searched around the rect
SAMPLE = 5000


def nodes(ns, path="", clip=None):
    for n in ns:
        p = path + "/" + n["name"]
        yield p, n, clip
        child_clip = clip
        if n.get("clipToBounds"):
            r = (n["x"], n["y"], n["x"] + n["width"], n["y"] + n["height"])
            child_clip = r if clip is None else (max(clip[0], r[0]), max(clip[1], r[1]), min(clip[2], r[2]), min(clip[3], r[3]))
        yield from nodes(n.get("children", []), p, child_clip)


def coverage_masks(export_dir, ordered, h, w):
    """covered_after[i]: canvas pixels drawn by any node after node i (any alpha) or by any text rect."""
    drawn = []
    text = np.zeros((h, w), dtype=bool)
    for _, n, clip in ordered:
        m = np.zeros((h, w), dtype=bool)
        if n.get("type") == "image":
            a = np.asarray(Image.open(export_dir + "/Images/" + n["imagePath"]).convert("RGBA"))[:, :, 3] > 0
            x0, y0 = n["x"], n["y"]
            ys, xs = np.nonzero(a)
            ys, xs = ys + y0, xs + x0
            ok = (ys >= 0) & (ys < h) & (xs >= 0) & (xs < w)
            if clip is not None:
                ok &= (xs >= clip[0]) & (xs < clip[2]) & (ys >= clip[1]) & (ys < clip[3])
            m[ys[ok], xs[ok]] = True
        elif n.get("type") == "text":
            text[max(0, n["y"] - 4):n["y"] + n["height"] + 4, max(0, n["x"] - 4):n["x"] + n["width"] + 4] = True
        drawn.append(m)
    after = [None] * len(drawn)
    acc = text.copy()
    for i in range(len(drawn) - 1, -1, -1):
        after[i] = acc.copy()
        acc |= drawn[i]
    return after


def measure(composite, png, x0, y0, rng, covered=None, clip=None):
    h, w, _ = composite.shape
    a = np.asarray(png.convert("RGBA")).astype(np.int16)
    opaque = a[:, :, 3] > OPAQUE_ALPHA
    if covered is not None or clip is not None:
        ys_all, xs_all = np.nonzero(opaque)
        gy, gx = ys_all + y0, xs_all + x0
        inside = (gy >= 0) & (gy < h) & (gx >= 0) & (gx < w)
        keep = inside.copy()
        if covered is not None:
            keep[inside] &= ~covered[gy[inside], gx[inside]]
        if clip is not None:
            keep &= (gx >= clip[0]) & (gx < clip[2]) & (gy >= clip[1]) & (gy < clip[3])
        opaque = np.zeros_like(opaque)
        opaque[ys_all[keep], xs_all[keep]] = True
    ys, xs = np.where(opaque)
    if len(ys) < MIN_OPAQUE:
        return None
    if len(ys) > SAMPLE:
        k = rng.choice(len(ys), size=SAMPLE, replace=False)
        ys, xs = ys[k], xs[k]
    tpl = a[ys, xs, :3]

    def cost(dx, dy):
        yy, xx = ys + y0 + dy, xs + x0 + dx
        ok = (yy >= 0) & (yy < h) & (xx >= 0) & (xx < w)
        if ok.sum() < MIN_OPAQUE // 2:
            return 1e9
        return float(np.median(np.abs(composite[yy[ok], xx[ok]] - tpl[ok]).max(axis=1)))

    # Flat art (a solid pill, a plain bar) matches equally well at many offsets. Among offsets that are
    # within 1 of the best cost, prefer the one closest to the rect -- a tie is not evidence of a shift.
    def pick(cands):
        best = min(c[0] for c in cands)
        return min((c for c in cands if c[0] <= best + 1), key=lambda c: (abs(c[1]) + abs(c[2]), c[0]))

    at_rect = cost(0, 0)
    coarse = pick([(cost(dx, dy), dx, dy) for dy in range(-SEARCH, SEARCH + 1, 3) for dx in range(-SEARCH, SEARCH + 1, 3)] + [(at_rect, 0, 0)])
    fine = pick([(cost(dx, dy), dx, dy) for dy in range(coarse[2] - 3, coarse[2] + 4) for dx in range(coarse[1] - 3, coarse[1] + 4)] + [(at_rect, 0, 0)])
    return {"dx": fine[1], "dy": fine[2], "medianAtBest": fine[0], "medianAtRect": at_rect, "samples": int(len(ys))}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export_dir")
    ap.add_argument("composite")
    ap.add_argument("--out")
    ap.add_argument("--tolerance", type=int, default=1)
    args = ap.parse_args()

    layout = json.load(open(args.export_dir + "/layout.json", encoding="utf-8-sig"))
    composite = np.asarray(Image.open(args.composite).convert("RGB")).astype(np.int16)
    rng = np.random.default_rng(0)
    rows = []
    ordered = list(nodes(layout["nodes"]))
    h, w, _ = composite.shape
    covered_after = coverage_masks(args.export_dir, ordered, h, w)
    for index, (path, n, clip) in enumerate(ordered):
        if n.get("type") != "image":
            continue
        row = {"node": path, "imagePath": n["imagePath"], "rect": [n["x"], n["y"], n["width"], n["height"]]}
        m = measure(composite, Image.open(args.export_dir + "/Images/" + n["imagePath"]), n["x"], n["y"], rng,
                    covered_after[index], clip)
        if m is None:
            row["verdict"] = "UNMEASURABLE"
            row["why"] = "fewer than %d visible opaque pixels (covered by upper layers, clipped, or semi-transparent art)" % MIN_OPAQUE
        else:
            row.update(m)
            if m["medianAtBest"] > MATCH_MEDIAN:
                row["verdict"] = "UNMEASURABLE"
                row["why"] = "no confident match (occluded or blended in the composite)"
            elif abs(m["dx"]) <= args.tolerance and abs(m["dy"]) <= args.tolerance:
                row["verdict"] = "PASS"
            else:
                row["verdict"] = "FAIL"
        rows.append(row)

    summary = {v: sum(1 for r in rows if r["verdict"] == v) for v in ("PASS", "FAIL", "UNMEASURABLE")}
    result = {"exportDir": args.export_dir, "composite": args.composite, "tolerancePx": args.tolerance,
              "matchMedian": MATCH_MEDIAN, "summary": summary, "nodes": rows}
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
    for r in rows:
        if r["verdict"] == "UNMEASURABLE" and "dx" not in r:
            print(f'{r["verdict"]:13s} {r["node"][-60:]:60s} {r["why"]}')
        else:
            print(f'{r["verdict"]:13s} {r["node"][-60:]:60s} rect=({r["rect"][0]},{r["rect"][1]}) shift=({r["dx"]:+d},{r["dy"]:+d}) '
                  f'median {r["medianAtBest"]:.0f} (at rect {r["medianAtRect"]:.0f})')
    print(json.dumps(summary))
    return 1 if summary["FAIL"] else 0


if __name__ == "__main__":
    sys.exit(main())
