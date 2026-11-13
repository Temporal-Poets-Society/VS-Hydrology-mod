# Hydrology version 1: how the rules are built

The rules are Earth's [decision 0024](../../Earth/docs/decisions/0024-hydrology-water-mod.md), section 2 (H1–H6). This
note records how version 1 implements them on top of vanilla 1.22.7, what vanilla does that made each piece necessary,
and how the design leaves room for the later targets (section 4).

## Where Hydrology hooks in

Vanilla's water, salt water and rapid water all use the block behaviour `FiniteSpreadingLiquid`
(`BlockBehaviorFiniteSpreadingLiquid`, VSSurvivalMod). A neighbour change schedules a delayed update of the cell
(`OnDelayedWaterUpdateCheck`, 75 ms for water, 125 ms for salt water, 50 ms for rapid water), which spreads, lowers or
raises that one cell. Hydrology keeps this behaviour and patches six of its methods with Harmony
([H5LiquidHooks](../src/Rules/H5LiquidHooks.cs)):

| Hook | Rule |
|---|---|
| prefix of `TrySpreadDownwards` | H1 refuses a fall into a same-liquid source; H3 then lets the cell spread sideways |
| prefix of `RemoveOtherLowerNeighbours` | H1: water flowing down removes only lower neighbours, never same-level ones (sources, or the other columns of a wide fall) |
| prefix, postfix and finalizer of `OnDelayedWaterUpdateCheck` | before: H4 lets a rapid sheet spread like ordinary water; after: H7's survival, H3's falling water in the air, H2, H7's level relax, then H4 on the cell and the cells it feeds; for a cell vanilla has just emptied, the sources beside it are woken (vanilla's unique callbacks could leave a source beside an empty cell for good); the finalizer resets the sheet switch even if the update threw (0.6.2). Lava's and boiling water's updates, which use the same behaviour, are skipped (0.6.2) |
| postfix of `GetLiquidBlockId` | H7's fall label, then H4's river flow labels |
| prefix of `GetMaxNeighbourLiquidLevel` | H7's side-feed rule (falling water in the air feeds no side) |
| postfix of `updateOwnFlowDir` | H7's wake on the fall label |

Every rule is its own class in [src/Rules](../src/Rules), and reads a bounded neighbourhood: a few cells, one column, or
H2's and H4's small searches. Changes are written with `Liquids.SetAndWake`: the block, its own delayed update and its
neighbours' updates, the same way vanilla wakes a cell. Hydrology registers no tick listener and no callbacks of its own.

The patches are applied on the server only (in singleplayer once for the shared process); each side removes only its
own patches when it stops (0.6.2).

**Game updates (0.6.2).** Every patch, and every reflection on the game's non-public members, goes through
[GamePatches](../src/GamePatches.cs). A feature names all its targets, by name, exact parameter types and return type
(declared on that type), plus any public game members its own code uses directly; all are looked up before anything is
patched, and a missing or changed one is logged once and switches off only that feature:

| Feature | Targets | Off means |
|---|---|---|
| the water rules (H1–H4, H7) | the first three hooks above; `multiplySpread`, `ReplacableThreshold`, `GetLiquidBlockId` | vanilla water (logged as an error); the next three are then not applied |
| H7's fall label and H4's flow labels | `GetLiquidBlockId` postfix | `FallLabel` and `QueryFlowLabels` off |
| H7's side-feed rule | `GetMaxNeighbourLiquidLevel` prefix | `NoSideFeedFromFalling` off, so level relax counts the same feeders as vanilla's lowering |
| H7's wake on the fall label | `updateOwnFlowDir` postfix | `WakeOnFallLabel` off |
| H8 (wheels on rivers) | `BEBehaviorMPWaterWheel.CheckWater(float)`, its `flowRate` | vanilla wheels |
| C1 (client foam) | `BlockWaterflowing.OnAsyncClientParticleTick` | vanilla particles |
| the chunk pack fix | `WorldChunk.Pack`, `TryCommitPackAndFree`, five fields (`MarkModified` optional) | vanilla commits |

Earth's river query is Earth's public interface, bound in `EarthRiverQuery` the same careful way (it never throws).

## Rule by rule

**H1, sources stay put.** Vanilla never lowers a source directly, but two spreading paths replace one:
- `CanSpreadIntoBlock` lets non-source water of the same flow rate spread *down* into a source when the source is not
  falling water, and `TrySpreadDownwards` then writes falling water there. Poking a river step in the 2026-10-22 test
  turned 48–153 pool sources into flowing water this way.
- `RemoveOtherLowerNeighbours` (rapid water only) removes neighbours of the *same* level when water flows down, which
  for a source are other sources.

**H2, filling** ([H2Filling](../src/Rules/H2Filling.cs)). After vanilla's update of a non-source cell: if it rests on
solid ground, and two of its four neighbours are sources of that liquid, or one is and so is the diagonal cell beside it
with the square's fourth cell holding the same liquid, it becomes a source and wakes. The square condition keeps a
1-wide channel flowing (its square always contains a wall). Vanilla's own rule (three sources, or two plus three
diagonals) stays in place.

**H2, held levels** (0.6.2-rc.3, [decision 0005](decisions/0005-held-levels.md); a player's report on 0.6.1: a source
spilled into the sea and the sea climbed to it). The ground rule, the vessels case and the uneven basin fill a cell only
where its level is held: the open space at or below the cell's level, reached from it through faces water passes (dry or
wet cells, but no source), closes against liquid-tight walls and sources within 4096 cells at the level and 16,384 in
all; more space than that is an outlet. A source of another liquid is a wall, as vanilla spreads into none; flowing
water of another liquid and a block water passes into (a fence, a slab, a water wheel) are part of the space. A trench, a
canal with an uneven bed (also one dug out whole, 3 x 3 x 500), a port or a walled basin is held and fills to the level
of the water it is dug from. The sea, a lake with an outflow or open shores, a field whose edge drops away or a floor
open to a cave below is not held; a pond in a hollow closed above its surface is, and a source over it fills the hollow
to its level. Before, a bucket emptied onto the sea raised all of it a block, a pool two wide over a cliff made the sea
below climb to it, and two sources on open flat land flooded all of it (lab `lab_spill`). A search's answer is kept:
held for 20 s for every cell it covered (filling a basin searched it again for every cell, quadratic), refused for 2 s
for the cell asked. Refill and the pocket keep their own tests.

**Communicating vessels** (0.1.4, switch `FillVessels`, off by default). A flowing cell resting on a source fills
when a source beside it also rests on a source. Canals and ports 2 or more deep, dug from deep water, then fill layer by
layer ([server test](server-test-0.1.4.md)). On Earth 0.16.x up to 0.16.7 it floods the 1-block river steps, which have no
sill; at later steps, breaking the sill flooded until 0.1.7 (see below).

0024 also lets a cell resting on a source fill. Version 1 does not: Earth's river steps and falls have no lip (the upper
pool's lower layer touches the lower pool), so a cell over the lower pool has the same neighbours as the top layer of a
2-deep trench, and filling there flooded touched Earth steps and falls in the server test. This awaits the user's
decision ([test report](server-test-0.1.0.md)).

**H3, falling water in the air** (0.1.3, [H3Flattening](../src/Rules/H3Flattening.cs)). Vanilla labels a non-source
cell still when it has no sideways flow and neither the cell above nor the one below is falling. Once a few such cells
formed in the air (stones put into a waterfall and taken out again, Victoria Falls), every cell later added to that
column was labelled still too, and the column stood: vanilla counts water above a cell as feeding it. When a non-source
cell updates, the run of non-source water in its column is checked. Every still cell with water above it and air or
flowing water below it becomes falling water of the same level and kind, all at once (a cell fixed alone was relabelled
still from its neighbours). Cells with a flow direction, and cells resting on the ground or a pool, are left to vanilla;
labelling those too made vanilla relabel them at once, a loop in Earth's rock tubes.

**H3, flattening** ([H3Flattening](../src/Rules/H3Flattening.cs)). Vanilla treats flowing water resting on a source as
"spreading down" whatever happens, and stops there: it never spreads sideways over lower water. With the H1 refusal
returning "nothing went down", vanilla carries on as on the ground and draws the 6-to-1 ramp.

**H4, rapids** ([H4Rapids](../src/Rules/H4Rapids.cs)).
- *Edges* (0.1.1, Earth 0024 section 1b). After a source's update, if it has a side open onto water or floor that is
  not a source, two small searches over columns measure the water surface around it:
  - downstream, up to 6 column steps away: where water from the source lands (`DropAt`: how far it falls before it
    lands on a source or the ground, down to `MaxDropScan`). The search carries on through water and across dry ground
    at the same level (water spreads there anyway), but ends where water has fallen onto dry ground, so a lone step's
    pool never reaches an unrelated drop beyond its bank;
  - upstream, up to 5 column steps away: the highest water surface, through water at or a little above each
    column's.

  If the surface falls 2 or more within 6 column steps across this edge's own step, a fresh source becomes
  `rapidwater-still-7`; a rapid source that no longer qualifies and has another source beside it becomes
  `water-still-7`. A single drop of 2 or more always qualifies. Earth 0.16.4 draws chutes with 1-block steps up to
  3 blocks apart along the river (`ChuteSpacing`), which is up to about 5 column steps on a diagonal river; 6 steps
  reach the water below the next step. Every edge of such a chute qualifies; the upstream half of the window is what
  keeps the chute's last step rapid. A lone 1-block step does not (H3): Earth keeps at least 32 blocks of pool on each
  side of one. An edge that changes schedules an update for the sources within 6 column steps and 4 levels that are
  edges or rapid, so the other steps of a chute follow.
  - 0.1.0 looked only at the drop right beside the edge, so a chute's steps became ordinary ramps once touched.
  - 0.1.1 used a 3-block window, sized for steps 2 apart.
  - A rapid cell about to turn ordinary because an ordinary source feeds it first has that source checked (0.1.2):
    Earth generates its chutes with ordinary sources and rapid ramps.
- *Kind follows the feed.* Vanilla never lets rapid water replace ordinary flowing water (flow rates differ), and an
  ordinary falling cell stays fed by rapid water above it. So after an update, the cell and the cells it feeds take the
  kind of the water feeding them: the water above, else the highest neighbour (rapid wins a tie). That turns the
  existing ordinary fall into a rapid one when the edge becomes rapid, and back when it reverts. A kind change wakes
  only the cell itself: waking its neighbours woke an ordinary source beside it, which spread ordinary water into the
  cell again, about a thousand times a second on an Earth river.
- *Re-checking edges.* A change in falling or flowing water re-checks the sources beside the top of its column, so
  filling the drop from below reaches the edge.
- *Sheets.* Vanilla rapid water spreads as one stream (`multiplySpread: false`): a cell lowers itself when a neighbour
  has a better way down, and only one cell of a row may fall, so a 2-wide fall kept one column. While vanilla updates a
  rapid cell that rests on water, has a same-level neighbour, or is beside an H4 edge, it spreads like ordinary
  water. "Rests on water" (0.1.2) settles Earth's irregular chute ramps, which vanilla's single-stream logic otherwise
  lowered and raised for ever (about 790 updates a second on an Earth world). Lone streams, such as
  vanilla rivulets, keep vanilla's behaviour.
- *Wheels.* Vanilla's water wheel turns ordinary the rapid cell below its first power cell
  (`BEBehaviorMPWaterWheel.ReplaceRapidWater`), once a second; the kind rule turns it rapid again. The wheel keeps its
  power cells.

**H6, buckets (historical, superseded in 0.6.0).** Hydrology no longer declares or changes the bucket setting and the
old reporting-only H6 class has been removed. Enabling vanilla's **Prevent liquid source transport by buckets** is
strongly recommended, under the player's/server's control. Existing saved choices are not rewritten. See
[decision 0003](decisions/0003-public-release-and-vanilla-buckets.md). The original mechanism below is historical.

Vanilla applies
`noLiquidSourceTransport` through a conditional asset patch (`survival/patches/unbucket.json`): a full bucket then spills
`water-still-3`. A new world's config is built from every mod's world-config defaults in load order, so Hydrology's
`worldconfig.json` declaring the same setting with default `true` wins over survival's `false`. The creation screen shows
one row for it (it keeps the first mod's entry by code) and a server's `WorldConfiguration` can still set it.

**H7, settling** (0.1.5, [H7Settling](../src/Rules/H7Settling.cs)), from the
[water rules study](water-rules-study-2026-10-23.md). An offline simulator of vanilla's spreading found what 0.1.3 left
behind after blocks go into and out of a waterfall:
- sources that H2 made on a floor that was later taken away, hanging in the air and feeding a column below for good;
- a second curtain beside the first, held up by falling water feeding it sideways;
- ramps whose heights depended on the order of updates.

Each rule is a switch in `hydrology.json`, on by default:
- `Survival`: a source with nothing under it (air or flowing water) becomes flowing water of height 6 and drains. Any
  block that is not open (not air, not a replaceable plant) holds it up, and so does a source of its liquid. Containment
  on its sides was tried in the study and left out.
- `FallLabel`: a postfix on `GetLiquidBlockId`. Non-source water resting on neither a block nor a source gets the
  falling label.
- `NoSideFeedFromFalling`: replaces `GetMaxNeighbourLiquidLevel` with the same check written from vanilla's behaviour,
  except that falling water still in the air feeds no side. Falling water that has landed feeds its sides as in vanilla.
- `WakeOnFallLabel`: a postfix on `updateOwnFlowDir`. A cell whose label has just turned falling wakes the lower
  flowing cells beside it. Vanilla writes labels without waking anyone.
- `LevelRelax`: after the update, a flowing cell lower than its feeders allow is raised (6 under water, else the
  highest neighbour − 1), unless vanilla has just lowered it.

H2 and H4 gained:
- `RefillOnSource` = 3: a flowing cell on a source with 3 source sides fills. This is vanilla's own threshold, which
  vanilla reaches only when no drop is near.
- `RapidSourceSheets`: a rapid source with a source beside it spreads to all sides.
- `WakeEdgesOnNewSource`: a new source wakes the H4 edges in reach.
- `LoneVertical`: a source above or below makes a rapid source not lone, so H4 can revert it.

**H2, vessels on Earth worlds.** The vessels case is also on when the world-config attribute `hydrologyFillVessels` is
true. Earth 0.16.8 (pool layout 5) sets it on new worlds whose 1-block steps have a sill. It is read lazily, at most once
a second until found, because Earth sets it after Hydrology has started. Config `WorldMayFillVessels` false (0.1.7)
ignores the attribute.

**H2, vessels: depth and containment** (0.1.7, switches `VesselsDepth` and `VesselsContained`, on by default). A player who
broke the sill under an Earth step's edge flooded a Congo river from one step to the next (2026-10-24). The hole filled,
the upper pool's edge source then rested on a source, and the ramp cell beside it met the vessels condition. The lower
pool rose one block, ramp cell by ramp cell, over its flush banks. The vessels case now also needs:
- **depth:** the side source's water reaches at least as deep as the water under the cell. A canal fills from water at
  least as deep as itself; a one-block hole is shallower than the pool below the step;
- **containment:** no side of the cell is a dry ledge (an open cell over something other than water of its liquid). A
  canal's walls hold its water; a river's flush banks do not.

Left over (simulator, `watersim flood`): where a step's edge row is 1 deep on both sides, breaking the sill raises that
row by one block beside the ramp, 6 or 7 cells, held by the bank Earth raises beside a ramp. Locally it looks like a
canal dug from water as deep as itself.

**Earth's river query** (0.1.8, [decision Earth 0027](../../Earth/docs/decisions/0027-river-flow-query-for-hydrology.md), [study](flowquery-2026-10-25.md)).
Earth's mod system `EarthMap.EarthMapSystem` offers `RiverQueryVersion` (1 in worlds with pool layout 8, whose steps have
no sills; 2 for layouts 9 and 10; 3 for layout 11), `RiverAt(x, z, out rapids, out slope, out discharge)` and, from
version 2, `RiverFlowAt(x, z)`. Hydrology binds them by reflection once the game runs (`EarthRiverQuery`), keeps the
answers per column (they are geography; with Earth's span they hold only in the river's own water, see the known
limits below), and declares
`HydrologySystem.EarthRiverQueryVersion` (1 from 0.1.8, 2 from 0.1.10, 3 from 0.6.2), a public const that Earth reads by
reflection: Earth 2.0.0-rc.2 needs 3 (2.0.0-rc.1 needed 2) and draws a new world without sills (1), with flow-labelled rapids
(2) or with calm flow-labelled rivers and white water only at the drops (3, decision Earth 0046). A world whose version
is newer than this Hydrology reads is read at this Hydrology's version, with a warning. Where the version is 0 or Earth
is absent, nothing below applies and 0.1.7's rules stand.
- **No vessels in river water:** H2's vessels case never fills a cell in a natural river column. This is always on with
  the query; `QueryNoVessels=false` only logs a warning, because the sill-less steps would flood. Such worlds ask for
  vessels with `hydrologyFillVesselsWithRiverQuery`, which counts only where the query answers, so an older Hydrology
  keeps vessels off there. One exception, the enclosed pocket below.
- **Enclosed pockets** (`FillPockets`, 0.6.2, with the vessels case on; the author's playtests at the Nile, 3 November
  2026). A flowing cell with a source of its liquid beside it fills when it belongs to an enclosed pocket, in natural
  river water too. All open, non-source cells at its level that it reaches (at most 128) must rest on a source of its
  liquid, or hold water and rest on the ground, and sources or walls must surround them. Two cases led here:
  - A basin dug beside the Nile filled except where it overlapped the river's columns. There a pocket of flowing water 3
    deep stayed in the river's pool, and its drop kept the bank's sources rapid.
  - A pit dug into the delta's river bed left a nook a block wide and 10 to 14 deep beside the river. The ground rule
    keeps a 1-wide channel flowing, and the vessels case needs a 2 × 2 square, so the nook stayed rapid flowing water.

  So with flooding on, a 1-wide ditch or nook enclosed by water now fills, where it used to carry 6 blocks of flowing
  water. A step's ramp never forms such a pocket. At its level the lower pool runs on to the next drop, where a cell rests
  on flowing water or air, and out over its flush banks, which are dry ground. A dry cell on the ground is land, even
  where walls close it in: the offline river test, whose last pool has no outflow, floods its steps otherwise. Only a
  pocket's lowest layer rests on sources or its floor. So a flowing cell with a source beside it, over flowing water,
  wakes the cell below it. A block placed and broken at the surface of a pocket left by an older version then reaches
  the layer that can fill.
- **Rapids by reach** (`QueryRapids`): in a column the query marks as rapids (versions 1 and 2: a whole rapids reach;
  version 3: only a rapids reach's drops, the ramps below its steps and the steps' edges), a source at the surface is made
  rapid and never turned back, and flowing water is rapid. Covered sources are left alone. Elsewhere H4's window decides
  as before; it gives no answer, and changes nothing, where its 6-block window reaches a chunk column that is not loaded
  (0.6.2: such a column reads as air, a bottomless drop).
- **Flow labels** (`QueryFlowLabels`, 0.1.10 with query version 2; every river column from 0.6.2 with version 3):
  Earth's pool layout 9 draws a rapids reach's sources as `rapidwater-<flow>-7`, and layout 11 every river column's top
  source as `water-<flow>-7` or `rapidwater-<flow>-7`: a source with the river's flow, which looks like running water and
  pushes (a `still` source looks calm and has no push). Vanilla relabels a source from the levels around it whenever
  water beside it updates (`updateOwnFlowDir` and `GetLiquidBlockId`): `still` in a pool, toward a hole someone dug.
  A postfix on `GetLiquidBlockId` (after H7's fall label) gives a surface source resting on a source or the ground in a
  labelled column the query's flow instead of whatever vanilla chose. Covered sources and sources over air or flowing
  water are left to vanilla. `EarthRiverQueryVersion` 2 tells Earth this Hydrology keeps them in rapids reaches (layout
  9), 3 in every river column (layout 11). Since 0.6.2 this has its own switch: it no longer also needs `Rapids` and
  `QueryRapids`, so a server without white water keeps calm rivers' labels.
- **H8, wheels on rivers** (`QueryWheels`, `WheelPowerRef` 10, `WheelMaxBoost` 3): a postfix on
  `BEBehaviorMPWaterWheel.CheckWater`, which vanilla runs once a second. It raises a turning wheel's `flowRate` (its
  torque) by × (1 + slope · discharge / 10), at most × 3, when the wheel's axle stands over natural river water. The
  client keeps vanilla's reading for the sound.

**C1, rapid foam on the client** (0.1.11, [C1RapidFoam](../src/Rules/C1RapidFoam.cs), config `RapidSourceFoam` 0.2 and
`RapidFlowingFoam` 0.5).
- **Vanilla:** flowing water blocks (every flow label but still and falling) spawn foam particles on the client within
  37 blocks of the player, fading in over 7 s. Rapid water keeps about 24 alive per block.
- **The problem:** Earth's rapids reaches are rapid sources with the river's flow across their whole width, plus their
  ramps. At the Congo that is some 5 900 emitters, 140 000 particles wanted against the async pool's 80 000. The pool
  ran full and the foam pulsed while the player stood there (server test
  [0.1.11](server-test-0.1.11.md)).
- **The rule:** a prefix on `BlockWaterflowing.OnAsyncClientParticleTick` lets a rapid source spawn on 20 % of its
  ticks and flowing rapid water on 50 %; falling water (another block class) and ordinary flowing water are unchanged.
- **Calm sources** (0.6.2, `CalmSourceSpecks` 0, decision Earth 0046): an ordinary source labelled with a flow throws
  none of vanilla's white specks. Earth's layout 11 labels every calm river's top sources so, and thousands of specks
  read as bubbles on slow water. The client cannot tell Earth's rivers apart, so it also applies to vanilla's few
  direction-labelled sources (a pond's sources at its outflow, a spring on a slope).
- **Where it runs:** on the client, where Hydrology runs there: it is required on clients since 0.6.1, and a client
  turns it off on a server without Hydrology. It changes no block and sends nothing.

**Not a water rule: the chunk pack fix** (0.1.9, [EngineChunkPackFix](../src/Rules/EngineChunkPackFix.cs), config
`ChunkPackFix`). The game can lose block edits when it commits a chunk's compressed copy late
([VintageStory-Issues#10177](https://github.com/anegostudios/VintageStory-Issues/issues/10177),
[report](upstream/chunk-pack-commit-lost-edits.md)). The patch drops a pending copy if the chunk changed or was used after
its pack began. It counts writes from ChunkDirty and MarkModified, and notes the count and time in `Pack()` under the
chunk's lock. "auto" applies it on game 1.22.x, "on" on any version, "off" never. It is harmless beside a fixed engine;
switch it off once the game's fix is confirmed. Tests: [server-test-0.1.9.md](server-test-0.1.9.md).

## Room for the later targets (0024 section 4)

- *Long-running flow and sub-levels (floods, overflow).* All Hydrology writes go through vanilla block ids and one
  helper; a later flowing-water model can replace the `FiniteSpreadingLiquid` hooks rule by rule. No rule depends on
  vanilla's 6 levels except H3's ramp, which reads them from the blocks.
- *Ponds rising to a spill point, dams that fail, rivers drying below a diversion.* These need bounded searches over a
  body of water, off the main thread. Version 1 keeps no state outside the blocks, so a future area record (keyed by
  area, as 0024 requires) has nothing to reconcile.
- *Power from drop height × flow.* H4 already measures the drop height (`DropAt`). A wheel patch can read it, together
  with Earth's river flow query, instead of relying on rapid water.
- *Sluices and locks* fit the same edge model: a gate opening or closing is a neighbour change.

## Known limits of version 1

- Salt water has no rapid block, so salt-water drops stay vanilla.
- Earth's river query answers per column. With an Earth that offers `RiverSpanAt` (2.0.0-rc.2 and later) Hydrology
  applies its rules for flowing water only from the column's bed to two blocks above its water surface (the ramp below
  a step, and water spilt over a step's edge in Earth's layouts 8 and 9), and its rules for sources (flow labels, rapid
  sources) only up to the surface, so a channel on a bridge over a calm river or a cave pool under its bed is ordinary
  water; H8's wheels count where a wheel's rim, within 3 blocks of its axle, reaches that water. A span that fails is
  asked again, and one whose bed lies above its surface counts as none. With an older Earth the rules hold at any
  height in a river column, as in 0.6.2-rc.1.
- A `RiverAt` that throws gives no answer for that call, and nothing is kept: the column is asked again next time.
  Earth 2.0.0-rc.3 throws `EarthMap.Core.RiverQueryNotReadyException` where its drawn answer is not ready yet (a height
  tile not on disk; rare). Since 0.6.2-rc.5 (the Earth RC session's proposal, the author's go-ahead) Hydrology knows that
  exception by its type name: it logs no failure, and the water in that column stays as it is. The column's updates
  wait, vanilla's own included, and are tried again every second; a source in it keeps its river label when a neighbour
  updates. A column waits a minute at most; then its water updates without an answer, as before rc.5, with one
  warning. Any other exception still gives "no river" for that call.
- H3 applies on lower water only. Water spilling onto a dry floor one block lower behaves as in vanilla (0024 open
  question 3).
- Since 0.1.12, with vessels enabled, supported cells below a wide source sheet seed a lower layer across an uneven
  bed. The scan follows continuous same-liquid water upwards, at most 16 cells including the landing cell, and needs
  a wet 2 x 2 square with two adjacent sources along its far side. It respects liquid barriers. It runs only for salt
  water or fresh water with Earth's query in dimension 0, outside natural river columns. Fresh water without the
  query retains the older depth/containment rules. Since 0.6.2-rc.3 the landing's level must be held (above), so a
  sheet falling into the open sea seeds nothing. This is a local extension to H2, not the later managed-pond model.
  Vanilla's new sources notify neighbors once, so previously settled cells can use their new support. See
  [decision 0001](decisions/0001-uneven-canal-filling.md) and the [issue #20 report](issue-20-suez-flood.md).
- No filling rule can start when an edit sends no update to any water. Unsupported sheets, one-wide streams, and
  sheets whose nearest source square is beyond the bounded scan do not acquire a source through the new rule.
- Bucket source transport follows the vanilla world setting; 0.6.0 removes Hydrology's original default override.
- A held level is judged from at most 4096 open cells at the level and 16,384 in all: a basin larger than that, dug out
  whole before it is opened, does not fill by H2 in one go (dug outward from the water, each step fills). A fill
  refused because the space was open is asked again only when its water is next disturbed: closing a gap far from the
  water's edge does not wake it. A held answer stands for 20 s, so a basin opened within that time may fill a few more
  cells. Unloaded chunks read as air, so a search reaching one counts it as open. Water an older version flooded stays.
