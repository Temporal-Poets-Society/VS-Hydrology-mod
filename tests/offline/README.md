# Hydrology offline regressions

Run from the repository root, with .NET 8 SDK already installed:

```powershell
./tests/offline/run.ps1 -Baseline # expected failures with the 0.1.11 H2 source
./tests/offline/run.ps1           # fixed production H2; throws on a failure
```

No game process, game DLLs, packages or player data are needed. Generated files and isolated NuGet/CLI settings stay in
the working copy. Baseline mode links H2 from commit `37d7dad` into the same harness (with the new-source wake enabled
in both runs); it does not switch branches. Run the fixed mode last because both modes use the same build directory.

`Issue20.csproj` compiles the real `src/Rules/H2Filling.cs`. `GameAdapter.cs` supplies a small fake API and the liquid
facts for stone/air/fresh/salt water. `watersim` supplies its existing calibrated engine port and the other rule ports.
`FillOverride` routes the simulated delayed update to production H2. `WakeOnNewSource` mirrors H5's new notification;
Harmony and the actual H5 hooks are **not** exercised. The fake river query is geography fixed for each test, not Earth.

The matrix digs 24-block canals, 2/6 wide, across a hump, pit or descending staircase, fresh/salt, in one operation or
row by row. Every removed block gets neighbor updates. It checks full source filling, settling, stability after waking
all water again (ignoring flow labels), the bed, and the feed level as the maximum height. Controls cover width 1,
disabled Fill/vessels, fresh water without a query, natural river columns, unsupported sheets, air/solid/other-liquid
gaps, barriers, the 16-cell bound, and another dimension. Four existing synthetic river layouts are also woken and
have a bed block removed, checking that natural steps never acquire sources above their generated levels.

This is offline evidence of the rule defect and fix, not a dedicated-server or screenshot replay. See
[the report](../../docs/issue-20-suez-flood.md) for the runtime checks still needed.

For 0.1.13, the suite also exercises production `VesselsActive` across all 96 combinations of missing/false/true
`hydrologyFlooding`, `FillVessels`, `WorldMayFillVessels`, both legacy Earth flags and river-query availability. It
checks explicit false overriding the legacy positive cache, explicit true/false canal outcomes, the `Fill` master
switch and natural-river protection. The adapter distinguishes attribute presence and the three world keys.
Networking, serialization and world-creation UI require the coordinator's game build/runtime checks; this harness
does not emulate them. Baseline mode now also fails the new override cases, as expected for 0.1.11.

For 0.6.2 the suite adds, compiled out in baseline mode (`BASELINE`, which 0.1.11's H2 cannot build):
- enclosed pockets (`PocketCases`): a walled nook, the switch, a dry end, another liquid at the rim, the 128/129-cell
  bound, a 3-deep chain woken from its top;
- liquid-tight faces (`BarrierCases`): a source behind one feeds neither the ground rule nor a pocket. The adapter knows
  only whole solid blocks and air (`Liquids.Passes` is "neither cell solid"), so slabs, fences and partial barriers are
  covered in the game and the headless labs only;
- held levels (`HeldCases`, rc.3; `HeldTrenchAndLongCanal`, rc.4): a salt sea with land around it, a bucket emptied onto
  it, a pool two wide on a cliff over it and two sources on the land, each where the land drains to a lowland (nothing
  rises) and walled all round (the basin fills to the spring's level); a trench dug from that sea beside the open land
  fills; a canal 3 x 3 x 501 dug out whole from a reservoir and opened fills completely. The outlet cases are decided by
  the 4096-cell bound (the lowland is more open space than H2 follows), as the sea is in the game.
- supplied spaces (`PondCases`, decision 0006): a pond 7 x 7 x 2 broken 2 wide on a mound over a walled hollow fills
  nothing outside it (5,054 sources without the rule), nor onto flat land at its level; a lake of 1,628 sources fills the
  hollow;
- pond release (`ReleaseCases`, decision 0007; the rule `src/Rules/H2Release.cs` is compiled in too): the same pond,
  each break followed by `H2Release.Opened` as the server's player break event runs it: its top layer collects on the
  hollow's floor; onto flat land, or a plateau whose edge is 20 blocks off, nothing moves; onto a ledge whose edge is 5
  blocks off, the water falls and collects at the foot; the lake's top layer fills the hollow around it; over river
  water (the query), the river takes it without rising; at `PondDrainRate` 1 (`hollow-rate`, decision 0008) two opened
  cells let out 2 sources in the first second, the emptied cells stay empty, and the drain ends within 30 s with the
  same water below (the other shapes run at rate 0, all at once);
- a falling sheet fed by 2 sources fills the 2-cell basin it lands in (`DirectGuards`; decision 0008: measured at its own
  level and fed through the sheet; rc.7 refused it, measuring the open air up to the sheet's top).
- world options: a world asking for vessels only with Earth's query keeps them off while the query is not bound (rc.4).

Out-of-bounds cells read as solid here, the opposite of the game, where unloaded chunks read as air.
