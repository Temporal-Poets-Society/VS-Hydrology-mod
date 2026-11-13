# Hydrology 0.1.10 with Earth's pool layout 9: the play-test's river issues #3 and #4, server test (2026-10-25)

**The reports** (the user's play-test of Earth 0.16.14-playtest.1 with Hydrology 0.1.9, a 512k world):
- **#3** (the Ticino leaving Lake Maggiore, 45.71°N 8.66°E): "River here looks strange with rapids in two directions."
- **#4** (the Ticino at Bellinzona, 46.17°N 8.96°E): "rapids have breaks where they should not have and I would expect
  no jumps in water height. Same issue on Congo rapids that also missing rapid water in betweens."

**What changed** ([decision Earth 0027](../../Earth/docs/decisions/0027-river-flow-query-for-hydrology.md), "Pool layout
9"; [design](design.md), "Earth's river query"):
- **Earth 0.16.14-rivers.1, pool layout 9:**
  - steps one block at a time, at least 7 blocks apart where the valley allows, so each ramp reaches the next step;
  - a rapids reach's sources labelled with the river's flow;
  - the river beside a lake takes the lake's level by its centre line;
  - query version 2 with `RiverFlowAt`.
- **Hydrology 0.1.10:** `EarthRiverQueryVersion` 2. With a version 2 query, a surface source in a rapids reach carries
  the river's flow whenever vanilla relabels it (`QueryFlowLabels`, a postfix on `GetLiquidBlockId`). Nothing changes
  with a version 1 query (layout 8 worlds) or without Earth.

## Reproduction: the user's save and a new world

**The user's save** was read in probe mode on a copy (`REUSE_SAVE`, the new `WA_AREA`: every column's fluids in a square):
- **Bellinzona:** three 1-block steps 3 apart (203 → 200 over 13 blocks). Each ramp (w-7, 6, 5, 4) ends in a vertical
  face 5/8–7/8 block high where the next step's ramp starts at 6, a block lower. Between the chutes lie 40–60 blocks of
  `rapidwater-still-7`.
- **Sesto Calende:** the river is 198 on its west half and 197 on its east half for 30 blocks. Its step runs along the
  river, then turns across it (Earth's per-column lake level).
- **The Congo (-5.6°, 13.6°):** chutes of four steps 3 apart, with 45–50 rows of still rapid sources between them.

**A new world** with the installed builds reproduces the same water at all three places, number for number (row
`l8-generated` below). The faces and still pools come from generation, not from play. The settings: seed 1660372859,
`earthWorldSize` 512000, Cylinder World 0.11.15-playtest.2. New in mkrun: `SEED`, `EARTH_WIDTH`, which also sets the Earth
tab's `earthWorldSize`.

Generated chutes are rapid from world generation, not only after a poke: Earth draws the rapid blocks, and
`rapidwater-still-7` is in the probe of a save nobody had touched there.

## Results

The same world, sites and scenarios ([scenarios-rivers-3-4.json](../tests/server/scenarios-rivers-3-4.json): `river_poke`,
a new kind that pokes the river water nearest a site). Settings: `WA_WAKEALL=20`, `WA_SHAPE_RADIUS=24`, areas of ±48
and ±64 blocks.
- **Layout 8:** Earth 0.16.14-playtest.1 with Hydrology 0.1.9.
- **Layout 9:** Earth 0.16.14-rivers.1 with Hydrology 0.1.10. The log shows "Earth's river query version 2 bound (with
  the river's flow)".

**The water surface** ([surface.py](../tests/server/surface.py) on the area dumps; [surface.txt](server-test-0.1.10/surface.txt)).
- **What counts:** river water only, that is columns with a source in their top 3 cells; hillside rivulets don't count.
- **Faces:** neighbouring columns whose surfaces differ by more than 0.55 block. A ramp's step is 1/8 block, and a
  layout 9 step edge to the next pool is 2/8.

| | Layout 8 (save = new world) | Layout 9, generated | Layout 9, after wake-all and poke |
|---|---|---|---|
| Bellinzona: faces / largest | 19 / 0.88 | 0 / 0.38 | 0 / 0.25 |
| Sesto Calende: faces / largest | 10 / 0.88 | 0 / 0.25 | 0 / 0.25 |
| Congo: faces in rapid water | 134 | 34 | 34 |
| Rapid surface sources labelled still: Bellinzona, Sesto, Congo | 740, 1147, 7671 | 0, 0, 0 | 0, 0, 0 |
| Rapid surface sources with the river's flow | 8, 56, 152 | 723, 1284, 7591 | 725, 1283, 7592 |

Notes:
- **The Congo's remaining faces** are spill cells (falling rapid water a block above a ramp) at the few steps 5 or 4
  blocks apart, where the valley falls faster than a block per 7. The area also has 17 faces in a small pond beside the
  river (ordinary water), not counted above.
- **Maps** (12 px per block: colour is the water's height, numbers are levels, white dots are rapid sources):
  - layout 8: [Bellinzona](server-test-0.1.10/layout8-bellinzona.png), [Sesto](server-test-0.1.10/layout8-sesto.png),
    [Congo](server-test-0.1.10/layout8-congo.png);
  - layout 9: [Bellinzona](server-test-0.1.10/layout9-bellinzona.png), [Sesto](server-test-0.1.10/layout9-sesto.png),
    [Congo](server-test-0.1.10/layout9-congo.png).

**Shape test** ([layout8-shape.json](server-test-0.1.10/layout8-shape.json),
[layout9-shape.json](server-test-0.1.10/layout9-shape.json)): water cells changed (labels apart).

| Site | Layout 8: first update / after the poke | Layout 9: first update / after the poke |
|---|---|---|
| Bellinzona | 0 / 1 | 11 (a hillside rivulet north of the river running on) / 1 |
| Sesto Calende | 0 / 0 | 0 / 2 (the poked cell refilling as a ramp cell and the source under it) |
| Congo | 0 / 1 | 0 / 1 |
| Congo, upstream | 0 / 1 | 0 / 1 |

- **Labels in the first update:** in layout 9 they are sources under ramps turning still (520–822 per site). They can't
  be seen: the rule leaves covered sources to vanilla.
- **After the poke:** the poked cell and its neighbours are back to the river's flow. A first build without the river's
  flow, which kept the labels the sources had, left the sources around a poked hole pointing into it, and at Bellinzona
  a pair of falling-labelled sources. That is why `RiverFlowAt` was added.
- **Counter:** 39,223 relabels by the rule in 300 s; 48,656 updates in all.

**Tick time:** the mean after the first 30 s is 46.9 ms in layout 9 and 47.0 ms in layout 8. The longest tick falls in
the first 10 s (generation and the wake-all): 714 ms against 432 ms. Other servers of other sessions shared the machine.

## Regression

**The 61 vanilla labs** ([scenarios-labs.json](../tests/server/scenarios-labs.json), vessels on, no query), 0.1.9 against 0.1.10:
- **Results:** wheels 14.9, 14.9 and 7.2 in both; mean tick 46.79 / 46.86 ms; 25.4 k updates in both.
- **Labs that differ:** 3. `lab_drop2_wheel` and `lab_fall_stones` are known to vary. The 3-deep canal off a 1-deep
  pool ends with 5 or 6 flowing cells in its lowest layer. Two runs of 0.1.10 differ in the same way, so this is run to
  run.

**Fake query labs** ([scenarios-query.json](../tests/server/scenarios-query.json)):
- **Version 1:** 0.1.9 and 0.1.10 give the same final profiles in all 10 labs. The river wheel is 21.73 and the control
  7.24 in both.
- **Version 2:** `fakeriverquery` answers `RiverFlowAt` (east) from version 2, and the new `WA_PLAN_AREA` dumps each lab
  at the end. Every surface source of the three rapids labs carries the flow: `step_touch_rapids` 136 of 136,
  `chute_wheel_river` 72, `chute_s9_rapids` 78. The calm labs are as with version 1. The profiles are the same as with
  version 1.

## Earth, offline

- **Layouts 0, 7 and 8 unchanged:** the settled water at the three places and the Congo profile, at 512k
  (`SIZESCALE=auto`), are byte-identical to the play-test build.
- **Suites:** EarthMapTests fails only the 4M Dalmatia coast check, as the play-test build does. RiverIndexTests passes.
- **Profiles** (`profile` mode):
  - Bellinzona to Lake Maggiore: steps closer than 7 went from 7 of 16 (smallest gap 2) to 0 of 15.
  - 4000 blocks of the Congo: from 13 of 35 to 1 of 41 (a gap of 4), and its 6-block fall (y188 → y182) is gone.
  - No rises in either layout.
- **World audit at 512k:**

| | Layout 8 | Layout 9 |
|---|---|---|
| Drops of 2+ where the layout puts them (chute steps, curtains) | 4919 | 121 |
| Uphill spots | 79 | 72 |
| Other falls of 2+ | 106 | 113 |

  The uphill spots and other falls are rivers or lakes touching at different levels. Layout 9 moves them between
  reaches, but they are the same kind as layout 8's.

## Not verified

- The client's view:
  - how `rapidwater-<flow>-7` sources look across a whole reach;
  - the remaining spill cells;
  - boats and swimmers pushed along rapids reaches (rapid water's push is per flow label, so layout 9 pushes where
    layout 8's still sources did not).
- A water wheel on a layout 9 river.
- The H2 question on the overview card (fill on water) is not needed for this: natural river water is handled by the
  query since 0.1.8.
