# Draft upstream report: block edits lost when a chunk's pending compression is committed (not posted)

Draft, 2026-10-25. Not posted anywhere. Investigation: [congo-revert-2026-10-25.md](../congo-revert-2026-10-25.md).

---

**Title:** Server silently reverts block edits: `ServerSystemCompressChunks` commits a packed copy taken before the edit

**Game version:** 1.22.7 (Stable), dedicated server, Windows. Network v1.22.6, API v1.22.0.

**Summary.** Block edits in a loaded chunk sometimes go back to the chunk's earlier state, tens of seconds after they were
made:
- Broken blocks reappear, and a dug trench is soil and rock again.
- No chunk event is raised: no load, unload or generation, and nothing is marked dirty.
- The reverted state is saved with the world.

It happens when the edit falls between the compression thread's `Pack()` of that chunk and the main thread's later
`TryCommitPackAndFree()`. The commit only checks that the chunk has been idle for `UncompressedChunkTTL`. It does not
check whether the chunk was written after the packed copy was made.

Without mods it reproduced only with a shorter `UncompressedChunkTTL` (1500 ms: 4 of 13 runs). With default settings it
reproduced in a modded world (3 of 11 runs), not yet in a vanilla one (0 of 20).

## Reproduction (no gameplay mods)

The only mod is a small server-side test helper, `reverttest`. It doesn't change any game rule and only calls the public
API:
- `WorldManager.LoadChunkColumnPriority` with `KeepLoaded`;
- `BlockAccessor.BreakBlock` and `GetBlock`;
- the chunk events, for logging.

Source: Hydrology `tests/server/reverttest`.

1. **World:** a new standard world, 1 024 000 × 1 024 000, seed 386217, survival. No player joins.
2. **Load:** 5 s after the game runs, the helper loads 13 spots of 7 × 7 chunk columns each, `KeepLoaded`, one after the
   other: 3 near spawn, then 10 far away (15 000–70 000 blocks).
3. **Edit:** 20 s later it edits the 3 spots near spawn, 202 blocks in all, as a player would:
   - it breaks 3 bed blocks under water;
   - it digs a 2-wide, 6-long canal from the shore, 2 deep below the water's surface.
4. **Check:** every 2 s it reads each edited block. A block that is the same as before the edit again counts as reverted.
5. **Log:** every chunk event (loads, `BeginChunkColumnLoadChunkThread`, unloads, `ChunkDirty` with its reason, world
   saves) is logged with its time.

| Settings | Runs | Runs with reverted edits | Edits reverted |
|---|---|---|---|
| `UncompressedChunkTTL` 1500 | 9 | 3 | every edit in one or two chunk columns: 59, 147, 147 of 202 |
| `UncompressedChunkTTL` 1500, and each spot edited once the helper saw its chunk packed (`blocksCompressedTmp` set) or after a 120 s timeout (the 50 ms poll missed the short window, so all edits fell to the timeout) | 4 | 1 | 59 of 202, 24 s after the edit |
| **Fast packing, all runs** | **13** | **4** | |
| Default (`UncompressedChunkTTL` 15000) | 8 | 0 | |
| Default, heavier load (40 far spots of 9 × 9 columns) | 6 | 0 | |
| Default, the spot edited while its chunk's packed copy was pending (caught in 5 of 6 runs) | 6 | 0 | |
| **Default, all runs** | **20** | **0** | |

**With default settings it has not reproduced without mods (0 of 20 runs).** That fits the mechanism below. At default the
commit must come more than 15 s after the edit, so the compression queue must take that long to drain. Edits made while
a packed copy was pending were kept, because the commit followed soon and saw the recent write. In a modded test world
(Earth Map + Hydrology + Cylinder World, 384 blocks high, more water activity) it happened in 3 of 11 runs with default
settings, and in 0 of 4 with `UncompressedChunkTTL` 600 000. A probe of a reverted run's saved world showed the edits gone
from the save too.

**Chunk log in every reverted run:**
- After the spots are loaded there are no chunk events until the shutdown save.
- The reverted chunk columns get `ChunkDirty` (MarkedDirty) only at the edit (t20–21 s), then nothing.
- In the first reverted run every edited block in chunk column 15998,16004 was back at t 78.7 s: soil, rock-granite and
  tallgrass where the canal was.

## Where it goes wrong (from reading 1.22.7's server code)

- **Scan:** `ServerSystemCompressChunks.FindFreeableMemory` runs every 4 s on the main thread. It queues every loaded,
  unpacked chunk idle longer than `UncompressedChunkTTL`, except chunks near a playing client.
- **Pack:** `OnSeparateThreadTick`, on the compression thread, takes one chunk from the queue and calls
  `WorldChunk.Pack()`, which fills `blocksCompressedTmp` (and the light and fluid copies):
  - if `PotentialBlockOrLightingChanges` is set, it compresses the current data;
  - if not, it reuses the existing `blocksCompressed`.
- **Commit:** `FreeMemory`, on the main thread, runs only when the queue is empty. It calls `TryCommitPackAndFree`, which
  installs the copy and frees the uncompressed data, unless the chunk was read or written within `UncompressedChunkTTL`.
- **The gap:** a write between `Pack()` and the commit goes through `Unpack()`, which refreshes `lastReadOrWrite` and sets
  `PotentialBlockOrLightingChanges`. But it leaves `blocksCompressedTmp` in place.
- **The loss:** while the queue is long (many loaded chunks), the commit comes much later. If the chunk is idle for
  `UncompressedChunkTTL` by then, the time check passes and the copy made **before** the write is installed. The write is
  lost without any event. `PotentialBlockOrLightingChanges` is then cleared, so nothing marks the chunk as changed.

**Suggested fix:** whichever of these fits the engine:
- drop a pending packed copy when the chunk is written: clear `blocksCompressedTmp` and the other copies in `Unpack()`,
  under `packUnpackLock`;
- or record a write counter or timestamp in `Pack()`, and have `TryCommitPackAndFree` discard the copy if the chunk was
  written since.

**Who is affected:**
- **Servers with many loaded chunks:** large or busy servers, or mods that keep areas loaded. The compression queue is
  long there and commits lag behind packing.
- **Not near players while they stay:** chunks near a playing client are not queued while the client is there. An edit
  made just before the player leaves (a canal still filling, a dug area), or in a chunk queued before the player arrived,
  can be committed away once it is idle.
- **What a player sees:** blocks come back after a relog, or after leaving and returning.

## Aside (unrelated to the bug, possibly intended)

`FindFreeableMemory` skips chunks near a client when `Math.Abs(dx) < 2 || Math.Abs(dz) < 2`. That excludes a cross-shaped
band through the whole map rather than a square around the player (`&&`).
