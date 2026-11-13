# Compares the wateraudit results of several runs: analyze.py <runs dir> [run ...] -> summary.json + analysis.txt (also printed)
# Each run dir holds wateraudit/{world,plan,profiles,wheels,lag,stats}.json, written by the wateraudit helper.
import json, os, sys
from collections import defaultdict

runs = sys.argv[1]
names = sys.argv[2:] or sorted(d for d in os.listdir(runs) if os.path.isdir(os.path.join(runs, d, "wateraudit")))


def load(run, f, default=None):
    p = os.path.join(runs, run, "wateraudit", f)
    return json.load(open(p)) if os.path.exists(p) else default


data = {n: {f[:-5]: load(n, f, {}) for f in ("world.json", "plan.json", "profiles.json", "wheels.json", "lag.json", "stats.json")} for n in names}
out = []
p = out.append

for n, d in data.items():
    w = d["world"]
    p(f"== {n}: earth={w.get('earth')} hydrology={w.get('hydrology')} noLiquidSourceTransport={w.get('noLiquidSourceTransport')} "
      f"(set={w.get('noLiquidSourceTransportSet')}) full bucket spills {w.get('fullBucketSpills')}")

# profiles, scenario by scenario, every run side by side
scenarios = []
for d in data.values():
    for s in d["profiles"]:
        if s not in scenarios: scenarios.append(s)
for s in scenarios:
    p("")
    plan = next((q for d in data.values() for q in d["plan"] if q["name"] == s), None)
    where = f" at {plan.get('lat')},{plan.get('lon')}" if plan and "lat" in plan else ""
    p(f"--- {s} ({plan['kind'] if plan else '?'}){where}")
    tags = []
    for d in data.values():
        for t in d["profiles"].get(s, {}):
            if t not in tags: tags.append(t)
    keys = []
    for d in data.values():
        for t in tags:
            for k in d["profiles"].get(s, {}).get(t, {}):
                if k not in keys: keys.append(k)
    for k in keys:
        p(f"  {k}:")
        for t in tags:
            row = []
            for n, d in data.items():
                v = d["profiles"].get(s, {}).get(t, {}).get(k)
                if v is None: continue
                if isinstance(v, dict): v = ", ".join(f"{a} {b}" for a, b in sorted(v.items()))
                row.append(f"{n}: {v}")
            if row: p(f"    {t:>5}  " + "  |  ".join(row))

# wheels: flow rate over time
p("")
p("--- wheels (flowRate / power source cells, every 4 s)")
for n, d in data.items():
    by = defaultdict(list)
    for r in d["wheels"]:
        by[r["name"]].append(r)
    for wname, rs in by.items():
        turning = sum(1 for r in rs if r.get("flowRate", 0) > 0)
        p(f"  {n} {wname}: turning in {turning} of {len(rs)} samples; " +
          " ".join(f"{r['t']}s:{r.get('flowRate', 0):.2f}/{r.get('sources', 0)}" for r in rs[:6]) + (" ..." if len(rs) > 6 else "") +
          f" last {rs[-1]['t']}s:{rs[-1].get('flowRate', 0):.2f}/{rs[-1].get('sources', 0)}")

# tick time per window
p("")
p("--- server tick (s): mean / p99 / max / ticks over 100 ms, per window since the previous sample")
for n, d in data.items():
    p(f"  {n}: " + "; ".join(f"{r['tag']} {r.get('mean', 0)*1000:.1f}/{r.get('p99', 0)*1000:.0f}/{r.get('max', 0)*1000:.0f} ms/{r.get('over100ms', 0)}" for r in d["lag"] if r.get("n")))
for n, d in data.items():
    rs = [r for r in d["lag"] if r.get("n") and r["tag"] != "ops"]
    tot = sum(r["n"] for r in rs)
    if tot:
        mean = sum(r["mean"] * r["n"] for r in rs) / tot
        p(f"  {n}: overall mean {mean*1000:.2f} ms over {tot} ticks, worst p99 {max(r['p99'] for r in rs)*1000:.0f} ms, ticks over 100 ms {sum(r['over100ms'] for r in rs)}")

# Hydrology's own counters
p("")
p("--- Hydrology counters (cumulative)")
for n, d in data.items():
    if not d["stats"]: continue
    last = d["stats"][-1]
    ms = last.get("Ticks", 0) / 1e7 * 1000  # Stopwatch ticks at 10 MHz on Windows
    p(f"  {n}: at {last['tag']}: " + ", ".join(f"{k} {v}" for k, v in last.items() if k not in ("tag", "Ticks")) + f", time in rules {ms:.1f} ms")

text = "\n".join(out)
print(text)
open(os.path.join(runs, "analysis.txt"), "w", encoding="utf-8").write(text + "\n")
json.dump(data, open(os.path.join(runs, "summary.json"), "w"), indent=1)
