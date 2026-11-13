# Hydrology 0.1.0: server test (2026-10-22)

This test checks Hydrology's version 1 rules (H1–H6 of Earth's
[decision 0024](../../Earth/docs/decisions/0024-hydrology-water-mod.md)) on dedicated servers. It ran on Vintage Story
1.22.7, on an Earth world and on a plain vanilla world, each against a control world without Hydrology.

Nothing was installed in the game folder and no existing save was opened. All worlds are new and live in the git-ignored
`testruns/` folder (about 0.5 GB per round of four worlds, safe to delete).

## Verdict

| Rule / scenario | Without Hydrology (control) | With Hydrology |
|---|---|---|
| **H2** 2-wide trench, 30 or 40 long, opened last, fresh and salt | 6 blocks of flowing water | **Fills to its end with source water** within 10–30 s (labs, Daugava, Baltic coast) |
| **H2** 2-wide trench dug outward, 1 row per second, fresh and salt | 6 blocks | **Fills to its end**, following the digging |
| **H2** 1-wide channel | 6 blocks of flowing water | The same: 6 blocks of flowing water |
| **H3** 1-block step opened (lab: lip removed between two pools) | 1 flowing cell; the lower pool loses 8 sources | **Ramp of flowing water 6→1** over the lower pool; the lower pool keeps all 112 sources; no source above it (no vertical face) |
| **H3** Earth river step poked (Daugava, place and remove a block) | 81 sources turn into flowing or falling water; the river restores itself within 300 s | **Nothing changes**: 98 flowing and 1 736 source cells before and after |
| **H2 + H3** 2-wide channel dug around a 1-block step | Each half carries 6 blocks of flowing water; nothing reaches the step | Upper half: sources at the upper level. Lower half: sources at the lower level. **Ramp 6→1** at the step on the lower water; no source on top of the lower half |
| **H4** 2-wide channel dug from a pool through to a 2-, 3- or 5-block drop | Water never reaches the drop (6 blocks) | **Edge cells become `rapidwater-still-7`**; both columns fall as rapid water; a rapid ramp 6→1 forms on the floor below |
| **H4** Vanilla 3 m water wheel in the fall | Never turns (no water) | **Turns** at 2-block (flowRate 9.6, 5 power cells) and 3-block drops (14.9, 5 cells), 73–74 of 75 samples |
| **H4** Wheel on Earth's rapids (Congo, Livingstone rapids) | Does not turn there (0 of 75 samples) | **Turns** in 75 of 75 samples (5.1, 2 power cells): Earth's rapids get rapid sources when touched |
| **H4** Earth 3–4 block drop poked (Congo) | The fall stays ordinary falling water | The edge becomes rapid (52 rapid sources along it) and the fall turns into rapid water |
| **H4** Drop filled back to 1 block | — (no water reaches it) | **Edge reverts to `water-still-7`**; the water below becomes an ordinary 6→1 ramp |
| **H6** A full bucket emptied on a ledge beside a 3-deep pit | Source water: it stays on the ledge and keeps the pit wet | New worlds have `noLiquidSourceTransport` on: the bucket spills `water-still-3`, which drains away. No source and no rapids |
| **H1** Sources in every pool | Lab pools and the lab step's lower pool lose sources | Every pool keeps every source in every scenario |
| **H5** Server tick time, 300 s, mean / p99 | Earth 46.96 / 49 ms; vanilla 46.92 / 50 ms | **Earth 46.96 / 49 ms; vanilla 46.92 / 50 ms**. 126 ms spent in Hydrology's rules over 300 s on Earth, 36 ms on vanilla |

The lab results are the same on the Earth and the vanilla world. They match cell for cell, except for the tip of one
rapid ramp: a level-1 cell present in one world and missing in the other.

## What changed while testing

The first two rounds found four problems. The third round (`testruns/full3`) is the one reported above.

1. **Wide rapid falls kept one column.** Vanilla rapid water spreads as a single stream (`multiplySpread: false`):
   - a cell flowing down removes neighbours of the same level;
   - a cell next to one with a better way down lowers itself.

   So a 2-wide fall kept one falling column, and the wheel beside it got 1 power cell. Two fixes:
   - H1's guard now keeps water flowing down from removing same-level neighbours (for a source, these are other
     sources).
   - H4 lets a rapid cell that is part of a sheet spread like ordinary water during its update. A sheet cell has a
     same-level neighbour or sits beside a rapid edge.

   Lone streams, such as vanilla rivulets, keep vanilla's behaviour.
2. **H6 was not applied.** The world config is filled with every mod's defaults before any mod code runs. Hydrology's
   `worldconfig.json` default lost to survival's, because the mod list is ordered by dependencies, then by id.
   Declaring a dependency on `survival` makes Hydrology's default the later one. A server's `WorldConfiguration` can
   still set the value either way.
3. **Filling over water flooded Earth's rivers.** 0024 lets a cell resting on a source fill. Earth's steps and falls
   have no lip: the upper pool's lower layer touches the lower pool's water. So a cell over the lower pool has the same
   neighbours as the top layer of a 2-deep trench. When touched, the Daugava ramp turned into 210 new sources, and the
   Congo fall filled to the top (196 001 fills in 300 s, tick p99 79 ms). **H2 now fills only on solid ground**
   (see "Decision needed" below).
4. **Rapid/ordinary flip-flop.** A cell fed both by an ordinary source and by a rapid edge flipped kind about a thousand
   times a second on the Congo: each change woke the ordinary source, which spread ordinary water into the cell again.
   A kind change now wakes only the cell itself, and the cells it feeds are updated directly. Earth-world updates fell
   from 1.08 M to 23 k in 300 s.

## Decision needed: H2 on water

0024 says a flowing cell fills when it rests "on solid ground or a source". Version 1 fills **on solid ground only**,
because Earth's lipless steps cannot be told apart from a 2-deep player trench by looking at neighbouring blocks.

What version 1 loses: a trench 2 deep fills its bottom layer, but its top layer stays flowing, 6 blocks. This was
expected from the rules, not tested.

Options for later:
- Keep "solid ground only".
- Allow filling on water where Earth's river query (0022 part A) says the water is not a natural river.
- Have Earth give its steps a lip.

## Method

| World | Mods | Purpose |
|---|---|---|
| earth-hydro | Earth 0.16.3 (`release/earthmap-0.16.3.zip`) + Cylinder World 0.11.12 + Hydrology 0.1.0 | under test |
| earth-control | Earth 0.16.3 + Cylinder World 0.11.12 | vanilla water |
| vanilla-hydro | Hydrology 0.1.0 | under test |
| vanilla-control | none | vanilla water |

- Earth worlds were 256 000 × 384 × 102 400, seed 386217, with story-location moves off. Vanilla worlds used the
  standard world type, 1 024 000 × 256 × 1 024 000, seed 386217.
- Earth's Debug build (`bin/Debug/mod`) was still 0.16.2, so the 0.16.3 release zip was used.
- All four servers of a round ran at the same time on the same machine.

The test-only helper mod [wateraudit](../tests/server/wateraudit) is moved from Earth's Realistic Water test and
extended:
- **Labs.** It builds small test basins from granite and water in the air above the terrain, so the same scenario runs
  on any world: 1 or 2-wide trenches from a pool, a 1-block step between two pools, a channel around a step, a 2-wide
  channel ending at a 2/3/5-block drop with or without a wheel, and a refill. It builds them with plain `SetBlock`
  (no updates, like world generation). Then it acts like a player:
  - `BreakBlock` for digging;
  - placing cobblestone with a neighbour update;
  - a vanilla bucket spill (set the block, wake it and its neighbours).
- **Earth sites.** It finds features on Earth's real rivers and coasts, as before: trenches from the Daugava at
  Jēkabpils and the Baltic coast near Ventspils, a step on the Daugava at Koknese, and a 4-block drop and a rapid ramp
  on the Congo's Livingstone rapids.
- **Records.** It writes short profiles of the water along each feature at 0, 10, 30, 60, 90, 120, 180 and 300 s, the
  wheels' `flowRate` every 4 s, the server tick time every 20 ms, and Hydrology's own counters (`HydrologyStats`).

[analyze.py](../tests/server/analyze.py) puts the runs side by side: [analysis.txt](server-test-0.1.0/analysis.txt) and
[summary.json](server-test-0.1.0/summary.json) for the four worlds of the third round. The bucket case with H6 in force
is in [analysis-bucket.txt](server-test-0.1.0/analysis-bucket.txt) and
[summary-bucket.json](server-test-0.1.0/summary-bucket.json): a separate vanilla pair, because the third round's
lab_bucket ran with H6 in force but the first round's did not.

To reproduce, from the project folder:
1. Build Hydrology with `dotnet build`.
2. Build the helper with `dotnet build -c Release` in `tests/server/wateraudit`.
3. Start one server per world with `python tests/server/mkrun.py <absolute run dir> <earth|vanilla> <hydro|control> <port> [scenarios file]`.
   The run dir must be absolute, because the server runs with the game folder as working directory. Earth worlds take
   `../Earth/release/earthmap-0.16.3.zip` and `../Cworld/release/cylinderworld-0.11.12.zip` (`EARTH_ZIP` and `CWORLD_ZIP`
   override them). `WA_DUMPS=10,30` shortens a run.
4. Compare the runs with `python tests/server/analyze.py <runs dir> [run ...]`.

## Side finding: Earth worlds undo builds

In some Earth runs, labs were back to the untouched terrain between two samples. The granite, water and dug cells were
all gone:
- in the control world at 30–60 s, in two rounds;
- in the Hydrology world of the second round at 10–30 s.

This never happened on vanilla worlds. The third round's Hydrology world kept every lab. The logs show nothing at those
times. Earth's Realistic Water test saw the same thing ("trenches undone in the first run", also in both worlds). The
cause was not found. It is not Hydrology's.

Lab results above therefore come from the vanilla pair and the third round's Earth Hydrology world. The Earth control's
labs are used only up to 30 s.

## Not verified

- **Client side:** how the rapid edges, sheets and ramps look, boats on the filled channels, the wheel animation, and
  the bucket default on the singleplayer world creation screen.
- **Time:** longer than 5 minutes, chunk unload and reload, and saved worlds reopened.
- **Scale:** large Earth falls (Iguazu's 8 blocks, the listed waterfalls) when touched, and many players.
- **Other rules:** salt-water drops (stay vanilla by design), and 1-block drops onto a dry floor (vanilla, 0024 open
  question 3).
- **Startup warning:** the warning for Realistic Water or Realistic Rapids was not exercised. The code checks
  `IsModEnabled("realisticwater" / "realisticrapids")`.
- **The Earth undo:** the cause of the side finding above.
