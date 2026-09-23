"""Text placement check: Unity render vs Photoshop composite, per text node (mechanical).

Text pixels are isolated as the difference between each picture and the images-only re-composite
(recomposite.py), which already matches Photoshop outside text. For every text node the two text masks
are compared inside the node rect (+pad): best shift by mask overlap, and the width/height of the ink box.

    python text_vs_composite.py <PhotoshopExport dir> <ps_composite.png> <unity_render.png> <images_recomposite.png> --out text.json
"""
import argparse
import json

import numpy as np
from PIL import Image

PAD = 24
INK = 48          # max-channel difference from the images-only picture that counts as text ink
MIN_INK = 40
SEARCH = 20


def walk(ns, path=""):
    for n in ns:
        p = path + "/" + n["name"]
        yield p, n
        yield from walk(n.get("children", []), p)


def box(mask):
    ys, xs = np.nonzero(mask)
    return None if len(xs) == 0 else (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("export_dir")
    ap.add_argument("composite")
    ap.add_argument("render")
    ap.add_argument("recomposite")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    layout = json.load(open(a.export_dir + "/layout.json", encoding="utf-8-sig"))
    ps = np.asarray(Image.open(a.composite).convert("RGB")).astype(np.int16)
    un = np.asarray(Image.open(a.render).convert("RGB")).astype(np.int16)
    rec = np.asarray(Image.open(a.recomposite).convert("RGB")).astype(np.int16)
    h, w, _ = ps.shape
    ps_ink = np.abs(ps - rec).max(axis=2) > INK
    un_ink = np.abs(un - rec).max(axis=2) > INK
    rows = []
    for path, n in walk(layout["nodes"]):
        if n.get("type") != "text":
            continue
        x0, y0 = max(0, n["x"] - PAD), max(0, n["y"] - PAD)
        x1, y1 = min(w, n["x"] + n["width"] + PAD), min(h, n["y"] + n["height"] + PAD)
        p, u = ps_ink[y0:y1, x0:x1], un_ink[y0:y1, x0:x1]
        row = {"node": path.split("/")[-1], "text": (n.get("text") or "")[:24], "rect": [n["x"], n["y"], n["width"], n["height"]],
               "fontSize": n.get("fontSize"), "fontToken": n.get("fontToken")}
        if p.sum() < MIN_INK or u.sum() < MIN_INK:
            row["verdict"] = "UNMEASURABLE"
            row["why"] = "no text ink found (clipped, hidden or same colour as background)"
            rows.append(row)
            continue
        best = (-1.0, 0, 0)
        for dy in range(-SEARCH, SEARCH + 1):
            for dx in range(-SEARCH, SEARCH + 1):
                shifted = np.zeros_like(u)
                ys, xs = np.nonzero(u)
                ys2, xs2 = ys + dy, xs + dx
                ok = (ys2 >= 0) & (ys2 < u.shape[0]) & (xs2 >= 0) & (xs2 < u.shape[1])
                shifted[ys2[ok], xs2[ok]] = True
                inter = (shifted & p).sum()
                union = (shifted | p).sum()
                iou = inter / union if union else 0.0
                if iou > best[0] + 1e-9 or (abs(iou - best[0]) <= 1e-9 and abs(dx) + abs(dy) < abs(best[1]) + abs(best[2])):
                    best = (iou, dx, dy)
        bp, bu = box(p), box(u)
        row.update({
            "unityToPsShift": [best[1], best[2]], "maskIoUAtBest": round(best[0], 3),
            "inkBoxPs": [bp[2] - bp[0], bp[3] - bp[1]], "inkBoxUnity": [bu[2] - bu[0], bu[3] - bu[1]],
            "inkWidthRatio": round((bu[2] - bu[0]) / max(1, bp[2] - bp[0]), 3),
            "verdict": "PASS" if abs(best[1]) <= 2 and abs(best[2]) <= 2 else "FAIL",
        })
        rows.append(row)
    measured = [r for r in rows if r["verdict"] != "UNMEASURABLE"]
    summary = {
        "PASS": sum(r["verdict"] == "PASS" for r in rows), "FAIL": sum(r["verdict"] == "FAIL" for r in rows),
        "UNMEASURABLE": sum(r["verdict"] == "UNMEASURABLE" for r in rows),
        "medianAbsShift": [float(np.median([abs(r["unityToPsShift"][0]) for r in measured])) if measured else None,
                           float(np.median([abs(r["unityToPsShift"][1]) for r in measured])) if measured else None],
        "medianInkWidthRatio": float(np.median([r["inkWidthRatio"] for r in measured])) if measured else None,
    }
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"summary": summary, "nodes": rows}, f, ensure_ascii=False, indent=1)
    for r in rows:
        if r["verdict"] == "UNMEASURABLE":
            print(f'UNMEASURABLE {r["node"][:28]:28s} {r["text"]}')
        else:
            print(f'{r["verdict"]:12s} {r["node"][:28]:28s} {r["text"][:16]:16s} size={r["fontSize"]} shift={r["unityToPsShift"]} IoU={r["maskIoUAtBest"]} '
                  f'ink PS={r["inkBoxPs"]} Unity={r["inkBoxUnity"]} widthRatio={r["inkWidthRatio"]}')
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
