# Continuous white water in rapids reaches (2026-10-24)

**The question** (the user, via the Earth session). At 256 k the Congo's rapids look like thin white stripes across a calm
river. Each 1-block step has a rapid ramp about 6 cells long, and the steps are 6 or more columns apart because they
follow the real slope, so calm pool shows between them. Which reach shape looks like continuous rapid water and stays
exactly as generated under Hydrology 0.1.7 (recommended rules, with the vessels case on as on Earth worlds)?

**Answer: steps 2 to 5 columns apart.** Each step's ramp (6, 5, 4, …) then reaches the next step, the whole surface is
rapid water, and every step's edge is rapid. The shape stays as generated at every width tested. Drawing rapid water
over calm pools does not hold: H4 turns it back on the first update.

## Simulator: `watersim whitewater`

The reach: 6 one-block steps `spacing` apart, 3, 6 or 10 wide, built as Earth 0.16.10 builds it:
- pools 2 deep between the steps (or tapered: 1 deep at the edge rows), banks flush with the water;
- sills under the steps;
- the Hydrology shape, with the rapid rule's marks (`watersim rapidrule`) and spills from ramp height 2.

Rules: recommended plus 0.1.7's vessels (depth and containment). The full table is in
[whitewater.md](whitewater-2026-10-24/whitewater.md). Width 6:

| Steps apart | First update (cells / visible labels) | Pokes changing it | Single breaks adding water | Rapid surface | Water cells per column |
|---|---|---|---|---|---|
| 2 | 0 / 0 | 4 of 120 | 0 of 75 | 100 % | 1–4 |
| 3 | 0 / 0 | 0 of 120 | 0 of 95 | 100 % | 1–4 |
| 4 | 0 / 0 | 0 of 120 | 0 of 113 | **100 %** | 1–4 |
| 5 | 0 / 0 | 0 of 120 | 0 of 135 | **100 %** | 1–4 |
| 6 | 0 / 0 | 0 of 120 | 0 of 155 | 77 % (first step ordinary) | 1–3 |
| 7 | 0 / 0 | 0 of 120 | 0 of 168 | 80 % | 1–3 |
| 9 (today's look) | 0 / 0 | 0 of 120 | 0 of 205 | 63 % | 1–3 |
| 4, 4, 14, 4, 4 (two clusters, calm pool between) | 0 / 0 | 0 of 120 | 0 of 150 | 77 % | 1–4 |
| 7, pools' top layer drawn as rapid sources | **180** / 0 | 7 of 120 | 0 of 168 | 80 % | 1–3 |
| 9, pools' top layer drawn as rapid sources | **240** / 0 | 23 of 120 | 0 of 205 | 84 % → 63 % | 1–3 |
| 7, rapid flowing 1 over the calm pool | **30** / 0 | 18 of 120 | 0 of 168 | 80 % | 1–3 |
| 9, rapid flowing 1 over the calm pool | **90** / 0 | 25 of 120 | 0 of 205 | 84 % → 63 % | 1–3 |

- **Why 5 is the limit.** H4 marks a step rapid when water from its edge lands 2 or more lower within its window (6
  cells). The first step of a reach sees that drop only through the next step, so the next step must be at most 5
  columns downstream. That is the same rule Earth's rapid rule already uses. At 6 apart the first step goes ordinary,
  and with it its ramp. From 6 apart the ramp's last cell (height 1) no longer spills, and calm pool shows beyond it.
- **Rapid water over calm pools.**
  - Rapid sources in the pools' top layer: H4 turns every one more than its window away from a drop back to ordinary
    water, 180–240 cells on the first update.
  - A thin rapid flowing layer: nothing feeds it (flowing water needs a neighbour one higher), so vanilla removes it.
  - No switch keeps either: the sheet and lone-vertical rules only decide how a rapid source spreads, not whether it
    stays rapid.
- **Tapered rivers** (1 deep at the edge rows, flush banks): the same 0 cells on the first update. A few pokes and
  breaks at the edge rows change 3–20 cells. The same happens at every spacing, today's 9 included: this is the 1-deep
  edge row of [0.1.7](server-test-0.1.7.md), not something the closer steps add.
- **The flood check** (0.1.7's): no single break in an untapered reach adds water, at any spacing.

## The shape Earth should draw: pool layout 7

**Within a rapids reach, put the 1-block steps 2–5 columns apart (5 gives the longest white water per block of fall).**
Everything else stays as in pool layout 6:
- sills under the steps;
- the Hydrology shape;
- the rapid rule's marks; with steps at most 5 apart it marks every step and ramp rapid;
- spills from ramp height 2.

The total fall does not change: a reach that falls D blocks still has D one-block steps. What changes is where they sit.
- **Where the real slope is gentler** (reach length L over D steps more than 5 columns each): the steps go together into
  a rapids section about 5·D columns long, and the rest of the reach is calm pool.
- **Where the slope is steeper:** keep the real spacing, at least 2.
- **Splitting a section into clusters works too.** Two clusters of steps 4 apart with a 14-column calm pool between them
  stay as generated, and the first step after the calm pool is rapid. A cluster should have at least 2 steps. A single
  step with 6 or more columns of calm water on both sides is today's stripe.

Column by column, the middle row of a reach with steps 5 apart ([spec-s5.md](whitewater-2026-10-24/spec-s5.md); 3
and 4 apart and the mixed case are in the same folder):

| x | bed top | water top | ramp | cells from bed + 1 up | after waking every cell |
|---|---|---|---|---|---|
| 8, 9 | 9 | 12 | | S S S (upper lake) | = |
| 10 | 11 | 12 | | R:e (edge source on the sill) | = |
| 11 | 9 | 11 | 6 | S S r6:e | = |
| 12 | 9 | 11 | 5 | S S r5:e | = |
| 13 | 9 | 11 | 4 | S S r4:e | = |
| 14 | 9 | 11 | 3 | S S r3:e | = |
| 15 | 10 | 11 | 2 | R:e r2:e (sill, edge source, the ramp's last cell) | = |
| 16 | 8 | 10 | 6 | S S r6:e rd1 (spill: rapid falling 1 over the ramp) | the source under r6 relabels (hidden) |
| 17–19 | 8 | 10 | 5, 4, 3 | S S r5:e … | = |
| 20 | 9 | 10 | 2 | R:e r2:e | = |
| … | | | | the same pattern for each step | |
| 36–41 | 3 | 6 | 6 … 1 | S S S r6:e … r1:e (lower pool, the last ramp) | = |

Key: S/R ordinary/rapid source, rN rapid flowing of height N, rdN rapid falling, ":e" the flow direction (here east).
- **Every column of a pool** carries a rapid ramp cell one above the pool's water top, of height 6 down to 7 − spacing.
- **The pool's last column** is the next step's edge: a sill (bed one below its water top), a rapid edge source, and the
  ramp cell above it.
- **A ramp cell of height h ≥ 2 beside the next step** spills: rapid falling water of height h − 1 above the next pool's
  r6.

**Depth for boats:** 2 sources plus the ramp cell in the pools (3 cells), 1 source plus the ramp cell at the edge
columns (2 cells). That is the same as today's chutes, which also have a sill at every step.

## Server: wheels in chutes 3, 4 and 5 apart

Vanilla world, Hydrology 0.1.7 with vessels on, 300 s
([scenarios-whitewater.json](../tests/server/scenarios-whitewater.json)). The harness's lab chute: three 1-block steps
3, 4 or 5 apart, 2 wide, built from plain source water. A vanilla 3 m wheel sits with its rim in the ramp below the second
step. Data: [server-chutes-profiles.json](whitewater-2026-10-24/server-chutes-profiles.json).

| Steps apart | Edges after 300 s | Middle pool's surface | Wheel |
|---|---|---|---|
| 3 | RR RR RR | `S S R r6 r5 r4 rd3` | turns in 74 of 75 samples, 7.24 |
| 4 | RR RR RR | `S S S R r6 r5 r4 r3 rd2` | 74 of 75, 7.24 |
| 5 | RR RR RR | `S S S S R r6 r5 r4 r3 r2 rd1` | 74 of 75, 7.24 |

The chutes settle into the shape of the table above on their own: a ramp over every column of the pool, a spill of
height 7 − spacing − 1 over the next step, and rapid edges. Without wheels (steps 4 and 5 apart) they settle the same.

## Not verified

- An Earth world with pool layout 7: the Earth session's shape test and flood check.
- How the white water looks in the client.

## Layout 7 floods: pools must be 2, better 3, deep (2026-10-24, later)

**The finding.** Earth 0.16.11 (layout 7, uncommitted) cut its rapids sections as gorges whose pools between the steps are
**1 deep**. In the server sill-break test (`testruns/sill-01611`) one broken block raised a whole pool by one level:
- `congo_rapids_bank`: 109 cells; the "bank" target there is really the sill of a diagonal step;
- `congo_rapids_sill`: 198 cells, at the bank site's level, 39 blocks away, so probably the same flood.

The run counted 316 vessels fills, 38 refills and 15 refusals.

**Why 0.1.7 lets it through.** The hole under the step's edge is 1 deep, and so is the pool beside it, so the depth
rule (side water at least as deep) holds. Earth raises the banks beside ramps, so containment holds too. The pool rises
cell by cell up to the next step.

**No local rule tells this apart from a canal** (simulator, `WW_POOL=1`):
- **No vessels in or beside rapid water** (`VesselsCalm`, each of the four cells tried alone with `VesselsCalmMask`):
  - looking at the cell or at the side source: fixes every reach;
  - but canals 3 deep fill to 33 % instead of 100 %. Their filling front is rapid water (a rapid source on one block of
    water beside a rapid ramp), cell for cell the same as the flood's start;
  - looking at the sources underneath: those are never rapid, so it changes nothing.
- **A rapid side source must be strictly deeper** (`VesselsRapidDeeper`): fixes every reach, 1 or 2 deep. Canals 3 deep
  reach only 36–48 %, for the same reason.

Both are kept in the simulator as rejected switches. Hydrology stays at 0.1.7.

**What holds: deeper pools** (`watersim whitewater`, `WW_POOL`, `WW_DOUBLE=1` breaks the block under the broken one
too, a 2-deep hole):

| Pools between the steps | Single breaks adding water, rapids reaches (steps 3–5 apart, mixed) | 2-deep holes |
|---|---|---|
| 1 deep (layout 7 as built) | up to 78 cells | (more) |
| 2 deep | 0; tapered edge rows: 1 break, 6 cells | up to 13 cells (79 in the mixed reach) |
| 3 deep | 0; tapered edge rows: 1–3 breaks, 6–12 cells | 0 at full width; **80 cells** where edge rows taper to 1 |

So Earth should make the pools in a rapids section at least 2 deep, better 3, across the full width, edge rows
included. The depth rule then needs a hole deeper than the pool before vessels can join the two pools. Physically a
deep enough connection does equalise the levels: a player who digs that deep at a step can still flood the pool.
