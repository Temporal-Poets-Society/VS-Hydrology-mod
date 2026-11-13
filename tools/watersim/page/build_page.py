"""Builds the results page: the template with the study's data (scenes.json, scores.json) inlined.

    python build_page.py <study folder>   ->   <study folder>/results.html
"""
import json
import os
import sys

here = os.path.dirname(os.path.abspath(__file__))
study = sys.argv[1]
scenes = json.load(open(os.path.join(study, "scenes.json"), encoding="utf-8"))
scores = json.load(open(os.path.join(study, "scores.json"), encoding="utf-8"))

base = "hydrology-0.1.3"
rec = scenes["recommended"]
rec_cfg = next(r["config"] for r in scores["heavy"] if r["rules"] == rec)

NAMES = {
    "Survival": "survival", "SurvivalContained": "containment", "FallLabel": "fall label",
    "FallingFeedsSideways": "no side feed from falling water", "LevelRelax": "level relax",
    "WakeEdgesOnNewSource": "wake edges on a new source", "RapidSourceSheet": "rapid sources spread as sheets",
    "LoneVertical": "lone means no source above or below", "RefillOnSource": "refill on a source",
    "WakeOnFallLabel": "wake on the fall label",
}


def label(row):
    if row["rules"] == base:
        return "Hydrology 0.1.3", "base"
    if row["rules"] == "vanilla":
        return "Vanilla 1.22.7", "base"
    if row["rules"] == rec:
        return "Recommended", "rec"
    cfg = row["config"]
    diff = [k for k in rec_cfg if k != "Name" and cfg.get(k) != rec_cfg[k]]
    if len(diff) == 1:
        k = diff[0]
        if k == "SurvivalContained":
            return "Recommended, survival also needs containment", "abl"
        if k == "FallingFeedsSideways":
            return "Recommended, falling water feeds sideways again (vanilla)", "abl"
        return "Recommended without " + NAMES.get(k, k), "abl"
    return row["rules"], "abl"


rows = []
for r in scores["heavy"]:
    name, kind = label(r)
    rows.append({
        "name": name, "kind": kind, "score": r["total"], "fail": r["CheckFailures"], "loops": r["Loops"],
        "floating": r["ScriptFloating"] + r["CanonicalFloating"], "fuzz": r["Fuzz"], "fuzzChanged": r["FuzzChanged"],
        "pokes": r["Pokes"], "pokesSettled": r["PokesChangedCanonical"], "pokesEarth": r["PokesChangedGenerated"],
        "earthDiff": r["CanonicalDiff"], "gained": r["FuzzSourcesGained"], "lost": r["FuzzSourcesLost"],
        "updates": r["updates_per_edit"], "failures": r["failures"][:8],
        "scenes": r["scenes"],
    })
order = {"Recommended": 0, "Hydrology 0.1.3": 1, "Vanilla 1.22.7": 2}
rows.sort(key=lambda x: (order.get(x["name"], 3), x["score"]))

data = {"scenes": scenes, "scores": rows}
template = open(os.path.join(here, "results-template.html"), encoding="utf-8").read()
blob = json.dumps(data, separators=(",", ":")).replace("</", "<\\/")
out = template.replace("/*__DATA__*/null", blob)
path = os.path.join(study, "results.html")
open(path, "w", encoding="utf-8").write(out)
print(path, len(out) // 1024, "KB")
