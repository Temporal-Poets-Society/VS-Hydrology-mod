# Server test: Hydrology 0.1.5 on an Earth 0.16.8 world, 2026-10-24

The vanilla-world test of 0.1.5 is in [server-test-0.1.5.md](server-test-0.1.5.md). This one covers the Earth world
test that was left open there: Earth 0.16.8's drops (pool layout 5: sills under every drop, and the rule study's edge,
curtain and spill blocks) with the 0.1.5 rules.

## Runs

Each run is seed 386217, a 256k world at height 384, 300 s long, with the 31 scenarios of
[scenarios.json](../tests/server/scenarios.json). The mods are Earth `earthmap-0.16.8.zip` (`a0dd56a`), Cylinder World
0.11.14 and Hydrology `7f559ef`.

| Run | Hydrology | Vessels |
|---|---|---|
| `earth0168-hydro` | 0.1.5, default config | as the world asks (Earth sets `hydrologyFillVessels`) |
| `earth0168-vessels` | 0.1.5, `FillVessels: true` | forced on |
| `earth0168-control` | none | — |

Data: [analysis.txt](server-test-0.1.5-earth/analysis.txt), [summary.json](server-test-0.1.5-earth/summary.json).

## Results

Each scenario is described by the water around it at the start (t0) and after its pokes (t300).

| Scenario | 0.1.5 (both runs) | No Hydrology |
|---|---|---|
| Ramps below steps (`ramp`) | unchanged (1664 sources, 98 flowing) | 81 sources lost, flowing water spreads |
| Rapids poked (`rapids_poke`) | unchanged | rapid curtains and ramps mostly gone |
| Congo chute poked | 67–70 rapid edges, all stay rapid; ramps unchanged | 54 rapid / 30 ordinary; 229 ramp cells gone |
| Rhine chute poked | 42 rapid edges, all stay rapid; ramps unchanged | 30 rapid / 38 ordinary |
| Victoria fall poked | curtain 273 rapid falling cells, unchanged (2 sources moved by the stones) | curtain 266 → 104, water falling sideways |
| Wheels (Congo, Rhine) | turning in every sample (5.12 and 6.44) | Congo stops after 2 s; Rhine turns (5.12) |
| River and sea trenches 2 wide | fill completely, both dig orders | 6 cells of flowing water |
| River and sea trenches 1 wide | 6 cells of flowing water, as designed | the same |

- **Vessels forced on, Earth's steps:** 0 cells filled on water. No step, chute or ramp floods, because the sills keep the
  upper pools' lower layers away from the lower pools.
- **Tick time:**

  | Run | Mean tick | Ticks over 100 ms |
  |---|---|---|
  | `earth0168-hydro` | 46.95 ms | 2 (max 112 ms) |
  | `earth0168-vessels` | 46.88 ms | 0 |
  | `earth0168-control` | 46.93 ms | 0 |

- **Hydrology's own work:** about 31 000–35 000 liquid updates per run. 17 332 cells got the falling label, 179–231 were
  relaxed and 7 unsupported sources were drained.

## Notes

- **The world's request isn't logged.** Earth wrote `hydrologyFillVessels` into the save (found in both Earth saves).
  Hydrology's "the world asks for H2's vessels case" line, though, is checked on SaveGameLoaded before Earth has set the
  attribute, so it isn't printed. Hydrology then reads the attribute lazily on the first fill on water: that was
  confirmed in the vanilla test, where the harness set it. These Earth scenarios have no deep canal, so neither Earth
  run filled anything on water.
- **Cave openings under rivers.** Under Survival, a river source over a cave opening (no bed under it) drains into the
  cave once something wakes it. This is a bounded hole, not the whole river. It is not exercised here.
