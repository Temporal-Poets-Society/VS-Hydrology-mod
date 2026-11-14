# 0009: Closed basins fill to their sill, open valleys run off, dams on Earth's rivers

Date: 2026-11-14. Status: in release candidate **0.6.2-rc.11** (HY-0009, EA-0066, with HY-0010's allocation fix), point 5
refined in **rc.12** after the in-game look (below); not pushed, not installed. Code: `src/Rules/H2Release.cs` (`Search.Beyond`, the partial drain in `Opened`, `Body`'s river
floor, `MarkDammed`, `Dammed`), `src/Rules/H2Filling.cs` (the uneven basin's river guard), `src/EarthRiverQuery.cs`
(`RiverWater`, `RiverColumn`). Offline cases: `BasinCases`, `RiverDamCases`, `RiverRegressions` (now with Earth's
span), `AllocCases` (HY-0010).

## Context

Decision 0008 built the first steps of HY-0009: drains at a rate set by the opening (`PondDrainRate`), supply followed
upstream, dams on dug reaches. Two of the author's points from the discussion (13 November) were not yet in the rules:

- "It is a question of closed or open valleys": most dry valleys are open and lead to a river or the sea, most closed
  ones are lakes already. A drained pond's water should run off down an open valley, and fill a closed basin to its
  sill. rc.10 instead kept the whole pond whenever the path left the search or the loaded land, even after the water had
  fallen well below the opening, and whenever a closed basin on the way was too small for all of it.
- "A later wish is to dam a river to raise its level and make rapids for a water wheel." Decision 0008 left Earth's
  natural river columns alone: the H2 rules never stood water above a river's generated surface, so a dam across an
  Earth river raised nothing, and EA-0066 asked how such water should behave. Earth's decision 0022 (part A) says its
  query describes the river as generated and that a consumer keeps its own record of what players did; the author's
  words there: "if trying to dam river then it overflows to restore the downstream flow".

## Decision

1. **Open valleys run off.** When the path of a drained pond's water reaches the end of the search (`MaxSearch`) or an
   unloaded chunk after the water has fallen from the opening's level, the water is running down an open valley: the
   rest of it runs off there, as into the sea (counted as absorbed, it stands nowhere). At the opening's own level both
   still keep the water: there is no lower land in reach. This applies to ponds (bodies under 4,096 sources). A lake
   large enough to feed a channel (decision 0008, 4,096 sources or more) keeps its water there as in rc.10: the first
   rc.11 build let such lakes' top layers run off, and in the reach labs a channel dug from a 41 x 41 lake then showed
   only vanilla's flowing water, dry 5 blocks from the lake, with the reach below empty for the drain's 14 minutes, the
   very thing HY-0008 fixed. A draining lake whose stream runs the length of its channel is still open.
2. **Closed basins fill to their sill.** When the basins on the way hold only part of the water (filled up to the
   opening's level, or the water would land back in the pond, in another liquid or on the world's floor), the pond lets
   out the whole layers the basins hold, top layer first, and keeps the rest; its level then stands at the opening. A
   layer let out in part would fill again from the sources left beside its holes (vanilla's endless supply), so a basin
   smaller than one layer takes nothing, and H2 may complete a basin's last layer from the stream (as decision 0006
   allows within a layer). This is the same rule as 0008's drain whose openings are closed.
3. **Earth's query is not changed.** It describes the river as generated (contract version 3, `RiverSpanAt` gives the
   column's bed and surface). Water above a river column's generated surface is not river water: Hydrology owns it, and
   it is ordinary water that stands where it is held (0005) and drains when it is no longer held.
4. **A dam marks the reach it can back up.** A block a player places beside a natural river's water marks the river
   columns a dam there can raise: the surface layer of the river water beside the block (its pool) is walked across
   (16,384 columns at most), through the columns whose own water reaches that height, so the pool and the pools upstream
   (which stand higher) are marked and the pools downstream (lower) are not. Only in a marked column may the uneven
   basin stand water above the generated surface, and still only where the level is held and fed (the river feeds it).
   Without a player's block a river column is left alone as before: a natural river whose end is closed (an endorheic
   river, the labs' walled rivers) does not rise. Marks last for the session; after a restart the next placed block
   marks its reach again, and water raised before stays where it stands.
5. **The river is a feed, not part of a drained body.** A drained body never takes river water in. River water makes the
   body unlimited (nothing drains) only where it meets a cell outside the river's columns at or above the opening's
   level: a channel dug from the river keeps its water as before. Under or beside water raised over the river's own
   columns, the river is the body's floor and feed: when a dam's top course is broken, the raised water drains
   downstream at the drain's rate, and the river stands at its generated level again. rc.12: a body that holds any
   water raised over the river's own columns is a dam's water, even where it also covers bank cells beside river water
   at or above the opening (in game, the strip between a levee and the Rhine filled to the upper pool's level beside
   it, and rc.11 then took the whole dam's water for a channel fed by the river and drained nothing); only a body wholly
   outside the river's columns is a fed channel.
6. **The river below a dam keeps its generated water.** Hydrology never empties a river column. Drying a river below a
   diversion (0022 part B, item 5: a saved drained stretch down to the next tributary, a length cap) stays a proposal.

## In game (rc.11 and rc.12, 14 November)

Own AgentBridge bench, Earth main `ebceed0` as test zip 2.0.0-rc.12-hylook.1, 1:10:

- **Dam in a river column** (the Rhine below its falls, 47.68 N 8.62 E: an upper pool at y 149, a rapids ramp, a lower
  pool at 147, 11 wide). Levees to 149 along both banks, a dam 1 over the lower pool's surface 15 blocks below the step.
  After a block was placed beside the step's ramp, the reach rose to 148 (the dam's top, one below the upper pool) from
  the dam to the step, about 250 cells; the river below the dam stayed at 147. A block placed on the dam itself woke
  nothing: its reach (4,096 sources) is spent in the large pool below before it gets to the ramp. On rc.11 breaking the
  top course drained nothing (point 5); on rc.12 a 2-wide breach let out 254 sources in 127 s and a 1-wide one 254 in
  254 s, and the reach was back at 147 with the ramp where it was (six sources under the ramp lost their flow label).
- **Closed basin**: a 7 x 7 pond two deep (98 sources) in a stone block, a hole in its lower layer, a closed pit of 61
  cells below: it let out its top layer, 49 sources in 24 s through 1 cell, and kept the lower one; the pit held 58.
- **Open valley**: the same pond on the plateau above the Rhine (153), the hole facing down the slope: all 98 ran down
  into the river ("98 join river water", planned at 49 s, 2 blocks of water over the hole's floor); the pond is empty.
- Not seen in game: the run-off past unloaded land or the search's end (offline only).

## Consequences

- A pond opened over an open valley that leaves the loaded land or the search drains instead of keeping its water; one opened low
  over a pit that holds less than its water lets out what the pit holds (`pit-deep`: 49 of 98 into a 60-cell pit; rc.10
  let out nothing).
- A dam across an Earth river backs the pool behind it up to the dam's top (offline: 144 cells to the upstream pool's
  level, nothing below the dam or in the pool above); breaking its top course drains the raised water into the river
  (144 let out, none left raised, the river's own water unchanged). The pool rises only as high as the water that feeds
  it, as a dug reach does: on Earth's 1-block river steps that is the next pool's level, so a dam one block over the
  surface raises its pool to that level and a dam two blocks over raises it no higher (offline: 144 at the upstream
  pool's level, none at the crest). The raised water stands calm against the dam; nothing falls over the crest, so a
  river dam makes no rapids for a wheel yet. A river's own flow filling the pool to the crest and spilling over it (Earth
  0022 B2-B3: inflow from the reach's discharge) is the next step for the author's water-wheel wish, still open.
- No change for natural rivers without a player's block: `RiverRegressions` wake every cell of walled lab rivers, with
  Earth's span, and find no raised source.
- Costs: a placed block beside river water walks the pool's surface layer once (16,384 columns at most, one cached river
  answer each); `Body` asks the river query per source instead of per column (cached per column).
- Open: a river dam filling to its crest from the river's flow and spilling over it; a lake of 4,096 sources or more
  draining down an open valley with its stream visible the length of its channel; bodies whose level is kept as data
  when they are larger than the loaded area, batched fills for very large basins, one rule in place of H2's guards
  (HY-0009); drying below a diversion (Earth 0022 B5); the marks are not saved.
