# Hydrology 0.6.2 — calm rivers for Earth 2.0

**0.6.2** (14 November 2026) is rc.13 with the code review's fixes and refactors (HY-0012 to HY-0027, re-reviewed by
the Fable reviewer until nothing was open) and decision 0011; its checks are under
[0.6.2, the release](#062-the-release) in Verification. The history of the candidates follows.

Prepared 8 November 2026 on `claude/rc-0.6.2`: `release/0.6` (0.6.1, `433f44c`) with the calm-rivers work
(`claude/calm-rivers`, 0.6.2-rivers.7, `be9d859`) merged at `1d91e7e`, then reviewed, tidied, fixed and baked. Built for
[Earth's rivers](https://mods.vintagestory.at/earth) and Earth 2.0; standalone gameplay is not tested for this release.
[Decision 0004](decisions/0004-release-0.6.2.md) records the scope. rc.1 and rc.2 were baked the same evening: rc.2
fixes what rc.1 had left, with Earth-side changes on Earth branch `claude/rc-2.0-hydrology-leftovers`, merged into
Earth 2.0.0-rc.2 (`d50f2ae`). rc.3 (9 November)
fixes a flood a player reported on 0.6.1 ([decision 0005](decisions/0005-held-levels.md)); rc.4 (the same day) follows a
second review and an in-game water playtest; rc.5 handles Earth 2.0.0-rc.3's "not ready yet"; rc.6 (13 November) logs
the world's Water flooding and brings the server tests to Earth 2.0, with no change to the water; rc.7 (the same day) stops
a broken pond from flooding the land below it ([decision 0006](decisions/0006-water-volume.md), HY-0007); rc.8 (the same
day) lets the water of a pond a player opens run downhill and collect below ([decision 0007](decisions/0007-pond-release.md));
rc.9 (the same day) lets channels from lakes and rivers run on past drops, drains opened ponds over time and makes dams
work ([decision 0008](decisions/0008-fed-reaches-drains-dams.md), HY-0008 and the first steps of HY-0009); rc.10
(14 November) fixes two dam faults found in the lab and in game, after the author's "fix then", "look yourself" and
"install but do in game test by yourself by building dam".

## Release notes

- **Calm rivers** (Earth decision 0046): Hydrology reads Earth's river query version 3. Every natural river column
  keeps the river's flow label, so a block placed in a calm river or a dug bank no longer leaves still water around it,
  and Earth 2.0 gives new worlds calm, gently flowing rivers with white water only at the drops of steep rivers (pool
  layout 11) when it finds this Hydrology. Older worlds keep their layout and behave as before.
- **Enclosed pockets fill** (with Water flooding on): a basin dug into a river's edge where it overlaps the river, or a
  1-wide nook or ditch enclosed by water, fills with source water instead of staying flowing or rapid water (the
  author's Nile playtests).
- **No white specks on calm river water** (`CalmSourceSpecks` 0); rapids keep their reduced foam.
- **Fixed:** water poured onto lake ice drained away, and H4 measured drops through the ice, so a source beside a
  frozen pond turned into rapid water; lake ice now counts as a floor, as in vanilla.
- **Fixed:** white water could appear on a calm step next to a chunk that was not loaded (it read as a bottomless drop)
  and stayed after the chunk loaded.
- **Fixed:** lava and boiling-water updates woke the water sources beside them.
- **Fixed:** with `Rapids` or `QueryRapids` set to false, calm rivers lost their flow labels again; `Rapids=false` did not
  switch off all of H4. `QueryFlowLabels` is now the labels' own switch.
- **Fixed:** a rapid block type could keep spreading as ordinary water after an exception in an update; an Earth that
  failed to answer the river query was remembered as "no river"; the first second after start could refuse the vessels
  case in Earth worlds; `ChunkPackFix: "false"` meant on.
- **Game updates:** every game patch and private-member reflection goes through one registry. A target a game update
  removes or changes switches off only its feature, with one warning in the log, instead of leaving the water rules
  half-patched (and the chunk-pack fix skipped). In singleplayer each side now removes only its own patches.
- An unreadable `hydrology.json` is no longer overwritten with the defaults; `/hydrology stats` counts since the world
  started; the release zip holds a fixed list of files.

### 0.6.2 (after rc.13: the code review's fixes and refactors)

- **Fixes from the code review of rc.12** (`docs/code-review-2026-11-14.md`, HY-0012 to HY-0019):
  - H2 release (drains, dams, river pools) and H2's retries run only while the water rules are applied and their
    breaker has not tripped; when they go off, running drains and pools end once, logged. A drain whose tick throws
    releases all its guarded cells, the other drains still tick, and errors go to a breaker (HY-0012).
  - A pond reaching an unloaded chunk is not measured: a player's break keeps the water, and a drain saved with the
    world waits for its land to load (asked again every 5 s, 60 times at most) instead of measuring a lake as a pond. A
    drain's step waits while the cell its source will stand at is unloaded, instead of losing the source (HY-0013).
  - A player's break or placement forgets every remembered "held" search with a cell within one block of it, and the
    refusals there; the memo is keyed by liquid. A trench whose far end is broken open while it fills stops filling
    (HY-0014).
  - Level relax goes off with the side-feed rule; the registry test hashes the seven vanilla methods H5, H7 and H8
    patch or re-implement and names a changed body (HY-0015).
  - A column whose Earth query throws rests 5 s; 50 failures within a minute unbind the query (the rules run as without
    Earth); answers are kept per chunk (256 chunks, the least recently used evicted) (HY-0016).
  - Tidy and docs: shared scratch, dead code, class summaries, design.md, the decision index; H2Release and H2Filling
    split into partial files (H2Drains, H2Dams, H2Held) without code changes (HY-0018).
- **Second batch** (after the Fable reviewer's re-review of the first): Earth's river query unbound at run time also drops
  the world's remembered vessels answer and tells the clients (a world without `hydrologyFlooding` could keep vessels on
  without the river guards); remembered held searches that share a cell are forgotten together; a drain forgets by
  search beside each cell it changes instead of scanning the whole memo; a saved drain's restart runs in its own try and
  logs when it gives up; a game version new to the registry's method hashes fails the run once; a query failure is
  logged once. H4 merges the wakes of a basin's rim (HY-0017): filling a basin woke the same rim sources once per
  filled cell nearby; `/hydrology stats` counts the wakes. (This batch keyed the merge by the millisecond, which merged
  almost nothing; the third batch made it a 40 ms window.)
- **Third batch** (the Fable reviewer's second and third re-reviews):
  - H2's held search no longer recurses when it forgets linked searches (a long chain of them overflowed the stack);
    links are kept only between live searches of the same generation (HY-0014).
  - H4's wake merge is a 40 ms window of edge cells, at most a fixed number per window (HY-0017). Its first version
    started the window at `long.MinValue`, which overflowed, so the window never reset: the new offline test found it.
  - The production H4 and H7 rules are compiled into the offline suite and driven on small scenes: rapid edges and their
    revert, a 1-block step, unloaded windows, the wake window, sources over air, falling labels, the side-feed rule and
    level relax (HY-0019 c).
  - Earth's river query unbound at run time drops the cached vessels answer at once; a later bind clears the pending
    reset. Hooks assert they run on the main thread (Debug builds). H5's map of hot cells is capped.
- **Fourth batch** (the reviewer's fourth pass):
  - A river pool planned over cells a drain was emptying looped for ever on the main thread (critical). Pools and drains
    now use one test for a cell a pool may take; a draining cell opens the pool's layer; a planned layer that places
    nothing ends the pool; unloaded land makes a pool wait (2 minutes at most) instead of ending it; a dam's pool waits
    while a drain runs in its columns; a pool restarts after a world load only when the land around it is loaded.
  - Ending the rules keeps the saved drains and pools (they were dropped from the save when the rules went off).
  - A body found large (a sea) is remembered for 30 s, so blocks broken along its shore are not measured again each
    time (offline: 15 ms, then 0.06 ms); another liquid keeps the water before the body beyond it is measured.
  - Liquids' handled and fresh-water tests read a table by block id; the breaker logs each distinct error once (five at
    most); the dam mark cap is logged once.
  - Refactors (R1 to R5): one world reset (`RuleState`, which now also resets H4's wake window); the break, place and
    tick policy in `ReleaseDriver`, driven by the offline suite; one "can take water" test for drains and pools; the
    drains' save starts with a format version (a save without it reads as before, a newer one is refused and logged);
    every block fact the rules ask is a table lookup in `Liquids`; Held and Body read a cell's own solid block once.
    Ended pools keep their river marks: the marks also let H2's own fill stand above the river surface, which a pool's
    count of raised sources does not see.
- **Fifth batch** (the reviewer's re-reviews of the fourth): a dam whose river runs on into unloaded land starts its
  pool over the columns it could mark and, while the river beyond is not known, takes an unmarked river column for a
  wait, not an outlet, marking again before it plans (a pool marked over part of a long reach ended as soon as the rest
  loaded; waiting for all of it kept a dam below a step from ever starting); waiting dams are one entry per dam,
  asked again with the river's marks and the pool alone, logged once, capped and given up after 20 minutes; a pool keeps
  at most a second of its river's water while it places nothing; a block added to a filling pool is planned with; a pool
  that waited too long for land goes back to waiting; a block placed beside a remembered large body forgets it (a break
  keeps it: it only joins water); no
  flooding setting is written while a world's legacy answer needs Earth's query and it is not bound.
- **Decision 0011** (option 3, the release coordinator, no behaviour change): a world without `hydrologyFlooding` gets
  it written at RunGame with the legacy policy's answer, logged once; the README says how to have flooding in a new
  dedicated-server world. Offline `FloodingKeyCase`.
- **River pools** (decision 0010): flowing holes in a river's own water hold a layer up as the river does (in game the
  layer over a crest was taken for open above the Rhine falls); a pool that stops below its crest names its outlets in
  the log.

### 0.6.2-rc.13

- **A river fills the pool behind a player's dam from its own flow** (decision 0010, HY-0011): layer by layer to the
  crest, then one layer runs over it and falls past the dam as rapid water. Config `DamFill` (true), `DamFillPerCubic`
  (0.1 sources a second per m³/s of Earth's discharge), `DamFillMax` (64). Pools are kept with the world; at most 8 fill
  at once; `/hydrology stats` counts their layers and sources.

### 0.6.2-rc.12

- **Fixed: a dam's water over an Earth river never drained when levees enclosed bank strips** (decision 0009 point 5,
  found in game below the Rhine falls): the strip between a levee and the river filled to the upper pool's level beside
  it, and the whole raised body counted as a channel fed by the river. A body holding water raised over the river's own
  columns now always drains when the dam opens.

### 0.6.2-rc.11

- **Closed basins fill to their sill** (decision 0009, HY-0009). A pond opened over a basin that holds only part of its
  water lets out the whole layers the basin holds and keeps the rest (before, it kept everything).
- **Ponds run off down open valleys** (decision 0009). A pond's water that has fallen from the opening and reaches
  unloaded land or the end of the search runs off there, as into the sea (before, the pond kept it). Lakes of 4,096
  sources or more keep their water there, so a channel dug from them still carries it to the drop.
- **Dams on Earth's rivers** (decision 0009, EA-0066). A block placed beside a natural river marks the river's columns a
  dam there can back up; in them the pool may rise above the generated surface, only where the level is held and only as
  high as the water feeding it (the next step up). Breaking the dam's top course lets the raised water drain into the
  river. Earth's river query is unchanged (version 3). On a calm river with no step near the dam nothing rises yet.
- **Less garbage** (HY-0010): `H2Release` reuses its searches' sets and positions; a block placed on dry land
  allocates nothing, one beside a pond nothing (was 10.9 KB), a kept opening 2.4 KB (was 37 KB).

### 0.6.2-rc.10

- **Fixed: a dam built by hand raised nothing.** Placing a dam a block at a time, the first block woke the reach while
  the dam was still open; H2 refused and remembered that for 2 s, and the last block's wake got the same answer. A
  placed block now makes H2 forget what it remembered about the reach before it is asked again.
- **Fixed: water stayed standing behind a lowered dam.** When a dam's top course was taken away, vanilla made new
  sources in the gap from the water over the crest falling beside the full reach; they closed the reach again, and part
  of the raised layer (22 of 38 sources in the lab) stayed. While a pond drains, its emptied cells and openings take no
  new sources; the reach goes back to its natural level.

### 0.6.2-rc.9

- **Fixed: channels from lakes stopped after a few blocks, and nothing filled below a drop** (the author's rc.8 play
  test, HY-0008). A channel at a lake's level that ends at a drop was refused as open (decision 0005), so vanilla's
  flowing water died about five blocks out; the reach below a drop was measured up to the water above it (rc.7) and its
  supply counted only sources joined to it. Now the supply follows the flowing water upstream across falls and rapids;
  the reach below a drop is measured at its own level; and a channel fed by a lake of 4,096 sources or more or a river
  is held to where it falls, as long as it is closed sideways and leaves only over lips of ground (at most 8 edge cells).
  A trench with a free outfall carries one layer; a terrace all edge holds nothing; a pond's channel stays flowing water.
- **New: a pond drains over time** (HY-0009). One source a second per block of the breach's width and per block of
  water over the opening's floor (`PondDrainRate`); the stream runs out through the opening while the pond drops, and the drained
  cells stay empty. A body whose top layer would take longer than `PondUnlimitedSeconds` (2,880 s, a game day) keeps
  its water, as a lake; the fixed 4,096-source limit is gone, so a lake of 41 x 41 drains its top layer through a
  2-wide breach in about 14 minutes. Drains are saved with the world and resumed. Closing every opening ends a drain
  once the layer it is draining has gone. Water landing on a pool runs on over it to the pool's outfall.
- **New: dams** (HY-0009; the author: "work on dams also after or include logic in pipeline"). A block a player places
  beside water wakes the reach it may close: a reach fed through a fall rises to the dam's top, and when a lake or a
  river feeds it one more layer spills over the crest, falling past the dam. Taking the dam's top course away drains the
  raised water downstream; the course left holds one layer. Earth's natural river columns keep their generated levels.
  Needs Water flooding.

### 0.6.2-rc.8

- **New: an opened pond lets its water out.** The author: "if pond is drained then it should end up in lowest point by
  flow". When a player breaks a pond's rim or bank, the pond's sources above the opening go, and as many sources stand
  where that water comes to rest: it runs along the opening's level to an edge up to 8 blocks away, falls, and pools
  where it lands, spilling on over any edge its flowing water would reach; a hollow it fills to the walls it fills
  layer by layer. The sea, rivers (Earth's query) and lakes of 4,096 sources or more take it without rising, and are
  never drained themselves. With no lower land within 8 blocks of the opening, or a basin too small for the water, the
  pond keeps its water and lets out flowing water, as rc.7. Only a player's break starts it (the server's break event);
  config `PondRelease` (true). The move is immediate; volume is kept to the layer (a partly filled last layer may be
  completed by H2 under decision 0006). One log line per release says where the water went.
- **Docs:** the changelog's release-candidate entries are one 0.6.2 entry, and the README states current behaviour.

### 0.6.2-rc.7

- **Fixed: a small pond no longer floods the land below it.** A player broke the rim of a small pond on high ground,
  and the land below flooded several blocks deep. Hydrology filled any closed space under a source to the source's
  level, however little water stood behind it. A space now fills only from water that could fill it: the water bounding
  it, counted from the space's floor up, must be at least half the space, unless it is a natural river (Earth's river
  query). A broken pond lets out flowing water and keeps its own; a lake, the sea and rivers still fill what they
  bound; a canal dug from a lake or a pool fills as before. A bucket emptied over a closed sea or pond no longer raises
  it. New config `SupplyFactor` (2; 0 turns the check off).

### 0.6.2-rc.5

- **Earth 2.0.0-rc.3's "not ready yet":** where Earth cannot answer for a spot yet (a height tile still to come; rare),
  the water there stays as it is and waits for Earth, at most a minute, instead of losing its river handling for that
  moment (a calm river cell could briefly turn still). Nothing is logged as a failure.

### 0.6.2-rc.4

- **Fixed (rc.3's check):** a long canal dug out whole and then opened (3 x 3 x 500) filled only up to a layer below
  its surface; a canal joining a river to the salt sea did not fill; filling a big basin searched it again for every
  cell it filled. Now each of these fills, once.
- **Fixed:** a source on a bridge deck just above a calm river took the river's flow label (and could turn rapid); a
  water wheel high over a big river, or in a cave stream under it, took the river's power. Both now need the river's own
  water. A failed or odd answer from Earth's span is no longer kept.
- **Robustness:** rare errors no longer add up over days to switch the water rules off (counted per minute); a world
  started after another in the same game applies its patches afresh; a renamed game class costs only its own feature.
- **Safety:** an Earth world whose river steps need the river query keeps the vessels case off while the query is not
  bound, so a broken binding cannot flood its steps.

### 0.6.2-rc.3

- **Fixed: the sea no longer climbs.** A player reported on 0.6.1 that water spilled into the sea made the sea
  rise to it and flood most of the map, with heavy lag. A bucket emptied onto the sea or a lake raised all of it a block;
  a pool two wide spilling over a cliff made the sea below climb to the pool; two sources placed on open flat land
  flooded all of it. Water now stands at a level only where walls and water of that level hold it in: trenches, canals,
  ports and walled basins fill as before; the sea, lakes with an outflow or open shores and open land do not rise. A
  pond in a hollow closed above its surface still fills to the level of a source over it, as vanilla's endless sources
  would.

### 0.6.2-rc.2

- **Fixed:** water behind a liquid-tight face (a waterlogged stair's back, a chiseled block's solid side) no longer feeds
  a fill; a pocket is not enclosed where a block lets water through (a fence, a slab, a waterlogged beam).
- **With Earth 2.0.0-rc.2** (branch `claude/rc-2.0-hydrology-leftovers`, merged at `d50f2ae`): Hydrology's river rules apply only in the river's own water, so a channel on a bridge over a
  calm river, or a cave pool under its bed, is ordinary water (Earth's new `RiverSpanAt`); and Earth's query no longer
  marks rapids past the drawn ramps at 1:10, so calm pool sources below a steep river's steps stay calm.
- **Runtime breaker:** a hook that throws no longer disrupts the server's tick: the error is logged, vanilla's code runs
  for that call, and after 50 errors only that feature switches off.

## Scope and compatibility

Requires Vintage Story 1.22 and the built-in Survival module, on the server and on clients. Earth 2.0.0-rc.1 requires
Hydrology 0.6.1 or newer; Earth 2.0.0-rc.2 requires 0.6.2-rc.1 or newer with
query version 3; Earth 2.0.0-rc.3 (`claude/rc-2.0` `a52373b`) requires 0.6.2-rc.4, since rc.1 to rc.3 could let the sea
flood the map. Earth reads Hydrology's `EarthRiverQueryVersion` (now 3; the contract is unchanged, and `RiverSpanAt` is
an optional addition Hydrology finds by its signature). Do not combine with
Realistic Water or Realistic Rapids. No world format, block or save field changes. `hydrology.json` gains `FillPockets`
and `CalmSourceSpecks` (from the calm-rivers work); a playtest build's `CalmSourceRipples` is dropped when the file is
written back.

## Review

Five read-only audits by area (water filling and flattening; rapids and flow; the Earth river query contract; config,
world options and start-up; client effects and game-update fragility), Codex's correctness review and adversarial
design review, and an adversarial pass over their findings. Fixed: the items above, the patch registry, start-up and
shutdown order, a hash for the river cache, fewer allocations in H4's edge search and H2's pocket search, dead code
(`RapidsFlow`, a `DropAt` overload, `EngineChunkPackFix.Applied`), comments that no longer matched the code, and the
release zip's file list. What rc.1 left (barrier semantics, permeable pocket walls, a runtime breaker, the query at any height, rapids past the ramps at 1:10, Earth's Hydrology minimum) was fixed in rc.2; the old
saves' Water flooding key stays as it is, since old saves do not matter for this release (the author), and the
pocket wake at waterfalls stays as it was: narrowing it saved no measurable time and may have changed rapid tongues
on deep canal floors (see 0004).
[Decision 0004](decisions/0004-release-0.6.2.md) has the details.

A second round for rc.4 (9 November, the author's request through the Earth RC session): five read-only audits again
(H2 and the held check; the hooks, rapids and settling; the Earth query contract; config, start-up and game updates;
client, docs and tests), Codex's correctness review (one finding: the offline suite's baseline mode no longer built)
and adversarial review (the held check's cost and retries, a failed span kept), and the water playtest below. Two loops
an audit suspected (a hole under deep water; a pool floored with slabs over air) did not reproduce, in the game or in
headless labs. Earth's side (a stand-in rapids answer Hydrology keeps, its cost per chunk, a reversed span on some
chute columns, Earth's Hydrology minimum) went to the Earth RC session.

## Verification

### 0.6.2, the release

Vintage Story 1.22.7 (Windows), the commit of the version bump on `main` (see the card for the hash), paired with the
rc.13 zip as the baseline.

- **Builds:** Debug and Release, 0 warnings, 0 errors.
- **Offline suite:** 14,522/14,522 (the review's cases: a pool over a running drain on a thread with a time limit, a
  long pool and three stepped pools on partly loaded land, a sea's shore through the release driver, the versioned
  save, the flooding attribute, production H4 and H7).
- **Registry test:** 89/89 (IL hashes of the vanilla bodies Hydrology rewrites, breakers, the query's failures).
- **Headless labs**, the release zip's DLL against rc.13's: regular 239 of 243 end states the same (the differences in
  `lab_drop2_wheel`'s ramp and `lab_fall_stones`' column labels, which vary run to run); wheels 14.92, 0 and 7.24 as
  before; query 54 of 55 (one edge source of the river chute ordinary instead of rapid), the river wheel 21.73 as
  before; reach 70 of 70; pond 47 of 49 (one lake one source higher, a few flowing cells); held: trenches opened while
  filling stop (rc.13 filled them on, HY-0014). Liquid updates within 3 % of rc.13's. No errors in any log.
- **Server smoke:** the release zip on a new Earth 1:10 world (Earth `main` `cac542d` as a test zip, Cylinder World
  1.1.0-rc.4): Earth's query version 3 bound with its span, Water flooding read from the world, the calm-river sites
  with rc.13's counts exactly (22 updates, 2 pockets filled, 92 flow labels kept), no errors.
- **In game** (own AgentBridge bench, the release zip, the same Earth): a dam on the Rhine below its falls fills layer
  148 (214 cells) and stops, naming its outlets; a dam across the Inn at Innsbruck (5,467 river columns) waits quietly,
  its next layer reaching land beyond the loaded area (rc.13 ended there, raising nothing too); saved and restarted,
  the pool is back among the filling ones without a new "backs up" line. No errors.

### The candidates

All on Vintage Story 1.22.7 (Windows), against the merge `1d91e7e` (0.6.2-rivers.7) as the behaviour benchmark.

- **Builds:** Debug and Release, 0 warnings, 0 errors.
- **Offline suite** (`tests/offline/run.ps1`, production H2 on watersim's engine): 14,345/14,345 checks in rc.4
  (rc.1: 14,322 with 7 new pocket cases; rc.2: 14,328 with barrier cases (whole blocks only: the adapter has no partial
  barriers); rc.3: 14,340 with the held cases; rc.4 adds a trench beside open land and a canal 3 x 3 x 501 dug out
  whole, which rc.3's H2 leaves at 3,006 of 4,509 sources). Its 44 canal and river lines, update counts included, are
  identical to `1d91e7e`'s after every refactor and in rc.4. `run.ps1 -Baseline` builds again and fails as expected. The held cases (a salt sea 3 deep with land around it, played with production H2: a bucket emptied onto it, a
  pool two wide at the top of a cliff over it, two sources on the land; each with land draining to a lowland and walled
  all round): rc.2's H2 raised 4,095, 15,662 and 4,094 sources where the land drains, in 66,000 to 116,000 updates;
  rc.3's raises none, in 20 to 148 updates; walled all round, both fill the basin to the spring's level.
- **Registry test** (`tests/registry`, the installed game's DLLs): 65/65 in rc.5 (rc.1: 40/40; rc.2 and rc.3: 53/53
  with the runtime breaker and the river span; rc.4: 58/58 with sources and wheels within the span, a flaky span, a
  reversed span and errors spread over two minutes; rc.5 adds Earth's "not ready yet": no answer kept, no failure
  logged, the water waits, the next answer used and kept, the one-minute cap). Every target of H5, H8 and C1 resolves by
  exact signature and is patched with no warning; the chunk-pack fix's targets resolve; missing, changed or refused
  targets switch off only their feature, logged once, rolled back.
- **Headless servers** (`tests/server` harness; runner and comparison in this session's scratchpad). The server's
  water is not deterministic run to run: two runs of `1d91e7e` differ by 5 to 10 water cells (the rapid ramp tips of
  the drop labs) and 20 to 30 labels (falling or still) in 38,750 columns. Against that noise:
  - 61 vanilla-world labs (trenches, canals, salt, steps, drops, chutes, falls, wheels): RC and refactor-only builds
    stay within it; wheels read the same flowRate (14.92, 14.92, 7.24); the one lab outside the drop labs that differed
    (a canal, 7 cells) was the baseline run's own outlier, matched by three other baseline runs and the RC.
  - 10 fake-query (version 3) labs: within noise; the wheel on a river reads 21.73 in every run.
  - **Earth 2.0.0-rc.1, new 1:10 world** (the author's installed zip, Cylinder World 1.0.4, seed 386217), 12 sites
    found: **calm rivers hold**: the Olt at Racovița, the Aare at Bern, the Tanaro, the Danube at the Iron Gates and the
    Daugava at Pļaviņas show flow-labelled ordinary sources (`water-e-7`, `water-se-7`, ...), 0 water and 0 label changes
    on the first wake of the generated water and after a poke; blocks left in the Olt, the Tanaro and the Congo replace
    their water cells and change no labels (no still halos). The RC matches the baseline's counts at every site; the
    maps differ only by world-generation decorations (water plants), the same between two baseline runs.
  - **Lake ice lab** (Earth 1:10, lab on the Greenland ice sheet, rim walled): baseline drains a bucket's source on the
    ice and turns the source beside the ice rapid; the RC keeps both and spreads flowing water over the ice as on the
    ground, settled within 10 s.
  - **The release zip itself** (extracted, Release build) on the same Earth 1:10 bench: the baseline's counts at all 12
    sites, 0 water and 0 label cells different in 7,500 columns at the end, Earth's query version 3 bound, the chunk-pack
    fix applied, no feature switched off.
  - Errors: none in any run's server log. Hydrology's time per liquid update in paired runs (both builds at once, Debug):
    labs 19.1 µs (baseline) and 19.0 µs (RC); query labs 20.6 and 24.4 µs, then 19.1 and 18.6 µs in a second pair, so
    no measurable change.
  - **rc.2** (Debug builds; the final one keeps rc.1's pocket wake): three lab runs, paired with baseline runs. The two
    deep canal labs end as every baseline run in two of them; in the third a rapid tongue ends one cell short, as the
    baseline's own runs vary. Every other lab is within the baseline's spread. Wheels read 14.92, 14.92 and 7.24.
    Time per liquid update in a pair with nothing else running beside it: 13.4 µs (baseline), 13.6 µs (rc.2). The
    fake-query labs and the lake ice lab, repeated with the narrowed-wake build, behave as in rc.1.
  - **rc.2 with Earth's leftovers branch** (1:10, seed 386217): the calm rivers hold as above (0 changes), and the
    Congo's first wake now changes 0 water cells (6 with Earth 2.0.0-rc.1); `RapidSourcesByQuery` 139 → 0. Against
    rc.1 with Earth 2.0.0-rc.1, every differing cell is Congo rapid water that is now calm (105 cells; 0 labels).
  - **The rc.2 zip itself** (extracted Release build) with that Earth: all 12 sites, rc.1's counts at every site but
    the Congo's first wake (6 → 0), Earth's query version 3 bound with its span, the chunk-pack fix applied, no feature
    switched off, no errors.
  - **rc.3, the spill labs** (`lab_spill`, a sea 3 deep in a rim wall with a shore; Water flooding on; a fake river
    query of version 2, as on Earth, so fresh water seeds too). 0.6.1 and rc.2, but not vanilla: a source emptied onto
    the sea or a lake raises its whole surface a block (255 sources); a pool two wide at the top of a 4-block cliff
    makes the sea climb to it and floods the plateau (salt water; fresh water with the query); two sources on a flat
    field behind a dyke flood all of it (180). A single source running down a shore or over a cliff, and fresh water
    into salt, raise nothing in any build. rc.3, with a gap through the rim so water above the sea's level runs off:
    the bucket leaves one source and 36 flowing cells, the cliff's sea does not rise, the field keeps 2 sources and 94
    flowing cells; without the gap the lab is a closed basin and fills to the spring's level, as designed.
  - **rc.3, the other labs** (Debug; the 61 labs paired with a baseline run, the 10 fake-query labs paired too): the
    new check refused no fill in either set (`Unheld` 0). Every fake-query lab matches a baseline run; five labs differ
    by 1 or 2 columns as baseline runs do among themselves, a drop lab by 5 at its ramp tip (as two baseline runs do),
    and one deep canal's rapid tongue ends a level short, as in one rc.2 run (it shows in 2 of 6 RC runs of that lab, in
    none of 9 baseline runs, and in one baseline run of its sibling canal). Wheels read 14.92, 14.92 and 7.24, and 21.73
    on a river. A first version of the check counted a water wheel's cells as an outlet and left a closed chute's top
    flowing (fixed in `860cb7b`).
  - **The rc.3 zip itself** (extracted Release build) with Earth 2.0.0-rc.2's branch at 1:10: the same as the rc.2
    zip, 0 water and 0 label cells different in 7,500 columns, every site's counts identical; Earth's query version 3
    bound with its span, the chunk-pack fix applied, no feature switched off, no errors. Hydrology's own time in the
    whole run: 171 ms (rc.2's zip: 145 ms). The same zip on the spill labs: as rc.3 above.
  - **rc.4** (Debug; the 61 labs and the 10 fake-query labs, each paired with a baseline run): the held check refused
    no fill; two labs of each set differ by one column, as baseline runs do among themselves; wheels read 14.92, 14.92,
    7.24 and 21.73. Time per liquid update: 14.8 µs (rc.4) and 14.1 µs (baseline) in the labs, 15.5 and 16.1 µs in the
    query labs. The rapids audit's two loop candidates (`lab_spill` "slabs": a pool floored with slabs over air; "hole":
    the block under a 3-deep sea's bottom broken) settle within 10 s on rc.3 and rc.4 alike.
  - **The rc.4 zip itself** (extracted Release build) with Earth 2.0.0-rc.2 as installed (`ce3c0084`) at 1:10: the same
    as the rc.3 zip with Earth's leftovers branch, 0 water and 0 label cells different in 7,500 columns, every site's
    counts identical; query version 3 bound with its span, the chunk-pack fix applied, no feature off, no errors.
    Hydrology's own time: 234 ms in the run (the rc.3 zip's run, with the leftovers Earth build: 171 ms; Earth's query
    cost is counted in it, so the two Earth builds are not a pair). The same zip on the spill-outlet labs: as rc.3.
  - **rc.5** (Debug; the fake river query taught Earth 2.0.0-rc.3's "not ready yet"): with the 10 fake-query labs
    "not ready" for their first 40 s, 6,666 water updates waited and were tried again; the log has one note and no
    failure; every lab settled, with the same number of fills (949 against 947 without the wait). Nine labs ended as
    without the wait, within run-to-run noise; the river chute with a wheel was poked entirely inside the wait, so its
    water never saw the poke, only its end, and two of its lower pool's sources stayed ordinary (the wheel reads 15.36
    against 21.73), as designed: the water waits for Earth. Without the wait rc.5 matches the baseline runs, and on the 61
    regular labs (no query, so nothing waits) it stays within the baseline's spread.
  - **The rc.5 zip itself** with Earth 2.0.0-rc.3 (`f6778b0e`) at 1:10: the same water as the rc.4 zip with Earth
    2.0.0-rc.2, 0 water and 0 label cells different in 7,500 columns, every site's counts identical; nothing waited
    (the bench's Earth data is complete); no errors; Hydrology's own time 195 ms.
  - **rc.7** (Debug, 13 November; Water flooding on in every run; vanilla worlds, seed 386217): the new pond labs
    (`lab_pond`, `scenarios-pond.json`, with a fake river query of version 3 so fresh water seeds an uneven basin as on
    Earth). On rc.6, a pond 7 x 7 x 2 (98 sources) in a mound 4 above a walled lowland, its rim broken 2 wide, filled the
    lowland with 3,818 sources to the pond's level, and the same pond broken onto closed flat land at its level flooded
    it with 956; salt water the same; a rim broken 1 wide, or a lowland with an outlet, kept flowing water only, as
    vanilla does in every case. On rc.7 no pond case leaves a source outside the pond, and a lake 41 x 41 x 3 (5,043
    sources) breaking into its hollow still fills it (6,514 sources), as on rc.6. Paired with rc.6 runs: of the 61
    regular labs 56 end identical and the wheels read 14.92, 14.92 and 7.24 in both; the four chute labs keep their
    last step, which rc.6 drowned with Water flooding on and no river query (the lowest pool rose to the step above
    it); `lab_drop5`'s pool (72 sources) no longer fills the edge of its 5-deep pit (168 cells), so the edge stays
    flowing water instead of two rapid sources. The 10 fake-query labs: 9 identical, the river chute one cell
    different (as runs differ among themselves), its wheel 21.73 in both. The 32 spill labs: those with an outlet as
    on rc.6; the 10 closed ones, which rc.3 to rc.6 raised to the spring's level (255 sources for a bucket on the sea,
    up to 1,035 for a pool falling into it, 180 for two springs on a field), raise nothing. Time per liquid update in
    the labs: 18.7 µs (rc.7), 19.4 µs (rc.6). Caves under rivers (HY-0004's check) on a new 1:10 Earth world (Earth
    2.0.0-rc.12, Cylinder World 1.1.0-rc.4): 44 openings carved under six rivers all filled with source water within
    60 s of the wake, every river column stayed, and the new check refused nothing. Offline: 14,360 checks, with new
    pond cases (the rc.6 rule floods a hollow with 5,054 sources from a pond of 98; rc.7 none) and the walled-sea,
    long-canal and sheet cases changed as decision 0006 records. Registry 65/65. No server errors in any run.
  - **Second batch and HY-0017** (14 November). Offline 14,473 (`LinkedSearchCases`), registry 87 (the vessels answer
    dropped with the query). HY-0017 paired with main (`a072356`): the 61 regular labs 59 identical (`lab_drop2_wheel`'s
    ramp and `lab_fall_stones`' plunge column, as before), every chute and wheel the same (14.92, 0, 7.24), the 10 query
    labs identical; liquid updates 47,474 -> 45,880 in the regular labs (-3.4 %), 13,219 -> 13,339 in the query labs,
    tick mean the same. The whole batch paired with main: reach 11/11 and query 10/10 identical, pond 6/7 (two flowing
    cells), the held trenches stop as on main (a cell or two apart). In game (own bench, Earth main `c37f45e`): the step
    below the Rhine falls, its edge block broken and put back, kept its white ramp and settled back exactly.
  - **After rc.13: code review fixes HY-0012 to HY-0016, HY-0018, HY-0019** (14 November). Offline 14,470 (new
    `LoadedLandCases`, `SaveLoadCases`); registry 85 (water rules on/off, breaker windows, the vanilla method bodies'
    hashes for 1.22.7, level relax following the side-feed rule, query failures resting and unbinding, the per-chunk
    cache). Headless, paired with rc.13 (`87a0303`, every lab break a player's except the held labs): reach 11/11, query
    10/10, spill-hole and spill-outlet 11/11 identical; pond 5/7 (a few flowing cells and one source, run noise); the 61
    regular labs 60 (`lab_drop2_wheel`'s ramp), wheels 14.92, 0 and 7.24 and tick mean 47.2 ms on both. The new held
    lab (`scenarios-held.json`): a trench whose far end is broken open 3 s or 8 s after the dig kept filling on rc.13
    (32 cells of 40 standing at its top) and stops on main (3 and 8). In game (own bench, Earth main `27c460f`): a
    trench dug from the Rhine and opened at its far end fills to its outfall (a fed channel, decision 0008); a dam
    below the falls fills its reach and spills over its crest in one go (a fix found there: holes in the river's own
    water held the layer over the crest for open). No Hydrology errors.
  - **rc.13** (14 November). Offline 14,455 (`RiverFlowDamCases`: a calm river with no step, dams 1 and 2 high and one
    standing already: the pool fills to the crest and 390 over it, nothing below, 0.3 to 7 ms for a whole pool; breaking
    the top course lets 774 out). Headless, paired with rc.12 (`9d06264`): the 11 reach labs end identical; the 10 query labs 9, the river chute two
    sources different; the 61 regular labs 58, `lab_drop2_wheel`'s ramp, `lab_fall_stones`' plunge column and one canal's
    flowing tip different (no dammed pool started in any lab: run noise, as these labs varied before); wheels 14.92, 0
    and 7.24 on both; tick mean 46.55 ms on both. In game (own bench, Earth main `6371e32` as
    2.0.0-rc.13-hydam.1, 1:10; decision 0010): below the Rhine falls the river filled the reach and 266 cells over the
    crest in about 4 s, falling past the dam as rapid water, and drained back (508 sources in 21 s) when two blocks of
    the top course were broken; on the Inn a dam with low banks raised nothing, and with walls round 28 blocks of the
    reach the river filled 186 cells and 324 over the crest. No Hydrology errors. Package
    `release/hydrology-0.6.2-rc.13.zip` (84,065 bytes, SHA-256 `db22cab4fbe871334993a6c70b8801614ca3e4aad85a76a4e996fb81ddeb836c`), not installed.
  - **rc.12** (14 November). Offline 14,434 (`RiverDamCases` with bank strips: rc.11 0 let out and 192 stay raised,
    rc.12 214 let out and none stay). Headless, paired with rc.11 (`4840f88`): the 10 query labs and the 11 reach labs
    end identical. In game (own bench, Earth main `ebceed0` as 2.0.0-rc.12-hylook.1, 1:10; decision 0009 "In game"): a
    dam below the Rhine falls raised its reach to the dam's top and drained it back, 254 sources in 127 s through 2
    cells and 254 s through 1; a pond over a closed pit let out its top layer and kept the rest; a pond on the slope
    above the river drained all 98 into it. No Hydrology errors. Package `release/hydrology-0.6.2-rc.12.zip` (79,590 bytes, SHA-256
    `bbba72a04029ceb4beb617fa6e1864b61cb0949bf4b3060103361ef341434a42`), not
    installed.
  - **rc.11** (Debug, 14 November; vanilla worlds, seed 386217, every lab break a player's, fake river query 3 for the
    reach, pond and query labs), paired with rc.10 (`234bf6c`): the 11 reach labs, the 7 pond labs and the 10 query labs
    end identical (differences only at 10-30 s, run timing); the 61 regular labs: 60 identical, `lab_drop2_wheel`'s ramp
    a few flowing cells shorter (the lab varies between runs), wheels 14.92, 0 and 7.24 on both; tick mean 47.1 ms
    (rc.10 47.3). A first rc.11 build let the reach labs' 41 x 41 and 72 x 72 lakes run off through their channels
    into the lab's unloaded edge (channel dry 5 blocks from the lake, the reach below empty); the final build keeps
    lakes of 4,096 sources or more there (decision 0009). Offline 14,424 checks: `BasinCases` (pit, pit-small,
    pit-deep, valley, loaded valley), `RiverDamCases` (1 and 2 high), `RiverRegressions` with Earth's span,
    `AllocCases` (HY-0010: placed on dry land 136 -> 0 B, beside a pond 10,896 -> 0 B, a kept opening 37,128 -> 2,408
    B). In game (own AgentBridge bench, Earth 2.0.0-rc.12-karstflint.1, 1:10, a pre-fix rc.11 build with the same river
    rules): a dam 1 over the surface across the Inn at Innsbruck (47.2687 N 11.3935 E, a calm pool with no step within
    reach) raised nothing, as decision 0009 says; no Hydrology errors. Spill labs not run: they make no player breaks or
    places, and H2 outside river columns did not change. Package `release/hydrology-0.6.2-rc.11.zip` (79,480 bytes,
    SHA-256 `d6c257c065f2a4dca8be7d63dbcae36f79ba156615483aa4d8e7734ba8b9351f`), not installed.
  - **rc.10** (14 November). Headless (as rc.9): the dam lab, built at once and a block a second, raises 38 sources
    behind the dam and 38 over the crest, and after the top course is broken the reach is as before the dam (0 raised;
    rc.9: 22 stayed, and the hand-built dam raised only 12 over the crest); vanilla's source in the gap is refused once.
    Paired with rc.9 (its zip): 60 of 61 regular labs identical (one flowing level in `lab_drop2_wheel`'s ramp, which
    varies between runs), the wheels 14.92, 0 and 7.24 on both, the 10 query labs and 32 spill labs identical; reach and
    pond labs as rc.9 (the 41 x 41 lake kept its water in this run: its drain's plan met chunks not loaded yet). Offline
    14,391. No server errors. **In game** (the AgentBridge bench, its own data folder; Earth 2.0.0-rc.12-karstflint.1,
    Cylinder World 1.1.0-rc.4, a new 1:10 world, Water flooding on), at the author's lake at 61.8155 N, 8.9425 E
    (surface y 196): a channel 2 wide dug from the shore, its last two cells broken as a player, runs 6 blocks at the
    lake's level to a 3-block drop and 58 blocks below it to an outfall; after a minute every cell of the channel is
    source water to the drop and the whole lower reach is source water to the outfall, where it falls off. A dam of 2
    courses placed by hand a block at a time across the lower reach: with the first rc.10 build (without the second
    fix) nothing rose until the reach was woken again; with the final build the reach behind a new dam rose to the
    dam's top with a layer over the crest falling past it, and the channel's fall onto the raised water became a ramp.
    Breaking the top course let out 38 sources and left the reach exactly as before the dam; breaking the old dam's
    bottom course closed the gap with source water. No Hydrology errors in the logs.
  - **rc.9** (Debug, 13 November; Water flooding on, vanilla worlds, seed 386217, every lab break a player's). The new
    `lab_reach` (`scenarios-reach.json`, fake river query 3): a channel 2 wide from a lake of 81 x 81 x 3 (holds: its
    top layer would take 55 minutes through 2 cells) runs 6 blocks to a 3-block drop and the reach below fills to its
    banks (upper 12/12, lower 60/60 and the layer over it 60); with a 1-block drop, at the lake's level and with a
    3-block upper reach the same; with an outlet at the lower reach's end the lower reach carries one layer (the layer
    over it stays flowing water); from a 7 x 7 pond the channel stays flowing and the pond lets its 49-source top layer
    out over time. A 41 x 41 lake is finite: its top layer (1,681) drains through the 2-wide channel at 2 sources a
    second and stands in the reach below. The dam (`reach_lake_dam`, 81 x 81 lake, 2 courses placed by a player at
    60 s, the top course broken at 100 s): 0 raised before, 38 sources raised behind the dam and 38 over the crest with
    it; after the top course goes, 78 sources drain in 14 s (19 land in cells the stream had already filled) and 22
    of the 38 cells over the remaining course stay as sources, steady to 280 s (the offline dam case leaves none; open
    point in HY-0009). Save and resume: the lake lab saved 60 s into a 1,681-source drain (120 out) is planned again
    on load with the 1,561 left, through the same 2 cells, at the same rate. A save from an earlier rc.9 build, whose
    openings had refilled, now counts its saved openings as the breach. The 61 regular labs paired with rc.8: 55
    identical; the four chute labs fill their closed bottom trough, fed through the falls from the head pool, up to
    the last step's lip (that step's edge reads SS instead of SR); `lab_drop2_wheel`'s ramp below and `lab_fall_stones`'
    plunge column (falling water instead of sources while the pool drains) differ; the wheels read 14.92, 0 and 7.24 on
    both. The 10 fake-query labs: 9 identical, the river chute one source different (run noise; rivers keep their
    water). The 32 spill labs identical to rc.8. Pond labs: each 7 x 7 pond lets out its 49-source top layer in about
    49 s and it collects below; flat land keeps 98. Offline 14,391 checks (`DamCases`, the `hollow-rate` and
    `hollow-closed` release shapes, the sheet case now filling). No server errors in any run.
  - **rc.8** (Debug, 13 November; Water flooding on, vanilla worlds, seed 386217). Lab breaks do not raise the server's
    player break event, so `lab_pond`'s breaks run the release as the event does (WaterAudit's `PlayerBreak`), and
    `WA_PLAYER_BREAKS=1` makes every lab break do so. The pond labs (fake river query 3): the ponds 7 x 7 x 2 broken 1
    or 2 wide over the walled hollow, fresh or salt, and the one over a lowland with an outlet, each keep 49 sources and
    49 stand on the lowland's floor (with 167 flowing cells round them); onto flat land at the pond's level, with or
    without an outlet, nothing moves (98 stay); the lake of 5,043 sources is unlimited and fills its hollow as on rc.7
    (6,514). The 61 regular labs, two plain runs (identical to each other in all 61) against a run with player breaks:
    56 identical; the five that differ are the labs whose pool a break opens onto lower ground, as the decision intends:
    `lab_drop3_wheel` and `lab_drop5` (the 72-source pool and its channel, 84 sources, lie in the race below and the
    race runs dry), `lab_fall_stones` (each stone pulled from the pool's edge lets one 72-source layer fall into the
    plunge basin, where H2 completes each partly filled layer: 224 sources stand for 144), and the two canals dug
    inward from a 99-source pool (the pool drops into the 3-deep canal: 120 stand, H2 completing the last layer). Of
    1,044 player breaks beside water, 6 moved water and 1,038 left it (a trench opening onto land at the pool's level).
    A first build forgot H2's remembered held answers everywhere on a release, which changed two other labs
    (`lab_drop2_wheel`, `lab_salt_w1_d2_p3_out`); forgetting them only around the moved water leaves those identical.
    The 10 fake-query labs with player breaks: 8 identical, the control canal fed from a 90-source pool drains into it,
    and the river chute differs by one rapid source (as runs differ among themselves; its wheel 21.73 in both); no river
    lost water. The spill hole lab (a break under the sea's floor) identical with player breaks. Time per liquid update
    in the labs: 14.0 µs with player breaks, 15.3 µs without. Offline: 14,377 checks, with `ReleaseCases` (hollow,
    flat, lake of 1,628 sources: 814 moved and 816 stand, river, a ledge whose edge is 5 blocks off: the water falls and
    collects at its foot, a plateau whose edge is 20 blocks off: nothing moves); `run.ps1 -Baseline` builds and fails
    as expected. No server errors in any run. Earth worlds were not run: rivers and lakes over 4,096 sources are never
    drained, and nothing else in H2 changed.
- **Water playtest in the game** (AgentBridge bench, Earth 2.0.0-rc.2 as installed, Cylinder World 1.0.4, a new 1:10
  world; rc.3, then a recheck with rc.4):
  - the Olt at Racovița, a calm river: a bucket taken from it heals at once; poured onto its bank, a source with a little
    spill, the river unchanged; a channel 2 wide and 2 deep and one 1 wide dug outward from it fill with source water at
    both levels; a dam across it holds both sides at their level, and a breach fills at once;
  - the Congo's step: a bucket out of the step's edge source and back restores the step exactly (rapid edge, ramp
    levels 6 to 1); one covered source under the edge kept vanilla's label;
  - the Black Sea coast (the player's report): salt sources poured on the coastal plain and at its edge spill into the
    sea and stay as vanilla leaves them, on rc.3 and on rc.4; nothing rises;
  - Lake Taymyr in January: water poured on the lake ice spreads over it as on the ground; a hole cut in the ice
    refreezes;
  - the Zambezi above Victoria Falls: a waterlogged fence, slab and stairs hold source water and stay quiet, and the
    river heals when they are taken out; a hole under the bed fills with a source, no loop;
  - not done in the game: water poured at a cliff's face over the sea (the bench cannot aim at a side face) and a
    natural waterfall (none drawn at the surface near Victoria at 1:10); the rc.4 recheck of a canal dug out whole was cut
    short by the bench (below), so that case rests on the offline suite.
  - The bench itself: three client crashes in AgentBridge's aiming ("Given pos contained NaN" for the player), while
    blocks were broken through its hover command; the world's unsaved changes were lost with each. Not Hydrology's or
    the game's; reported to the sessions using the bench.
- **Package (rc.10):** `release/hydrology-0.6.2-rc.10.zip`, built from `a494fba` (14 November), 77,586 bytes,
  SHA-256 `355ebf20f5ee969f62e025a69f09de31b0d32930dd421302f0a6222f7f3d388d`: the same six files (modinfo 0.6.2-rc.10).
  This zip ran the in-game check above. Installed in the author's game on the author's word ("install"), replacing
  rc.8, which is backed up in `VintagestoryData\Backups\hydrology-before-0.6.2-rc.10-20261114-0040`; `main` is not pushed.
- **Package (rc.9):** `release/hydrology-0.6.2-rc.9.zip`, built from `bff10c7` (13 November), 77,109 bytes,
  SHA-256 `4dd99e51e162a7d7369c9c2305f5e66b4f78667159c3f292badac0fda1ad46a9`: the same six files (modinfo 0.6.2-rc.9).
  The extracted zip on the pond labs: as the Debug build (each 7 x 7 pond lets out 49 and they collect below, the lake
  drains through its breach), no errors. Not installed in the author's game; `main` is not pushed.
- **Package (rc.8):** `release/hydrology-0.6.2-rc.8.zip`, built from `4f91ac4` on `main` (13 November), 67,439 bytes,
  SHA-256 `33fd8e09213eacd3f15837ebcb90b109bb139e690fa984c670fdbb34e1662a29`: the same six files (modinfo 0.6.2-rc.8).
  The extracted zip on the pond labs: as the Debug build (each pond 49 kept and 49 below, flat land 98 kept, the lake
  6,514), "pond release=True" in the start log, no errors. Not installed in the author's game; `main` is not pushed.
- **Package (rc.7):** `release/hydrology-0.6.2-rc.7.zip`, built from `59bc8b6` on `main` (13 November), 62,259 bytes,
  SHA-256 `a8ec205f0b8d55e63e6919c5612bc5859a571d380fd884dffcb33938a8346673`: the same six files as rc.5 (modinfo
  0.6.2-rc.7). The extracted zip on the pond labs: as the Debug build (no pond case leaves a source outside the pond;
  the lake fills its hollow with 6,514), "supply factor 2" in the start log, no errors. Not installed in the author's
  game; `main` is not pushed. `release/hydrology-0.6.2-rc.6.zip` was built from `827e8ac`.
- **Package (rc.5):** `release/hydrology-0.6.2-rc.5.zip` (built from `dc73ab7` in the `rc-0.6.2` worktree, which was removed in
  the 12 November tidy-up; the zip survives as installed in the author's `VintagestoryData\Mods`), 61,226 bytes,
  SHA-256 `98673f8d36c468f0a705d4b6007720d9e6ec18b1a241afd9a209b27275f52a96`: Hydrology.dll and .pdb, modinfo.json
  (0.6.2-rc.5, required on client and server, game 1.22.0+), worldconfig.json, LICENSE,
  `assets/game/lang/worldconfig-en.json`. Installed in the author's game on 9 November evening by the Earth RC session,
  on the author's "Yes, install rc.5", with Earth 2.0.0-rc.3 and Cylinder World 1.0.4 (the installed zip's SHA-256
  matches). Earlier packages (only rc.4 and rc.2 survive, in `VintagestoryData\Backups`): `release/hydrology-0.6.2-rc.4.zip`, 59,730
  bytes, SHA-256 `8d5e8045df8345560be78ca954741b84bcb3bfee0ecf9152e93b29c66dfe7a00` (installed with Earth 2.0.0-rc.3
  before rc.5; kept in `VintagestoryData\Backups\hydrology-before-0.6.2-rc.5-20261109-202137`); `release/hydrology-0.6.2-rc.3.zip`, 58,357
  bytes, SHA-256 `a3141825414c55374a93885570201b6ba757e3217431bbf0d72c1054fce0acd1`; `release/hydrology-0.6.2-rc.2.zip`,
  57,718 bytes, SHA-256 `a81c49b2e1ee37d127324888e85a47b4f8addfb49744ed84607cf0cbe9634a7d` (installed with Earth
  2.0.0-rc.2 in the author's game on 9 November, by another session); `release/hydrology-0.6.2-rc.1.zip`, 55,110 bytes,
  SHA-256 `0ba29d31915d3830f96c28a2fc4f5dcce08148a4ebff6adf6a9af0c0624a5cc9`.
- Not done: a check in the author's own game (decision 0046 did one for the calm rivers on Earth 1.0.3), boat drift,
  and Earth's real "not ready" (the fake stands in; Earth's data on the bench is complete); `main` (with rc.5) is pushed to
  the private origin; the release is not uploaded.
