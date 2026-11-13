# Hydrology 0.1.1: rapids over 3 blocks, server test (2026-10-22)

Version 0.1.1 changes one rule, H4. A source's edge becomes rapid when the water surface falls 2 or more blocks within
3 blocks, not only beside a single drop of 2 or more.

The reason is a user decision of 2026-10-22, recorded in Earth's
[decision 0024](../../Earth/docs/decisions/0024-hydrology-water-mod.md) section 1b. Rapids are up to 4 blocks high,
waterfalls start at 5. Earth's pool layout 2 draws a 2–4 block fall as a chute of 1-block steps, one every 2 blocks.
Under 0.1.0 those steps would turn into ordinary 6→1 ramps (H3) as soon as a block update reached them.

The test used the same harness, worlds and controls as the [0.1.0 test](server-test-0.1.0.md): Earth 0.16.3 and
vanilla, each with and without Hydrology, run at the same time. The results are in
[analysis.txt](server-test-0.1.1/analysis.txt) and [summary.json](server-test-0.1.1/summary.json). Earth's pool
layout 2 was not built yet, so the chute is a lab basin.

## The rule

For a source with a side open onto water or floor that is not a source, two small searches run over columns:
- **downstream,** up to 3 columns away: the level where water from the source lands;
- **upstream,** up to 2 columns away: the highest water surface.

The edge is rapid when the surface falls 2 or more within 3 blocks across the edge's own step. The upstream part keeps
a chute's last step rapid: downstream of it there is only one more block of fall.

An edge that changes schedules an update for the source edges within 3 blocks, so a chute settles as a whole. A lone
1-block step between long pools stays ordinary (H3).

## Results

The lab chute is a pool, then three 1-block steps every 2 blocks, then a calm channel. It is 2 wide and built from
source water without block updates, the way terrain is generated. A player then pokes the first step: places a block
on the ramp spot and takes it away again.

| Scenario | Without Hydrology | With Hydrology (Earth and vanilla alike) |
|---|---|---|
| Chute poked | The poked spot gets 1 flowing cell; 1 source cell turns into flowing water. Nothing else changes. | **All three edges become `rapidwater-still-7`** (both cells of each) within 10 s, with a rapid ramp r6→r4 below each step. Unchanged at 300 s; no source lost (112 water cells before, 106 + 6 rapid sources after). |
| Vanilla 3 m wheel above the second step | 0 of 75 samples | **Turns** in 74 of 75 samples (flowRate 7.24, 4 power cells) |
| First step filled back in at 120 s (its bed raised to the pool's floor) | Ordinary flowing water | The pool now reaches the second step. Its new edge (a 2-block drop) and the last step stay rapid, and the old first edge reverts to ordinary source water. Every edge follows the rule. |
| Lone 1-block step (lab_step) | 1 flowing cell; the lower pool loses 8 sources | Ordinary ramp 6→1, as in 0.1.0 |
| Earth's touched Congo rapids | — | 65 rapid sources around the poked drop (0.1.0: 52): neighbouring steps within 3 blocks now count |

The other scenarios are unchanged from 0.1.0: trenches, the channel around a step, 2-, 3- and 5-block drops, the
refilled drop, the bucket case, and Earth's rivers and coasts. Two details:
- The tips of thin rapid ramps on a dry floor end at level 1, 2 or 3 depending on the run. They are stable within a
  run, and 0.1.0 varied the same way.
- In the wheel scenarios, one sample showed an edge ordinary for a moment. Vanilla's wheel turns rapid water below its
  power cells into ordinary water once a second; H4 turns it back.

**Server tick,** mean over 300 s:

| World | With Hydrology | Without |
|---|---|---|
| Earth | 47.09 ms | 47.08 ms |
| Vanilla | 47.27 ms | 47.14 ms |

The vanilla difference is all in the first 10 seconds, while the lab basins are built: 3 ticks over 100 ms against 1.
Hydrology's rules had used 20 ms in total by then. From 30 s on, the two runs match (47.0–47.1 ms).

Over 300 s, the rules spent 308 ms on the Earth world (0.1.0: 126 ms) and 109 ms on the vanilla world (0.1.0: 36 ms).
The Earth figure is higher because more edges around Earth's rapids now check their neighbours.

## Not verified

- **Earth's own chutes:** pool layout 2 did not exist yet. Earth's chutes have rock at the surface and beds 1–2 deep;
  the lab chute has neither.
- **Diagonal rivers:** a chute along a diagonal river (the searches follow the grid, up to 3 columns in any direction).
- **The client:** how rapid chutes look in game.
