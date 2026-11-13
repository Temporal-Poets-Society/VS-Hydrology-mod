# Hydrology (`hydrology`)

Water mechanics built for Earth's rivers: dug channels fill, one-block steps become smooth ramps, drops of two or
more become rapids that turn water wheels, and deep canals fill when Water flooding is on. Vanilla blocks only.
Suite rules: `C:\VS mods\AGENTS.md`. Current state: the card `C:\VS mods\overview\mods\hydrology.md`. Open issues:
`tools\Update-Backlog.ps1 -Mod hydrology` from `C:\VS mods` (prefix HY). Decisions: `docs\decisions\` (index
`README.md`); Earth's side: Earth decisions 0024, 0027, 0046.

## Repository
- Trunk `main`, tracking the private `origin` (`andrisberzins-1/VS-Hydrology-mod`); no public repo. Published
  versions are tags (`v0.6.0`, `v0.6.1`, both on the Mod DB); old lines are `archive/*` tags.

## Build
- `dotnet build` gives `bin\Debug\mod`, which the server tests and the registry test use.
- `dotnet build -c Release` also writes `release\hydrology-<version>.zip` from a fixed file list. It refuses to
  overwrite an existing zip: bump the version, or pass an explicit `-p:PackageFile=<path>`.
- Game path: `-p:VintageStoryPath`, `VINTAGE_STORY` or `%APPDATA%\Vintagestory`.

## Test
- Offline: `.\tests\offline\run.ps1` (the production H2 rule against dug canals; no game needed).
  `-Baseline` runs the old H2 and fails on purpose; run the fixed mode last, both share one build folder.
- Registry: `dotnet run --project tests\registry` after a Debug build. It applies every patch to the installed
  game's DLLs in a test process (65/65 at rc.5); run it after a game update.
- Server: `python tests\server\mkrun.py <absolute run dir> <earth|vanilla> <hydro|control> <port> [scenarios]`.
  Needs the Debug build and `tests\server\wateraudit` built with `-c Release`. Options are environment variables
  listed at the top of the script (`EARTH_ZIP`, `CWORLD_ZIP`, `PLAY_STYLE=earth-10`, `WA_BEDSCAN`, `FAKE_CW`, ...).
  Earth worlds take the newest Earth and Cylinder World zips from the player's `VintagestoryData\Mods` and the
  player's `ModData\earth` (glo90 linked as a junction). Runs (0.3-1 GB each) go in `testruns\` (git-ignored);
  `tests\server\analyze.py` compares runs.
- In game: the AgentBridge bench with the Earth, Cylinder World and Hydrology zips on an Earth play style. Pouring
  a bucket needs Ctrl held (`press ControlLeft`, then `click right`); breaking many blocks with `hover` and
  `click left` crashed the client ("Given pos contained NaN"), so keep such runs short and save-tolerant.

## Release
- Bump `version` in `modinfo.json`; add the entry at the top of `CHANGELOG.md` (newest first) and the
  verification in `docs\release-<x.y.z>.md`; update the README where behaviour changed. Build Release, commit,
  tag `vX.Y.Z` on `main`.
- Earth's `modinfo.json` names the minimum Hydrology version (river query 3): release together with Earth's
  session when that changes.

## Gotchas
- Remove a server run only with `python tests\server\mkrun.py --remove <run dir>`, which unlinks the glo90
  junction first; deleting the folder recursively can follow it into the player's 8.5 GB of heights.
- The river query contract with Earth (`EarthRiverQueryVersion`, version 3) is checked at start: change its shape
  only with an Earth decision.
- All patches and reflection go through `src\GamePatches.cs`, which switches off only the broken feature.
- Keep `ChunkPackFix` until the engine fix is verified (repro material in `docs\upstream\`).
- Water stands only where something holds it (decision 0005) and fills a space only from water that could fill it
  (decision 0006): never let spilled water raise the sea or open lakes, or a small pond flood the land below it. A pond a
  player opens moves its water downhill at the break (decision 0007, `H2Release`); lab breaks run it only through
  WaterAudit's `PlayerBreak` or `WA_PLAYER_BREAKS=1`, because `BreakBlock` does not raise the player break event.
- Realistic Water and Realistic Rapids are not compatible (Hydrology warns at start).
