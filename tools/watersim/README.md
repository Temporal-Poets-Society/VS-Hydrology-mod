# watersim

An offline voxel simulator of Vintage Story 1.22.7's water (`BlockBehaviorFiniteSpreadingLiquid` and the server's
event model), with Hydrology's rules and candidate rules as switches. It was used for the
[2026-10-23 study](../../docs/water-rules-study-2026-10-23.md).

The vanilla part is written from the game's behaviour, not copied. Keep decompiled game code out of this repository.

```
dotnet build -c Release
bin/Release/net8.0/watersim calibrate                       # known in-game behaviours
bin/Release/net8.0/watersim scene victoria hydrology-0.1.3  # one scene: generated, settled, after its script
bin/Release/net8.0/watersim why chute recommended           # pokes and edit sequences that do not come back
bin/Release/net8.0/watersim shape fall recommended          # where Earth's shape differs from the settled one
bin/Release/net8.0/watersim verify-shape recommended        # Earth 0.16.5's drops vs the Hydrology shape: first update and every poke
bin/Release/net8.0/watersim sweep --out <folder>            # greedy search from 0.1.3, then the heavy round
bin/Release/net8.0/watersim sweep --start "<rules>" --rounds 0 --out <folder>   # heavy round only
bin/Release/net8.0/watersim play victoria recommended --frames <folder>   # one text section per frame (--alt: the Hydrology shape)
bin/Release/net8.0/watersim features <folder>/features.md    # later targets: prototypes against the v1 rules
bin/Release/net8.0/watersim canals <folder>/canals.md        # canals by width and depth, with and without basin fill
bin/Release/net8.0/watersim export <folder>/scenes.json hydrology-0.1.3 recommended vanilla
python page/build_page.py <folder>                          # <folder>/results.html (needs scores.json, scenes.json)
```

Rule sets are `vanilla`, `hydrology-0.1.3`, `recommended`, or `hydrology-0.1.3+Switch=value,...` (fields of
[RuleSet](src/RuleSet.cs)).

| File | What |
|---|---|
| [World.cs](src/World.cs) | cells: stone, and fluid packed as kind / flow / height |
| [Sim.cs](src/Sim.cs) | callbacks (one per cell, replaced on re-register), 33 ms ticks, the 100 ms pulse, bulk commits, player edits |
| [Vanilla.cs](src/Vanilla.cs) | the spreading behaviour; `hook` comments mark where Hydrology patches it |
| [Hydrology.cs](src/Hydrology.cs) | H1–H5 of 0.1.3 and the candidate rules |
| [Scenes.cs](src/Scenes.cs) | Earth 0.16.5's columns, `SettleSteps` and `PlaceRamps`; the 12 scenes and their checks |
| [Study.cs](src/Study.cs), [Invariants.cs](src/Invariants.cs), [Sweep.cs](src/Sweep.cs) | settling, pokes, random edit sequences, scoring, the search |
| [Playback.cs](src/Playback.cs), [PlaybackScenes.cs](src/PlaybackScenes.cs) | frames over time (a frame per 100 ms pulse and per edit); the synthetic cases: partial basin, lake rim, shape comparisons, dam, bucket |
| [Features.cs](src/Features.cs), [FeatureScenes.cs](src/FeatureScenes.cs), [FeatureCheck.cs](src/FeatureCheck.cs) | the river-query stub, prototype rules for the later targets (managed bodies, basin fill, sub-levels), their scenes and the check; [Canals.cs](src/Canals.cs): the canal matrix |
| [Export.cs](src/Export.cs), [page](page) | data and template for the 3D results page with playback |
