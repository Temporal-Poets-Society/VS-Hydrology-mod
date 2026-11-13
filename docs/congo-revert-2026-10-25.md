# Edits undone near the Congo rapids: investigation (2026-10-25)

**The finding** ([server-test-0.1.8.md](server-test-0.1.8.md)). In some Earth layout 8 test runs, the edits the water audit
made near the Congo rapids were back to their earlier state 10–40 s later, solid blocks included:
- a dug canal, full of sources;
- broken bed blocks at a step edge and under a ramp.

The Earth session asked for three experiments (the user: "have the study run the full set with the Congo sites last, log
every chunk event worldwide around t30-t60, and try a run without Cylinder World").

**The world really reverts.** Probe mode (`REUSE_SAVE` and `WA_PROBE` in `tests/server/mkrun.py`) reopened copies of the
saves:
- **The reverted run (B):** the canal cells are solid soil again and the broken blocks are muddy gravel.
- **A run that kept its edits (D):** the canal and the holes hold water.

So the change is in the world, not in what the audit read.

## Runs

All are full 13-site sets on Earth `earthmap-0.16.13.zip` (rebuilt) with Hydrology 0.1.8, `WA_WAKEALL=20`, unless noted.
"Reverted" means at least one edit at the Congo sites was undone (shape solids back to 0, or the canal dry again).

| Setup | Runs | Reverted | Which sites |
|---|---|---|---|
| Default engine settings, original order (l8b–l8d, bisect A–D, orig1–4) | 11 | 3 | the rapids edge, ramp bed and canal (l8c, l8d, B) |
| Congo sites last in the list (last1–4) | 4 | 0 | |
| The Congo cluster alone (congo1–4) | 4 | 0 | |
| Without Cylinder World (nocw1–4; `tests/server/fakecylinder`, see below) | 4 | 1 | the rapids edge and ramp bed (canal kept) |
| Chunks packed after 1.5 s idle (`UncompressedChunkTTL` 1500; ttlshort1–8) | 8 | 3 | the rapids edge, ramp bed, bank, canal; also the Congo step's edge |
| Chunks not packed during the run (`UncompressedChunkTTL` 600 000, request queue 20 000; ttloff1–4) | 4 | 0 | |
| (`UncompressedChunkTTL` 100 000 000 alone: the server's chunk request queue overflowed while loading the sites, and it shut down before the tests: ttllong1–4) | 4 | – | |

**Bisect** (earlier): the first and the rebuilt Earth zip, and Hydrology from 15:42 and 0.1.8, in all four combinations.
Identical builds both kept and lost edits, so neither mod's version predicts it.

## The chunk log

The water audit now logs every chunk event to `wateraudit/chunkevents.json`:
- column loads, including `BeginChunkColumnLoadChunkThread`;
- unloads and generation (pass PreDone);
- chunk-dirty reasons: NewlyCreated and NewlyLoaded worldwide; MarkedDirty near the sites, counted per second;
- world saves.

In every run, chunk events stop once the sites are loaded, apart from one world save at about 200 s.
- **The reverted run nocw1:** the edge's hole filled at t20 (+1 source at t30) and was solid again at t60.
- **In that window there was no event at all:** no load, unload, generation or dirty marking anywhere in the world.
- **The reverted columns** (4050,1699 and 4049,1701) show one generation and one load before the edits, two marked-dirty
  writes at t20 (the break), and nothing after. The restoring write marked nothing dirty.

## Without Cylinder World

Earth needs Cylinder World's geometry for new worlds (decision 0023) and reads it by reflection. The test-only mod
`tests/server/fakecylinder` serves that geometry and nothing else:
- the mod id `cylinderworld` with version 0.11.11 (Earth's dependency) and the type name `CylinderWorld.CylinderWorldCore`;
- `Params` with the fields Earth reads;
- `SetSpawn`.

Its values are dumped from a real run (`wateraudit/cylinder.json`; `FAKE_CW` in mkrun). None of Cylinder World's runtime
systems run: seam jumps, chunk pre-sending, walls, sea ice, weather sync. Earth reported the same geometry ("world 256000 x
102400 blocks (ring 204800 …)"). Earth's source was not changed.

## Best guess at the cause

**The engine's chunk compression, unconfirmed.**
- **The mechanism:** the server packs chunk data that has been idle for `UncompressedChunkTTL` (15 s), on its own thread
  (`ServerSystemCompressChunks`, which scans every 4 s), and commits the packed copy on the main thread. If it commits an
  older compressed copy, the revert is silent: no chunk event, nothing marked dirty, every block back as it was when that
  copy was made. That matches the reverts landing 10–40 s after an edit.
- **Packing timing changes the sites hit:** with packing after 1.5 s, other sites than the usual three reverted.
- **Against it:**
  - 0 of 4 runs without packing is only weak evidence against a baseline of 3 in 11 (chance about 28 %);
  - 3 of 8 with fast packing is not clearly more than the baseline;
  - reading the engine's pack and unpack code, I found no path that writes without unpacking first. The race, if it is
    one, is not visible statically.
- **Why the harness meets it:** no player is online, so every loaded chunk is packed when idle. The engine skips chunks
  near a playing client: within 2 chunk columns along x *or* z, a cross-shaped band.

**Would players see it?** Possibly, rarely. A player's edits near them aren't packed while they stay. Chunks they leave,
for example a canal still filling with water, are packed once idle. If the race is in packing, an edit made shortly before
leaving could come undone. Neither Earth nor Hydrology causes it, and neither can prevent it. A report to the game's
developers would need a reproduction without mods.

## Tooling (Hydrology tests only)

- `mkrun.py`:
  - `REUSE_SAVE` + `WA_PROBE`: probe a finished run's save;
  - `FAKE_CW`: the fake Cylinder World;
  - `MAGIC`: engine magic numbers on top of `servermagicnumbers-default.json`;
  - `HYDRO_MOD`: an older Hydrology build.
- **Water audit:** `chunkevents.json`, `cylinder.json`, probe mode, and a site-chunk set that is safe to read from the chunk
  thread.
- **Scenarios:** `scenarios-earth-l8-congolast.json`, `scenarios-congo-cluster.json`.

## Without any gameplay mods (later the same day)

The user asked for a reproduction on an unmodded world, with a draft upstream report if it reproduced.
- **The helper:** `tests/server/reverttest`, a minimal server-side test mod. It loads spots with KeepLoaded, makes edits a
  player would (breaks bed blocks under water, digs a short canal), checks them every 2 s, and logs every chunk event. No
  Earth, Hydrology or Cylinder World, and no stand-ins.
- **The world:** a standard 1 024 000-block world, no player.
- **Results:**
  - `UncompressedChunkTTL` 1500: **4 of 13 runs reverted**, every edit in one or two chunk columns, with no chunk event at
    the revert;
  - default settings: **0 of 20** (8 plain, 6 with a heavier load, 6 editing while a packed copy was pending).
- **The mechanism, from reading the commit path:** a write between the compression thread's `Pack()` and the main
  thread's later `TryCommitPackAndFree()` does not drop the pending packed copy. The commit only checks that the chunk has
  been idle for the TTL. Long compression queues delay the commit past that, and the earlier copy is installed.
- **Draft upstream report, not posted:** [upstream/chunk-pack-commit-lost-edits.md](upstream/chunk-pack-commit-lost-edits.md).
