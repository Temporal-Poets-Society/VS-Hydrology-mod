# Hydrology 0.1.5: the study's settling rules, server test (2026-10-23)

**What changed.** The recommended rules of the [water rules study](water-rules-study-2026-10-23.md) are now in the
mod. Each is a switch in `ModConfig/hydrology.json`, on by default
([design](design.md), "H7, settling"):
- **H7:** survival (supported from below only), fall label, no side feed from falling water in the air, wake on the
  fall label, level relax;
- **H2:** refill on a source with 3 source sides;
- **H4:** rapid sources spread as sheets, a new source wakes the edges near it, a source above or below makes a rapid
  source not lone.

The vessels case is also on where the world-config attribute `hydrologyFillVessels` is true. Earth 0.16.8 (pool layout
5) sets it on new worlds whose steps have a sill. The config default stays off, so vanilla worlds and older Earth worlds
don't flood.

## Runs

All on vanilla worlds, 300 s each, 62 lab scenarios ([scenarios-labs.json](../tests/server/scenarios-labs.json)):
- the 18 labs of the earlier tests: trenches, steps, drops, chutes, the bucket, and the stones-in-a-waterfall lab;
- the 44 deep-canal labs of 0.1.4.

The comparison:
- 0.1.4 with vessels on;
- 0.1.5 with vessels on;
- 0.1.5 with defaults;
- 0.1.5 with vessels switched on only by the world attribute, set at start the way Earth sets it
  (`WA_VESSELS_ATTR=1`).

Data: [analysis.txt](server-test-0.1.5/analysis.txt).

## Results

| | 0.1.4 | 0.1.5 |
|---|---|---|
| Stones into a fall and out again | the curtain keeps flowing cells (`r6`), and a second column stands beside it (`e-5 d-6 d-6 d-6 e-6`) | the curtain is falling water all the way down, and the column beside it is gone |
| Chute ramps | end at the next step as `r3` | end with a falling spill cell `rd3`, as the study's Hydrology shape |
| Drops onto dry floor | ramps of 4 cells, stop at `r3` | ramps 6 to 1 (level relax) |
| Drop filled back to 1 block | the edge stays rapid (`R R`) | the edge turns back to ordinary water (`S S`) |
| Canals (vessels on) | 2+ wide, 2–3 deep: 100 % | the same |
| Trenches, steps, step channel, bucket | | unchanged |
| Step whose pools touch (Earth 0.16.x) | floods with vessels on | floods with vessels on; unchanged with defaults |

Load, with the final build (see "Found by the tests" for what changed on the way):

| | 0.1.4 | 0.1.5 |
|---|---|---|
| Mean server tick | 47.07 ms | 47.15 ms |
| Worst p99 | 50 ms | 49 ms |
| Ticks over 100 ms | 0 | 1–2, in the first 10 s while all 62 labs start at once (380–430 ms) |
| Water updates in 300 s | 17.5 k | 24.6–25.5 k |
| Hydrology's own time in the first 10 s | 96 ms | 165 ms |

Both versions report overloaded ticks of 600–800 ms while the harness builds its labs, before measuring starts.

**The slow tick at the start.** It appears in every 0.1.5 run, never in 0.1.4. It is not in Hydrology's own code: the
rules spent 165 ms in total over that whole 10-second window. The vanilla spreading ran about 35 % more updates in the
burst when all 62 labs open together.
- Switching off any single new rule leaves it (8 runs).
- Switching off survival, the side-feed rule, the fall-label wake, refill and lone-vertical together removes it.
- Cutting the rules' allocations did not.

Watch for it in play. Where nothing starts 62 water works at once, it should not show.

## Found by the tests and the reviews, and fixed

- **An update loop on vanilla rivulets** (server test). Level relax raised cells that vanilla's rapid single stream
  lowers on purpose. In the first 0.1.5 run updates rose to 79 k, and relax kept firing about 18 times a second to the
  end. The busiest cells were natural rivulets of the vanilla terrain, not the labs. Relax now skips updates that ran in
  vanilla's single-stream mode. Then the simulator's study results stayed the same, and the server's relax count stays
  flat after the first seconds.
- **The world attribute was never read** (Codex review). The once-a-second throttle started at `long.MinValue`, and
  the subtraction overflowed, so the attribute was never read. The first read now always happens. The attribute is
  read with `GetAsBool`, so a string "true" counts too.
- **Level relax and vanilla's lowering judged feeders differently** (Codex review). Relax used a simpler barrier test.
  It now takes its target from the same feed function as the lowering.
- **Allocations in the hot paths** (own review while chasing the slow tick). The label, feed, survival and fill
  checks now reuse block positions per thread instead of allocating one per call.
- **Water in a block with a closed floor** (Codex review, second pass). Water inside a waterlogged block whose own
  bottom face is closed, such as a chiseled block, is held up as far as vanilla is concerned. Survival, the falling test
  and the fall label now agree with that.

Nothing the reviews raised was rejected. Codex checked the Harmony signatures against vanilla's published source,
because it could not read the installed game DLL. The patches load and act in every server run.

## Earth 0.16.8 world (pool layout 5), 2026-10-24

The world: Earth `a0dd56a` (`earthmap-0.16.8.zip`), Cylinder World 0.11.14, Hydrology 0.1.5 with its default config
(vessels only through Earth's world attribute), seed 386217, 256 k wide.

At each of 7 sites the harness records the area (±14 blocks):
- as generated;
- after every water cell is woken once (`WA_WAKEALL=20`);
- at the end, after the site's poke (at the falls, stones into the curtain and out again).

Data: [earth-0.16.8-shape.json](server-test-0.1.5/earth-0.16.8-shape.json), with maps of each layer.

| Site | First update: water / labels | After the poke: water |
|---|---|---|
| Daugava step | 0 / 119 | 0 |
| Congo step | 229 / 11 | 0 |
| Congo rapids | 218 / 16 | 0 |
| Congo chute | 336 / 30 | 1 |
| Rhine chute | 80 (4 not labels) / 136 | 0 |
| Victoria fall | 0 / 180 | 15 (see below) |
| Niagara fall | 95 (39 not labels) / 205 | 8 (see below) |

- **What reshapes on the first update: Earth's stretched ramps.** At the Congo, Niagara and the rapids, Earth draws
  ramps with each height repeated over 2–3 cells (`11223344556`, `fffeeeddd`). Vanilla water keeps a flowing cell only
  beside a neighbour exactly one higher, so the first update rebuilds them as one cell per height (`123456`), the same
  everywhere. Where Earth's ramps are one cell per height (Daugava, Victoria, most of the Rhine), only labels change.
  The fix is on Earth's side: draw ramps 6, 5, 4, 3, 2, 1, one cell per height, at every world scale.
- **Labels.** Earth labels water with the river's direction; vanilla labels it from the neighbouring heights (`nw`
  becomes `n`, a still source becomes `se` at an edge). Only the look of the water changes.
- **After the poke.** Nothing reshapes at the steps and chutes.
  - At Victoria and Niagara, the harness's stone poke breaks every block at the stone positions on the way out, and
    3 and 2 of them were Earth's rock under the lip.
  - The lip sources above them lost their floor, and survival drained them, as it should.
  - This is a harness artefact: the harness should not break blocks it did not place.

## Not verified

- Earth worlds with Earth 0.16.8's sills and Hydrology shape: waiting for Earth's test build.
- The client's view.
