# Hydrology 0.1.4: deep canals ("communicating vessels"), server test (2026-10-23)

**The question.** The user asked whether canals dug 2 or more blocks deep fill like 1-deep ones: large canals and
ports, without needing Earth's river data. The [study](water-rules-study-2026-10-23.md), section "Canals by width and
depth", found that they do not: H2 fills only cells resting on solid ground, so only a canal's bottom layer fills.

**The change (0.1.4).** A new case in H2, behind the config switch `FillVessels` in `ModConfig/hydrology.json`, off by
default:
- A flowing cell resting on a source of its liquid fills when a source beside it also rests on a source of that liquid.
- The 2 × 2 vertical square under and beside the cell is then all source water, one body, so the layer above takes its
  level.
- It reads 3 neighbouring cells, needs no river data, and works on any world.
- 1-wide channels keep flowing water: their bottom layer never fills, so nothing above it rests on a source.

**Why it is off by default.** Earth 0.16.x's 1-block river steps have no sill: the upper pool's lower layer touches
the lower pool below the step. The ramp cell below a step then looks the same as a canal's upper layer, and the step
floods. Earth would have to draw a sill under each 1-block step (the upper pool's edge column holds water only at its
top). That is a change in Earth's generation, not made here.

## Run

Two vanilla worlds with Hydrology 0.1.4, `FillVessels` on and off, 44 lab scenarios
([scenarios-vessels.json](../tests/server/scenarios-vessels.json)), 300 s each. Data:
[analysis.txt](server-test-0.1.4/analysis.txt) and [summary.json](server-test-0.1.4/summary.json).

```bash
HYDRO_CONFIG='{"FillVessels": true}' python tests/server/mkrun.py <abs run dir> vanilla hydro 42611 tests/server/scenarios-vessels.json
```

New harness options: `lab_trench` takes `Depth` and `PoolDepth`; `lab_step` takes `Touching` (pools touching below the
step, as Earth 0.16.x draws them); `mkrun.py` reads `HYDRO_CONFIG`.

## Results

Each canal is 20 long. The percentage is the share of its cells, all layers, that are sources at 300 s.

| Canal | From a pool 3 deep, off | From a pool 3 deep, on | From a pool 1 deep, off / on |
|---|---|---|---|
| 1 wide, 1–3 deep | flowing 6 blocks, 0 % | the same | the same |
| 2–3 wide, 1 deep | 100 % | 100 % | 100 % / 100 % |
| 2–3 wide, 2 deep (fresh, salt; dug opened last or outward) | 50 % (bottom layer) | **100 %** | 0 % / 0 % |
| 2–3 wide, 3 deep | 33 % | **100 %** (one salt run 98 %: one cell of the top layer still flowing at 300 s) | 0 % / 0 % |

| Other lab | Off | On |
|---|---|---|
| Step with a sill (the harness's usual lab step) | ramp 6..1 over the lower pool | the same |
| Step whose pools touch (Earth 0.16.x) | ramp 6..1 | **flooded**: the 48 ramp cells became sources, so the upper pool's level runs over the lower pool |
| Chute with sills (steps 3 apart) | rapid edges, rapid ramps | the same |
| Channel around a step | as 0.1.3 | the same |

Server tick: mean 47.06 ms on, 47.05 ms off; worst p99 49 ms in both; no tick over 100 ms. Hydrology's rules spent
155 ms over the 300 s with the switch on (71 ms off). 941 cells were filled on water.

These results match the simulator exactly (`watersim canals`, the column "+ vessels (local)").

## Not verified

- Earth worlds: no Earth build with sills exists yet, and with the switch on, Earth 0.16.x worlds' steps would flood.
- A canal from a bank only 1 deep: water pours in and runs off. Only a non-local rule could fill it (the study's basin
  fill with Earth's river query).
- The client's view.
