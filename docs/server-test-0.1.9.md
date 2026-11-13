# Hydrology 0.1.9: the chunk pack fix, server test (2026-10-25)

**What changed.** A workaround for an engine bug that loses block edits
([VintageStory-Issues#10177](https://github.com/anegostudios/VintageStory-Issues/issues/10177); report
[upstream/chunk-pack-commit-lost-edits.md](upstream/chunk-pack-commit-lost-edits.md); investigation
[congo-revert-2026-10-25.md](congo-revert-2026-10-25.md)). It is not a water rule; Hydrology carries it because its tests
found the bug. `src/Rules/EngineChunkPackFix.cs`:
- **Counting writes:** a per-chunk write count goes up on every ChunkDirty event and every `WorldChunk.MarkModified`.
- **At pack time:** `Pack()`, holding the chunk's `packUnpackLock` for the whole pack, notes the count and the time
  (`Environment.TickCount`).
- **At commit time:** `TryCommitPackAndFree()` runs under the same lock. It drops the pending copy and refuses the commit if
  the count has moved, or if the chunk was read or written from a second before the pack began (`lastReadOrWrite`, compared
  as wrapping `int`). The chunk stays unpacked and is queued again at the next scan.
- **No pending copy, or a pack the patch didn't see:** vanilla's commit runs unchanged.

**When it applies** (config `ChunkPackFix`):
- "auto" (default): on game versions up to 1.22.x, read from the running game (the API's version constant would be the
  one Hydrology was built against);
- "on": any version;
- "off": never.

It is a version cap, not detection of the game's own fix: two reviews found that no check of the engine's code can tell
reliably whether a fix covers the right path. The patch is harmless beside a fixed engine, because it only drops copies of
chunks that changed after packing began. Switch it off once the game's fix is confirmed. Missing targets are logged once
and nothing is patched.

**Not covered** (as in vanilla):
- a write by another thread still under way while the main thread commits. Gameplay edits run on the main thread, which
  also commits;
- worldgen writing into an already loaded chunk without marking it.

## The revert runs

A run "lost edits" if any edited block was back in its earlier state at the end.

**Vanilla world** (`tests/server/reverttest`, `UncompressedChunkTTL` 1500, 13 spots, 202 edits; mkrun `HELPER=reverttest`):

| Setup | Runs | Lost edits |
|---|---|---|
| No mods at all (earlier) | 13 | 4 |
| Hydrology 0.1.9, `ChunkPackFix` "off" | 8 | 1 |
| Hydrology with the fix: first build | 7 | 0 |
| … after the first review | 13 | 0 |
| … final build | 13 | 0 |
| **All runs with the fix** | **33** | **0** |

**Earth world** (`earthmap-0.16.13.zip`, layout 8, default engine settings, the 13-site set `scenarios-earth-l8.json`,
`WA_WAKEALL=20`):

| Setup | Runs | Lost edits |
|---|---|---|
| Hydrology 0.1.8 (earlier) | 11 | 3 |
| Hydrology 0.1.9, "off" (first build, final build) | 2 + 2 | 0, **1** (the canal, the rapids edge and ramp bed, the Congo step edge) |
| With the fix: first build | 6 | 0 |
| … final build | 11 | 0 |
| **All runs with the fix** | **17** | **0** |

**The odds of chance:**
- Earth: with the fix off, 4 of 15 runs lost edits. 0 of 17 by chance at that rate is about 0.5 %.
- Vanilla: with the fix off, 5 of 21 runs lost edits. 0 of 33 at that rate is about 0.01 %.

**The fix was busy in every run:** 22–60 commits refused per Earth run, and 33 in the vanilla labs run. Each one is a
packed copy that would have been installed after its chunk was used.

## Nothing else changed

- **Earth shape, break and canal scenarios** (the same runs):
  - 0 water cells change on the first update at every site;
  - breaks change 1 cell (the hole);
  - canals 2 deep (river in, river out, rapids, sea) fill completely: 56, 84, 56 and 56 cells.
- **The 62 vanilla labs**, vessels on, against 0.1.8:
  - 3 labs differ, all known to vary or differing only in a label (`lab_drop2_wheel`, `lab_drop5`, `lab_fall_stones`);
  - wheels 14.9, 14.9 and 7.2, as before;
  - updates 27.0 k (0.1.8: 25.3 k).
- **Tick:** the mean is 46.8–47.0 ms on Earth worlds with the fix on or off, the same as 0.1.8, and 40–47 ms in the
  vanilla labs.
- **Memory:** a refused commit keeps a chunk unpacked until the next scan (4 s on). That was 22–60 chunks per run against
  about 7 000 loaded, so negligible; process memory was not measured separately.

## Review (Codex, twice)

**First pass**, five findings, all fixed:
1. The pack time was noted outside the chunk's lock, so a second pack could overwrite it while an older copy was pending.
   It is now noted under the lock, held through `Pack()`.
2. The time margin alone didn't cover a long write. A write count from ChunkDirty is now compared too.
3. The tick comparison sign-extended across the clock wrap. It now uses `int` subtraction.
4. The version gate read the API constant compiled into Hydrology. It now reads the running game's.
5. The "game fixed it" check accepted any assignment. It was replaced, see below.

**Second pass**, three findings:
- ChunkDirty doesn't report every write (exchanges, lighting). **Fixed:** `MarkModified` is counted too.
- A writer on another thread still active at commit can't be caught by counting finished writes. **Documented as not
  covered:** gameplay writes and commits share the main thread.
- The fix detection still accepted incomplete fixes. **Fixed by dropping it:** "auto" is now a version cap.

Codex found no lock-order problem. Vanilla already holds the chunk lock while compressing, and the commit keeps vanilla's
chunk-lock, then datapool-lock order. With no pending copy, nothing changes.
