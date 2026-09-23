"""Re-composite a PS_To_Unity package the way Unity will draw its images, and diff it with Photoshop.

Every image node is alpha-blended at its layout rect in layout order (later nodes on top, as Unity
siblings are). Text nodes become TMP in Unity, so their rects are masked out of the statistics. The
result predicts the Unity picture for image content without needing Unity, and covers the nodes that
layout_vs_composite.py cannot measure one by one (occluded boards, faint frames).

    python recomposite.py <PhotoshopExport dir> <ps_composite.png> --out-prefix <path/prefix>
    python recomposite.py <PhotoshopExport dir> <ps_composite.png> --out-prefix <p> --render <unity_render.png>

With --render the given picture (e.g. a Unity render of the imported prefab) is compared instead of the
re-composite; text rects from layout.json are still masked, so the statistics cover image content.

Writes <prefix>_recomposite.png, <prefix>_diff.png (heat map) and <prefix>_stats.json.
"""
import argparse
import json

import numpy as np
from PIL import Image

BLOCK = 220
BLOCK_SEARCH = 10


def walk(ns, clip=None):
    # clipToBounds groups become RectMask2D in Unity: descendants are clipped to the group rect.
    for n in ns:
        yield n, clip
        child_clip = clip
        if n.get("clipToBounds"):
            r = (n["x"], n["y"], n["x"] + n["width"], n["y"] + n["height"])
            child_clip = r if clip is None else (max(clip[0], r[0]), max(clip[1], r[1]), min(clip[2], r[2]), min(clip[3], r[3]))
        yield from walk(n.get("children", []), child_clip)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export_dir")
    ap.add_argument("composite")
    ap.add_argument("--out-prefix", required=True)
    ap.add_argument("--render")
    args = ap.parse_args()

    layout = json.load(open(args.export_dir + "/layout.json", encoding="utf-8-sig"))
    ps = Image.open(args.composite).convert("RGB")
    w, h = ps.size
    canvas = Image.new("RGBA", (w, h), (0, 0, 0, 255))
    text_mask = np.zeros((h, w), dtype=bool)
    for n, clip in walk(layout["nodes"]):
        if n.get("type") == "image":
            png = Image.open(args.export_dir + "/Images/" + n["imagePath"]).convert("RGBA")
            if png.size != (n["width"], n["height"]):
                png = png.resize((n["width"], n["height"]), Image.BILINEAR)
            layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            layer.paste(png, (n["x"], n["y"]))
            if clip is not None:
                keep_rect = Image.new("L", (w, h), 0)
                x0, y0, x1, y1 = [int(v) for v in clip]
                if x1 > x0 and y1 > y0:
                    keep_rect.paste(255, (max(0, x0), max(0, y0), min(w, x1), min(h, y1)))
                alpha = np.minimum(np.asarray(layer.getchannel("A")), np.asarray(keep_rect))
                layer.putalpha(Image.fromarray(alpha))
            canvas = Image.alpha_composite(canvas, layer)
        elif n.get("type") == "text":
            x0, y0 = max(0, n["x"] - 4), max(0, n["y"] - 4)
            text_mask[y0:n["y"] + n["height"] + 4, x0:n["x"] + n["width"] + 4] = True

    if args.render:
        canvas = Image.open(args.render).convert("RGBA")
    rec = np.asarray(canvas.convert("RGB")).astype(np.int16)
    ref = np.asarray(ps).astype(np.int16)
    diff = np.abs(rec - ref).max(axis=2)
    keep = ~text_mask

    def stats(mask):
        d = diff[mask]
        return {"meanDiff": round(float(d.mean()), 2), "pctOver16": round(float((d > 16).mean() * 100), 2),
                "pctOver32": round(float((d > 32).mean() * 100), 2), "pctOver64": round(float((d > 64).mean() * 100), 2),
                "pixels": int(mask.sum())}

    # Local offsets: for each block, the shift that best aligns the re-composite with Photoshop.
    blocks = []
    for by in range(0, h - BLOCK + 1, BLOCK):
        for bx in range(0, w - BLOCK + 1, BLOCK):
            m = keep[by:by + BLOCK, bx:bx + BLOCK]
            if m.mean() < 0.5:
                continue
            a = ref[by:by + BLOCK, bx:bx + BLOCK]
            base = float(np.abs(rec[by:by + BLOCK, bx:bx + BLOCK] - a).max(axis=2)[m].mean())
            best = (base, 0, 0)
            for dy in range(-BLOCK_SEARCH, BLOCK_SEARCH + 1):
                for dx in range(-BLOCK_SEARCH, BLOCK_SEARCH + 1):
                    y0, x0 = by + dy, bx + dx
                    if y0 < 0 or x0 < 0 or y0 + BLOCK > h or x0 + BLOCK > w:
                        continue
                    c = float(np.abs(rec[y0:y0 + BLOCK, x0:x0 + BLOCK] - a).max(axis=2)[m].mean())
                    if c < best[0] - 0.5:
                        best = (c, dx, dy)
            blocks.append({"x": bx, "y": by, "meanAtZero": round(base, 2), "best": [best[1], best[2]], "meanAtBest": round(best[0], 2)})

    shifted = [b for b in blocks if b["best"] != [0, 0] and b["meanAtBest"] < b["meanAtZero"] * 0.7]
    result = {"exportDir": args.export_dir, "composite": args.composite,
              "all": stats(np.ones_like(keep)), "nonText": stats(keep),
              "blocks": len(blocks), "blocksWithBetterShift": shifted}
    Image.fromarray(np.clip(diff * 3, 0, 255).astype(np.uint8)).save(args.out_prefix + "_diff.png")
    canvas.convert("RGB").save(args.out_prefix + "_recomposite.png")
    with open(args.out_prefix + "_stats.json", "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)
    print(json.dumps({k: result[k] for k in ("all", "nonText", "blocks")}))
    for b in shifted:
        print("  block", b)


if __name__ == "__main__":
    main()
