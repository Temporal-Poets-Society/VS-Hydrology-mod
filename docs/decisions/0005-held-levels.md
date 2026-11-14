# 0005: Water stands only where it is held

Date: 2026-11-09. Status: bugfix in release candidate **0.6.2-rc.3** (branch `claude/rc-0.6.2`, commits `f697980` and
`860cb7b`), refined in **0.6.2-rc.4** after a second review (`373c8aa`, see "rc.4" below); on `main`, pushed to the private
origin, not uploaded;
installed in the author's game with rc.4, now rc.5, on 9 November (by the Earth RC session). The headless lab is `lab_spill` (`60497df`, `312b6ea`; scenarios `tests/server/scenarios-spill-*.json`).

## Context

A player's comment on the Mod DB (8 November 2026), about the released 0.6.1 with Earth: a friend made a water source
that spilled into the sea, the sea climbed up to the source, and it looped until a "tsunami" covered most of the map;
the server ran slowly. The author passed it on: "old version comment by user".

The headless lab (`lab_spill`, vanilla 1.22.7 as the control, Water flooding on as new worlds have it) reproduced three
ways, the same on 0.6.1 and on 0.6.2-rc.2, none in vanilla:

- **A bucket emptied onto the sea or a lake** raised all of it a block (255 sources one level up, against vanilla's one
  source and four flowing cells). H2's vessels case fills a flowing cell resting on a source when a source beside it also
  rests on one; H3 spreads the bucket's water over the surface, and every new source feeds the next.
- **A pool two wide at the top of a cliff over the sea** made the sea climb to the pool, one level at a time, and then
  flood the plateau at the pool's level. The uneven basin (0.1.12, [0001](0001-uneven-canal-filling.md)) seeds the cell
  under a falling sheet from the source square at its top, and the vessels case spreads each seeded level. Salt water does
  this anywhere, fresh water where Earth's river query answers (outside river columns). One source alone does not.
- **Two sources placed on open flat land** flooded all of it at their level (180 cells in the lab). The ground rule fills
  a flowing cell on the ground beside two sources, or beside one with a source diagonally; it does so with Water flooding
  on or off.

On a hillside these chain: a flooded terrace falls two wide onto the next, which floods, down to the sea, which then
rises. The offline suite with rc.2's H2, in a world whose land drains to a lowland, raised 4,095, 15,662 and 4,094
sources in 66,000 to 116,000 updates for the three cases: the lag.

All three treat any source as a body of water whose level holds. A canal dug from a lake fills to the lake's level
because the canal's walls and the lake hold that level; a bucket on the sea has nothing holding it a block above the sea.

## Decision

H2 fills a cell by the ground rule, the vessels case or the uneven basin only where its level is **held**: the open space
at or below the cell's level, reached from the cell through faces water passes (dry or wet cells, but not sources of its
liquid, which are water already held), closes within 4096 cells against liquid-tight walls and sources of its liquid.
Another liquid or more open space than that is an outlet. A block water passes into (a fence, a slab, a water wheel) is
part of the space, and what lies beyond it decides: a first version counted it as an outlet, and a closed chute with a
wheel in it then kept flowing water at its top where it had filled before. (`H2Filling.Held`; refused fills count in
`HydrologyStats.Unheld`.)

Refill (three source sides, vanilla's own rule) and the enclosed pocket (which has its own, stricter test) are unchanged.

### rc.4 (second review: the H2 audit and Codex's adversarial review)

- **The level and the whole space have their own limits**: 4096 open cells at the cell's level, 16,384 in all. rc.3
  counted every open cell against 4096, so a canal 3 x 3 x 500 dug out whole and then opened (4,500 cells, the
  author's issue #20 use) was refused at its surface while its depths were unfilled, and nothing asked again: it ended a
  layer short (offline: 3,006 of 4,509 cells).
- **Answers are remembered**: a held answer stands 20 s for every cell the search covered, a refusal 2 s for the cell
  asked. rc.3 searched a basin again for every cell it filled (Codex: 41 million block reads for one 63 x 63 layer),
  and a cell the vessels case refused was searched again by the uneven basin in the same update.
- **A source of another liquid is a wall**, as vanilla spreads into none; rc.3 called it an outlet, so a canal joining
  a fresh river to the salt sea never filled. Flowing water of another liquid is part of the space.

## Consequences

- A trench, a canal with a pit or a hump in its bed, a port and a walled basin fill as before: the offline scenario
  lines (canals, uneven beds, pockets, barriers, rivers) are identical to rc.2's, update counts included; the 61
  headless labs stay within run-to-run noise, with no fill refused. Also a canal 3 x 3 x 501 dug out whole (rc.4).
- The sea, a lake with an outflow or open shores, a field whose edge drops away and a floor open to a cave below do not
  rise. A source on them stays a source with flowing water around it (H3 spreads that water over the surface below:
  36 flowing cells in the lab against vanilla's 4).
- A closed basin under a source fills to the source's level (walled all round, the lab's sea still rises to a bucket
  emptied onto it; so does a pond in a hollow closed above its surface): with vanilla's endless sources, that is where
  the water stands. Superseded in 0.6.2-rc.7 by [0006](0006-water-volume.md): a space fills only from water that could
  fill it, so a bucket no longer raises a closed basin and a broken pond no longer floods a hollow below it.
- A basin with more than 4096 open cells at a fill's level or 16,384 in all, dug out at once before it is opened, no
  longer fills by H2 in one go; dug outward from the water, as players dig, each step is small and fills.
- The search runs only when a fill's own conditions hold. Over the sea or open land it walks its whole limit before it
  refuses (about 5 ms in the labs); the refusal is then remembered for 2 s.
- A refused fill is asked again only when its water is next disturbed: closing a gap far from the water's edge does not
  wake it (Codex). A held answer stands 20 s, so a basin a player opens within that time may fill a few more cells.
- Unloaded chunks read as air: a search that reaches one counts it as open and refuses.
- Worlds already flooded by an older version keep their water; nothing drains it (old saves do not matter for this
  release, the author, 8 November).

After rc.13 (HY-0014, the code review of 14 November): the answers this decision's search remembers for 20 s are
forgotten when a player breaks or places a block within a block of any cell they cover (the whole search goes), and
they are kept per liquid. In the lab a trench opened at its far end 3 s after it was dug kept filling for the memo's
20 s (30 of 40 cells); now it stops (3).
