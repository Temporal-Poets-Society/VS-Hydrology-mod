# 0006: A space fills only from water that could fill it

Date: 2026-11-13. Status: bugfix in release candidate **0.6.2-rc.7** (HY-0007), on `main`, not pushed, not installed.
Since 0.6.2-rc.8 a pond a player opens does lose its water above the opening ([0007](0007-pond-release.md)); the rule
here still decides what endless and unlimited water fills. The
headless lab is `lab_pond` (`tests/server/scenarios-pond.json`); the offline cases are `PondCases`, plus changed
expectations in `HeldCases`, `HeldTrenchAndLongCanal` and `DirectGuards`.

## Context

A player broke the rim of a small pond on high ground, and a large area below flooded, with water standing several
blocks over the land (HY-0007; two screenshots, 13 November 2026). The author asked for water to account for the volume
of the body it comes from: "Sea get unlimited volume and probably rivers."

Decision [0005](0005-held-levels.md) lets H2 fill a cell only where its level is held: the open space at and below the
cell's level, reached from it, closes against walls and sources within 4096 cells at the level and 16,384 in all. It
said, as a consequence, that a closed basin under a source fills to the source's level, "as vanilla's endless sources
would". That is the flood. Vanilla's own refill needs three source sides, so in vanilla a broken rim lets out flowing
water and nothing more. H2's ground rule, vessels case and uneven basin treat any source as a body whose level holds,
however little water stands behind it.

The headless lab (`lab_pond`, a vanilla world with a fake Earth river query of version 3, Water flooding on) reproduces it
on 0.6.2-rc.6, not in vanilla. A pond 7 x 7 x 2 (98 sources) in a mound 4 above a walled lowland, its rim broken two
wide at the pond's level, filled the lowland with 3,818 sources, 4 deep, to the pond's level: the ground rule fills the
two broken cells from the pond, and the uneven basin then seeds each layer of the lowland under the falling sheet. The
same pond breaking onto closed flat land at its level flooded it with 956 sources (the ground rule, cell by cell). A rim
broken one wide, or a lowland with an outlet, kept only flowing water, as in vanilla. Salt water did the same as fresh.
Which version the player ran is not known. 0.6.1, the version on the Mod DB, has no held check, so a breach floods
open land as well; 0.6.2-rc.3 to rc.6 flood a hollow closed within the held limits. Both come from the same rules.

## Decision

A held space fills only when the water that bounds it could fill it (`H2Filling.Supplied`, config `SupplyFactor`, default
2; 0 turns it off). The held search already walks the space. The sources of the cell's liquid it meets on its way are
the bodies that would feed the space, and their water is counted from the space's floor up: water below the floor
cannot rise into it. The fill goes ahead when that water is at least half the open space (`SupplyFactor` 2), or when one
of its sources stands in a natural river column (Earth's river query): a river is fed from upstream. The sea and large
lakes pass by their size; no liquid is unlimited by kind.

The uneven basin, which fills a cell under a falling sheet, measures the space up to the top of the sheet, not to the
cell's own level, so the body at the top of the fall is the one counted.

A refused fill counts in `HydrologyStats.Unsupplied` and is remembered for 2 s, as an unheld one.

Sources stay endless, as in vanilla: nothing drains a pond. The factor bounds what one opening floods; it is not a
balance of volumes. A balance (a pond losing a layer for what it gives) was considered and left out: H2's refill, vessels
case and vanilla's three-sided refill rebuild a lowered layer from the sources around it, so a pond would have to lose
every source of a layer at once, across the pond and everything joined to it, and re-check that on every change.

## Consequences

- A pond breaking into a hollow below it, or onto closed flat land at its level, lets out flowing water and keeps its own
  water. In the lab: 0 sources outside the pond in every case, against 3,818 and 956 on rc.6.
- A lake (41 x 41 x 3, 5,043 sources) breaking into the same kind of hollow still fills it (6,514 sources), as do the sea
  and rivers into anything they bound.
- A canal or trench dug from a lake, a pool or the sea fills as before: the offline canals (pools of 840 sources) and the
  headless trench and canal labs, whose pools hold 90 to 297 sources above a trench's floor, end as on rc.6. A pool feeds
  a space up to twice its water above the space's floor: the smallest lab pool (9 x 11 x 1) fills a canal 3 wide, 3 deep
  and 20 long (180 cells).
- Of the 61 regular headless labs, 56 end as on rc.6. The four chute labs keep their last step: with Water flooding on
  and no river query, rc.6 raised the lowest pool to the step above it from the six sources of that step's pool.
  `lab_drop5`'s pool (72 sources) no longer fills the edge of a channel over a closed pit 5 deep (168 cells), so the edge
  stays flowing water instead of two rapid sources. The wheels read as before.
- A bucket emptied over a closed sea or pond no longer raises it: only the bucket's water stands at the new level
  (offline `HeldCases`, which expected the closed basin to rise from rc.3 to rc.6, now expect nothing to rise).
- A canal 3 x 3 x 501 dug out whole from a reservoir of 216 sources keeps flowing water; from a river (Earth's query) it
  fills, as for the author's issue #20 use.
- Dug outward a few cells at a time from a small pond, each new space is small and the pond's water grows with every
  filled cell joined to it, so a ditch dug that way still fills cell by cell, as before. The rule is about what one
  opening floods by itself.
- The count stops as soon as it has enough water (half the space at most, 8,192 sources), and a held answer is still
  remembered for every cell of the space for 20 s, so a large basin is counted once.
- Worlds already flooded keep their water; nothing drains it.
