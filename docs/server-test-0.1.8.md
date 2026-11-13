# Hydrology 0.1.8: Earth's river query, server test (2026-10-25)

**What changed** ([decision Earth 0027](../../Earth/docs/decisions/0027-river-flow-query-for-hydrology.md),
[study](flowquery-2026-10-25.md), [design](design.md), "Earth's river query"):
- **Binding:** Hydrology binds Earth's river query by reflection (`EarthRiverQuery`) once the game runs, keeps its
  answers per column, and declares `HydrologySystem.EarthRiverQueryVersion = 1`.
- **Where the query answers** (Earth worlds with pool layout 8, steps without sills):
  - **no vessels in river water**, always with the query;
  - **rapids by reach** (`QueryRapids`);
  - **H8, wheels on rivers** (`QueryWheels`), a postfix on `BEBehaviorMPWaterWheel.CheckWater`;
  - **vessels** are on where the world sets `hydrologyFillVesselsWithRiverQuery` and the query answers.
- **Elsewhere:** 0.1.7's rules, unchanged.

## Vanilla world, fake query

The test mod `tests/server/fakeriverquery` has Earth's mod system name. It answers the query (version 1, or 0) over
rectangles the water audit registers around chosen labs ([scenarios-query.json](../tests/server/scenarios-query.json),
`FAKE_QUERY=1`). Vessels are on. Data: [profiles](server-test-0.1.8/fake-query-v1-profiles.json),
[shape](server-test-0.1.8/fake-query-v1-shape.json).

| Lab | Query version 1 | Version 0 (as 0.1.7) |
|---|---|---|
| Step whose upper pool touches the lower one (no sill), lip broken | not river: floods (112 cells); **river: a 6..1 ramp forms (48 cells), no flood** | floods (112) |
| The same, marked rapids | **rapid ramp `R r6 … r1`, the lower pool's surface rapid** | floods |
| Canal 2 wide, 3 deep, off a 4-deep pool marked river | **100 % sources** | 100 % |
| The same with the canal itself marked river (a wrong flag) | stays flowing (vessels refused) | 100 % |
| Chute wheel, river with slope × discharge = 20 | **flowRate 21.73 = 3 × 7.24** (capped) | 7.24 |
| Chute steps 9 apart, marked rapids | **every pool's surface rapid** | ordinary |

Covered sources are left as they are: an edge source under the ramp of the step above it stays ordinary. That cannot be
seen, and the ramp above it is rapid.

**The 62 regular labs on 0.1.8 without Earth** match the 0.1.7 run:
- 3 labs differ, all known to vary or differing only in a label: a drop's ramp tail, a wheel's ramp, and a hidden source
  label under a curtain;
- wheels 14.9, 14.9 and 7.2, as before;
- updates 25.3 k (0.1.7: 25.2 k); 0 query reads;
- one tick of 1.13 s at t180, while four other servers, two of them generating Earth worlds, shared the machine.

## Earth world, layout 8

The world: Earth `earthmap-0.16.13.zip` (test build of 0027, rebuilt after its review), Cylinder World
`0.11.15-playtest.2`, Hydrology 0.1.8 with its defaults, seed 386217, 256 k. Earth set its own vessels key; the harness
set nothing. The log shows "Earth's river query version 1 bound".

**Shape test** ([scenarios-earth-shape.json](../tests/server/scenarios-earth-shape.json), `WA_WAKEALL=20`; data
[earth-l8-shape.json](server-test-0.1.8/earth-l8-shape.json)):

| Site | First update: water / labels | After the poke: water |
|---|---|---|
| Daugava ramp | 0 / 70 | 0 |
| Congo rapids | 0 / 56 | 0 |
| Congo chute | 0 / 22 | 1 |
| Rhine chute | 0 / 186 | 0 |
| Congo step | 0 / 90 | 0 |
| Victoria | 0 / 253 | 8 (the harness breaks rock under the lip) |
| Niagara | 0 / 273 | 4 (the same) |

Nothing reshapes. Only labels change, Earth's still sources taking vanilla's flow labels. At the Congo the rapids reach is
`R` across the whole pool between the steps.

**Breaks and canals** ([scenarios-earth-l8.json](../tests/server/scenarios-earth-l8.json); data
[earth-l8-breaks.json](server-test-0.1.8/earth-l8-breaks.json)). New in the harness: step edges without sills are found
too; their "sill" target is the bed under the edge column. River trenches take a depth.

| Site | Broken or dug | Water changed after |
|---|---|---|
| Congo step | the bed under the edge | 1 (the broken cell) |
| Rhine chute | the bed under the edge | 1 |
| Daugava step | the bed under the edge | 1 |
| Congo rapids | bank | 0 |
| River canal 2 wide, 2 deep, dug in (Daugava) | | **100 % sources** (56 cells) |
| River canal 3 wide, 2 deep, dug outward (Elbe) | | **100 % sources** |
| Sea canal 2 wide, 2 deep (Riga) | | **100 % sources** |
| Congo rapids: the bed under the edge, the ramp bed, the mid-pool bed, a canal 2 deep | | see below |

**Vessels in river water:** 9,342 fills refused in the 300 s. These are the sill-less steps, which the rule now keeps as
generated. The mean tick is 46.7–47.2 ms after the start, as with 0.1.7. The start burst has one tick of 0.37–0.46 s, as
before.

**Edits in the Congo rapids area were undone, twice** (runs l8c and l8d, both on the rebuilt Earth zip). The canal off
the rapids pool:
- was dug at t20, and was full of sources at t30 (l8c) and t60 (l8d);
- 30 s later its cells read dry, and at the end every block of the area was as before the dig, solid blocks included.

The broken blocks at the edge, the ramp bed and the mid-pool bed were back too.
- **Not Hydrology:** it never places solid blocks.
- **Not chunk loading:** the water audit logs every load and unload of the chunk columns near the sites, and none
  happened between t40 and the shutdown.
- **The earlier Earth zip kept them:** the same scenarios on the first 0.16.13 test build (run l8b, which also set the old
  vessels key) kept every Congo edit, and that canal filled.
- **Earth's runtime code places no blocks** (only the boat-test command).

So it looks like world generation writing into already generated columns, for example Earth's settling margin while
neighbouring columns generate. The audit's generation hook (`ChunkColumnGeneration`, world type "standard") never fired
in the Earth world, so this is not confirmed. **Handed to the Earth session.** The Congo rapids results above can't be
read until it is found.

## Review

- **Codex:** the flowing-water path of the rapids-by-reach rule did not check H4's master switch (`Rapids=false` would
  still have made flowing water rapid in rapids reaches). Fixed: the rule now checks it.
- **Codex:** nothing else found. It checked:
  - the reflection binding, including `out` parameters and the version getter;
  - the moment of binding: RunGame, after every mod's SaveGameLoaded, before water updates run;
  - the wheel patch on a protected method and field;
  - that the early return in `CheckSource` skips nothing 0.1.7 needs;
  - that 0.1.7 is unchanged where the query is inactive.
- **Own review:** the answers are geography, so they are cached per column. That way the query stays cheap (Earth's
  `RiverAt` takes 1–5 µs) even though `WantRapid` runs for each cell `Propagate` touches.

## Not verified

- The Congo rapids edits (above).
- A wheel on a real Earth river.
- A dam on an Earth river (the simulator's dam check is in the study).
- The client's view.
