# 0008: Reaches fed from upstream, drains that take time, dams

Date: 2026-11-13. Status: in release candidate **0.6.2-rc.9** (HY-0008, first steps of HY-0009), dam fixes in
**0.6.2-rc.10** (14 November, checked in game on the AgentBridge bench), on `main`, not pushed, rc.10 installed in the
author's game. Code: `src/Rules/H2Filling.cs` (`Supplied`, `TraceUp`, `FedChannel`, the uneven basin), `src/Rules/H2Release.cs`
(drains, `Placed`), config `PondDrainRate` and `PondUnlimitedSeconds`. The headless lab is `lab_reach`
(`tests/server/scenarios-reach.json`); the offline cases are `ReleaseCases` (`hollow-rate`) and the sheet case in
`DirectGuards`.

## Context

The author's rc.8 play test (HY-0008): channels dug from a large lake stopped filling after a few blocks, and below a
3-block drop nothing filled at all; from a pond, a channel with a drop made rapids, but the water below stopped
spreading. "Rapids are fine but from large lakes or rivers with infinite water." The lab (`lab_reach`) showed three
causes on rc.8:

1. A channel at a lake's level that ends at a drop is open below its level (the drop, the slope beyond), so decision
   0005 refused to hold it; vanilla's flowing water then dies about five blocks from the lake, before the drop.
2. Since rc.7 the uneven basin (the rule that seeds the cells under a falling sheet) measured the reach below a drop up
   to the water above it; on a slope the lower reach's banks stand below that, so it read as open.
3. Decision 0006 counted only sources joined to a space, and a reach below a fall is fed through falling and flowing
   water, which the count did not cross.

The author's direction for the lasting design (HY-0009): no constant ticking, the water redrawn on change; water from a
large body fills, or finds its way to the lowest body or area to fill, in the width of its opening; overflow has to
work, because a later wish is to dam a river to raise its level and make rapids for a water wheel; "drainage of large
lake would depend on drain rate - size of opening irl". And: "work on dams also after or include logic in pipeline."

## Decision

1. **Supply follows the flow upstream.** When the sources joined to a space are too few, 0006's count follows the
   flowing water that feeds them (the flowing water over the space, and over any source counted) upstream, at its own
   height or above, across falls, rapids and ramps, up to 256 flowing cells a feed and 16 feeds, and counts the body it
   reaches. A reach below a drop is fed by the lake its channel comes from.
2. **A reach is measured at its own level again.** The uneven basin asks whether the cell's own level holds (rc.7
   measured up to the sheet's top); the space up to the top is no longer what the water above has to fill, because the
   supply now crosses the fall.
3. **A fed channel carries its water to where it falls.** A level that 0005 refuses (open below it) is still held when
   the cells joined to the cell sideways at its level, through cells resting on ground or on the liquid, are closed by
   walls and sources within 4,096 cells, end in at most 8 edge cells with nothing under them (where the water falls),
   every edge is a lip (reached from a cell resting on ground: a channel's floor ending at a drop, a dam's crest), and
   they are fed, through the supply count above, by a body of 4,096 sources or more or a natural river. A terrace whose
   rim is all edge, or open land at the level, is refused as before; a pond's channel stays flowing water. An edge
   reached over water does not count: water over water that already runs out below stands no higher, so a trench with
   a free outfall carries one layer, not its depth (the first build filled such trenches to their banks, and the dam
   then had nothing to raise). Because that refusal may only mean that the layer below is still filling (behind a new
   dam), the cell is asked again after 3, 6, 12 and 24 s, then left.
4. **A pond drains over time.** Decision 0007's release lets the water out at a rate instead of at once:
   `PondDrainRate` (1) sources a second for each block of the breach's width (the columns of open cells beside the
   pond within 8 blocks of the opening, so a cell broken beside an earlier gap widens it) and each block of water over
   the opening's floor (a weir). A body whose top layer would take longer than `PondUnlimitedSeconds` (2,880, a game day at the default speed)
   to let out is unlimited and keeps its water, as a body of 65,536 sources or more does; the fixed 4,096-source limit
   is gone (water the released water lands in still takes it without rising at 4,096). Each second the drain removes the
   next sources (the top layer first, the far ones first) and stands as many at the next cells of the plan made when it
   started; the cells it has emptied, and its openings, are not filled again by H2 while it runs, so the pond's remaining water runs out as a
   visible stream. Another opening into a draining pond widens its outlet; an opening closed again stops its share, and
   once every opening is closed the layer being drained goes all the same and the drain ends (its holes would fill again
   from the sources left, water from nothing). Water landing on a pool runs on over it as a stream: only dry land at the
   opening's level counts towards the 8 blocks it runs to an edge, so the water raised behind a dam runs along the reach
   below to its outfall. A drain pauses while its opening's chunk is unloaded, is saved with the world (its openings) and planned again when
   the world loads. When it ends, water standing at or above the opening again drains in a new round (8 at most).
   `PondDrainRate` 0 lets it all out at once, as rc.8.
5. **Dams.** A block a player places (the server's `DidPlaceBlock`) wakes the flowing water over and beside the reaches
   it may close (the sources beside it and beside the cells below it, up to 4,096). H2 then fills the closed reach to
   the dam's top (a closed space fed through the fall above it), and, when a lake or a river feeds it, one more layer
   that spills over the crest (a fed channel whose outlet is the crest), so the water falls past the dam and H4 makes
   rapids there. Banks lower than the crest are the outlet instead; a terrace all edge holds nothing. Taking the dam's
   top course away is an opening: the raised water drains, and H2 keeps what the crest that is left still holds.
   Earth's natural river columns keep their generated levels (the H2 rules leave river columns alone): a dam on a
   river raises nothing until Earth decides how raised water in its river columns should behave. (Decided in [0009](0009-basins-valleys-river-dams.md), rc.11: Earth's query is unchanged, and a dam may back a
   river's pool up above its generated surface, as high as the water feeding it.)

## Consequences

- A lake at least 4,096 sources large carries a channel dug from it to its drop, the reach below the drop fills to its
  banks and, if it falls on, carries the water to the next drop; a pond's channel stays flowing water.
- A pond's water drains visibly: a 7 x 7 pond's top layer through a 2-wide break in about 25 s; a lake layer of 20,000
  sources would take hours through one cell and keeps its water.
- Lakes from 4,096 to 65,536 sources that rc.8 treated as unlimited now drain when a player opens them, as fast as their
  opening allows (a 41 x 41 top layer through 2 cells: about 14 minutes).
- Dams need Water flooding (H2's vessels and uneven-basin rules are its parts). Dams on Earth's rivers raise nothing yet
  (EA-0066).
- rc.10 (two fixes found in the dam lab and in game): while a pond drains, its emptied cells and openings hold no new
  sources. Vanilla makes one where a source spreads down into a cell beside two sources (`TrySpreadDownwards`) or a cell
  has three source neighbours; behind a dam whose top course was taken away, the water over the crest dropped sources
  into the gap, which closed the reach again and left 22 sources standing over the course left. H5 empties such a
  source after the update that made it. And a placed block makes H2 forget what it remembered about the reach before
  waking it: a dam built a block at a time by hand raised nothing in game, because the first block's refusal (the dam
  still open) was remembered for 2 s when the last block went in. `lab_reach`'s `DamStepMs` builds the dam a block a
  second (`reach_lake_dam_hand`).
- Costs: a refusal also tries the fed channel (up to 4,096 cells at the level and a supply count of up to 4,096); a
  placed block next to water looks at up to 4,096 sources; a drain does a few block writes a second. Nothing runs on
  still water.
