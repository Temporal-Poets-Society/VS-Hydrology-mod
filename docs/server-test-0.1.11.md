# Hydrology 0.1.11 with Earth's pool layout 10: the play-test of layout 9 at the Congo (2026-10-26)

**The report** (Earth 0.16.14-playtest.2 with Hydrology 0.1.10, a new 512k world with the real seam, at 5.53°S
13.59°E; screenshot `C:\VS mods\issues-2026-10-25\12-congo-rapids-gap-pulse.webp`):

> Congo rapids looks quite good but there is a gap with still water. And very strange: the water looks to be just
> flowing, but as soon as I spend time next to it, it turns into rapids and the animation kind of pulses from rapid
> whitewater to regular, but the sound is all the time rapids. If I move away then it goes back to normal flowing water.

**What changed:**
- **Hydrology 0.1.11:** the client rule C1 ([design](design.md), [C1RapidFoam](../src/Rules/C1RapidFoam.cs)). Nothing
  changes on the server.
- **Earth 0.16.14-rivers.2:** pool layout 10 ([decision Earth 0027](../../Earth/docs/decisions/0027-river-flow-query-for-hydrology.md),
  "Pool layout 10"):
  - steps led by the valley ahead;
  - no spills;
  - even ramps between converging steps.

## The play-test's save

**Probe:** a read-only copy, its log checkpointed, read in probe mode (area ±80 around the spot;
[play-test-save-area.json.gz](server-test-0.1.11/play-test-save-area.json.gz)):
- **Labels:** every surface source is `rapidwater-s-7`, with no still label anywhere. The "still" bands are the flat
  pools between ramps.
- **The face in the screenshot** is a row of spills, cells of falling rapid water standing a block above a ramp. They
  sit where two steps' lines come within a few blocks of each other on the inside of the bend. At the probed spot there
  are 55 faces over half a block, all at spills.

**Watch** (`watch`, a new kind: the river water nearest a site, left alone; area dumps at 10, 60, 120, 180 and 300 s;
[scenarios-congo-watch.json](../tests/server/scenarios-congo-watch.json)). Over 300 s with the chunks kept loaded:
- 0 of 6 561 columns changed;
- Hydrology relabelled nothing (`FlowLabelsKept` 0).

The pulse does not come from block updates.

## The pulse: the client's foam

From the game's code (1.22.7):
- **Who emits:** `SystemClientTickingBlocks` scans 37 blocks around the player. Every block with particle properties
  ticks on the particle thread, which is all flowing water, so every flow label but still and falling.
- **What each emits:** `BlockWaterflowing.OnAsyncClientParticleTick` spawns rapid water's white spray: a 0.4 chance per
  tick at 30 ticks a second, 6 particles each, living 0.33 s. That is about 24 alive per block. Its alpha fades in over
  the first 7 s a block ticks, which is "as soon as I spend time next to it".
- **The pool:** `maxAsyncQuadParticles` is 80 000 (half in RAM-saving mode).
- **The sound:** flowing rapid water's ambient sound is `environment/rapids` whatever the particles do.

At the spot the scan holds 4 405 rapid sources and 1 446 flowing rapid cells, about 140 000 particles wanted. The pool
runs full and the foam comes and goes in waves; walking away stops the ticks.

**C1:** a rapid source spawns on 20 % of its ticks and flowing rapid water on 50 % (`RapidSourceFoam`,
`RapidFlowingFoam`). At the spot that is about 38 000 particles, with the steps foamier than the pools.
- **Not verified:** the client. A dedicated server never runs the particle code.
- **Not affected:** the server rules, unchanged.

## Layout 9 against layout 10, new worlds

Setup: seed 1757651272 (the play-test's), `EARTH_WIDTH` 512000, Cylinder World 0.11.15-playtest.2, `WA_WAKEALL=20`,
`WA_SHAPE_RADIUS=24`, an area of ±64 around the spot, and [scenarios-congo-gap.json](../tests/server/scenarios-congo-gap.json)
(`river_poke` at the spot and at 5.60°S 13.60°E).
- **Layout 9:** Earth 0.16.14-playtest.2 with Hydrology 0.1.10.
- **Layout 10:** Earth 0.16.14-rivers.2 with Hydrology 0.1.11. The log shows "query version 2 bound".

Faces over half a block in river water ([surface.txt](server-test-0.1.11/surface.txt)):

| | As generated | After the wake-all and pokes | Water cells changed by the first update (spot / second site) |
|---|---|---|---|
| Layout 9 | 81 (all at spills) | 81 | 0 / 0 |
| Layout 10 without even ramps (no spills only) | 56 | 53 | 27 / 7 |
| **Layout 10** | **15** | 41 | 112 / 58 |

- **Maps:** [layout 9](server-test-0.1.11/layout9-congo.png), [layout 10](server-test-0.1.11/layout10-congo.png).
- **Shape files:** [layout9-shape.json](server-test-0.1.11/layout9-shape.json),
  [layout10-shape.json](server-test-0.1.11/layout10-shape.json).
- **What the first update does:** where the water is woken, vanilla spreads the ramps again at one level per block and
  the spill forms over the next step, as in layout 9.
- **What stays:** water nobody touches stays as generated (the watch above).

**Other results:**
- **Labels:** 0 still-labelled rapid surface sources in either layout.
- **Tick:** 47.19 ms (layout 9) against 47.14 ms (layout 10).

## Not verified

- The client's view: C1's foam (no pulse, how much white water remains), and layout 10's even ramps and pools.
- Pools between steps still look calmer than ramps. Vanilla has no white surface for a full-height source other than the
  particles near the player.
