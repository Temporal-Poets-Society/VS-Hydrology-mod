# Caves under rivers (HY-0004), 13 November 2026

The question (HY-0004, from the 0.1.5 Earth report): does a river over a cave opening drain into the cave? H7's survival
rule turns a source with open air under it into flowing water, with no exception for river columns.

## Set-up

- Hydrology `claude/hy-items` (0.6.2-rc.5 plus HY-0003), Debug build; Earth `main` `0681adc` (2.0.0-rc.7), Debug build
  from a clean export; Cylinder World 1.1.0-rc.3 (the player's zip); a new 1:10 world (play style `earth-10`, height
  1024, seed 386217) on a headless server through `tests/server/mkrun.py`.
- `wateraudit`'s bed scan (`WA_BEDSCAN`) over 13 squares of 129 x 129 columns around the river sites of the earlier
  scenario files: Olt, Tanaro (no water in its square), Congo, Aare, Rhine, Daugava, Zambezi above Victoria Falls,
  Niagara, Gauja, Saale, Elbe at Dresden, Iguazu and Ticino. For every column, it checks the water from the top down
  for open air (no fluid, an air or replaceable block) under the lowest cell, or beside a cell and below the
  neighbouring column's ground. It also counts how many solid blocks lie between each river column's water and the
  first open air below it.
- Two more runs on copies of that save, with `WA_BEDSCAN_CAVES` (a 3 x 3 x 3 cave whose roof is the bed under one river
  column per square, so 9 river columns stand over air: a control for the scan and the case itself), each opening's
  water woken, and the squares scanned again 60 s later. One run had Hydrology as shipped; the other had `Survival`
  off (vanilla's handling of a source over air).

## Results

- **As generated: no openings.** 35,280 river columns hold 262,098 water cells. No cell has open air under it or a cave
  beside it. The nearest cave air is 2 solid blocks under a bed (5 columns), then 3 (9 columns), 4 (10 columns); 34,807
  columns have none within 32 blocks. Vanilla's `GenCaves` refuses a cave whose box, widened by 1 sideways and 2
  vertically, touches a fluid. Earth writes its river water into the fluid layer in the terrain pass, before caves are
  carved, so this check sees the rivers, and the 2-block floor matches its margin.
- **A carved cave under a river fills, and the river stays whole.** The scan found all 11 carved caves (84 openings;
  two squares had no column deep enough to carve under). 60 s after the wake no opening was left: the caves held 166
  more water cells, and the river columns were unchanged (35,280). With Hydrology as shipped, every cell was a source:
  H7 drained 95 sources, H2 filled the caves as enclosed pockets (28 pocket fills, 239 fills) and refilled 12. With
  `Survival` off the result was the same apart from 14 flowing cells left in two caves.
- No server errors in any of the three runs.

## Conclusion

Not reproduced, and no change to Hydrology or Earth. Generated worlds have no cave openings under river water. A cave
opened under a river fills to the river's level and the river keeps its sources. A cave too big to fill (H2 fills only
water that is held) still takes water as a fall from the cells directly above it. That is the bounded hole of the 0.1.5
note, and a sinkhole would do the same. This run did not test that case.
