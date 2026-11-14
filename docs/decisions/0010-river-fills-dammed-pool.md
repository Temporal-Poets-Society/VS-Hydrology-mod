# 0010: A river fills the pool behind a player's dam from its own flow

Date: 2026-11-14. Status: in release candidate **0.6.2-rc.13** (HY-0011), not pushed, not installed; checked offline,
in the headless labs and in game on the AgentBridge bench. Code: `src/Rules/H2DamPools.cs`, called from
`H2Release.Placed` (after `MarkDammed`) and stopped by `H2Release.Opened`; config `DamFill`, `DamFillPerCubic`,
`DamFillMax`. Offline: `RiverFlowDamCases`.

## Context

Decision 0009 (rc.11, rc.12) lets water stand above an Earth river's generated surface behind a player's dam, but H2
fills a level only from water standing above it. A dam across a calm pool therefore raised its pool only near a step, as
high as the next pool up, and only once a block woke the step's ramp: on the Inn at Innsbruck (a pool of 120+ blocks
with no step) a dam raised nothing, and below the Rhine falls a dam 15 blocks from the step woke nothing until a block was
placed beside the ramp. Nothing ever ran over a river dam's crest, so a river dam made no rapids for a wheel, which was
the author's wish (13 November). Earth's decision 0022 (part B2-B3) proposed a pond record whose level rises by the
reach's discharge and spills at the lowest point of its rim; Earth decision 0024 gave part B to Hydrology. The author
(14 November): "Then build and test in game", after the proposal and the questions on performance ("big performance
issue?") and ownership ("hydrology or Earth?").

## Decision

1. **A record per dammed pool.** A block a player places beside a natural river starts a record of the pool it marks
   (decision 0009 point 4: the pool beside the block and the pools upstream, up to 16,384 columns), or adds to the record
   already filling that pool. Earth's query is unchanged: its discharge (`RiverAt`, m³/s) gives the fill rate.
2. **The river's flow fills it.** The record takes `DamFillPerCubic` (0.1) sources a second per m³/s of the river's
   discharge, from 1 to `DamFillMax` (64): the Rhine at Schaffhausen (359 m³/s) 36 a second, the Inn at Innsbruck (154)
   15. Water is placed layer by layer above the pool's level, as sources.
3. **A layer is filled only where it is closed.** The open cells at the next height joined to the marked columns,
   resting on ground or on water, are walked to their end. A part that reaches a river column that is not marked and is
   open under the layer (the river running on below the dam, a lower pool), a cell with nothing under it reached over
   water, or unloaded land is open and not filled; more than 16,384 cells at one height ends
   the record. A closed part is filled and the pool rises a block.
4. **One layer over the crest.** A part whose only openings are at most 64 cells with nothing under them reached from
   cells resting on ground (the dam's crest, a low bank) is the water over the crest: it is filled once and the record
   ends. That water runs over the crest as vanilla's flowing water and falls past the dam; where it drops two blocks or
   more it is rapid water (H4), which a wheel needs.
5. **Low banks are an outlet.** A dam whose crest stands as high as the river's banks floods the plain in reality;
   Hydrology does not flood open land, so such a pool does not rise (the Inn's banks stand at its water's level). Banks
   or walls a player builds along the reach close it.
6. **Opening the dam ends it.** Breaking the dam starts a drain (decisions 0008, 0009); the record of a pool the drain
   takes water from ends, and the raised water drains back into the river.
7. **Bounded and saved.** At most 8 records fill at once (a ninth dam waits for a new block); a record rises at most 32
   blocks; it pauses while the block that started it is in an unloaded chunk; records are saved with the world (the
   block that started each, marked again when its chunk loads). Nothing runs once a pool is full or open.

## Consequences

- A river dam works anywhere its reach is enclosed, with no step needed. Offline (a calm river with no step, banks two
  above the water, then three): a 1-high dam fills 384 cells and 390 over the crest, a 2-high dam two layers and 390 over
  the crest; nothing below the dam; a dam standing already (built at once) and finished by one hand-placed block the same
  (the first in-game build missed this: the dam's own columns counted as the river running on); breaking the top course
  lets out 774 and a 2-high dam keeps the 384 its lower course holds.
- In game (own bench, Earth main `6371e32` as 2.0.0-rc.13-hydam.1, 1:10): below the Rhine falls (levees to 149, a dam to
  148) the river filled the reach and a layer over the crest (266 cells) in about 4 s, and it fell past the dam as rapid
  water; breaking two blocks of the top course let 508 sources out in about 21 s through the 12-cell overflow, and the
  reach went back to its own level with its labels and the step's ramp. On the Inn the same dam with low banks raised
  nothing; with walls one block above the water around 28 blocks of the reach, the river filled 186 cells and a layer
  over the crest (324) in about 20 s, with rapid water under the crest.
- Cost: offline, planning and placing a whole pool (774 to 1,158 cells) takes 0.3 to 7 ms in all; in game it is spread
  over the seconds the river takes to fill it (at most 64 block writes a second per pool, 8 pools). Each layer walks at
  most 16,384 cells once. Still water and full pools cost nothing.
- Not done: a vanilla water wheel was not built on the bench (seven construction stages); dams that give way (Earth 0022
  B4), drying below a diversion (B5); the run-off of a lake's drain along its channel (HY-0011's second point).
