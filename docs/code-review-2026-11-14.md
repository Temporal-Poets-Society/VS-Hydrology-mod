# Hydrology code review, 14 November 2026

Scope: Hydrology `main` at `9d06264` (0.6.2-rc.12), all of `src/` (4,355 lines), the offline suite, the registry test and
the server-lab tooling. A fresh look after the 0.6.0 release review
(`overview/archive/reviews-2026-10/release-2026-11-01/hydrology.md`) and decision 0004's review fixes, and after the large
changes since: held levels (0005), supplied spaces (0006), pond release and drains (0007, 0008), basins, valleys and
river dams (0009), HY-0010's scratch reuse. Read-only: no code was changed. Findings are ranked by severity; each has
file:line, evidence, a suggested fix and its risk. Backlog items HY-0012 to HY-0019 carry the ones worth doing.

What was run:

- `tests/offline/run.ps1`: **14,434/14,434** pass. `AllocCases`: `Opened` 2,408 B / 137 µs (plateau), 30,368 B / 350 µs
  (pools); `Placed` 0 B.
- `dotnet build` (Debug) and `dotnet run --project tests/registry`: **65/65** against the installed 1.22.7 DLLs.
- A scratch micro-benchmark on the offline adapter (production `H2Filling.cs` and `H2Release.cs`, outside the repo):
  - `ForgetHeldNear` with a full memo (65,536 cells) and a box that misses them all: 107 µs and 123 B per call.
  - A held search refused on an open beach (a salt sea at one level, open dry land beside it, then `FedChannel` and
    `Supplied`): 2.8 ms per cold call, 9 µs when answered from the refusal memo. The adapter allocates in its own
    `GroundBelow`/`Below`, so its byte counts are not the game's; the times are indicative only.
- The engine side of the chunk-pack fix and of the side-feed rule was read in the 1.22.7 decompile
  (`devtools/vs-ref/decompiled/1.22.7`).
- Not run: the headless server labs (`tests/server/mkrun.py`). They need 0.3-1 GB per run and the Earth zips, and a
  Hydrology session is working in the `dam-pools` worktree. The labs' recorded cost per liquid update (14-19 µs in
  `docs/release-0.6.2.md`) is used where a hot path is discussed.

## Summary

The architecture holds up well: one registry for every patch and reflection, all-or-nothing features with runtime
breakers, bounded searches with named limits, scratch reuse on the main thread, and rationale for every rule next to
the code. The main risks are where the newer, stateful parts (drains, dams, remembered answers) meet the older,
stateless contract: H2 release keeps running when the water rules it relies on are switched off (1), a saved drain
restarts on half-loaded land (2), a remembered "held" answer outlives a broken wall (4). The test suite covers H2 well
but the rest of the production rules only through ports and labs (8). The rest is maintainability and doc drift.

| # | Severity | Finding | Item |
| --- | --- | --- | --- |
| 1 | medium | H2 release runs on when the water rules are off | HY-0012 |
| 2 | medium | A saved drain restarts on half-loaded land and mis-measures its body | HY-0013 |
| 3 | medium | A failing drain leaves its openings guarded for the session and skips the tick | HY-0012 |
| 4 | medium | A remembered "held" answer stands 20 s after a wall is broken | HY-0014 |
| 5 | medium | The registry checks signatures, not the bodies Hydrology re-implements | HY-0015 |
| 6 | medium | Earth query failures are retried on every call, in the hottest hooks | HY-0016 |
| 7 | medium-low | Every new source wakes 765 cells (H4 fan-out) | HY-0017 |
| 8 | medium-low | Offline tests compile only H2; H3/H4/H5/H7, Liquids and the query are fakes or stale ports | HY-0019 |
| 9 | low | Per-update allocations left outside H2Release | HY-0017 |
| 10 | low | `ForgetHeldNear` scans the whole memo per call | HY-0014 |
| 11 | low | Query cache cleared whole at 65,536 columns | HY-0016 |
| 12 | low | Shared scratch aliasing and mixed scratch conventions | HY-0018 |
| 13 | low | Dead code and duplicated helpers | HY-0018 |
| 14 | low | Docs drift (README version, design.md, class summaries, decision index) | HY-0018 |
| 15 | low | Debug-only `Hot` map is unbounded, and the labs run the Debug build | HY-0017 |
| 16 | low | A drain's planned cell in unloaded land loses its water | HY-0013 |

## Findings

### 1. H2 release runs on when the water rules are off (medium)

**Where:** `src/HydrologySystem.cs:136-154` (break, place and tick handlers), `src/Rules/H2Release.cs:133` and `:452`
(`Opened`, `Placed` gates), `src/Rules/H5LiquidHooks.cs:53-54`, `:79-83` (water rules not applied) and `:36` (`Water`
breaker).

**Evidence:** `Opened` and `Placed` check only `H2Release.Enabled` and `H2Filling.Enabled` (the config). When the water
rules are not applied (a game update changed a target, `ApplyWaterRules` returns at line 79) or their breaker trips at
run time, the drains, dams and `Retry` keep running. The parts of the drain that keep its volume are in H5, though:
the rc.10 guard that empties sources vanilla makes in a draining pond (`H5LiquidHooks.cs:303-318`) and H2's refusal to
fill drained cells (`H2Filling.TryFill`, line 133) only run inside the hooks. Without them a drain removes sources each
second while vanilla's endless-source rules (`TrySpreadDownwards`, three source neighbours) put new ones in its holes,
and `Step` then writes the plan's sources below as well: water is duplicated, which is what decision 0007 exists to
prevent. The registry's promise is "switches off only the affected feature", but this feature depends on the one that
went off.

**Fix:** expose one flag, `H5LiquidHooks.WaterRulesOn` (applied and `!Water.Off`), and gate `Opened`, `Placed`,
`H2Release.Tick` and `H2Filling.Retry` on it. When it goes off with drains running (the breaker's `onOff`), end them as
an opening closed would (`Finish` without a new round) and log once. Add a registry check: with `TrySpreadDownwards`
missing, a break beside a pond starts no drain.

**Risk:** low. Only changes behaviour when the water rules are already off.

### 2. A saved drain restarts on half-loaded land and mis-measures its body (medium)

**Where:** `src/Rules/H2Release.cs:374-386` (`Tick` starts a pending drain when `openings[0]`'s chunk is loaded),
`:696-742` (`Search.Body` has no `Loaded` check).

**Evidence:** after a restart, `Load` queues the saved drains, and `Tick` replans one as soon as its first opening's
chunk is loaded. Chunks around it load over the following seconds. `Body` reads an unloaded neighbour as air (the
server's accessor), so a pond or lake that reaches into a chunk not yet loaded is measured smaller than it is. Its top
layer then looks small enough to drain within `UnlimitedSeconds` (line 199), so a lake that kept its water before the
restart can start to drain, and a pond drains only the part that was loaded (the rest stays above the opening and
starts a second round later). `Pour`/`Layer` already treat unloaded land as a stop (`Beyond("unloaded land")`); only
`Body` does not. The same applies to a player's break at the edge of the loaded area, which is rarer.

**Fix:** in `Body`, when a neighbour's chunk is not loaded, stop with a third answer ("unknown": `why = "unloaded
land"`). `Opened` then keeps the water (no drain) for a player's break, and `Tick` puts a saved drain back in `pending`
with a short wait instead of dropping it. The chunk check per new cell is cheap if it is cached per chunk column
(`ba.GetChunkAtBlockPos` once per 32 x 32 column, as `H4Rapids.WindowLoaded` does).

**Risk:** low. Offline: `GameWorld.Unloaded` already exists in the adapter, so a case is easy (a 2-chunk pond, one
chunk unloaded at load).

### 3. A failing drain leaves its openings guarded for the session and skips the tick (medium)

**Where:** `src/Rules/H2Release.cs:387-397`, `src/HydrologySystem.cs:150-154`.

**Evidence:** when `TickJob` throws, the catch removes the job and its drained cells from `draining`, but not its
openings or origin, which were added at line 250. `TryFill` refuses every cell in `draining` (`H2Filling.cs:133`), so
those cells never fill again in that session. The catch then rethrows: the other drains and `H2Filling.Retry` are
skipped for that tick, and the handler logs an error ("a pond drain failed and stops"). An error in `Retry` or in the
place/break handlers is logged on every call with no breaker, unlike every Harmony hook.

**Fix:** in the catch, remove the openings and origin too, do not rethrow, and hand the exception to a
`GamePatches.Breaker("H2 release")` so that a systematic error switches the release feature off after the usual 50 in a
minute, logged once with its stack trace. Wrap the break/place handlers in the same breaker. `Retry` gets its own try
inside the tick.

**Risk:** low.

### 4. A remembered "held" answer stands 20 s after a wall is broken (medium)

**Where:** `src/Rules/H2Filling.cs:366` (`HeldMemoMs = 20000`), `:473-476` (every cell of the search remembered as
held), `:734-744` (`ForgetHeldNear`, called only from H2 release), `src/Rules/H2Release.cs:142-148` (`Opened` returns
before any forgetting when no source is beside the broken cell).

**Evidence:** a held search remembers every cell it covered as held for 20 s, so the rest of a basin fills without
searching again (the rc.4 fix for 41 million reads). Nothing forgets those answers when the space opens, except a
drain or a placed block near water. A player who breaks the far wall of a trench that is still filling (the cells next
to the break hold flowing water, not sources, so `Opened` returns at line 148 without forgetting anything) leaves the
trench's cells "held" for up to 20 s. H2 keeps filling them, and H1 keeps the sources once made, so the trench can end
up standing full at the feeding level while it is open to the slope: the water stands where it is not held, which is
what decision 0005 rules out. The window is short and needs a fill in progress, so this is a risk to check in a lab,
not a seen bug. The memo key also ignores the liquid (salt and fresh water share an answer).

**Fix:** give each successful search a token (an int), remember `cell -> token` and keep a set of live tokens. On any
player break (the existing `DidBreakBlock` handler, before `Opened`'s early returns) and in `Placed`, drop the tokens of
the remembered cells within one block of the change: the whole search they came from is forgotten, wherever its cells
are. That also replaces `ForgetHeldNear`'s full scan (finding 10). Add the liquid to the key, or keep salt and fresh
memos apart.

**Risk:** medium-low. More searches after breaks (each a few ms at most); `lab_reach`, `lab_spill` and the offline
`HeldTrenchAndLongCanal` should end the same. A lab case: a 2 x 3 x 40 trench opened from a lake, its far wall broken
into a slope 5 s later.

### 5. The registry checks signatures, not the bodies Hydrology re-implements (medium)

**Where:** `src/Rules/H5LiquidHooks.cs:92-95` and `src/Rules/H7Settling.cs:92-120` (a prefix that replaces vanilla's
`GetMaxNeighbourLiquidLevel` entirely), `tests/registry/Program.cs:82-100`.

**Evidence:** `GamePatches` makes sure every target has the expected name, parameter types and return type, and the
registry test applies the patches to the installed DLLs. Neither notices a game update that keeps a signature but
changes what the method does. That matters most where Hydrology rewrites vanilla's logic: H7's `MaxNeighbourLevel` is
"written from vanilla's behaviour" and returns instead of it. Compared with 1.22.7's
`BlockBehaviorFiniteSpreadingLiquid.GetMaxNeighbourLiquidLevel` (decompile line 836) it already differs on purpose: it
reads walls with `MostSolid` (lake ice), vanilla with the solid-blocks layer. `TryRelax` relies on the two agreeing
("so the two never disagree"), and they agree only while the side-feed hook is on: if that feature alone is switched
off, level relax raises cells against vanilla's lowering at ice edges, a loop of the kind the comment warns about.
The prefixes of `TrySpreadDownwards` and `RemoveOtherLowerNeighbours` also encode facts about vanilla's control flow
(`pathFacing = UP` skips the removal) that a refactor in the game would silently break.

**Fix:** record, in the registry test, a SHA-256 of the IL of each patched or re-implemented vanilla method
(`MethodBody.GetILAsByteArray()`) for the game version tested, and report a changed body by name ("re-read
`GetMaxNeighbourLiquidLevel` in vs-ref before releasing"). AGENTS.md already says to run the registry after a game
update; this makes that run catch semantic changes. Separately, make `TryRelax` use vanilla's own feed check when the
side-feed feature is off (or switch level relax off with it, through the breaker's `onOff`, as the other refinements
do).

**Risk:** none for the IL check (test only); low for the relax coupling.

### 6. Earth query failures are retried on every call, in the hottest hooks (medium)

**Where:** `src/EarthRiverQuery.cs:266-272` and `:295` (no answer kept on an error), `:308` (logged once).

**Evidence:** a `RiverAt` that throws (other than "not ready") is not cached, by design, so the column is asked again
next time. The callers include the label postfix (`GetLiquidBlockId`, every source relabel), `Defer` in every water
update's prefix, `Supplied` per column and `MarkDammed` per column. If an Earth version starts throwing systematically,
every one of those calls pays for a thrown and caught exception (tens of µs each, against 14-19 µs for a whole update
in the labs), while the log shows one warning. The water still works, but the tick slows with no sign of why.

**Fix:** keep a failure for a short time per column (a negative entry with a 5 s expiry), and count failures in a
`GamePatches.Breaker("Earth's river query")` whose `onOff` unbinds the query (the rules then run as without Earth, as
`Bind` already allows), logged once with the reason.

**Risk:** low.

### 7. Every new source wakes 765 cells (medium-low)

**Where:** `src/Rules/H4Rapids.cs:309-326` (`WakeEdgesAround`), called from `WakeEdgesOnNewSource` (`:345`) by every H2
fill (`H2Filling.cs:197`), refill and vanilla-made source (`H5LiquidHooks.cs:331-336`), and from every edge change.

**Evidence:** a new source reads a diamond of 85 columns x 9 levels (765 fluid reads) and schedules an update for every
fresh source within it that has an open side or is rapid; each of those updates runs `CheckSource` and usually
`IsEdge`'s two searches. Filling a basin wakes the same rim sources once per filled cell nearby. A 64 x 64 layer is
about 3 million fluid reads for the wakes alone, before the woken updates. The labs' average cost per update (14-19 µs)
shows this is fine at lab scale; it is the term that grows with the size of a fill.

**Fix:** coalesce: a per-tick set of edge cells already woken (cleared each server tick), so a cell is woken once per
tick whatever number of new sources is near it. Optionally skip the wake when no source within `Reach` is rapid and the
new source has no open side (it cannot have made or removed a drop).

**Risk:** medium-low: wake order affects how chutes settle. Pair a regular lab run with rc.12's (`lab_drop2_wheel`, the
chutes) before and after.

### 8. Offline tests compile only H2; the rest are fakes or stale ports (medium-low)

**Where:** `tests/offline/Issue20.csproj` (compiles `src/Rules/H2Filling.cs` and `H2Release.cs` only),
`tests/offline/GameAdapter.cs:59-100` (fake `EarthRiverQuery`, `Liquids`, a no-op `H4Rapids`), `tools/watersim/src/`
(the engine and rule ports the suite runs on).

**Evidence:** production `Liquids`, `EarthRiverQuery`, H3, H4, H5 and H7 never run offline: the suite drives
production H2 through watersim's port of the other rules (`tools/watersim/src/Hydrology.cs`, last changed 30 October),
while H4 and H7 changed on 9 November. H4's wake (finding 7) is a no-op there. The adapter reads out-of-bounds cells as
solid, the opposite of the game (noted in `tests/offline/README.md`). The registry test covers bindings, the query's
span and "not ready" handling, but not the runtime breakers (`Fault` 50 times in a minute switches a feature off and
runs `onOff`). Save and load of drains is covered only by the mid-drain save in `lab_reach`.

**Fix (in order of value):** (a) a registry case for `Breaker`: 50 faults switch it off once, `onOff` runs, 49 spread
over two windows do not; (b) an offline `Save`/`Load` round trip with a malformed line, a missing liquid and two
dimensions; (c) compile production H4 and H7 offline against the adapter (they need `Liquids.Open`, `Passes`,
`FallingOf` and `GetLiquidBlockId`, which the adapter can supply) and retire the matching watersim ports, or check the
ports against production with a shared scene set.

**Risk:** none (tests only); (c) is a day's work.

### 9. Per-update allocations left outside H2Release (low)

**Where:** `src/Rules/H3Flattening.cs:41`, `:56`, `:70`; `src/Rules/H4Rapids.cs:312`, `:330`, `:360`, `:389-390`,
`:438-450`, `:459-460`, `:474-480`; `src/Rules/H2Filling.cs:302`, `:334-335`, `:423-424`, `:656-657`, `:702-703`,
`:764-771`, `:806`, `:820`; `src/Rules/H5LiquidHooks.cs:305`.

**Evidence:** HY-0010 removed `H2Release`'s garbage. The liquid update path still makes a new `BlockPos` (or a
`Copy`/`DownCopy`/`UpCopy`) in most helpers: about 10 to 20 per flowing-water update across H3, H4 and H2's guards, a
few hundred bytes each update. H3 also builds the falling variant through `world.GetBlock(c.CodeWithParts(...))` (an
`AssetLocation` and a lookup per changed cell) while `Liquids.FallingOf` (`Liquids.cs:80`) already has that table, and
H3/H7 read `Variant["flow"]` (a string dictionary lookup) per cell in loops.

**Fix:** the HY-0010 pattern: scratch positions per rule (main thread), `Liquids.FallingOf` in H3, and a `flowKind`
byte table in `Liquids.Init` (still / falling / directional) to replace `Variant["flow"]` in H3 and H7. Measure with an
allocation counter in the labs' stats (`GC.GetAllocatedBytesForCurrentThread` around `After`, Debug only).

**Risk:** low; mechanical, but the scratch rules (finding 12) must be kept.

### 10. `ForgetHeldNear` scans the whole memo per call (low)

**Where:** `src/Rules/H2Filling.cs:734-744`; called each drain step (`H2Release.cs:335`, every 0.5 s per running
drain), at `Finish` (`:356`) and per placed block beside water (`:507`).

**Evidence:** it walks every key of both memos (up to about 70,000) and allocates an array and a list per call: 107 µs
per call with a full memo in the benchmark. The box spans the drained and the filled cells plus 8 blocks, which can be
large when the water lands far below, so it also drops answers that the change does not affect.

**Fix:** the search tokens of finding 4, or an index of memo cells by chunk.

**Risk:** low.

### 11. Query cache cleared whole at 65,536 columns (low)

**Where:** `src/EarthRiverQuery.cs:68-69`, `:296-299`.

**Evidence:** the answers are geography and never change, but the cache is emptied at 65,536 columns, and every column
is then asked of Earth again (Earth's first answer in a chunk "can cost more"). A loaded 1:10 Earth area with active
water in several places, or `MarkDammed` walking up to 16,384 columns, can pass that and empty it repeatedly.

**Fix:** keep answers per chunk column (a 32 x 32 array of `Answer` per chunk key) and evict whole chunks, least
recently used, or when the chunk unloads; or raise the limit (each answer is about 40 bytes; 262,144 columns is about
10 MB).

**Risk:** low.

### 12. Shared scratch aliasing and mixed scratch conventions (low, maintainability)

**Where:** `src/Rules/H2Release.cs:460` and `:554` (`Placed`'s `n` and `MarkDammed`'s `c` are the same `placeN`),
`src/Rules/H2Filling.cs:418-421` and `:547-552` (`FedChannel`, called from inside `Held`'s loop at `:455`, clears
`Held`'s `heldSeeds` and `heldTraces`), `src/Liquids.cs:109-121`.

**Evidence:** both are safe today only because the caller re-sets the position before its next use and `Held` returns
straight after `FedChannel`. H2Filling, H4 and Liquids use `[ThreadStatic]` scratch; H2Release uses plain statics. All
of it relies on one invariant (every caller is on the server's main thread, one liquid update at a time), which was
confirmed against 1.22.7 (`RegisterCallbackUnique`, main-thread callbacks) but is stated in different words in each
file.

**Fix:** one convention: per-rule scratch objects (as `H2Release.Search` already is), plain statics, and the invariant
stated once, with a Debug-only assertion of the main thread's id in the hook entry points and the release handlers.
Give `MarkDammed` and `FedChannel` their own scratch.

**Risk:** low.

### 13. Dead code and duplicated helpers (low)

- `H2Filling.Held`'s `level` parameter is never passed (`H2Filling.cs:400`, callers at `:173`, `:247`, `:795`); its doc
  still says the uneven basin passes the sheet's top (dropped in rc.9). `FedChannel`'s `if (pos.Y != top)` (`:544`) is
  therefore always false.
- `H2Filling.IsSourceOf` (`:746`) is unused; `H2Release.opening0` (`:276`) is a one-line tuple.
- The doc comment of `FedChannel` (`:488-502`) sits directly above the doc comment of the `retries` field (`:503-505`),
  so the compiler attaches neither to `FedChannel`.
- The bounding box of a set of cells is computed three times in `H2Release` (`Step`'s `Grow`, `Finish`, `Placed`).
- "Held up from below" is written four times: `H7Settling.Supported`, the tail of `FallingInAir`, the tail of `Label`
  (`H7Settling.cs:27-85`) and H3's check (`H3Flattening.cs:66-69`).

**Fix:** remove or fold these; one `Liquids.HeldUp(ba, pos, b)`. **Risk:** low (the offline suite and a regular lab pair).

### 14. Docs drift (low)

- `README.md:8`: "Current version: 0.6.2-rc.10" (it is rc.12).
- `docs/design.md:26`: "Hydrology registers no tick listener and no callbacks of its own". Since rc.5 it registers
  callbacks (`TryAgainLater`), since rc.9 a 500 ms tick (drains, `Retry`), the break and place events and a save key.
  design.md has no section for decisions 0006 to 0009 (supply, fed channels, drains, dams, river dams); the
  architecture of H2 release lives only in the decision records and class comments.
- `H2Release`'s class summary (`H2Release.cs:19-21`) still says a body with a natural river column is unlimited;
  rc.11/rc.12 made the river a floor and feed (decision 0009, point 5). `Placed`'s summary (`:446-447`) still says
  "Earth's natural river columns keep their generated levels".
- `docs/decisions/README.md`: 0009 is listed before 0008, and 0004's status (rc.5, "installed") is stale.
- The card says offline 14,424 checks; the suite has 14,434 since rc.12.
- The 0.6.0 review's S1 (a dedicated server's new vanilla world has no `hydrologyFlooding` key and takes the legacy
  policy, vessels off) is not recorded as decided anywhere; Earth worlds are unaffected (Earth sets its own key).
- Style: class comments mix the current contract with history ("rc.4 (audit): ...", "0.1.7: ..."). The history is
  valuable, but it makes the contract hard to find: keep the current behaviour first and move the version notes under
  a "History" paragraph or into the decisions.

**Fix:** one docs pass. **Risk:** none.

### 15. Debug-only `Hot` map is unbounded, and the labs run the Debug build (low)

**Where:** `src/Rules/H5LiquidHooks.cs:286-289`, `src/HydrologyStats.cs` (`Hot`), `tests/server/mkrun.py:98`,
`tests/server/wateraudit/src/WaterAudit.cs:1926` (its only reader).

**Evidence:** in Debug builds every update formats a string key and grows a dictionary that is never trimmed. The labs
copy `bin/Debug/mod`, so long labs grow memory per updated cell and the per-update times in the release notes include
this cost (small, about 1 %).

**Fix:** a `long` key (packed coordinates) and a cap, or switch it on only with an environment variable the labs set
when they need the hot-cell report.

**Risk:** none.

### 16. A drain's planned cell in unloaded land loses its water (low)

**Where:** `src/Rules/H2Release.cs:323-331`.

**Evidence:** the plan is made once. If the cell where a source should stand is unloaded when its turn comes, the
source is still taken from the pond and counted as `lost`. Water landing far below the pond can be in another chunk.

**Fix:** when the target's chunk is unloaded, put the source back (skip the step for that cell and wait), as
`Step` already waits for an unloaded drained cell at line 313.

**Risk:** low.

## What is good

- **The game-patch registry** (`src/GamePatches.cs`): every target looked up by name, parameter and return types
  before anything is patched; features patched whole or not at all, rolled back on a refused patch, logged once per
  feature and world; public fields and methods the compiled code uses directly are checked too. The runtime breaker
  counts faults in a one-minute window and runs an `onOff` that keeps the remaining rules consistent with vanilla.
- **Hook hygiene:** thin wrappers around a method of their own inside a try, so even a JIT failure after a game update
  lands in the catch; the finalizer that restores `multiplySpread` (the 0.6.0 review's S6) is in place; lava and
  boiling water are skipped.
- **The chunk-pack fix** takes the same `packUnpackLock` vanilla takes in `Pack` and `TryCommitPackAndFree` (checked in
  the 1.22.7 decompile: both lock it, and the lock is re-entrant), so it adds no lock-order risk; the per-chunk stamps
  live in a `ConditionalWeakTable`.
- **Bounded searches with named limits** (`MaxHeldLevel`, `MaxHeldSpace`, `MaxChannel`, `MaxSearch`, `MaxReach`,
  `MaxMarkWalk`, `MaxDropScan`) and memo tables with sizes capped; nothing ticks still water.
- **The river query binding** (`EarthRiverQuery.Bind`) is defensive: each optional method in its own try, the version
  capped to what this build reads, "not ready" handled without counting as a failure, a column hash that avoids
  diagonal collisions.
- **HY-0010's allocation test** in the offline suite turns a performance property into a check, and the
  `AllocCases` pattern is the right template for findings 9 and 15.
- **Saves:** drains are saved as plain text lines with tolerant parsing, the store runs in `GameWorldSave` and is never
  unsubscribed (the suite's lesson that mods are disposed before the shutdown save).
- **Release hygiene:** the Release build packs a fixed file list and refuses to overwrite a zip.
- **Rationale next to the code:** every rule explains what it does and which report or lab case it came from, which
  made this review fast.

## Structure

The rules H1 to H8 map well onto files, with two exceptions that have grown: `H2Filling.cs` (831 lines) now holds five
filling cases plus the held/supplied/fed-channel machinery and its memos and retries; `H2Release.cs` (895 lines) holds
drains, their save format, the path search, dams and river marks. A split along those seams would make each part
reviewable on its own without changing behaviour:

- `H2Held.cs`: `Held`, `FedChannel`, `Supplied`, `TraceUp`, the memos, `Retry` (and the search tokens of finding 4).
- `H2Drains.cs`: `Drain`, `jobs`, `Tick`, `Step`, `Finish`, `Save`/`Load`; `H2Release.cs` keeps `Opened` and `Search`.
- `H2Dams.cs`: `Placed`, `MarkDammed`, `dammed`.

The rule switches are public static fields set in `StartPre`; that is simple and works, and the offline suite sets them
directly. A `RuleSettings` snapshot would only pay off if two worlds ever ran in one process at once, which they do not.

The query interface to Earth is clean: Hydrology binds Earth's public methods by reflection, Earth reads one public
const back, and every rule asks through `EarthRiverQuery`. The one thing it lacks is the failure handling of finding 6.

## Plan

1. **HY-0012** (findings 1, 3): gate H2 release on the water rules, and clean up after a failing drain. Small, low
   risk, closes the only way the registry's "one feature off" promise can duplicate water.
2. **HY-0013** (findings 2, 16): drains wait for loaded land. Small; an offline case with `GameWorld.Unloaded`.
3. **HY-0015** (finding 5): IL hashes in the registry test, and level relax follows the side-feed switch. Small; worth
   having before the next game update.
4. **HY-0016** (findings 6, 11): query failures kept briefly and a breaker; per-chunk answer cache. Small.
5. **HY-0014** (findings 4, 10): search tokens for remembered answers, forgotten on any break or placement. Medium; a
   lab case first to see whether the 20 s window shows in game.
6. **HY-0019** (finding 8): breaker and save/load tests now; production H4/H7 offline later.
7. **HY-0017** (findings 7, 9, 15): coalesced wakes and scratch in the hot path, measured in a lab pair. Medium; after
   0.6.2 ships, since it changes how chutes settle.
8. **HY-0018** (findings 12-14): tidy, docs pass, and the file split above. Any time; the docs part before 0.6.2's
   release notes are final.

## Fable pass

A second, independent review of `main` at `f74df59` (0.6.2-rc.13 plus the review-fix batches), made on Claude Fable 5.2 on
14 November 2026. The review above and the three re-reviews of the fix batches ran on another model; this pass does not
take their findings or verdicts as given. All of `src/` (5,122 lines in 21 files) was read again, the engine side was
checked in the 1.22.7 decompile, and each finding below was checked against the code and, where a test was possible, by a
run. No code was changed.

`claude/review-fixes-3` (`4e43c71`, four commits) was not merged into `main` when this was written. Where that branch
already changes a finding, the finding says so.

What was run:

- `tests/offline/run.ps1` on `f74df59`: 14,475/14,475. `dotnet run --project tests/registry`: 88/88 (game 1.22.7).
- Scratch programs outside the repo, built from production `H2Filling.cs`, `H2Held.cs`, `H2Release.cs`, `H2Drains.cs`,
  `H2Dams.cs` and `H2DamPools.cs` on the offline adapter (`tests/offline/GameAdapter.cs`, watersim's engine):
  - the river dam of `RiverDamCases`, its drain running at rate 1, a block placed beside the pool: the next tick never
    returns (finding F1);
  - the same dam with part of its marked columns unloaded for 2 s: the pool's record is gone (F5);
  - a block broken beside a sea of 90,000 and of 360,000 sources: 16.4 ms and 8.6 ms per break (F3);
  - the string work of `Liquids.Handles` for a water block, as the API does it: 63 ns and 136 B per call (F4).
- Not run: the headless server labs and the game (a Hydrology session is working in the repo).

Counts: 1 critical, 2 high, 3 medium, 7 low, 5 refactor.

| # | Severity | Finding | Item |
| --- | --- | --- | --- |
| F1 | critical | A dammed river pool's fill loops for ever when its layer lies in a running drain: the server hangs | HY-0021 |
| F2 | high | H4's wake window never resets until 65,536 wakes: later edge changes wake nothing (fixed on `claude/review-fixes-3`) | HY-0017 |
| F3 | high | Every block broken beside a sea or large lake walks 65,536 sources on the main thread | HY-0022 |
| F4 | medium | `Liquids.Handles` splits the block's code on every call: about 15-25 allocations per liquid update | HY-0023 |
| F5 | medium | River pools give up on unloaded land, at run time and when a saved pool starts again | HY-0024 |
| F6 | medium | No test runs a river pool together with a drain, or a pool on half-loaded land | HY-0021, HY-0024 |
| F7 | low | The rules going off erases the saved drains and pools that were still waiting | HY-0025 |
| F8 | low | Water landing on a large body of another liquid is absorbed | HY-0025 |
| F9 | low | "A dam waits" is logged, but the dam is not queued | HY-0025 |
| F10 | low | River marks stop silently at 1,048,576 columns | HY-0025 |
| F11 | low | A river's discharge is not checked: NaN makes a pool that never fills and never ends | HY-0025 |
| F12 | low | The breaker logs only its first error | HY-0025 |
| F13 | low | `Held` reads the cell's own block six times per cell | HY-0023 |
| R1-R5 | refactor | One world reset; the release driver out of `StartServerSide`; one "can this cell take water" test; a versioned save; block facts in tables | HY-0026 |

### The earlier fixes, checked again

| Item | Holds on `main`? | Note |
| --- | --- | --- |
| HY-0012 (release follows the water rules) | yes | `WaterRulesOn` gates the break, place and tick handlers; a failing drain frees its openings. Gap: F7 |
| HY-0013 (drains wait for loaded land) | yes, for drains | `Search.Body` stops at an unloaded chunk; the saved drain waits. River pools have the same weakness: F5 |
| HY-0014 (held answers and broken walls) | yes | Search tokens, links deduped, `Kill` iterative (a chain of 60,000 survives); the liquid is in the key |
| HY-0015 (registry hashes vanilla bodies) | yes | A new game version fails until the file is committed; level relax goes off with the side-feed rule |
| HY-0016 (Earth query failures) | yes | Failing columns rest 5 s, 50 failures a minute unbind; answers per chunk, 256 chunks. The 0.5 s gap after an unbinding is closed on the branch (`4e43c71`), not yet on `main` |
| HY-0017 (H4 wake fan-out) | **no** | F2: the window added in `f74df59` never resets. The 40 ms window itself is sound (below) |
| HY-0018 (tidy) | yes | The split into `H2Held`, `H2Drains`, `H2Dams` is moves only |
| HY-0019 (tests) | partly | Breaker windows and the save round trip are tested; production H4/H7 offline is on the branch |

The 40 ms window is safe once it resets. `RegisterCallbackUnique` stores the callback at the live clock plus the liquid's
delay (`EventManager.AddSingleDelayedCallback`, `InWorldEllapsedMs => server.ElapsedMilliseconds`), replacing the cell's
pending one, and a callback fires only in a tick whose start stamp has reached that time. The shortest delay is rapid
water's 50 ms, so a cell woken at time L updates at L + 50 or later, after every change the window (L to L + 40) merged.

### F1. A dammed river pool's fill loops for ever when its layer lies in a running drain (critical)

**Where:** `src/Rules/H2DamPools.cs:157-181` (`Step`), `:195-242` (`Plan`), `:175` (the refusal); started from
`src/Rules/H2Dams.cs:69-74` (`Placed`) and the tick in `src/HydrologySystem.cs:191`.

**What happens:** `Step` runs `while (pool.credit >= 1)`. When the planned layer is used up it calls `Plan` again and
continues. A cell costs credit only when a source is placed; `Step` skips a cell that is not open, holds a source or is in
`H2Release.Draining`. `Plan` and `Part` do not know about `Draining`. So when every cell of a planned layer is one a
running drain has emptied, `Step` places nothing, `Plan` returns the same layer again, and the loop never ends. It runs on
the server's main thread inside the tick listener: the server stops responding and has to be killed; everything since the
last autosave is lost.

**How a player gets there:** a finished dam on an Earth river holds a pool with a layer over its crest (the normal end
state, decision 0010). The player breaks one block of the top course: a drain starts and lets the top layer out over
tens of seconds to minutes, the far cells first. While it runs the player places any block beside the pool's water (a
second block of a two-block breach put back, a levee block, a misclick). `Placed` marks the river and starts a new pool
(the old one ended when the drain started). On the next tick the new pool plans the layer the drain is emptying: the
emptied cells at the far end are closed in by the banks and by the sources not yet drained, so the part is closed, and
all of its cells are in `draining`.

**Evidence:** the scratch run, on production code:

```
a dam at 65,6,37 backs up the river (540 columns, surface 8, 1200 m3/s: 64 sources a second)
the dam at 65,6,37 fills layer 9 (138 cells)
the dam at 65,6,37 fills layer 10 (390 cells, over its crest)
the dam at 65,6,37 holds its pool at 9, the water running over its crest (528 sources raised)
a pond opened at 65,9,39 lets out 534 sources (about 38 s through 7 cells) ...
a dam at 50,10,39 backs up the river (384 columns, surface 8, 1200 m3/s: 64 sources a second)
the dam at 50,10,39 fills layer 10 (91 cells)        <- repeated without end
THE NEXT TICK DID NOT RETURN IN 15 s
```

The steps: the `RiverDamCases` world (crest 9), `H2DamPools.RunAll`, `DrainRate = 1`, break `(65, 9, 39)` and call
`Opened`, six seconds of `H2Release.Tick`/`H2DamPools.Tick`, place a block at `(50, 10, 39)` and call `Placed`, then one
more tick.

**Fix:**

1. One test for "this cell can take a source now" (open, no source, the pool's liquid or empty, not draining), used by
   both `Part` and `Step`. A cell a drain guards is not part of a layer; a part that touches one is open, as unloaded
   land is.
2. `Step` must make progress or stop: when a whole planned layer placed nothing, end the pool (or leave it waiting, see
   F5) instead of planning again in the same call; add a bound on plans per call as a second guard.
3. `Dammed` starts no pool over a body that is draining (`H2Release` can answer "a drain's body covers these columns"),
   and the drain's `Finish` lets a later block start it.
4. An offline case with exactly the steps above, run under a time limit.

**Risk of the fix:** low. The labs' dams (reach, query) should end the same.

### F2. H4's wake window never resets until 65,536 wakes (high; fixed on the branch)

**Where:** `src/Rules/H4Rapids.cs:347` (`static long wokenTick = long.MinValue`), `:318` (the reset test), `:324` and
`:336` (the skips). Introduced in `f74df59`.

**What happens:** `now - wokenTick` with `wokenTick = long.MinValue` overflows to a negative number (the project is not
compiled with overflow checks), so `now - wokenTick >= WakeWindowMs` is false, and `now < wokenTick` is false too. The
sets `woken` and `scanned` are therefore cleared only when `woken` passes 65,536 entries. Until then a centre is scanned
once and an edge source is woken once for the whole session: `WakeEdgesAround` returns at `scanned.Add` for any centre
seen before, and skips every source in `woken`. An edge that changes a second time (a step's lip broken and put back
twice, a basin beside a chute filled, emptied and filled) wakes nothing, so the other steps of a chute are not
re-checked and rapid edges or ordinary edges stay as they were. After the first 65,536 wakes `wokenTick` holds a real
time and the window works. The statics are per process, so a singleplayer session sees it once.

The paired labs did not show it because each lab structure is disturbed once.

**Evidence:** the arithmetic above; `claude/review-fixes-3` commit `60f5ea2` names the same defect ("the window never
reset ... found by the offline H4 case") and sets `wokenTick = long.MinValue / 2`.

**Fix:** merge `60f5ea2`. More robust than a magic start value: reset when `wokenTick < 0` or keep a `bool windowOpen`;
and clear both sets in the world reset (R1). HY-0017 stays open until that is on `main`.

**Risk:** none.

### F3. Every block broken beside a sea or large lake walks 65,536 sources on the main thread (high, performance)

**Where:** `src/HydrologySystem.cs:149-156` (every player break), `src/Rules/H2Release.cs:118-124` (a source beside the
broken cell), `:141` (`s.Body(start, s.pond, MaxBody, pos.Y)`), `:365-414` (`Body`), `MaxBody = 65536` (`:65`).

**What happens:** a player's break calls `Opened`. If a source stands beside the broken cell, `Body` collects the water
joined to it until it has 65,536 sources, and only then says "a large body of water". For the sea or a big lake that is
the full walk every time: 65,536 cells, six neighbours each, a set lookup, a chunk lookup, two solid-block reads and a
fluid read per neighbour. Nothing is remembered: the next block broken one step along the shore walks the same water
again. Earth's rivers are cheap (the first cell is river water and `Body` returns at once); seas and lakes are not.

That is the mod's own main use: digging a canal or a harbour from the sea or a lake, breaking the shore block by block.

**Evidence:** on the offline adapter (whose block reads are a plain array lookup): 16.4 ms per break beside a sea of
90,000 sources one deep, 8.6 ms beside 360,000 sources four deep. The game's reads (`GetBlock` with a layer, the chunk
lookup, `GetLiquidBarrierHeightOnSide`) cost several times more, so a break at a shore is likely to cost more than one
33 ms tick. This is an estimate: measure it in a lab with `WA_PLAYER_BREAKS=1` on a coast.

**Fix:**

1. Remember the answer. Keep the last large body's set (the search already owns it) with a time; a break whose start
   source is in it within, say, 30 s returns "a large body" at once. Forget it on the same events that forget held
   answers.
2. Decide large bodies sooner. The drain is refused anyway when the top layer would take over `UnlimitedSeconds` to let
   out (`:177`): walk the opening's level first (two dimensions) and stop at
   `UnlimitedSeconds x DrainRate x (2 x MaxRun + 1) x head` cells, a few thousand, before any walk in depth.

**Risk:** low for 1; low for 2 if the two-dimensional pass only ever answers "too large".

### F4. `Liquids.Handles` splits the block's code on every call (medium, performance)

**Where:** `src/Liquids.cs:67-72`; callers in every rule (`IsFresh` at `:75`, H1 `WouldDrain`, H3, H4, H5
`MaxNeighbourPrefix` and `After`, H7 `FallingInAir`, `TrySurvival`, `TryRelax`).

**What happens:** `Handles` calls `b.FirstCodePart()`. In the API that is `Code.Path.Split('-')[0]`
(`RegistryObject.FirstCodePart`, 1.22.7 decompile line 223): for `water-still-7` it allocates an array and three strings,
then `Handles` compares the first with three literals. One flowing-water update calls it about 15 to 25 times: the
side-feed prefix and `FallingInAir` for each neighbour (twice, vanilla's call and level relax), H1's prefix, `After`,
H3, H2, `FollowFeeder` for the cell and its five fed cells, `RecheckEdgesAbove`. A wake scan (`WakeEdgesAround`) calls
`IsFresh` on up to 765 cells.

**Evidence:** 63 ns and 136 B per call for the same string work. That is roughly 1 to 1.5 µs and 2 to 3 KB of garbage
per update, against 14 to 19 µs per update in the labs; a wake scan in open water costs up to 48 µs and 100 KB.
The branch (`7528c4e`) removes the `BlockPos` allocations and adds a flow-kind table, but `Handles` is unchanged there.

**Fix:** tables by block id built in `Liquids.Init`, beside `toRapid` and `toFalling`: `handled[]` and `fresh[]`.
`Handles(b)` becomes `b.Id < handled.Length && handled[b.Id]`. The client never calls `Init`, so keep today's code as the
fallback while the table is empty (C1 does not call `Handles`).

**Risk:** low; the registry or offline H4/H7 cases can assert the tables against the string rule for every block.

### F5. River pools give up on unloaded land (medium)

**Where:** `src/Rules/H2DamPools.cs:213` (`Plan`: `if (!Liquids.Loaded(ba, p)) return false;`), `:161-166` (a `Plan`
that returns false ends the pool), `:287` (unloaded land makes a part open), `:129-136` (a saved pool starts again when
its one block's chunk is loaded), `src/Rules/H2Dams.cs:137-185` (`MarkDammed` has no loaded check).

**What happens:**

- At run time: a pool's columns are the dam's pool and the pools upstream, up to 16,384 columns. If any of them is in a
  chunk that unloads while the pool fills (the player walks downstream of a long reach, or logs out and a second player
  keeps the dam's chunk loaded), `Plan` returns false and `Step` ends the pool with "holds its pool ... the next layer
  is open". The record is removed, so it is not saved either. The log's reason is whatever the previous part left in
  `openWhys`, not "unloaded land".
- At load: a saved pool is one block. It starts again through `Placed` as soon as that block's chunk is loaded.
  `MarkDammed` then walks only the river that is loaded (an unloaded column reads as no source), so the pool gets a
  part of its columns; `Part` meets the rest as "river columns that are not marked" or as unloaded land and calls the
  layer open. The pool ends. This is HY-0013's defect, in the pools.

A dam on a river with a step in reach is finished by H2's own rules anyway; a dam on a calm reach (the Inn case that
decision 0010 was made for) stops rising and needs another block placed.

**Evidence:** the scratch run: with the columns at x < 32 unloaded for four ticks, the log shows "the dam at 65,6,37
holds its pool at 8 (the next layer is open: water falls at 91,9,37 beside water) (0 sources raised)" and
`H2DamPools.Running` is 0; `H2Release.Save()` then returns nothing.

**Fix:** `Plan` gets a third answer, "wait": an unloaded marked column or an unloaded neighbour leaves the pool as it
is and the pool is asked again later (a few seconds, as the saved drains do). A saved pool waits until `MarkDammed`
meets no unloaded column on its walk, with the same retry and give-up as HY-0013. Log the real reason.

**Risk:** low.

### F6. No test runs a pool with a drain, or a pool on half-loaded land (medium, tests)

The offline suite has `RiverDamCases` (drain at rate 0, pools never ticked with a drain running) and
`LoadedLandCases` (drains only). `H2DamPools.RunAll` calls the same `Step`, so a case like F1 would hang the suite: run
it in a task with a time limit. Add F5's two cases with `GameWorld.Unloaded`. The headless `lab_reach` dam is built and
broken, but nothing is placed during its drain.

### Low

- **F7. The rules going off erases waiting records.** `H2Release.EndAll` clears `pending`
  (`src/Rules/H2Drains.cs:67`) and the tick then calls `H2DamPools.ResetWorld()` (`src/HydrologySystem.cs:183`), which
  clears the pools' pending list too. Saved drains and pools whose chunks had not loaded yet are gone from the next
  save, though nothing was wrong with them. Keep `pending` (only `Running` things need ending).
- **F8. Another liquid's large body absorbs the water.** `Join` asks `Body` first and returns `Absorbed` for a body of
  4,096 sources or more before it checks the liquid (`src/Rules/H2Release.cs:494-498`); the same in `Layer`
  (`:551-554`). A pond drained onto a large lava lake vanishes into it. Check `Liquids.Same(f, b)` first.
- **F9. "A dam waits".** With eight pools filling, `Dammed` logs that the dam waits and returns
  (`src/Rules/H2DamPools.cs:87-91`); nothing remembers it. Queue it, or say "place another block later".
- **F10. River marks.** `dammed` holds up to 1,048,576 columns for the session and `MarkDammed` then returns without a
  word (`src/Rules/H2Dams.cs:21`, `:140`): dams stop working until a restart. Log once, and drop the marks of pools
  that have ended and hold no raised water.
- **F11. Discharge.** `Math.Clamp(discharge * FillPerCubic, 1, FillMax)` (`src/Rules/H2DamPools.cs:98`) passes NaN
  through; the pool's credit is then NaN, `Step` never runs and the pool holds one of the eight places for ever and is
  saved for ever. Treat a discharge that is not a finite positive number as 0.
- **F12. The breaker's log.** `Breaker.Fault` logs the first error with its stack trace and then nothing until it trips
  (`src/GamePatches.cs:180-198`). A second, different error in the same feature is invisible. Log the first error of
  each exception type (a small set).
- **F13. `Held`'s reads.** `Liquids.Passes(ba, p, n, face)` reads `p`'s own solid block for each of the six faces
  (`src/Rules/H2Held.cs:162-177`; the same in `Supplied`, `FedChannel`, `Body`). Read it once per cell: about a third
  fewer block reads in the largest searches.

### Refactor

- **R1. One world reset.** Rule state is static in nine classes and is reset in four places: `StartPre` (H2Filling, the
  query, the stats), `SaveGameLoaded` (`H2Release.Load` resets drains, marks and pools), `Bind`, `Liquids.Init`. H4's
  wake sets and the breakers in `HydrologySystem` are reset elsewhere or not at all. One `Rules.ResetWorld()` called in
  `StartPre`, which calls every rule's reset, makes "what survives a world change" one list.
- **R2. The release driver.** `StartServerSide` is 110 lines; the tick lambda holds policy (unbinding, the gate,
  ending drains, three breakers). Move it to a class with `OnBreak`, `OnPlace`, `OnTick` taking the world, so the
  offline suite can drive the same code the game runs. Today the registry test reaches these pieces by reflection.
- **R3. One "can take water" test** shared by planning and placing (F1's root cause), in `H2DamPools` and the drain's
  `Step` (`src/Rules/H2Drains.cs:109`), which repeats the same condition. `H2DamPools.Opened(string)` is a log helper
  with the name of `H2Release.Opened`; call it `NoteOpen`.
- **R4. A versioned save.** `hydrology:drains` is lines of two shapes told apart by their field count. Start it with a
  version line, so a later format can be read or refused on purpose.
- **R5. Block facts in tables.** With F4, every question about a block (handled, fresh, rapid, source, flow kind,
  falling variant) is a table lookup by id in `Liquids`; `Variant[...]`, `FirstCodePart` and `LiquidCode` comparisons
  leave the rules.

### Order of work

1. F1 now: it can hang a server from ordinary play with a dam.
2. Merge `claude/review-fixes-3` (F2), then F5 with F6's tests.
3. F3 and F4, measured in a lab pair (a coast for F3).
4. The low findings and R1 to R5 with the remaining review work, before 0.6.2 is published.
