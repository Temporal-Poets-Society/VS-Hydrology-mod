# 0011: Water flooding in a world that has no Water flooding setting

Date: 2026-11-14. Status: **decided, option 3** (by the release coordinator for 0.6.2 on 14 November 2026, as the choice
that changes no behaviour; the author may still choose option 2 later). Raised by the 0.6.0 release review (S1) and
again by the code review of 14 November (finding 14, HY-0018).

## Context

Decision 0002 made Water flooding (H2's vessels case and the uneven basin) a per-world setting: the world attribute
`hydrologyFlooding`, shown on the Earth tab, default on (`worldconfig.json`), changeable after creation. When a world has
the attribute, it decides (`H2Filling.VesselsActive`). When it has none, the legacy policy applies: the mod config's
`FillVessels`, or Earth's older attributes (`hydrologyFillVessels`, `hydrologyFillVesselsWithRiverQuery`, the latter only
while the river query is bound). That keeps saves from before 0.6.0 as they were.

A world gets the attribute when it is created in the game's world creation screen. A dedicated server that creates a new
vanilla world from `serverconfig.json` without listing `hydrologyFlooding` among its world configuration does not: the
default in `worldconfig.json` is only offered by the creation screen. Such a world takes the legacy policy, which is
vessels off unless the mod config turns them on. Earth worlds are not affected: Earth sets its own attributes.

## Options

1. **Keep it** (current): a world without the attribute is treated as an old save. A server admin who wants flooding sets
   `hydrologyFlooding: true` in the world configuration (or the mod config's `FillVessels`). Nothing changes for old saves.
2. **A new world without the attribute gets the default (on).** Tell new worlds from old saves by the save's creation
   (for example, a save Hydrology has never seen: no `hydrology:drains` data and no earlier Hydrology start recorded), write
   the attribute at the first start, and leave saves that Hydrology has run on before as they are. More code; a world
   that ran an old Hydrology without ever saving drains looks new.
3. **Write the attribute at every start when it is missing**, from the legacy policy's answer: the world then shows its
   real setting in the world settings screen, and an admin can change it there. No behaviour change, and the setting
   becomes visible.

## Recommendation

Option 3 now (no behaviour change, the setting visible and editable), and the README's server section to say how to turn
flooding on for a new world. Option 2 only if the author wants vanilla dedicated servers to flood by default.

## Decision

Option 3, with the README's server section, decided by the release coordinator on 14 November 2026 for 0.6.2: it
changes no behaviour, and it makes the setting visible and editable. The author may still choose option 2 (new
dedicated-server worlds flooding by default) later; it would then apply only to worlds that have no attribute yet, which
after 0.6.2 means worlds Hydrology has never started on.

## Status

In 0.6.2 on `main`: `H2Filling.WriteMissingFlooding` runs at RunGame, after Earth's query is bound (the legacy answer may
depend on it), writes `hydrologyFlooding` with the legacy policy's answer when the world has none, and logs it once; a
world that has the attribute is left alone, and so is a world whose legacy answer needs Earth's query while the query is
not bound at that start (it would get "off" written for good; it is written at a start where the query binds; the
reviewer's re-review, low 7). The server's `World.Config` is the save's `WorldConfiguration`, so the
value is kept with the world. One consequence: once written, the world's setting decides, so changing the mod config's
`FillVessels` later no longer changes that world (`/worldconfig hydrologyFlooding` does). The README's server section
says how to have flooding in a new dedicated-server world. Offline `FloodingKeyCase`: a world without the attribute gets
the legacy answer (off, and on for an old save that asked for vessels), a second start leaves it alone, and an explicit
setting is never touched.
