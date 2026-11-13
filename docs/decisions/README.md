# Hydrology decisions

Earlier water-rule decisions are referenced in [design.md](../design.md) and the versioned server reports; the shared
Earth decisions remain their historical source. This local log begins with the playtest issue #20 investigation.

| Decision | Status | Evidence |
| --- | --- | --- |
| [0001: Fill wide sheets across uneven canal beds](0001-uneven-canal-filling.md) | Implemented in 0.1.12; runtime verification pending | [Issue #20 report](../issue-20-suez-flood.md), production H2 offline regression |
| [0002: Per-world flooding](0002-per-world-flooding.md) | Introduced in 0.1.13; 0.6.0 build/offline checks pass, client UI/network and Earth playthrough pending | Shared world-options spec, production H2 offline cases; [release verification](../release-0.6.0.md) |
| [0003: Public release and vanilla bucket settings](0003-public-release-and-vanilla-buckets.md) | 0.6.0 built; 14,315 offline checks and three native bucket controls pass | Author's 31 October publication request; [release verification](../release-0.6.0.md); replaces the old H6 default policy |
| [0004: Release candidate 0.6.2 for Earth 2.0](0004-release-0.6.2.md) | 0.6.2-rc.5 (after rc.1 to rc.4) on `main`, pushed to the private origin, installed in the author's game, not uploaded; offline, registry, headless and in-game water checks pass | Author's requests for the review and RC bakes; [release notes and verification](../release-0.6.2.md); Earth decision 0046 |
| [0005: Water stands only where it is held](0005-held-levels.md) | Bugfix in 0.6.2-rc.3, refined in rc.4; offline and headless spill labs pass; the reported spill in game stays vanilla-like | A player's Mod DB report on 0.6.1 (the sea climbed to a spilled source); `lab_spill`; offline held cases; the rc.4 audits |
