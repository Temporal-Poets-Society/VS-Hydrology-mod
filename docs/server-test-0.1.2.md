# Hydrology 0.1.2: chutes with steps 3 apart, server test (2026-10-22)

Earth 0.16.4 (commit `d2c0546`, pool layout 2) draws a fall of 2–4 blocks as a chute of 1-block steps **3 blocks
apart** along the river (`RiverNetwork.ChuteSpacing = 3`; Earth's decision 0024 section 1b). Hydrology 0.1.1's
window was sized for steps 2 apart, so a chute's last step would have turned into an ordinary ramp when touched.
Testing on Earth's real chutes found more to fix.

## What changed in H4 (and H5)

1. **A wider window.** The surface fall is measured over 6 column steps (4-neighbour) instead of 3:
   - downstream: up to 6 columns away;
   - upstream: up to 5 columns away.

   Three blocks along a diagonal river is up to about 5 column steps, plus 1 to reach the water below the next step.
   A lone 1-block step is still ordinary: Earth keeps at least 32 blocks of pool on each side of one.
2. **Where the downstream search stops.** It carries on through water, and across dry ground at the same level,
   where water spreads anyway. It ends where water has fallen onto dry ground, so a lone step's pool cannot reach an
   unrelated drop beyond its bank.
3. **Sheets on water.** Any rapid cell resting on water spreads like ordinary water during its update. Before, only a
   rapid cell with a same-level neighbour, or one beside a rapid edge, did. Earth's chute ramps are irregular, with
   mixed flow directions and levels, and vanilla's single-stream rapid logic kept lowering and raising those cells for
   ever: about 790 updates a second on the Earth world, from 30 s to the end. Afterwards it settles.
4. **Ordinary feeders are checked first.** A rapid cell about to turn ordinary because an ordinary source feeds it first
   has that source checked. Earth generates its chutes with ordinary sources and rapid ramps, so a touch that reaches a
   ramp before its edge no longer turns the ramp ordinary for good.
5. **Waking sources beside an emptied cell (H5).** When vanilla empties a cell, Hydrology schedules an update for the
   same-liquid sources beside it. Vanilla wakes them each time it lowers the cell, but as "unique" callbacks.

   Under load, filling a chute's step back in sometimes left a source next to an empty cell for good, with orphaned
   rapid water below it. This happened in 2 of 2 runs with three chutes at once and in 0 of 2 runs with one; with this
   change, 0 of 2.

## Method

The test used the same harness as before, with four worlds run at the same time:
- Earth with the Debug build of `d2c0546` plus Cylinder World 0.11.12, with and without Hydrology. The build was
  copied from the Earth session's folder; its manifest still says 0.16.3, its code has `ChuteSpacing = 3`.
- vanilla, with and without Hydrology.

The lab chute now takes a step spacing: 3, and 2 as in 0.1.1. A new Earth scenario, `chute_poke`, does three things:
1. finds a rapid ramp cell on a lower pool beside an edge source;
2. pokes it: places a block there and takes it away again;
3. counts the edges within 12 blocks that turn rapid or stay ordinary.

The results are in [analysis.txt](server-test-0.1.2/analysis.txt) and [summary.json](server-test-0.1.2/summary.json)
(final round, `testruns/chute4`).

## Results

The lab results are the same in the Earth and vanilla worlds.

| Scenario | Without Hydrology | With Hydrology |
|---|---|---|
| Lab chute, steps 3 apart, poked | 1 flowing cell, 1 source lost | **All three edges rapid** within 10 s, a rapid ramp on every step, unchanged at 300 s; no source lost |
| Lab chute, steps 2 apart (the 0.1.1 case) | The same | All three edges rapid |
| Wheel above the second step of the 3-apart chute | 0 of 75 samples | **Turns**, 74 of 75 (flowRate 7.24) |
| First step of the 3-apart chute filled back in at 120 s | Ordinary flowing water | The pool reaches the second step, which is now a 2-block drop and turns rapid with a fall below; the old first edge reverts; the last step stays rapid |
| Lone 1-block step (lab and the Daugava) | Lab: 8 sources lost; Daugava: 81 sources lost | Ordinary ramp, no source lost; the Daugava step unchanged (98 flowing, 1 736 sources) |
| **Earth 0.16.4 chute, Congo (Livingstone rapids, −5.60, 13.60), one ramp cell poked** | 0 rapid edges (64–70 ordinary, then 256 as vanilla redraws the water) | **54 of 64 edges rapid** within 10 s, stable to 300 s |
| **Earth 0.16.4 chute, Rhine gorge (50.3199, 7.5307)** | 0 rapid edges | **32 of 33 edges rapid** |
| Wheel on the Rhine chute | Turns (5.12, 2 cells) | Turns (5.12, 2 cells), 75 of 75 |
| Wheel on the Congo chute (the rapid ramp nearest −5.60, 13.60) | Stops after the first sample | **Does not turn** (see below) |
| Congo waterfall (5+ blocks) | Ordinary falling water | Rapid edge and rapid fall |
| Trenches (labs, the Daugava, the Baltic), drops, refilled drop, bucket | as 0.1.0 | as 0.1.0 |

**Server tick,** mean over 300 s:

| World | With Hydrology | Without |
|---|---|---|
| Earth | 46.95 ms | 46.95 ms |
| Vanilla | 47.07 ms | 47.04 ms |

The vanilla difference is in the first 10 s, while the lab basins are built.

Rules over 300 s:
- **Earth:** 42 k updates and 300 ms. Before fix 3, the same Earth world needed 260 k updates and 2.3 s.
- **Vanilla:** 7 k updates and 59 ms.

## The Congo wheel, and the 10 ordinary edges

The wheel was placed on the rapid ramp nearest the Congo site. A top view around it shows the following:
- the step above it is a 1-block step whose next step is about 8–10 blocks away;
- there is a wide ramp of `rapidwater` at the upper level, with levels 6 down to 4 across about 9 cells.

By the rule, that is a lone step, so when touched its ramp becomes ordinary and the wheel stops. Without Hydrology the
wheel stops too, after one sample.

Either Earth drew this ramp rapid although its steps are farther apart than `ChuteSpacing` (a bend, or a chute's end),
or the window would need to be even wider. Seen from Hydrology, it is a lone step. The 10 edges that stay ordinary at
the poked Congo chute may be the same case. **For the Earth session to look at.**

## The Earth undo

In the previous rounds, Earth worlds undid builds, with and without Hydrology:
- labs were back to untouched terrain;
- in one round, both refill labs were consistent at 180 s and back to their pre-refill state by 300 s.

The world's only autosave before that, at about 90 s, saved 0 chunks, so the changes were lost without ever having
been saved. The final round was not affected. This is not Hydrology's; it needs investigating on the Earth or Cylinder
World side.

## Not verified

- Chutes along strongly diagonal rivers, apart from what the Congo and Rhine sites contain.
- Any client play.
