# 0007: An opened pond lets its water out

Date: 2026-11-13. Status: in release candidate **0.6.2-rc.8** (HY-0007), on `main`, not pushed, not installed.
Since rc.9 ([0008](0008-fed-reaches-drains-dams.md)) the water drains at a rate instead of at once, and the fixed
4,096-source limit for an unlimited body is replaced by the time its top layer would take. Code:
`src/Rules/H2Release.cs`, config `PondRelease`. The headless lab is `lab_pond` (`tests/server/scenarios-pond.json`), whose
break now runs the release as a player's break does; the offline cases are `ReleaseCases`.

## Context

Decision [0006](0006-water-volume.md) stopped a broken pond from flooding the land below it, but left vanilla's endless
sources alone: the pond kept its level and let out only flowing water, and 0006 called a true balance of volumes too
hard to keep against H2's and vanilla's refills. The author then asked for the water to go somewhere: "if pond is drained
then it should end up in lowest point by flow". In the coordinator's words: the water a drained pond loses must not
vanish or spread as a flat sheet; it runs downhill along the flow path and collects at the lowest reachable point (a
hollow fills there with the pond's volume, conserved); the sea and rivers absorb it without rising.

A balance kept on every update would fight the refills (0006). A player's opening, though, is one event: at that moment
the pond, the path and the destination can be measured once and the water moved in one step, with nothing for the
refills to rebuild.

## Decision

When a player breaks a block (the server's `DidBreakBlock`, which fires only for players), and a source of water or salt
water stands beside the opened cell at its level, Hydrology measures that body and, if it is small, moves its water:

1. **The body** is the sources of the liquid joined to that source. A body of 4,096 sources or more, or one with a
   natural river column (Earth's river query), is unlimited: nothing moves, and 0006's supply rule decides what its
   water fills. This is the size that bounds a held level (0005).
2. **What drains** is the body's sources at or above the opened cell. Water below the opening stays.
3. **The path.** From the opening the water runs along the opening's level to the nearest cell with nothing under it,
   up to 8 blocks away (vanilla's flowing water runs 7 blocks from a source), falls from there to whatever holds water,
   and runs on. Landing on, or meeting at its level, the sea, a river or a large lake, it joins that water, which does
   not rise. Landing on a small pool of the same liquid, it stands on top of it.
4. **Where it stands.** Where it lands the water stands as a pool, nearest the landing point first, one source for each
   it lost, unless the flowing water around that pool would reach an edge (within 8 blocks of the pool): then it spills
   over that edge and runs on down. A hollow it fills to its walls it fills layer by layer upwards. Every filled cell
   must be below the opening.
5. **Nothing moves** when the water does not fit below the opening (no lower land within 8 blocks of the opening on its
   level; a closed basin smaller than the water), when the search grows past 32,768 cells (with the bodies it meets),
   reaches an unloaded chunk, runs back into the pond or onto another liquid's pool. The pond then keeps its water and
   lets out flowing water, as under 0006: water never vanishes and never spreads as a sheet over open land.
6. **Apply.** The drained sources become air and the new cells sources, each woken; H2's remembered held answers are
   forgotten in a box around both (8 blocks wider), not everywhere: forgetting all of them changed fills elsewhere that
   were still running on remembered answers. One log line says where the water went (`Hydrology: a pond opened at … let
   out N sources: N collect lower down from …`); an opening whose water stays goes to the verbose debug log (a trench
   dug from a pond opens one at every break). `HydrologyStats` counts releases, sources moved, sources absorbed and
   openings whose water stayed.

The move is immediate. Water in transit has no volume in Vintage Story, so no stream is simulated; the flowing water
that the pond's remaining sources would send out is gone with them. Volume is kept to the layer: a partly filled last
layer that holds at least a third of its level is completed by H2 under 0006's supply rule (half the space), so a
release into a tight basin can stand up to two thirds of a layer higher (the offline lake: 814 moved, 816 stand); a pool
on a wide floor stays as it was placed.

Only players start it. Buckets, world generation, other mods' block changes and blocks broken by the game itself do
not. Config `PondRelease` (true) turns it off.

## Consequences

- A small pond whose rim breaks on high ground loses its top layer, and that water lies in the lowest hollow its flow
  reaches. In the lab: a pond 7 x 7 x 2 (98 sources) broken 1 or 2 wide at its surface keeps 49, and 49 sources lie on
  the walled lowland's floor (with flowing water around them); salt water the same.
- Onto closed flat land at the pond's level nothing moves (98 stay, no source outside).
- Onto a ledge at the pond's level whose edge is 5 blocks from the opening, the water runs to the edge, falls and
  collects at the foot (offline); with the edge 20 blocks away nothing moves.
- A lake of 5,043 sources is unlimited and fills its hollow as under 0006 (6,514). A lake under 4,096 sources drains its
  top layer like a pond (offline: 814 sources into the hollow around it).
- Water reaching the sea or a river joins it; the sea does not rise (offline: a pond over river water, 1,263 river
  sources before and after).
- Digging a ditch outward from a pond moves nothing while each break opens onto land at the pond's level; the break that
  opens the ditch onto a slope lets the pond's water above the ditch's floor out down that slope.
- A break beside the sea, a large lake or a river measures the body up to 4,096 sources and stops; a break onto open
  flat land looks at up to 32,768 cells once. Nothing runs on liquid updates.
- Worlds already flooded keep their water; the release acts only on openings made from now on.
