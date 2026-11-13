# Hydrology 0.1.3: no water standing in the air, server test (2026-10-23)

**The bug.** The user tested Hydrology 0.1.2 on Earth 0.16.4 at Victoria Falls (about 17.925 S, 25.844 E), on
2026-10-22. They placed stone blocks at or near the lip and removed them again. Afterwards a light-turquoise column of
rapid water stood at the lip edge "with nothing holding it up", and "current rule can be used to build water
columns".

## Reproduced

The harness has two new scenarios:
- **`lab_fall_stones`:** a pool 2 deep up to its lip, then an 8-block fall into a plunge pool, 8 wide. The lip is
  poked so the fall starts. Stones go into the curtain at the top cell, one below and four below (3 wide), at 20 s,
  and come out again at 40 s.
- **`fall_poke_victoria`:** the same on an Earth `d2c0546` world. The lip of the drop nearest the user's position is
  found (21 blocks high). Stones go into the curtain at three heights, 3 wide.

With 0.1.2, once the stones were out, both left `rapidwater-still-6` cells stacked in the air:

| Where (0.1.2, 50 s after the stones came out) | Column, top to bottom |
|---|---|
| Lab, the curtain | e-6 e-6 d-6 e-6 **still-6 still-6 still-6 still-6** (on the plunge pool) |
| Lab, the column beside it, where water had run off the stones | air air air e-5 **still-6 still-6 still-6** e-6 |
| Victoria, the curtain | s-6 s-6 **still-6** sw-6 d-6 … |

Without Hydrology, the lab curtain is falling water (`d-6`) all the way down. No source was left in the air, so the
cause was not H1 or H2. It was the falling-water labels.

**Why the column stood.** Vanilla labels a non-source cell "still" when it has no sideways flow and neither the cell
above nor the one below is falling water. Once a few such cells formed in the air around the stones, every cell later
added to that column was labelled still too. Vanilla counts water above a cell as feeding it, so each cell held up the
one above it.

## The fix (H3)

When a non-source cell updates, Hydrology checks the run of non-source water of the same liquid in its column, up
and down. Every still cell in that run with water above it and air or flowing water of its liquid below it becomes the
falling variant of the same level and kind (`rapidwater-d-6`, `water-d-6`, …).

- **The whole run at once.** A cell fixed alone was relabelled still by vanilla from its still neighbours.
- **Cells with a flow direction are left to vanilla**, and so is a cell resting on the ground or on a pool. A first
  version also turned those into falling water, and vanilla relabelled them still at once. In Earth's rock tubes that
  became a loop of about 85 relabels a second; it is gone.

## Results

Final round: four worlds (Earth `d2c0546` and vanilla, each with and without Hydrology), all scenarios, 300 s,
`testruns/fall6`. Data: [analysis.txt](server-test-0.1.3/analysis.txt) and
[summary.json](server-test-0.1.3/summary.json).

| Scenario | 0.1.3 |
|---|---|
| Lab fall, stones in and out | The curtain is falling water again (e-6 e-6 d-6 e-6 d-6 d-6 d-6 d-6) and the run-off column beside it too; no still cell in the air, no source left in the fall. The same in both worlds. |
| Victoria Falls, stones in and out | The curtain is falling water below its top four cells, which have flow directions (s, sw), like the world without Hydrology, whose curtain top also keeps a still cell. The column beside the curtain ends as a 4-cell stack of flowing water with no water below it, the same shape as without Hydrology; it most likely rests on a ledge of the cliff. **Please check this spot in game.** |
| 0.1.2's results | Unchanged: trenches, steps, the channel around a step, drops, wheels in falls (9.6, 14.9) and in the lab chute (7.24), the refilled drop and chute, the bucket, the Daugava step, the Congo waterfall. Earth chutes: Congo 56 of 65 edges rapid, Rhine 32 of 33; the Rhine wheel turns. The Congo chute wheel still does not turn (the lone-step case in the [0.1.2 report](server-test-0.1.2.md)). |
| Cells labelled falling by the rule | 35 on the Earth world and 28 on the vanilla world over 300 s, all in the first minute |

**Server tick,** mean over 300 s:

| World | With Hydrology | Without |
|---|---|---|
| Earth | 47.22 ms | 47.22 ms |
| Vanilla | 47.26 ms | 47.27 ms |

Rules over 300 s: 60 k updates and 0.7 s on the Earth world (with the Victoria site, which 0.1.2 did not have),
7 k updates and 67 ms on the vanilla world.

**The Earth undo struck again.** In the Earth world with Hydrology, by 300 s:
- some labs (three chutes and the 5-block drop) were back to untouched terrain;
- the refilled drop was back to its state before the refill;
- two river trenches were gone.

Those results come from the vanilla pair. See the [0.1.2 report](server-test-0.1.2.md).

## Not verified

- **How it looks in the client.** In particular, whether the Victoria spot now looks right to the user.
