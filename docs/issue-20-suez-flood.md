# Issue #20: Suez flooding stops at an uneven canal bed

30 October 2026. Branch `codex/playtest-issues-2026-10-30`, based on `37d7dad` (Hydrology 0.1.11).
Reported combination: Earth 0.17.0-pre.8, Wmap 0.5.0-pre.11, Hydrology 0.1.11.
Screenshot inspected read-only: `C:\VS mods\issues-2026-10-25\20-suez-flood-stalls-uneven-bottom.webp`.

## Finding

**Hydrology owns a reproducible filling defect consistent with this report.** Its H2 rules cannot start a new source
layer where a wide water sheet lands below a step in a dug bed. Separately, H5 does not notify adjacent water when
vanilla creates a source. Both can leave incomplete filling. This is sufficient to justify a Hydrology fix; it is not
proof of every cell or action in the user's scene. The screenshot has no block IDs or callback history, and the save
was not opened. A separate missed WorldEdit notification remains possible but was not needed to reproduce the defect.

## Code ownership audit

| Concern | Evidence in this baseline | Conclusion |
| --- | --- | --- |
| Block behavior and flow ticks | [H5LiquidHooks.Apply / AfterUpdate](../src/Rules/H5LiquidHooks.cs) patches `BlockBehaviorFiniteSpreadingLiquid.OnDelayedWaterUpdateCheck`, downward spreading, feed checks and flow labels | Hydrology changes how vanilla water, salt water and rapid water spread; it is not just a river renderer |
| Source creation | [H2Filling.TryFill / TryFillVessel](../src/Rules/H2Filling.cs): ground fill needs same-level sources; vessels need a source below plus a source over a source beside it | A lower landing sheet can meet neither rule, even with updates scheduled |
| Flow after a fall | [H7Settling.MaxNeighbourLevel / Label](../src/Rules/H7Settling.cs): falling water in air cannot feed sideways; landed water can. [H1](../src/Rules/H1SourcesStay.cs) and [H3](../src/Rules/H3Flattening.cs) protect sources and flatten ramps | The landed sheet flows finitely; it does not automatically become a filled basin |
| Notifications | [Liquids.SetAndWake](../src/Liquids.cs) writes directly, schedules the changed fluid, and triggers neighbor updates. H5's old vanilla-source transition only woke H4 edges | Hydrology-created sources notify their support change; vanilla-created ones could leave nearby cells waiting |
| Bulk edits | [Sim.Pulse / TriggerNeighbours / BreakBlock](../tools/watersim/src/Sim.cs) and the existing [engine study](water-rules-study-2026-10-23.md): direct writes wake nothing; bulk commits wake changed fluids, player edits notify neighbors | A mass edit with no water notifications is a separate engine/tool integration problem. This test explicitly notifies for every removal |
| Late chunk-pack commit | [EngineChunkPackFix](../src/Rules/EngineChunkPackFix.cs), [upstream notes](upstream/chunk-pack-commit-lost-edits.md), [0.1.9 evidence](server-test-0.1.9.md) | Drops stale packed data, never changes spreading/source criteria. The symptom it fixes is old blocks returning, not an intact dry bed. No chunk system exists in this reproducer |
| Earth / Wmap | [EarthRiverQuery](../src/EarthRiverQuery.cs) supplies immutable river geography by reflection; H2 reads it to exclude natural river water | Earth owns river placement and the exclusion query; Hydrology owns these source rules. No Wmap calls occur on this path |

The installed engine DLLs were inaccessible in this session. Engine details above are supported by the checked-in
calibrated simulator and prior engine investigation, not by a new decompilation or a runtime trace from Suez.

## Change in 0.1.12

[Decision 0001](decisions/0001-uneven-canal-filling.md) records the choice and boundaries.

- `src/Rules/H2Filling.cs`: fill a supported landing cell from a connected wide source sheet at or above it, through
  at most 16 continuous water cells. Respect barriers and the source-square width. Requires vessels; salt water is
  eligible, fresh water needs Earth's query and a non-river column in dimension 0. Original depth/containment guards
  remain on the old vessels path.
- `src/Rules/H5LiquidHooks.cs`: a newly created vanilla source triggers neighbor updates before its H4 edge check.
- `tests/offline/`: link actual production H2 into a fake API over the existing engine simulator; reproducible runner,
  old-source comparison, canal matrix and exclusion/regression checks.
- `tools/watersim/src/{Sim,Hydrology,RuleSet}.cs`: optional H2 adapter and an explicit new-source notification switch.
  Default simulator behavior is preserved unless the switch is set.
- `modinfo.json`: 0.1.12 and the bounded uneven-bed behavior. `README.md`, `docs/design.md`, and this local decision log:
  behavior, evidence and verification limits.

## Verification

Commands, from this working copy:

```powershell
./tests/offline/run.ps1 -Baseline
./tests/offline/run.ps1
```

Both compile with .NET SDK 8.0.419, zero warnings/errors, no packages. The baseline uses H2 from `37d7dad` and the
same new-source notification in the simulator, so it isolates the source rule rather than removing all notifications.
Baseline: **32 failures of 12,084 assertions** (expected exit 1). Fixed: **12,084/12,084 pass**, exit 0.
Raw outputs: [baseline](issue-20/offline-baseline.txt), [fixed](issue-20/offline-fixed.txt).

Each canal is 24 blocks long. Water surface Y=10; pool bed Y=4. Canals start with bed Y=5, with a raised section (bed
Y=7), depression (bed Y=3), or staircase (beds Y=7,5,3). The large edit removes every canal block in one operation and
notifies every removal. Other cases dig a row each second. Counts below are source cells in the entire excavated
canal below the original water surface, not just its top layer (salt water, width 6, bulk edit):

| Bed | 0.1.11 H2, with new-source wake | 0.1.12 | Total |
| --- | ---: | ---: | ---: |
| Raised middle | 336 | 636 | 636 |
| Depression | 210 | 804 | 804 |
| Descending steps | 126 | 756 | 756 |

All 25 eligible canal cases fill completely, settle, and keep the same liquid kind/height after a wake-all. Source
labels under water may change, as in vanilla. Eight canal controls retain incomplete flowing behavior: one-wide,
Fill off, vessels off, no fresh-water query, and natural river columns. Direct rule tests reject unsupported sheets,
air/solid/other-liquid breaks, a waterlogged barrier, an out-of-range source square, and unknown-dimension geography.
Four synthetic natural river layouts (widths 2/8, depths 2/3, no sills) settle after wake-all and a bed break without
raising their sources above generated levels. This protects the river-step behavior the older vessels guards served.

The existing `watersim calibrate` was also run successfully: six-block vanilla reach, 2-wide filling versus 1-wide
flow, source protection at a step, chute/column cases retain their existing outputs. It is a diagnostic calibration,
not an engine integration test. `git diff --check` passes.

## Runtime status and remaining work

The full mod build was attempted but **blocked by filesystem access** to the installed game's `VintagestoryAPI.dll`,
`VSEssentials.dll`, `VSSurvivalMod.dll` and Harmony. The offline project compiles production H2, but it does not compile
the rest of the mod against those assemblies. No release ZIP was produced. No dedicated server or game client was
launched, nothing was installed, and `VintagestoryData` was not accessed. No other worktree or branch was modified.

The test's API facts and Earth query are fakes; the surrounding engine, H1/H3/H4/H7 and H5 notification are ports.
Harmony binding, real WorldEdit batch notifications, chunk scheduling, reload, runtime performance, and the exact
Earth 0.17.0-pre.8/Wmap combination remain unverified. Next, with game DLL access, build 0.1.12 and use the existing
isolated `tests/server/mkrun.py` harness for the same synthetic beds, then replay the Suez dig in an authorized
playtest. Check one-wide channels and natural river steps alongside it. No client run was attempted here.

The fix runs only on normal water updates. Already stalled water needs an initial neighbor update after upgrading;
it is not swept periodically. A mass edit that notifies no adjacent water at all still needs a WorldEdit/engine fix
(notify boundary liquids once after commit), not a global Hydrology polling workaround. The 16-cell scan limit and
fresh-water behavior without a river query are deliberate remaining limits.

Suite roadmap row #20 and mod inventory were read but not updated: the user's instruction permits writes only in this
working copy. `C:\VS mods\tools\Update-ModInventory.ps1` writes outside it, so it was not run. A later suite update
should mark #20 fixed offline in 0.1.12, runtime pending, with this report as its source.

**Commit blocked:** `git add` and `git commit` were attempted on the requested branch, but Git could not create
`C:\VS mods\Hydrology\.git\worktrees\codex-playtest-issues\index.lock` (permission denied). That metadata is outside
the session's writable working copy. No commit was created and the changes remain unstaged here; no branch switch or
push was attempted. This filesystem restriction cannot be resolved within this session's allowed write paths.
