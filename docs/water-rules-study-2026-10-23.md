# Water rules study (2026-10-23)

**Why.** The user tested Hydrology 0.1.3 at Victoria Falls on 2026-10-23 and found two problems:
- placing and removing blocks at the lip still builds standing water columns;
- any block update reshapes Earth's generated water.

The user asked for local, Game-of-Life-like rules ("water needs either water on sides or block or support"), found by a
parametric study, with only the final scenes to check by eye.

**What was done.** An offline simulator ([tools/watersim](../tools/watersim)) ports vanilla 1.22.7's
`BlockBehaviorFiniteSpreadingLiquid` and the server's event model, adds Hydrology 0.1.3's rules H1–H6, and adds the
candidate rules as switches. A greedy search over the switches ran on 12 scenes built the way Earth 0.16.5 generates
rivers (pool layout 3). The result was then checked with 3 seeds × 1000 random edit sequences per scene, plus pokes.
- Results page (3D): https://claude.ai/artifact/Uud53ynJ865mmptAompGAw, built from
  [watersim-2026-10-23/results.html](watersim-2026-10-23/results.html).
- Data: [heavy.md](watersim-2026-10-23/heavy.md), [scores.json](watersim-2026-10-23/scores.json), the three search logs
  and [light-round.md](watersim-2026-10-23/light-round.md).

Nothing here was run in the game. Hydrology's mod code is unchanged.

## The simulator reproduces the known behaviours

`watersim calibrate`:

| Known in game | Simulator |
|---|---|
| Vanilla: a placed source spreads 6 blocks (heights 6..1) | reach 6, heights 7 6 5 4 3 2 1 |
| Vanilla: poking an Earth river step drains the lower pool (2026-10-22: 48–153 sources) | 28 pool sources lost from one block placed in and taken out of the ramp |
| 0.1.3: the same poke changes nothing | 0 cells changed |
| 0.1.3: 2-wide trenches fill, fresh and salt, opened last or dug outward; 1-wide stays flowing | all four fill; 1-wide stays flowing |
| 0.1.3: chute edges go rapid when touched; the wheel cell is rapid flowing water | all 12 edge sources rapid; both wheel cells rapid flowing |
| 0.1.2 / 0.1.3: stones in and out of a waterfall leave water standing in the air | 0.1.2: 14 standing cells + 2 sources in the air; 0.1.3: 17–20 standing cells + 3 sources in the air |

The event model follows the decompiled server code:
- an update is one pending callback per cell, and a later registration replaces it (time and handler);
- due callbacks run once per 33 ms tick;
- a 100 ms pulse runs queued neighbour updates, then wakes every cell a bulk commit changed (only that cell);
- a player's place or break wakes the cell, its 6 neighbours and the 4 cells beside the one above;
- direct writes (`world.BlockAccessor.SetBlock`) wake nothing.

Vanilla's quirks are kept: the shared BFS queue, the source rule reached only without a downward path, and the label
function with its diagonal neighbours.

## What goes wrong with 0.1.3

The edit sequences that do not come back fall into four kinds:
1. **H2 fills on a temporary floor.** A stone put under flowing water between sources lets H2 make a source there. Once
   the stone is gone, the source hangs in the air. Vanilla never lowers sources, so it feeds a column below it for good.
   This is the Victoria bug. At falls it also produced update loops that never stopped (217 runs in the heavy round).
2. **Falling water feeds sideways.** A curtain cell of height 6 holds up a height-5 cell beside it, which falls on its
   own: a second curtain one block out, which stays.
3. **Rapid kinds stick.** A passing hole makes a source an H4 edge, and nothing re-checks it when the hole fills.
   Rapid sources also spread in one stream, so a 2-wide channel beside rapids does not fill.
4. **Ramp heights depend on update order** (±1), and a knocked-out lip cell refills as flowing water, so the lip recedes.

## Ranking

Heavy round: 3 seeds × (1000 sequences + up to 400 pokes) per scene, 12 scenes. The score adds:
- 1000 per failed function check and 500 per run that never stopped;
- 50 per cell of water in the air after a script, plus water in the air per poke or sequence;
- the share of pokes and undone sequences that change the water;
- the cells by which Earth's shape differs from what the rules settle to.

| Rule set | Score | Checks failed | Never stopped | Water in the air | Sequences changed | Pokes changed (settled / Earth shape) |
|---|---|---|---|---|---|---|
| **Recommended** | **501** | **0** | **0** | **0** | **3.7 %** | **3 / 757 of 11238** |
| recommended, a rapid source with a source above or below can still be lone | 502 | 0 | 0 | 0 | 4.1 % | 3 / 757 |
| recommended without rapid source sheets | 504 | 0 | 0 | 0 | 4.5 % | 3 / 757 |
| recommended without refill on a source | 508 | 0 | 0 | 0 | 6.0 % | 3 / 757 |
| recommended without waking on the fall label | 511 | 0 | 0 | 0 | 7.2 % | 3 / 757 |
| recommended without waking edges on a new source | 513 | 0 | 0 | 0 | 5.5 % | 3 / 757 |
| recommended without level relax | 3564 | 3 | 0 | 0 | 9.3 % | 11 / 757 |
| recommended, falling water feeds sideways again (vanilla) | 4018 | 3 | 1 | 0 | 9.2 % | 3 / 757 |
| recommended without survival | 7002 | 6 | 0 | 8 | 5.7 % | 0 / 757 |
| recommended, survival also needs containment | 13507 | 0 | 26 | 0 | 4.7 % | 31 / 801 |
| recommended without the fall label | 16544 | 12 | 1 | 57 | 8.2 % | 3 / 757 |
| Vanilla 1.22.7 | 61005 | 48 | 0 | 183 | 23.3 % | 10 / 745 |
| Hydrology 0.1.3 | 122996 | 12 | 217 | 30 | 13.7 % | 8 / 757 |

The full table, with each scene, is in [heavy.md](watersim-2026-10-23/heavy.md). Two greedy searches were run, one from
0.1.3 and one from the user's rules. The search from 0.1.3 stopped at a local optimum (score 2284) without survival. The
search from the user's rules found the recommended set.

## Recommended rules

Keep H1–H6 as in 0.1.3 and add these. All are local, run inside a cell's own update, and wake only that cell and its
neighbours. The bounded search they use (H4's edge window) is the one that already exists.

| Rule | Switch | What it does |
|---|---|---|
| Survival | `Survival`, without containment | A source stays a source only while solid ground or a source of its liquid is right under it; otherwise it becomes flowing water (height 6) and wakes itself and its neighbours. This is the user's survival rule. |
| Fall label | `FallLabel` | In vanilla's label function (`GetLiquidBlockId`), non-source water that rests on neither ground nor a source is labelled falling (`d`). |
| No side feed from falling water | `FallingFeedsSideways = false` | In `GetMaxNeighbourLiquidLevel`, a falling neighbour still in the air (nothing solid and no source under it) does not feed a cell beside it, so water in a curtain feeds only the cell under it. Falling water that has landed feeds its sides as in vanilla. The first version also excluded landed water; that looped on a long run (later targets, below). |
| Wake on the fall label | `WakeOnFallLabel` | When vanilla's label update (`updateOwnFlowDir`) turns a cell falling, it no longer feeds its sides, so it wakes its flowing neighbours lower than itself. Vanilla writes labels without waking anyone, so without this a stray column beside a curtain is never re-checked (found in the playback, second round). |
| Level relax | `LevelRelax` | After the update, a flowing cell lower than its feeders allow is raised (6 with water above it, else one less than its highest feeder), unless vanilla just lowered it. |
| Refill on a source | `RefillOnSource = 3` | A flowing cell on a same-liquid source with 3 source sides becomes a source. This is vanilla's own threshold, but checked after the update, because vanilla never reaches it beside a drop. |
| Rapid sources as sheets | `RapidSourceSheet` | A rapid source with a source beside it spreads to all sides, like the H4 sheets. Lone rapid springs keep vanilla's single stream. |
| Wake edges on a new source | `WakeEdgesOnNewSource` | A cell that has just become a source (H2, refill, vanilla's rule) wakes the edges within H4's reach. |
| Not lone with water above or below | `LoneVertical` | A rapid source with a source above or below it is not a lone spring, so H4 may revert it (shafts). |

The following were tried and left out:
- **Containment**, the user's second survival condition (each side solid, a source or water it flows into). It adds
  nothing once the other rules are in. Without them it drains pool sources beside a freshly dug cell that a
  single-stream rapid source had not reached yet, and it caused 26 runs that never stopped.
- **Feeding only from a source or falling water above** (`AboveFeeds`). No effect with the fall label.
- **Filling on any source** (`FillOnSource`) floods steps, as in the 0.1.0 server test.
- **Birth conditions for survival** (`SurvivalBirth`). No effect.
- **H4 reach 4 or 5** scores slightly better (189 vs 192). The scenes run along one axis, so reach 6 stays for diagonal
  rivers.

## What Earth should generate

**Seen in game** (Niagara Falls, Earth 0.16.5 and Hydrology 0.1.3, reported 2026-10-23): ordinary sources stand at the
lip without rapids, and rapids form once a player touches something. The simulator shows the same. Under the
recommended rules, Earth 0.16.5's 1-block step stays as generated (only the edge sources' labels change). Its chutes and
falls do not: the first update changes 30 cells of the chute and 54 of the fall, and 108 of 420 chute pokes and 80 of
428 fall pokes change cells.

**The shape that stays as generated.** `watersim verify-shape` draws Earth's drops as listed below, wakes every
fluid cell once, and pokes every site beside the water (1,796 pokes over the step, chute, fall and Victoria scenes).
Under the recommended rules nothing changes, labels included. The only exception is the label of sources under water,
which vanilla sets from the order of updates and a player cannot see. Under 0.1.3 the same blocks also stay; there only
the labels of 12–16 cells at the top of the curtains and lips differ, because 0.1.3 has no fall label.

The blocks, with `<dir>` the river's flow direction as EarthWorldGen.FlowDirIndex gives it (n, ne, e, …):

| Where | Earth 0.16.5 | Generate |
|---|---|---|
| **Curtain** (drop of 5 or more, or a listed waterfall) | | |
| Lip: every upper-pool source beside the curtain column, at every level the curtain reaches (both layers of the 2-deep lip) | `water-still-7` | `rapidwater-<dir>-7` |
| Curtain, from one above the plunge pool up to the upper water's level, top cell included | `water-d-6`, top cell `rapidwater-<dir>-6` | `rapidwater-d-6` |
| Curtain's bottom cell, resting on the plunge pool | `water-d-6` | `rapidwater-<dir>-6` |
| Ramp after it, on the plunge pool | `rapidwater-<dir>-5` … `-1` | unchanged |
| **Chute step** (1-block steps of a 2–4 block drop) | | |
| Step edge: the upper segment's top source beside the ramp (the layers below touch the lower water and stay ordinary) | `water-still-7` | `rapidwater-<dir>-7` |
| Ramp on the lower segment | `rapidwater-<dir>-6, -5, -4` | unchanged |
| Spill: above the first ramp cell of the next segment, where a ramp cell of height h ≥ 3 ends beside it | air | `rapidwater-d-<h−1>` (`-d-3` with Earth's 6, 5, 4 ramps) |
| **Ordinary 1-block step** | | |
| Step edge: the upper pool's top source beside the ramp | `water-still-7` | `water-<dir>-7` (label only) |

The study assumes the lower water spans the whole curtain. Parts of a lip above dry cliff (seen at Niagara) are an
Earth layout question and were not modelled.

## Remaining risks

- **3.7 % of undone sequences still change something.** Most are one of:
  - a ramp one height off;
  - a source made on a floor that a player put in and took away, which moves a step or a lip edge by one cell (a fill
    on a real floor is what H2 is for, and no local rule can tell the floor was temporary);
  - a dead-end channel beside a ramp that stays dry until something else disturbs it, because vanilla spreads water
    only toward a drop when one is near.

  None of these leave water in the air or updates running.
- **Simulator only.** The port is written from the decompiled behaviour and matches the known in-game results above.
  The server harness (`tests/server`) still has to confirm each rule, especially:
  - the fall label, which changes vanilla's label function;
  - level relax, which writes levels vanilla never writes.
- **Not modelled:** diagonal rivers, chunk borders, ice, plants and other replaceable blocks, water wheel blocks,
  lava collisions, and the client's view.
- **A cell raised by vanilla does not wake the cells it feeds.** In the Hydrology-shape chute, a stone in a ramp leaves the spill cell one height low. A rule that wakes lower neighbours (`WakeLowerSides`) fixes it but scored the same overall, so it is not in the set.
- **Survival with a source below** keeps a source that sits on a pool's water. That is right for pools, and it is also
  why a step can move by one cell after a temporary floor.

## Playback and synthetic cases (second round, 2026-10-23)

The results page replays every scene over simulated time: a frame at each 100 ms pulse that changed something, and
one after each edit, with the changed cells outlined. `watersim play <scene> <rules> --frames <folder>` writes the same
frames as text sections. The new cases:

| Case | Recommended | 0.1.3 | Vanilla |
|---|---|---|---|
| **Lip over a partial basin.** An 8-wide lip over a 4-wide plunge pool; the other 4 columns drop onto dry ground at the pool's level. | The generated lip stands beside air until a poke wakes it. Then it pours down and runs over the dry ground as flowing water, fed for good: 96 cells, nothing in the air. | Same: 95 cells. | Same; on its first update vanilla also drains the lower pool. |
| **Lake rim beside a lower river** (pool layout 4, modelled from its description). A lake at 20, a 2-block rim at 21, a river at 9, an 11-block curtain at the drop only. Pokes at the curtain and on the rim. | The rim never leaks and the curtain stays at the drop. The only change is the rapid lip and curtain. | Rim holds, but 2 standing cells and 1 source in the air at the curtain. | Rim holds; 2 standing cells, 2 lake sources lost. |
| **Chute, Earth's blocks vs the Hydrology shape.** A stone in a ramp at 3 s, out at 6 s. | Earth's blocks: 30 cells change (edges turn rapid, spill cells appear). Hydrology shape: 1 cell (the spill one height low, see risks). | Earth's blocks: 29 cells. Hydrology shape: 0. | — |
| **Fall, Earth's blocks vs the Hydrology shape.** A stone in the curtain at 3 s, out at 6 s. | Earth's blocks: 54 cells change. Hydrology shape: 0. | Earth's blocks: 59 cells and 3 standing cells. Hydrology shape: 6 cells and 3 standing cells. | — |
| **Dam.** A wall across the river below a step, built, left 25 s, taken down. | Back exactly as generated. | Back exactly. | 54 pool sources lost. |
| **Bucket.** Pours on flat ground, into a 2×2 pit, a dry 2-wide trench, and a hole beside a pond. | H6: each bucket spills `water-still-3`, which runs out and dries up; no source made; the hole beside the pond holds the pond's flowing water. | Same. | Vanilla's bucket places sources, which stay and spread (95 cells). |

Found in the playback and fixed:
- **A stray falling column beside a curtain.** A stone in the curtain made the water above it spill one block out. When
  the stone came out, the curtain cell turned back to falling water by a label change, which wakes nobody, so the
  column one block out was never re-checked.
- The fix is the `WakeOnFallLabel` rule. It lowered the sequences that don't come back from 7.2 % to 3.7 % in the
  heavy round.

Not modelled: a bucket emptied into flowing water (the simulator only pours into empty cells).

## Later targets against the recommended rules (third round, 2026-10-23)

Earth 0024 section 4 and 0022 part B list features that version 1 must not block. Each has a synthetic scene and a
crude prototype rule as a switch, played under:
- Hydrology 0.1.3;
- the recommended set;
- the recommended set with the prototype;
- the prototype with each version-1 rule switched off in turn, and with `ProtoLocalOff` (the fills and level relax off
  in the columns a body is draining).

`watersim features` writes the table: [features.md](watersim-2026-10-23/features.md).

**The river-query stub** (0022 part A) answers for each column:
- whether Earth laid out natural water there, and its natural water top;
- the river's upstream and downstream end;
- one cell in each pool;
- the discharge, at gameplay scale and in m³/s.

**Prototype `ProtoLevels`** manages water bodies. It works on a body of sources that holds natural water, one layer at
a time, on a timer that runs only while something changes; all searches are bounded.
- **Fill.** Open cells beside the body fill up to its natural level: a canal, a lock chamber. A cell outside the river
  takes the level of the nearest natural water it is joined to.
- **Pond.** When no path of sources leads from the upstream end to the downstream end, and no water leaves below the
  surface, the body rises until it can spill.
- **Drain.** Cells above the level the body can hold are removed a layer at a time. That level is the natural one when
  the body reaches the upstream end, the natural level where falling water lands on it otherwise, and none if neither.

**Prototype `ProtoSubLevels`** gives flowing water K sub-levels per height, kept in a side array (in the game these
would be new liquid blocks).

| Feature | Verdict | Blocking rule | Smallest change that unblocks it |
|---|---|---|---|
| Pond behind a dam | Works as is: the recommended rules plus the pond rule rise to the crest and spill over. The river below the dam dries until the overflow lands, then fills again (0022's behaviour). | None. H1 guards only vanilla's own paths; H6 is about buckets; the pond's sources are made by the pond rule, which owns that exception. | A separate pond rule with a record keyed by area, and the river query. |
| Diversion drying | Works as is: the middle pool's own water dries, while the upper and lower pools stay. Without a prototype nothing dries (48 sources stay in both 0.1.3 and recommended), because a static pool cannot tell it lost its supply. | None. Survival neither blocks the drying nor dries anything by itself: the pool's sources rest on its bed. | The river query plus a bounded search in the blocks for where falling water lands. The stub's answers (natural level per column, river ends, one cell per pool, discharge) are enough; 0022 part A's `RiverAt` and `RiverReachInfo` give them. |
| Dam failure | Works as is: the pond drains through the gap to the river's own level, and nothing is left hanging or standing. | None. | The pond rule's drain, triggered by water leaving below the surface (a gap in the dam). |
| Lock beside a weir | Works as is: the chamber fills to 10 and empties to 7, and both rivers keep their levels. Without the prototype the chamber never fills (only flowing water enters). | None. With H1's drain guard off the chamber never fills: keep H1. | Non-river cells take the level of the nearest natural water they join. |
| Long runs (sub-levels) | Works with the side-feed rule refined: 48 blocks with 8 sub-levels, against 6 without. The first version of "no side feed from falling water" looped (falling water that had landed spread sideways but no longer fed what it spread into). | `FallingFeedsSideways = false` as first written (now refined, and in the recommended set). | Per-cell sub-levels as new liquid blocks: version 1 uses only vanilla's 6 heights. Then level relax and the flow labels should use the sub-level potential. |
| Power from drop × flow | Works as is: 8-block drop × 1200 m³/s read from the query (94 MW at full scale), and the wheel cell holds rapid falling water. Reading the stub touches no water rule. | None. | A patch on the wheel's water check that reads the river query. |

No feature needed a version-1 rule switched off: `ProtoLocalOff` changed nothing in any scene. Switching off
rules globally only broke things:
- H1's drain guard off: the lock fails, and the fall loops;
- H4 off: the power scene loops;
- H2 off: the ledge pool of the long run hangs.

**Which rules assume vanilla's 6 heights:**
- vanilla's own lowering (fed only by a neighbour exactly one higher); the prototype replaces it with sub-level
  potentials;
- level relax (raises to the highest feeder − 1, ignoring sub-levels). It did no harm in the long run, but should use
  potentials;
- vanilla's flow labels: cells of one height but different sub-levels get "still" labels, so the flow direction is
  lost;
- H3's ramp: drawn by vanilla's spread 6 → 1. With sub-levels on rivers, Earth's 6-cell ramps would no longer be fixed
  points, so sub-levels should stay off natural river water or Earth should draw longer ramps.

These make no assumption about heights:
- the H4 window (it measures water surfaces in blocks);
- H2, refill on a source and survival (they look at sources only);
- the fall label.

## Canals by width and depth (2026-10-23)

The question: does a large canal dug by a player fill as well 2 or more blocks deep as it does 1 deep? The setup:
- a lake at 8, deep, or only 1 deep at its bank like a river's edge;
- a canal 20 long, 1–6 wide and 1–3 deep, dug dry and opened last, or dug outward.

`watersim canals` writes the matrix: [canals.md](watersim-2026-10-23/canals.md). The results, for 0.1.3 and the
recommended set alike:

| Canal | From a deep lake | From a bank 1 deep |
|---|---|---|
| 1 wide, any depth | flowing near the mouth (6 blocks), dry beyond, by design | same |
| 2+ wide, 1 deep | full of sources | full of sources |
| 2+ wide, 2 deep | the bottom layer full; the layer at the lake's level flowing near the mouth, dry beyond (50 % sources) | almost empty: water falls in and runs off as flowing water (0 % sources) |
| 2+ wide, 3 deep | the bottom layer only (33 %) | almost empty (0 %) |

Why:
- H2 fills only cells resting on solid ground, so only a canal's bottom layer fills.
- Above that layer, a cell rests on water. Filling on water (0.1.0, `FillOnSource`) flooded Earth's steps, because a
  ramp cell beside the upper pool looks the same, locally, as the top layer of a 2-deep canal.
- From a shallow bank, no source ever reaches the canal's floor, so H2 never starts.

**The fix found: basin fill** (`ProtoBasinFill`, part of the managed-bodies prototype). From a body of natural water,
a bounded search finds the open cells joined to it sideways or downward, at or below its level and outside natural
river columns, so a step's ramp never fills. The lowest layer of them that rests on ground or sources fills, then the
next.
- Every canal 2 or more wide fills to the lake's level, at every depth, from either bank, whichever way it was dug.
- 1-wide passages stay flowing: a cell fills only inside a 2 × 2 square of open cells, as H2.
- The six later-target scenes still pass with it.

It needs the river query to know which water is natural and its level, so it belongs with the pond rule (0022 part B),
not in version 1's local rules. Without Earth, a vanilla lake would need another way to say it is natural water: open
question 4 of 0022.

The playback page has the case "Deep canal from a shallow bank" (3 wide, 2 deep).

**A local alternative: communicating vessels** (`FillVessels`). A flowing cell resting on a source fills when a source
beside it also rests on a source. The 2 × 2 vertical square under and beside it is then all source water: the water
below is one body, so the layer above takes its level.
- It needs no river query, works in any world, and reads only 3 neighbouring cells.
- It fills canals 2 or more wide from deep water to the lake's level at every depth (the same as basin fill), and keeps
  1-wide channels flowing.
- It does not help from a shallow bank: no source reaches the canal floor there.
- On Earth 0.16.5's 1-block steps it floods. The upper pool's bottom layer touches the lower pool, so a ramp cell
  looks like a canal's top layer: the step scene changes 100 cells and the chute 444 (its rapids are gone).
- With a sill under each step (the upper pool's edge column holds water only at its top, its bed at the lower pool's
  surface), the step and chute stay as before (0 and 30 cells, unchanged).
- A quick round with sills (3,600 undone sequences, 1,732 pokes): no failed checks, no endless updates, no water in
  the air. Score 264 against 180 for the recommended set alone; the difference is 7 pokes against 1 that left the
  settled shape changed.
- The experiment flag is the environment variable `WATERSIM_SILLS=1`.
- Not checked: vanilla terrain with stepped water whose lower layers touch, where it would also fill.

## Next step

**Done in Hydrology 0.1.5** (2026-10-24): the recommended rules are in the mod as config switches, on by default. The
vessels case is on wherever Earth sets `hydrologyFillVessels`. See the [server test](server-test-0.1.5.md). Two things
learned on the way:
- Level relax must stay out of vanilla's single-stream updates (rivulets). The simulator now does the same.
- The study's scenes had no vanilla rivulets.
