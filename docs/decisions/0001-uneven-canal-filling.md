# 0001: Fill wide sheets across uneven canal beds

Date: 2026-10-30. Status: implemented in Hydrology 0.1.12, offline verified; game build/server/client checks pending.

Issue #20 is the Suez creative canal test (31.1 N, 32.4 E). See the [investigation and evidence](../issue-20-suez-flood.md).

## Evidence and owner

Hydrology owns the demonstrated defect. H5 patches vanilla's delayed water update; H2 creates sources and decides
which supported water can fill. At a downward change in bed height, the landing water can have no same-level source,
so H2's ground rule never seeds the lower layer. The old vessels rule needs an existing source below and cannot seed
it either. Removing either the depth guard or the containment guard alone is not the solution. The offline case
schedules updates for every removed block, and remains incomplete after waking all water again. Linking 0.1.11's
actual H2 into the same test yields 32 failing assertions; the fix yields none.

A second mechanism is notification: vanilla's new sources can become support for already settled neighbors. H5
previously rechecked H4 edges on this transition but did not notify those neighbors. The test port reproduces a few
remaining flowing cells with the new H2 alone; a notification on source creation resolves them.

The 0.1.9 chunk-pack workaround drops stale compressed copies. It neither changes source criteria nor schedules water
updates. The offline defect has no chunks, compression, or reverted solids; that workaround remains unchanged.

## Decision

Extend H2 when vessels are enabled: a cell supported by ground or a same-liquid source may fill from a source square
at its own level or up to 15 cells above. The intervening vertical column must already contain that same liquid and
permit passage. The square has four wet cells, with two adjacent source cells along the side feeding the column.
All four square edges must permit passage. The landing cell becomes a source and uses the existing SetAndWake path;
the remaining basin fills from that support through normal delayed updates.

Apply this to salt water and, where Earth's river query is bound, to fresh water in dimension 0 outside natural river
columns. Keep fresh-water behavior without the query unchanged: those older worlds rely on the original depth and
containment guards to preserve river steps. Keep the original vessels path and all its guards. Also notify neighbors
when vanilla creates a source during H5's delayed update. This transition happens once; it adds no periodic scan.

## Consequences and limits

- Wide sea canals fill over humps, pits and descending beds; fresh constructed canals can do the same with Earth's query.
- A single falling stream, unsupported water, disconnected liquid, barriers, and natural river columns cannot seed
  through this rule. Fill/vessels switches retain control. No new config, block type, saved state or integration API.
- Work per attempt is bounded by 16 vertical cells and four square orientations. Runtime allocation/timing on a large
  canal still needs measurement; the offline update counts are not engine performance measurements.
- This does not supply missing WorldEdit notifications or implement general managed ponds/dam drainage. A stalled
  pre-existing scene needs an initial water update after upgrading; normal new edits supply it.
- Exact screenshot/save reproduction remains unverified. The report distinguishes the proven rule defect from the
  unproven possibility of a separate WorldEdit notification failure.
