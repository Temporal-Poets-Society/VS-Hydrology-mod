# 0004: Release candidate 0.6.2 for Earth 2.0: calm rivers, review fixes, one patch registry

Date: 2026-11-08. Status: **released as 0.6.2** (14 November 2026, built on `main` after rc.13 and the code review's fixes;
the tag, push and uploads are the author's). Earlier: release candidate **0.6.2-rc.5**, baked on branch `claude/rc-0.6.2`, now Hydrology's `main` (pushed to
the private origin; not uploaded to the Mod DB), after rc.1 and rc.2 the same evening, rc.3 on 9 November (item 7), rc.4 (item 8) and rc.5
(item 9) the same day; see [the release notes](../release-0.6.2.md) for its checks. The Earth side of item 6 is on Earth branch
`claude/rc-2.0-hydrology-leftovers`, merged into Earth's `claude/rc-2.0` at `d50f2ae` (Earth 2.0.0-rc.2), with which
rc.2 was installed in the author's game on 9 November. The Earth RC session then installed rc.4 with Earth
2.0.0-rc.3 (Earth `release/1.0` `0b34942`, which requires 0.6.2-rc.4) and, on the author's "Yes, install rc.5", rc.5
the same evening (rc.4 kept in `VintagestoryData\Backups\hydrology-before-0.6.2-rc.5-20261109-202137`): the author's
game now runs Earth 2.0.0-rc.3, Hydrology 0.6.2-rc.5 and Cylinder World 1.0.4.

## Context

The author is preparing Earth 2.0.0, with Cylinder World and Hydrology as its two required companion mods, and asked
for the same review, tidy-up and release-candidate bake for Hydrology that Earth 2.0.0-rc.1 had: audits by area, Codex's
correctness and design reviews, an adversarial pass, fixes held to the current build's behaviour except where something
is a bug, game-update proofing, verification and a baked RC.

Earth 2.0.0-rc.1 requires Hydrology 0.6.1 or newer (river query version 2), and gives new worlds its calm rivers (pool
layout 11, [Earth decision 0046](../../../Earth/.claude/worktrees/rc-2.0/docs/decisions/0046-calm-rivers.md)) only with
query version 3, which the calm-rivers work (`claude/calm-rivers`, 0.6.2-rivers.7, the author's playtest build) provides.

## Decision

1. **The 0.6.2 line** is `release/0.6` (0.6.1) with `claude/calm-rivers` merged: river query version 3 (every river
   column keeps the river's flow label), enclosed pockets (`FillPockets`), no white specks on calm river water
   (`CalmSourceSpecks` 0). Version `0.6.2-rc.1`, a pre-release the Mod DB accepts.
2. **The contract Earth reads is unchanged**: `Hydrology.HydrologySystem.EarthRiverQueryVersion`, a public const int, now
   3; Earth 2.0's `RequiredMods` (`RiverQueryMinimum` 2) and layout choice read it by reflection. Its doc now says what
   Earth needs of it. Hydrology reads Earth's `RiverQueryVersion`, `RiverAt` and `RiverFlowAt` as before; a newer world
   version is read at Hydrology's own, with a warning.
3. **One registry for the game's code** (`src/GamePatches.cs`, the author's "future proof for game updates"): every
   Harmony patch and every reflection on a non-public game member goes through it. A feature names its targets by exact
   signature; all are resolved before anything is patched; a missing or changed target is logged once and switches off
   only that feature (the water rules as a whole; the label, side-feed and wake refinements; H8; C1; the chunk-pack fix).
   Before, one missing method left the water rules half-patched and silently skipped H8 and the chunk-pack fix.
4. **Refactors keep behaviour**; bugs are fixed in separate, labelled commits (listed in the release notes). Byte-identity
   is checked offline (the H2 suite's scenario lines, update counts included); the headless server benches are not
   deterministic run to run (5 to 10 water cells at the drop labs' ramp tips, 20 to 30 labels, out of 38,750 columns
   between two runs of the same build), so a refactor is accepted there when its difference stays within that noise.
5. **What rc.1 left was then fixed in rc.2** (the author, 8 November: "fix the leftovers. old save does not matter with
   this release"):
   - Old singleplayer saves without the Water flooding key get it set to true by vanilla's Customize screen (audit D1):
     old saves do not matter for this release, so nothing is written into them; [0002](0002-per-world-flooding.md) stands.
   - H2 counted a source behind a liquid-tight face, and the pocket search took a block water passes into for a wall
     (Codex): both fixed (water must pass between the cell and its feeder; such a block is a leak).
   - The pocket wake under every waterfall's first cell (audit A2) was narrowed to cells that can fill, then backed
     out: its lab run ended two deep canals' rapid tongues 1-2 cells short, and while later baseline runs showed the
     same variation between themselves (so the evidence is weak either way), the narrowing saved no measurable time.
     The wake stays as in rc.1.
   - A hook that throws (audit E5): every hook now runs inside its feature's runtime breaker (logged, vanilla's way for
     that call, the feature off after 50 errors).
6. **Earth side** (Earth branch `claude/rc-2.0-hydrology-leftovers`, from `claude/rc-2.0` `bd9dbf2`):
   - Earth needs Hydrology 0.6.2-rc.1 or newer with query version 3 (`HydrologyMinimum`, `RiverQueryMinimum` 3), so
     every new world gets the calm rivers (audit C4).
   - The query's rapids come from the columns as the generator settles them for drawing: at 1:10 the model's raw ramp
     heights ran up to 4 columns past the drawn ramps, and Hydrology turned those calm pool sources rapid on the first
     update (a bench finding, probed with the harness's new QueryProbe).
   - Earth offers `RiverSpanAt` (bed and water surface), and Hydrology applies its river rules only in the river's own
     water (audit B4/C1): a channel on a bridge over a calm river keeps vanilla's labels. No query version change: an
     optional method, found by its signature; without it Hydrology answers at any height, as before.

7. **rc.3** (9 November): a player's report on 0.6.1, passed on by the author ("old version comment by user"): a source
   spilled into the sea made the sea climb to it. H2 now fills only where the level is held,
   [decision 0005](0005-held-levels.md).

8. **rc.4** (9 November; the author, through the Earth RC session: "Go through one more time through review cycle for
   Earth, Hydrology and Cylinder World. Also using /codex as before." and "playtest especially by playing with water"):
   five read-only audits by area, Codex's correctness and adversarial reviews, and a water playtest in the game on the
   AgentBridge bench (Earth 2.0.0-rc.2 as installed, Cylinder World 1.0.4, a new 1:10 world). Fixed in labelled
   commits: the held check's limits, memory and other-liquid walls ([0005](0005-held-levels.md)); source rules and
   wheels within the river's span, failed spans asked again, a reversed span ignored; errors counted per minute; patches
   applied afresh in a new world; one try per feature; vessels off while an Earth world's query is not bound; H3 reads a
   cell's own floor; the offline suite's baseline mode builds again. Earth's side went to the Earth RC session.
9. **rc.5** (9 November; the Earth RC session's proposal, the author: "ok do it"): Earth 2.0.0-rc.3 throws
   `RiverQueryNotReadyException` from `RiverAt` where its drawn answer is not ready yet. Hydrology knows it by its type
   name: no failure is logged, the column's water updates wait (vanilla's too) and are tried again every second, a
   source there keeps its label, for a minute at most. Before, the column counted as no river for that update. Earth
   2.0.0-rc.3 requires 0.6.2-rc.4 or newer, so it accepts rc.5.

## Consequences

Hydrology 0.6.2 runs Earth 2.0's calm rivers; earlier layouts and saves behave as with 0.6.1. A game update that
changes one of Hydrology's targets now costs that feature, with a warning naming it, instead of a half-patched mod.
