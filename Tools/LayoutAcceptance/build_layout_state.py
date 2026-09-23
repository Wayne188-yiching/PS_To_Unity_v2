"""Collect the mechanical layout evidence of one acceptance run into a compact state for Jev.

Everything here is measured by code (layout_vs_composite.py, recomposite.py, the Unity import result,
the Pipeline Validator). Jev only reads this text; it never sees pixels.

    python build_layout_state.py --geometry g.json --recomposite r_stats.json --unity u_stats.json
        [--import-result import1_result.json] [--validator v.json] [--extra extra.json] --label after --out state.json
"""
import argparse
import json
from collections import Counter


def load(path):
    return json.load(open(path, encoding="utf-8-sig")) if path else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--geometry", required=True)
    ap.add_argument("--recomposite", required=True)
    ap.add_argument("--unity", required=True)
    ap.add_argument("--import-result")
    ap.add_argument("--validator")
    ap.add_argument("--extra")
    ap.add_argument("--label", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    geometry = load(a.geometry)
    state = {
        "label": a.label,
        "geometryCheck": {
            "method": "each image node's opaque PNG pixels searched in the Photoshop composite; shift from its layout rect",
            "tolerancePx": geometry["tolerancePx"],
            "summary": geometry["summary"],
            "failures": [{"node": n["node"].split("/")[-1], "rect": n["rect"], "shift": [n["dx"], n["dy"]]}
                         for n in geometry["nodes"] if n["verdict"] == "FAIL"],
            "unmeasurable": [{"node": n["node"].split("/")[-1], "why": n["why"]}
                             for n in geometry["nodes"] if n["verdict"] == "UNMEASURABLE"],
        },
    }
    for key, path in (("imagesRecompositeVsPhotoshop", a.recomposite), ("unityRenderVsPhotoshop", a.unity)):
        s = load(path)
        state[key] = {"nonTextDiff": s["nonText"], "blocksCompared": s["blocks"],
                      "blocksWhereAShiftMatchesBetter": [{"x": b["x"], "y": b["y"], "shift": b["best"]} for b in s["blocksWithBetterShift"]]}
    if a.import_result:
        r = load(a.import_result)
        state["unityImport"] = {
            "status": r["status"],
            "autoNineSlice": [{"image": s["imagePath"], "decision": s["decision"],
                               "from": [s["originalWidth"], s["originalHeight"]], "to": [s["slicedWidth"], s["slicedHeight"]],
                               "borderLBRT": s["border"], "reconstructionMaxDiff": s["reconstructionMaxDiff"]} for s in r["autoSlices"]],
            "imagesWithoutNineSlice": sorted({i["imagePath"] for i in r["importedImages"]} - {s["imagePath"] for s in r["autoSlices"]}),
        }
    if a.validator:
        v = load(a.validator)
        codes = Counter(i["code"] for i in v["issues"])
        state["pipelineValidator"] = {"issueCodes": dict(codes),
                                      "note": "TMP_FONT_TOKEN_FALLBACK comes from the test project having no font map; all other checks are listed"}
    if a.extra:
        state.update(load(a.extra))
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=1)
    print(f"{a.out}: {len(json.dumps(state, ensure_ascii=False))} chars")


if __name__ == "__main__":
    main()
