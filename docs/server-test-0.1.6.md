# Hydrology 0.1.6: water plants at river steps, server test (2026-10-24)

**The report.** Earth 0.16.10 marks chute steps rapid with the study's rapid rule (`watersim rapidrule`). In the
shape test, rows at a Congo step went from rapid to ordinary on the first update: 87 cells, near 130108, 200, 53969.
Vanilla's world generation had put water plants (reeds, seaweed) into the pool there after Earth placed its water.

**The cause.** Hydrology counted a cell as open water only when its block was air or very replaceable (`replaceable`
5000 or more, vanilla's threshold for downward paths). Water plants have 3000. H4's window looks down from each cell
for where water lands. Where it met a plant cell full of water under a ramp, it read ground at the ramp's level, so it
missed the drop beyond and the step went ordinary.

**The change.** `Liquids.Open` also counts as open a plant block that liquid passes through up and down (liquid barrier
under 1 on both faces) and that holds liquid. Plants only: a first version took any block holding water. That made
waterlogged clutter in a cave open too, and a rivulet over it looped (10 k extra updates in 300 s).

## Server test

Vanilla world, 300 s ([scenarios-plants2.json](../tests/server/scenarios-plants2.json)): a chute with steps 3 or 5
apart, with seaweed or reeds in every top cell of the lower pools (under the ramps), as world generation would put
them.

| Chute | 0.1.5 | 0.1.6 |
|---|---|---|
| Steps 3 apart, no plants / seaweed / reeds | all edges rapid | all edges rapid |
| Steps 5 apart, no plants | all edges rapid | all edges rapid |
| Steps 5 apart, seaweed under the ramps | **all edges and ramps ordinary** | all edges rapid |

Plants in the edge cell itself make no difference in either version: vanilla breaks them on the first update, and
the edge goes rapid.

The 62 labs of the 0.1.5 test, with vessels on, compared with 0.1.5:
- 3 labs differ, all known to vary from run to run: the drop refill's timing, a ramp tail, and the ramp beside a
  water wheel, which the wheel itself relabels once a second;
- all three wheels turn as before (14.9, 14.9, 7.2);
- updates 22.6 k (0.1.5: 25 k); level relax stays flat after the start;
- the start burst's slow tick is unchanged (387 ms).

## Earth 0.16.10 world (2026-10-24)

The shape test (`scenarios-earth-shape.json`, `WA_WAKEALL=20`) was run on `earthmap-0.16.10.zip` with 0.1.6, at the same
sites as the run with 0.1.5. Data: [earth-0.16.10-shape.json](server-test-0.1.6/earth-0.16.10-shape.json).

| Site | First-update water changes, 0.1.5 | 0.1.6 |
|---|--:|--:|
| Daugava ramp, Congo rapids, Rhine chute, Victoria, Niagara | 0 | 0 |
| Congo step (130108, 200, 53969) | 87 | **0** |
| Congo chute | 2 | 1 |

- **Congo chute:** the one cell left is an ordinary ramp cell at the edge of the test area that turns rapid.
- **After the pokes:** the same as with 0.1.5 (Victoria 10, Niagara 8 from the harness breaking rock under the lip).
- Installed for the user with Earth 0.16.10.
