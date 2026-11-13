# Hydrology 0.1.7: the flood from a broken sill (2026-10-24)

**The report.** On a new Earth 0.16.10 world with Hydrology 0.1.6, the user broke one block under water in the Congo
rapids. Water then spread, cell by cell, until it filled the banks down to the next rapids.

**The cause: the vessels case of H2.** Earth worlds switch it on through the world attribute `hydrologyFillVessels`.
1. The broken block was the sill under the upper pool's edge source at a 1-block step, level with the lower pool's
   surface.
2. The hole filled and became a source. The upper edge source now rested on a source.
3. The ramp cell beside it (flowing water on the lower pool) then met the vessels condition and filled, then the next
   ramp cell, and so on.
4. The lower pool rose by one block across its width, over its flush banks, down to the next step.

## The change

The vessels case now needs two more things. Each is a switch in `ModConfig/hydrology.json`, on by default:
- **`VesselsDepth`:** the side source's water reaches at least as deep as the water under the cell. A canal fills from
  water at least as deep as itself. A one-block hole under a step's edge is shallower than the pool below the step.
- **`VesselsContained`:** no side of the cell is a dry ledge (an open cell over something other than water of its
  liquid). A canal's walls hold its water; a river's flush banks do not.

**`WorldMayFillVessels`** (true by default): set it to false to ignore the world attribute, which keeps vessels off on
Earth worlds unless `FillVessels` is set.

## Simulator

`watersim flood` ([output](server-test-0.1.7/watersim-flood.txt)) takes Earth's step, chute, diagonal step, and a step
and a chute whose river tapers to 1 deep at its flush banks, all with sills, drawn in the Hydrology shape. It breaks
every solid block beside or under water, one at a time. After each break it settles the area, wakes every cell, and
counts the cells that hold water now but were dry as generated.

| Scene | Vessels off | Vessels as 0.1.4–0.1.6 | 0.1.7 |
|---|---|---|---|
| Step | 0 of 80 breaks add water | 6 add water, up to 64 cells | 0 |
| Chute | 0 of 126 | 12, up to 68 | 2, up to 12 |
| Diagonal step | 0 of 70 | 4, up to 83 | 0 |
| Tapered step | 0 of 106 | 10, up to 132 | 2, up to 6 |
| Tapered chute | 0 of 316 | 30, up to 34 | 6, up to 7 |

Only sills start it. The bed under a ramp, a bank below the water and a bed in mid-pool add no water in any version.

**What is left in 0.1.7.** Where a step's edge row is 1 deep on both sides, breaking the sill raises that row by one
block beside the ramp, by 6–12 cells. The bank Earth raises beside a ramp holds it in the channel. Locally it looks the
same as a canal dug from water as deep as itself, which must still fill.

The other results are unchanged:
- **Canals** (`watersim canals`, 48 cases): identical. Canals 2+ wide and 2–3 deep from deep water: 100 % sources.
- **Generated shapes** (`verify-shape` with sills): step, chute, fall and Victoria stay as generated.
- **Pokes** (break a block, put it back): change 4 of the chute's 424, down from 18 with vessels as before; step, fall and
  Victoria change none.

## Server test

### The sill-break set on an Earth world

- **World:** Earth `earthmap-0.16.11-playtest.1` and Cylinder World `0.11.15-playtest.1` (the zips installed for the
  user), seed 386217, 256 k wide, with the world attribute set. Scenario file:
  [scenarios-sill-break.json](../tests/server/scenarios-sill-break.json).
- **New harness kind `step_break`:** it finds a 1-block step with a sill (a ramp cell on a lower-pool source, beside an
  upper-pool source standing on a block). As in the shape test, it then wakes every cell (`WA_WAKEALL=20`) and breaks
  for good one of: the sill, the bed under the ramp cell, the lower pool's bank beside the ramp, or the bed 4 cells
  downstream.

Water cells changed within 14 blocks of the step, 280 s after the break. The broken cell filling with water counts as
1. Data: [0.1.7](server-test-0.1.7/sill-break-0.1.7.json),
[with VesselsDepth and VesselsContained off](server-test-0.1.7/sill-break-0.1.6-rules.json).

| Site | Broken | Vessels as 0.1.6 | 0.1.7 |
|---|---|--:|--:|
| Congo rapids | sill | **184** | 1 |
| | bed under the ramp | 0 | 1 |
| | bank | **223** | 2 |
| | bed mid-pool | 1 | 0 |
| Congo step | sill | **196** | 1 |
| | bed under the ramp | 0 | 1 |
| | bank | 1 | 1 |
| | bed mid-pool | **269** | 1 |
| Rhine chute | sill | **71** | 1 |
| | bed under the ramp, bank | 1, 1 | 1, 1 |
| | bed mid-pool | 0 | 0 |
| Daugava step | sill, bed under the ramp | 1, 1 | 1, 1 |

- The steps are 48 or more blocks apart, but a flood runs further than that. The Congo step's "bed mid-pool" and the
  rapids' "bank" counts with the old rules probably include water from the sill flood at the step beside them.
- Whole run with the old rules: 690 vessel fills and 107 k water updates. With 0.1.7: 0 vessel fills, 4 refused as too
  shallow, and 70 k updates.

### The 62 labs on a vanilla world (vessels on), against the 0.1.6 run

- 4 labs differ at the end. Three are known to vary from run to run (drop refill, ramp tail, ramp beside a wheel).
- The fourth is a canal dug from a 1-deep pool: it fills in neither version, and its row of flowing water is one cell
  shorter.
- All three wheels turn as before (14.9, 14.9, 7.2).
- Mean tick is unchanged (47.0 ms). Updates: 25.2 k (0.1.6 run: 22.6 k; 0.1.5 runs: 24.6–25.5 k).

## Found by the review and fixed

- **Codex:** the reused scratch positions (`Liquids.Below`, `Above`, `Side`, `Side2`) kept dimension 0, because
  `BlockPos.Set(p)` copies the coordinates only. In another dimension, the rules read cells of the main world. They now
  copy the dimension too. This dates from 0.1.5 and does not change the main world.
- Codex confirmed that the depth scans start one block below the cell on both sides (`BlockPos.Down()` moves the
  position).

## Not verified

- On Earth 0.16.10, which the user's world was made with: the test ran on the 0.16.11 playtest build installed now.
- The client's view, and the residual 1-deep edge-row rise on a real Earth river.
