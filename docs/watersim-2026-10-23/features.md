# Later targets against the version-1 rules

## Pond behind a dam (pond)

A river in a canyon, upper pool at 8, lower river at 7. A dam goes across at x 17 up to 10, then 100 s pass. The pond should rise to the crest and spill over it.

| Rules | Failed checks | Water in the air | Updates | Stopped | Note |
|---|---|---|---|---|---|
| Hydrology 0.1.3 | 2 | 0 | 170 | yes | pond reaches the crest: no water at the crest's level behind the dam; water spills over the dam: nothing below the dam above the river |
| Recommended | 2 | 0 | 170 | yes | pond reaches the crest: no water at the crest's level behind the dam; water spills over the dam: nothing below the dam above the river |
| Recommended + prototype | 0 | 0 | 1412 | yes |  |
|   + ProtoLocalOff (fills and relax off where it drains) | 0 | 0 | 1298 | yes |  |
|   − Survival | 0 | 0 | 1412 | yes |  |
|   − FallLabel | 0 | 0 | 1652 | yes |  |
|   − FallingFeedsSideways | 0 | 0 | 1403 | yes |  |
|   − WakeOnFallLabel | 0 | 0 | 1396 | yes |  |
|   − LevelRelax | 0 | 0 | 1298 | yes |  |
|   − RefillOnSource | 0 | 0 | 1412 | yes |  |
|   − H2Fill | 0 | 0 | 1476 | yes |  |
|   − VanillaSourceRule | 0 | 0 | 1412 | yes |  |
|   − H4Rapids | 0 | 0 | 1170 | yes |  |
|   − H1DrainGuard | 0 | 0 | 1554 | yes |  |
|   − H3SpreadOnLowerWater | 0 | 0 | 1100 | yes |  |

## Diversion drying (diversion)

Pools at 10, 9 and 8. A channel at 10 is dug through the canyon wall from the upper pool around the middle pool to the lower river; then a dam at x 10 closes the middle pool's inflow, and 100 s pass. The middle pool's own water (above the lower river's level) should dry; the rest stays.

| Rules | Failed checks | Water in the air | Updates | Stopped | Note |
|---|---|---|---|---|---|
| Hydrology 0.1.3 | 1 | 0 | 323 | yes | middle stretch dries: 48 middle sources left at 9 |
| Recommended | 1 | 0 | 739 | yes | middle stretch dries: 48 middle sources left at 9 |
| Recommended + prototype | 0 | 0 | 1194 | yes |  |
|   + ProtoLocalOff (fills and relax off where it drains) | 0 | 0 | 996 | yes |  |
|   − Survival | 0 | 0 | 1194 | yes |  |
|   − FallLabel | 0 | 0 | 1612 | yes |  |
|   − FallingFeedsSideways | 0 | 0 | 1557 | yes |  |
|   − WakeOnFallLabel | 0 | 0 | 1160 | yes |  |
|   − LevelRelax | 0 | 0 | 961 | yes |  |
|   − RefillOnSource | 0 | 0 | 1194 | yes |  |
|   − H2Fill | 0 | 0 | 1193 | yes |  |
|   − VanillaSourceRule | 0 | 0 | 1194 | yes |  |
|   − H4Rapids | 0 | 0 | 778 | yes |  |
|   − H1DrainGuard | 2 | 0 | 9488 | yes | middle stretch dries: 42 middle sources left at 9; lower river stays: 52 of 54 lower sources |
|   − H3SpreadOnLowerWater | 0 | 0 | 743 | yes |  |

## Dam failure (dam-failure)

The pond scene; 60 s after the dam is built its middle (2 wide, bed to crest) is broken out, then 80 s pass. The pond should drain through the gap to the river's own level.

| Rules | Failed checks | Water in the air | Updates | Stopped | Note |
|---|---|---|---|---|---|
| Hydrology 0.1.3 | 1 | 0 | 491 | yes | pond is full before the dam breaks: no pond at the crest when the dam breaks |
| Recommended | 1 | 0 | 464 | yes | pond is full before the dam breaks: no pond at the crest when the dam breaks |
| Recommended + prototype | 0 | 0 | 2794 | yes |  |
|   + ProtoLocalOff (fills and relax off where it drains) | 0 | 0 | 2672 | yes |  |
|   − Survival | 0 | 0 | 2679 | yes |  |
|   − FallLabel | 0 | 0 | 2808 | yes |  |
|   − FallingFeedsSideways | 0 | 0 | 2605 | yes |  |
|   − WakeOnFallLabel | 0 | 0 | 2609 | yes |  |
|   − LevelRelax | 0 | 0 | 2604 | yes |  |
|   − RefillOnSource | 0 | 0 | 2794 | yes |  |
|   − H2Fill | 0 | 0 | 2644 | yes |  |
|   − VanillaSourceRule | 0 | 0 | 2739 | yes |  |
|   − H4Rapids | 0 | 0 | 2226 | yes |  |
|   − H1DrainGuard | 0 | 0 | 2869 | yes |  |
|   − H3SpreadOnLowerWater | 0 | 0 | 2021 | yes |  |

## Lock beside a weir (lock)

Upper river at 10, lower at 7, a 3-block weir at x 15 (z 7–9). A 2-wide chamber (x 12–14, z 4–5) between gates at x 11 and x 15. The upper gate opens at 3 s and closes at 33 s; the lower gate opens at 37 s and closes at 67 s.

| Rules | Failed checks | Water in the air | Updates | Stopped | Note |
|---|---|---|---|---|---|
| Hydrology 0.1.3 | 1 | 0 | 912 | yes | chamber fills to the upper level: 0 of 6 chamber sources at 10 before the upper gate closes |
| Recommended | 1 | 0 | 782 | yes | chamber fills to the upper level: 0 of 6 chamber sources at 10 before the upper gate closes |
| Recommended + prototype | 0 | 0 | 1046 | yes |  |
|   + ProtoLocalOff (fills and relax off where it drains) | 0 | 0 | 1046 | yes |  |
|   − Survival | 0 | 0 | 1046 | yes |  |
|   − FallLabel | 0 | 0 | 1129 | yes |  |
|   − FallingFeedsSideways | 0 | 0 | 1052 | yes |  |
|   − WakeOnFallLabel | 0 | 0 | 1046 | yes |  |
|   − LevelRelax | 0 | 0 | 990 | yes |  |
|   − RefillOnSource | 0 | 0 | 1046 | yes |  |
|   − H2Fill | 0 | 0 | 1010 | yes |  |
|   − VanillaSourceRule | 0 | 0 | 1018 | yes |  |
|   − H4Rapids | 0 | 0 | 455 | yes |  |
|   − H1DrainGuard | 1 | 0 | 2822 | yes | rivers keep their levels: 18 river sources lost |
|   − H3SpreadOnLowerWater | 0 | 0 | 720 | yes |  |

## Long run over flat ground (long-run)

A pool on a ledge, walled at its east side. The wall is broken at 2 s; the water spills one block down onto a plain 11 wide and 68 long and runs east as far as its levels allow.

| Rules | Failed checks | Water in the air | Updates | Stopped | Note |
|---|---|---|---|---|---|
| Hydrology 0.1.3 | 0 | 0 | 79 | yes | water runs 6 blocks over the plain |
| Recommended | 0 | 0 | 93 | yes | water runs 6 blocks over the plain |
| Recommended + prototype | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   + ProtoLocalOff (fills and relax off where it drains) | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − Survival | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − FallLabel | 0 | 0 | 531 | yes | water runs 48 blocks over the plain |
|   − FallingFeedsSideways | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − WakeOnFallLabel | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − LevelRelax | 0 | 0 | 534 | yes | water runs 48 blocks over the plain |
|   − RefillOnSource | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − H2Fill | 1 | 3 | 3711 | **no** | no water standing in the air: 3 hanging, 0 standing, 0 unsupported sources; water runs 0 blocks over the plain |
|   − VanillaSourceRule | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − H4Rapids | 0 | 0 | 525 | yes | water runs 48 blocks over the plain |
|   − H1DrainGuard | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |
|   − H3SpreadOnLowerWater | 0 | 0 | 529 | yes | water runs 48 blocks over the plain |

## Power from drop × flow (power)

The fall with a wheel cell in the curtain. A stone goes into the curtain at 3 s and out at 6 s. Power is read from the river query (drop and discharge), not from the water blocks.

| Rules | Failed checks | Water in the air | Updates | Stopped | Note |
|---|---|---|---|---|---|
| Hydrology 0.1.3 | 0 | 3 | 81 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
| Recommended | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
| Recommended + prototype | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   + ProtoLocalOff (fills and relax off where it drains) | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − Survival | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − FallLabel | 0 | 3 | 132 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − FallingFeedsSideways | 0 | 0 | 130 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − WakeOnFallLabel | 0 | 0 | 136 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − LevelRelax | 0 | 0 | 204 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − RefillOnSource | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − H2Fill | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − VanillaSourceRule | 0 | 0 | 259 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − H4Rapids | 0 | 0 | 27976 | **no** | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − H1DrainGuard | 0 | 0 | 2319 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |
|   − H3SpreadOnLowerWater | 0 | 0 | 143 | yes | drop 8 blocks (query: 16 to 8), discharge 1200 m³/s: 94 MW at full scale; wheel cell holds rapidwater-d-6 |

