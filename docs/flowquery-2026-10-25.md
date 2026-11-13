# Rules that read Earth's river flow query: simulator study (2026-10-25)

**The question** (the user, via the Earth session): Hydrology reads Earth's planned per-column river query
([decision 0022](../../Earth/docs/decisions/0022-river-damming-and-diversion.md) part A; proposal in Earth's
`docs/research/river-mods-comparison-2026-10-25.md`). Can that replace three things?
- Earth's sills: no vessels in natural river water.
- Patchy rapids: rapid water across a whole steep stretch, by the reach's average slope.
- Wheels: power from slope × discharge.

Simulator only; no mod code. Tools: `watersim query` (table), `query canals`, `query edits`, `query show=…`,
`query spec=…`; `WATERSIM_SILLS=0 WATERSIM_QUERY_RIVER=1` for verify-shape and flood on the older scenes.

## The rules

All are local to the cell being updated. Each reads only its own column of the query.

| Rule | Where it hooks | Condition | Effect |
|---|---|---|---|
| **No vessels in a river** (`QueryNoVessels`) | H2, the vessels case | the query calls the cell's column natural river | no vessels fill (everything else in H2 unchanged) |
| **Rapids by slope** (`QueryRapids`), sources | H4 `CheckSource`, after a source's update | the query marks the column as a rapids reach | the source, if it has no water of its liquid above it, becomes rapid; a rapid source there is never turned back. Covered sources are left alone. |
| **Rapids by slope**, flowing water | H4 `FollowFeeder` / `WantRapid` | the same | flowing water there is rapid |

Outside rapids columns H4 works as before, with the window and the edges. Vessels outside river columns work as in 0.1.7,
with depth and containment.

## The query Hydrology needs

| Field | Used by | Notes |
|---|---|---|
| `River` (bool): the column is natural river water as Earth generated it | the vessels case | banks, dug canals and ports are not river |
| `Rapids` (bool): the reach's average slope is at least the threshold | H4 for sources and flowing water | constant along a reach, see below |
| `Slope`, `Discharge` | wheels (hook only) | not used by the water rules |
| `FlowX`, `FlowZ` | not needed by these rules | |

**How often it is read:** 1.3–1.8 query reads per water update in the reaches studied. That is one read per H4 check on
a source or a flowing cell, and one per vessels check. The server's tests run 20–100 k water updates in 300 s, so the
query must answer in O(1). For example, Earth could keep a per-chunk-column cache filled when the chunk column is
generated or loaded. Hydrology reads it only for fresh water, on the server, inside an update that already runs.

**Rapids by the reach's slope, not a sliding window.** A first stub averaged steps over a sliding 96-column window. Halfway
between two steps 90 apart, the window held both steps (2/96, above 1 in 50), and it put a strip of rapids into the
middle of a calm pool. Earth should give the slope of the reach the column belongs to, constant along it. That is the
HydroRIVERS segment, or here the pool between two steps. Rapids columns then begin and end at steps or at reach ends.
The threshold used here is 1 block in 50 columns. It makes the Congo at 256 k (steps 35–46 apart) rapids, and leaves
steps 80–90 apart calm.

## The shape Earth should draw (no sills)

As layout 1/3, with the Hydrology shape:
- 1-block steps where the valley falls, and pools at their natural depth;
- the step's edge column as deep as its pool, with **no sill**;
- the 6-to-1 ramp below each step; spills from ramp height 2 where steps are 5 or fewer apart.

Then the rapids marks, by the query:
- **In a rapids column:** the top source of each pool column is a rapid source (`R`, its label kept; `R:e` at a step's
  edge), and the ramp is rapid.
- **A ramp follows the step's edge above it:** it is rapid when the edge column is in a rapids reach, even where the pool
  below is calm (the reach's last step).
- **A step's edge in a calm column above a rapids pool** (the reach's first step) stays ordinary (`S:e`), unless H4's
  window makes it rapid (`watersim rapidrule`).

The Congo-like reach (steps 40, 35, 46, 38 apart, 10 wide, pools 3 deep), middle row
([spec-congo.md](flowquery-2026-10-25/spec-congo.md)). Every row is the same after waking every cell:

| x | bed top | water top | query rapids | cells from bed + 1 up |
|---|---|---|---|---|
| 62–63 | 9 | 12 | | S S S (calm lake) |
| 64 | 9 | 12 | | S S S:e (first step's edge: calm column, ordinary) |
| 65–70 | 8 | 11 | yes | S S S r6:e … S S S r1:e (rapid ramp) |
| 71–103 | 8 | 11 | yes | S S R (rapid surface sources) |
| 104 | 8 | 11 | yes | S S R:e (step edge, as deep as its pool, no sill) |
| 105… | 7 | 10 | yes | S S S r6:e … (next ramp) |
| … | | | | the same for each step |
| 223 | 5 | 8 | yes | S S R:e (the reach's last step edge) |
| 224–229 | 4 | 7 | | S S S r6:e … r1:e (ramp rapid after its edge, over a calm pool) |
| 230… | 4 | 7 | | S S S (calm) |

Steeper stretches (steps 5 apart, [spec-steep.md](flowquery-2026-10-25/spec-steep.md)) look as in the
white-water study, without sills: `S S R:e r2:e` at the edge, `S S S r6:e rd1` after it (the source under r6 relabels,
hidden).

## Evidence

**Reaches** ([query.md](flowquery-2026-10-25/query.md)). Rules: recommended plus 0.1.7's vessels. Compared: layout
6/7 style with sills and no query, against no sills with the query rules. For each reach:
- first update (every cell woken);
- 150 pokes (place and take away a block, or the reverse);
- single breaks, 1 in 10 wet blocks;
- 2-deep holes (the block and the one under it).

| Reach | Rules | First update | Pokes changing it | Single breaks adding water | 2-deep holes adding water | Rapid surface in rapids columns |
|---|---|---|---|---|---|---|
| Congo-like, steps 40 / 35 / 46 / 38, 3 deep | sills, 0.1.7 | 0 / 0 | 0 | 0 | 0 | 0 % |
| | **no sills, query** | 0 / 0 | 0 | 0 | 0 | **100 %** |
| the same, tapered to 1 at the banks | sills, 0.1.7 | 0 / 0 | 0 | 0 | 1 (**254 cells**) | 0 % |
| | **no sills, query** | 0 / 0 | 0 | 0 | 0 | **100 %** |
| 40, then 5 / 5 / 5 / 5, then 40 (tapered) | sills, 0.1.7 | 0 / 0 | 2 | 1 (6) | 0 | 27 % |
| | **no sills, query** | 0 / 0 | 0 | 0 | 0 | **100 %** |
| 12 / 25 / 8 / 30, 6 wide, 2 deep (tapered) | sills, 0.1.7 | 0 / 0 | 0 | 2 (6) | 1 (**200**) | 9 % |
| | **no sills, query** | 0 / 0 | 0 | 0 | 0 | **100 %** |
| gentle: 90 / 80 (tapered) | sills, 0.1.7 | 0 / 0 | 0 | 0 | 1 (**694**) | (no rapids) |
| | **no sills, query** | 0 / 0 | 0 | 0 | 0 | (no rapids) |

In calm columns, 4 % of the surface is rapid: the ramp after the reach's last step.

**The older scenes without sills** ([older-scenes.txt](flowquery-2026-10-25/older-scenes.txt)):
- **With the query rules** (every water column marked river, none rapids): step, chute, fall and Victoria stay as
  generated, with 0 pokes changing them, and no break in the flood scenes (step, chute, diagonal step, tapered step and
  chute) adds water.
- **0.1.7 without sills and without the query:** the chute changes 20 cells on the first update, 38 pokes change it,
  and 4 never stop. Breaks flood up to 132 cells. The sills are what holds them today.

**Canals** ([canals.md](flowquery-2026-10-25/canals.md)): 2, 3 and 6 wide, 1–3 deep, off a lake and off a river (the
lake's columns marked as river), all 100 % sources with the query rules, as with 0.1.7. The canal's cells are not river.

**Player edits in the Congo-like reach, no sills, query rules** ([edits.md](flowquery-2026-10-25/edits.md)):
- **Canal off a pool into the bank** (3 wide, 3 deep): fills to the pool's level, 100 % sources; the river is unchanged.
- **Bypass canal** joining the pool above a step to the pool below it, along the bank:
  - the river does not rise;
  - 60 river cells change: flowing water fans out where the bypass enters the lower pool, as the water should.
- **Dam across the river** (stone to one above the surface): the river around it is unchanged. Hydrology version 1
  builds no pond; that needs the managed bodies of 0022 part B.
- **Dam removed 30 s later:** the gap fills to the same level. 18 sources under the surface come back as rapid water
  instead of ordinary, which cannot be seen.
- **A row of stones across a rapids pool, then taken away:** no change.
- **Rapid surface in rapids columns after every edit:** 100 %.

## Wheels by the query: the hook

The vanilla wheel's behaviour (`BEBehaviorMPWaterWheel`) keeps its power in private fields. The server harness already
reads them every second (`flowRate`, `suitablePowerSourceBlockCount`, `blocked`). The clean hook is a postfix on the
method that sets `flowRate` (the name must be checked against the game DLL when building):
- in a natural river column (by the query), scale or replace `flowRate` from slope × discharge;
- elsewhere, leave vanilla's value.

That is one query read per wheel per power tick. It needs no water rule.

## Remaining risks

- **Earth's river flag must be exact.** A natural river column wrongly marked "not river" falls back to vessels with
  depth and containment, which are 0.1.7's protection, not the sills'. A bank column wrongly marked "river" refuses a
  canal's fill there.
- **Lakes are not river** in this study, so vessels still work in them, with depth and containment. Lakes have no steps,
  apart from a lake's outflow, whose ramp cells are in river columns.
- **Whole reaches of rapid water** push boats and swimmers along their entire length, not only near steps. That is the
  look the user wants, but navigation through rapids changes with it.
- **Old worlds** with sills keep them. Without the query (no Earth, or an older Earth), Hydrology behaves as 0.1.7, and
  sill-less steps are unsafe there. So Earth must draw no sills only where Hydrology can read the query, for example
  when Hydrology is present and a query version is agreed.
- **Not verified:** a server world (the query needs Earth code), the client's look, and the real wheel method name.
