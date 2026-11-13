# Hydrology 0.6.2 — calm rivers for Earth 2.0 (release candidate 0.6.2-rc.5)

Prepared 8 November 2026 on `claude/rc-0.6.2`: `release/0.6` (0.6.1, `433f44c`) with the calm-rivers work
(`claude/calm-rivers`, 0.6.2-rivers.7, `be9d859`) merged at `1d91e7e`, then reviewed, tidied, fixed and baked. Built for
[Earth's rivers](https://mods.vintagestory.at/earth) and Earth 2.0; standalone gameplay is not tested for this release.
[Decision 0004](decisions/0004-release-0.6.2.md) records the scope. rc.1 and rc.2 were baked the same evening: rc.2
fixes what rc.1 had left, with Earth-side changes on Earth branch `claude/rc-2.0-hydrology-leftovers`, merged into
Earth 2.0.0-rc.2 (`d50f2ae`). rc.3 (9 November)
fixes a flood a player reported on 0.6.1 ([decision 0005](decisions/0005-held-levels.md)); rc.4 (the same day) follows a
second review and an in-game water playtest; rc.5 handles Earth 2.0.0-rc.3's "not ready yet".

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
- **Package:** `release/hydrology-0.6.2-rc.5.zip` (built from `dc73ab7` in the `rc-0.6.2` worktree, which was removed in
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
