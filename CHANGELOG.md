# Hydrology changelog

Newest first. Verification for each release is in its release notes and server test reports under `docs/`.

## 0.6.2 (13 November 2026, release candidate)

Calm rivers for Earth 2.0, and water that stays where it belongs.

- **Earth's calm rivers** (Earth decision 0046). Hydrology reads Earth's river query version 3: every natural river
  column keeps the river's flow label, so a block placed in a calm river leaves no still water around it, and calm
  river surfaces throw no white specks. Earth 2.0 gives new worlds this river layout (11) only with Hydrology 0.6.2.
  The river rules apply only in the river's own water (a channel on a bridge over a river stays ordinary water); calm
  pool sources past a steep river's ramps stay calm at 1:10; a water wheel takes a river's power only where its rim
  reaches the river. Where Earth's answer for a spot is not ready yet (a height tile still to come), the water there
  waits, unchanged, for a minute at most.
- **Water flooding** (H2's vessels case, the world's setting, logged once when the game starts). Enclosed pockets fill:
  a basin dug into a river's edge where it overlaps the river, or a 1-wide nook or ditch enclosed by water. Long canals
  dug out whole fill, and a canal joining a river to the sea fills from each end.
- **Water stands only where it is held** ([decision 0005](docs/decisions/0005-held-levels.md)). A trench, a canal or a
  walled basin fills to the level of the water it is dug from; water poured above the sea, a lake with an outflow or
  open shores, a pool spilling over a cliff into the sea, and sources placed on open flat land raise or flood nothing.
  (0.6.1 let a spill make the sea climb to it and cover the land.)
- **A space fills only from water that could fill it** ([decision 0006](docs/decisions/0006-water-volume.md), config
  `SupplyFactor`). The water bounding a space, counted from its floor up, must be at least half the space, unless it is
  a natural river. A canal dug from a lake, a pool, the sea or a river fills; a bucket emptied over a closed pond does
  not raise it.
- **An opened pond lets its water out** ([decision 0007](docs/decisions/0007-pond-release.md), config `PondRelease`).
  When a player breaks a pond's rim or bank, the water above the opening runs downhill and collects where it comes to
  rest, the same amount of water; the sea, rivers and large lakes take it without rising. With no lower land within 8
  blocks of the opening, the pond keeps it. A small pond broken on high ground no longer floods the land below it.
- **Fixes.** Water behind a liquid-tight face (a waterlogged stair's back) no longer feeds a fill, and a pocket is not
  enclosed where a block lets water through; a source poured onto lake ice no longer drains, and water spreading over
  ice no longer updates for ever; no white water on a calm step beside an unloaded chunk; lava's updates no longer wake
  the water beside them; `Rapids` or `QueryRapids` set to false no longer drop the calm rivers' labels; filling a big
  basin searches it once, not again for every cell.
- **Game updates.** Every game patch goes through one registry: if an update changes a target, Hydrology logs it and
  switches off only that feature. A hook that throws is logged and lets vanilla run; errors are counted per minute
  before a feature switches off. A world started after another in the same game applies its patches afresh.

See [the release notes](docs/release-0.6.2.md) and [decision 0004](docs/decisions/0004-release-0.6.2.md).

## 0.6.1 (1 November 2026)

Is required on clients as well as the server. Earth 1.0.x is client-required and
depends on Hydrology for its rivers, so a client without Hydrology was refused when joining an Earth server and the
game's automatic download skipped Hydrology. Marking it client-required lets joining players download it. No water
rule, world format or setting changes. Manifest author is "Andris", as in the other suite mods.

## 0.6.0 (31 October 2026)

Is the first public development release, based on the 0.1.13 candidate. It keeps
the per-world **Water flooding** option and removes Hydrology's bucket-source default override. Bucket source
transport now relies entirely on vanilla's setting. **Strongly recommended:** enable **Prevent liquid source
transport by buckets** under **Survival challenges** when creating a world. Hydrology does not enable it for you
or change existing worlds' choices. See [release notes and verification](docs/release-0.6.0.md) and
[the release decision](docs/decisions/0003-public-release-and-vanilla-buckets.md).

## 0.1.13 (31 October 2026)

The Earth tab offers **Water flooding** (`hydrologyFlooding`, on by default for new
worlds). It controls communicating vessels, with the server's effective value sent to modded clients. Worlds without
the key keep their existing mod-config and Earth saved-flag behavior. Build, offline tests and runtime verification of
the original candidate are recorded in its [decision](docs/decisions/0002-per-world-flooding.md); current checks are
recorded in the [0.6.0 release notes](docs/release-0.6.0.md).

## 0.1.12 (30 October 2026)

With vessels enabled, wide salt-water sheets and fresh-water sheets outside Earth's
natural river columns fill across uneven canal beds. New sources made by vanilla also wake adjacent water, so filling
can continue on the support they provide ([issue #20 report](docs/issue-20-suez-flood.md),
[decision](docs/decisions/0001-uneven-canal-filling.md)). The original report records passing offline regressions and
blocked game-DLL access. The full mod now builds as 0.6.0; the exact Suez replay remains pending.

## 0.1.11 (26 October 2026)

On the client, rapid water throws less foam (sources a fifth, flowing water half of
vanilla's), so Earth's wide rapids no longer overrun the particle pool and pulse while you stand by them
([report](docs/server-test-0.1.11.md)).

## 0.1.10 (25 October 2026)

In Earth's rapids reaches a source keeps the river's flow label that Earth's pool layout 9 gives it, so whole rapids
look like and push as running water even after the water is disturbed ([report](docs/server-test-0.1.10.md)).

## 0.1.4 to 0.1.9

See the server test reports in `docs/` (`server-test-0.1.4.md` to `server-test-0.1.9.md`).

## 0.1.3 (23 October 2026)

No water standing in the air: stones put into a waterfall and taken out again left columns of still water there
([report](docs/server-test-0.1.3.md)).

## 0.1.2

The rapids window fits Earth 0.16.4's chutes, whose steps are 3 blocks apart ([report](docs/server-test-0.1.2.md)).

## 0.1.1

Rapids are judged by the fall over a short distance, so chutes of 1-block steps stay rapid
([report](docs/server-test-0.1.1.md)).

## 0.1.0

The version 1 rules, tested on dedicated servers ([report](docs/server-test-0.1.0.md)). Earlier reports retain their
original version labels and verification limits.
