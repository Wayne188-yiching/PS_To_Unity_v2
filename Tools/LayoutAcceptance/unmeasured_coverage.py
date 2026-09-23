"""For nodes layout_vs_composite.py could not measure one by one, report how the rendered picture compares
with Photoshop inside each node's rect (text rects masked). A low difference there means the node is
covered by the full-scene check even though its own pixels could not be matched.

    python unmeasured_coverage.py <geometry.json> <PhotoshopExport dir> <render.png> <ps_composite.png> --out coverage.json
"""
import argparse
import json

import numpy as np
from PIL import Image


def walk(ns):
    for n in ns:
        yield n
        yield from walk(n.get("children", []))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("geometry")
    ap.add_argument("export_dir")
    ap.add_argument("render")
    ap.add_argument("composite")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    geometry = json.load(open(a.geometry, encoding="utf-8"))
    layout = json.load(open(a.export_dir + "/layout.json", encoding="utf-8-sig"))
    render = np.asarray(Image.open(a.render).convert("RGB")).astype(np.int16)
    ps = np.asarray(Image.open(a.composite).convert("RGB")).astype(np.int16)
    h, w, _ = ps.shape
    text = np.zeros((h, w), dtype=bool)
    for n in walk(layout["nodes"]):
        if n.get("type") == "text":
            text[max(0, n["y"] - 4):n["y"] + n["height"] + 4, max(0, n["x"] - 4):n["x"] + n["width"] + 4] = True
    diff = np.abs(render - ps).max(axis=2)
    rows = []
    for node in geometry["nodes"]:
        if node["verdict"] != "UNMEASURABLE":
            continue
        x, y, nw, nh = node["rect"]
        x0, y0, x1, y1 = max(0, x), max(0, y), min(w, x + nw), min(h, y + nh)
        region = np.zeros((h, w), dtype=bool)
        region[y0:y1, x0:x1] = True
        region &= ~text
        d = diff[region]
        rows.append({"node": node["node"].split("/")[-1], "why": node["why"], "rectPixelsCompared": int(region.sum()),
                     "meanDiffInRect": round(float(d.mean()), 2) if d.size else None,
                     "pctOver16InRect": round(float((d > 16).mean() * 100), 2) if d.size else None})
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"render": a.render, "unmeasurableCoverage": rows}, f, ensure_ascii=False, indent=1)
    for r in rows:
        print(f'{r["node"][:50]:50s} px={r["rectPixelsCompared"]:>8} meanDiff={r["meanDiffInRect"]} >16={r["pctOver16InRect"]}%')


if __name__ == "__main__":
    main()
