# Hydrology changelog

Newest first. Verification for each release is in its release notes and server test reports under `docs/`.

## 0.6.2-rc.6 (13 November 2026)

The final release candidate for Earth 2.0.0-rc.8: the world's Water flooding setting is logged once when the game
starts (HY-0003); the server tests run against Earth 2.0 and check that rivers don't drain into caves (HY-0004,
HY-0005); docs match the trunk (HY-0006). Water behaviour is unchanged from rc.5.

## 0.6.2-rc.5 (9 November 2026)

Where Earth 2.0.0-rc.3 says its river answer for a spot is not ready yet (a
height tile still to come; rare), the water there waits, unchanged, until Earth can answer (a minute at most), instead
of losing its river handling for a moment.

## 0.6.2-rc.4 (9 November 2026)

, after a second review and an in-game water playtest: long canals dug out
whole fill again (rc.3 left their surface layer flowing), a canal joining a river to the sea fills from each end, and
filling a big basin no longer searches it again for every cell; river labels and rapid sources stay in the river's own
water (not on a bridge deck just above it), and a water wheel takes a river's power only where its rim reaches the
river; errors are counted per minute before a feature switches off, and a world started after another in the same game
applies its patches afresh.

## 0.6.2-rc.3 (9 November 2026)

Fixes a flood a player reported on 0.6.1: water spilled into the sea made the
sea climb to it and cover the land. Water now stands at a level only where something holds it there: a trench, a canal
or a walled basin still fills to the level of the water it is dug from, but water poured above the sea, a lake with an
outflow or open shores, a pool spilling over a cliff into the sea and two sources placed on open flat land no longer
raise or flood anything. A pond in a hollow closed above its surface still fills to the level of a source placed over
it, as vanilla's endless sources would ([decision 0005](docs/decisions/0005-held-levels.md)).

## 0.6.2-rc.2 (8 November 2026)

Fixes what rc.1 left: water behind a liquid-tight face (a waterlogged stair's
back) no longer feeds a fill, and a pocket is not enclosed where a block lets water through; with Earth 2.0.0-rc.2 the
river rules apply only in the river's own water (a channel on a bridge over a calm river stays ordinary water) and calm
pool sources past a steep river's ramps stay calm at 1:10; a hook that throws is logged and lets vanilla run, and after
50 errors only its feature switches off.

## 0.6.2-rc.1 (8 November 2026, release candidate for Earth 2.0)

Brings Earth's **calm rivers** (Earth decision
0046): Hydrology reads Earth's river query version 3, so every natural river column keeps the river's flow label, and a
block placed in a calm river no longer leaves still water around it; Earth 2.0 gives new worlds this river layout (11)
only with Hydrology 0.6.2. With Water flooding on, enclosed pockets fill too: a basin dug into a river's edge where it
overlaps the river, or a 1-wide nook or ditch enclosed by water (the author's Nile playtests). Calm river surfaces throw
no white specks. It also fixes, after a code review: a source poured onto lake ice drained and water spreading over ice
updated for ever; white water could appear on a calm step beside a chunk that was not loaded; lava's updates woke the
water beside them; `Rapids` or `QueryRapids` set to false also dropped the calm rivers' labels. Every game patch now
goes through one place that switches off only the affected feature, with a log line, if a game update changes its
target. See [the release notes](docs/release-0.6.2.md) and [decision 0004](docs/decisions/0004-release-0.6.2.md).

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
