# Hydrology 0.6.0 — first public development release

Prepared 31 October 2026 from the 0.1.13 candidate (`10ce503`). The public version is a release milestone, not a
completion percentage. Built for [Earth's rivers](https://mods.vintagestory.at/earth); standalone gameplay is not
tested for this release.

## Release notes

- Water mechanics for Earth's rivers: source filling, smoother one-block drops, rapids and working mill races.
- Earth's river query preserves rapid flow directions and increases turning water-wheel torque by slope and discharge.
- Deep canal and basin filling, including supported uneven sea-canal beds and freshwater canals outside Earth's
  natural river columns when the compatible query is available.
- Per-world **Water flooding** under the Earth settings tab; enabled by default for new worlds. Reload after changing
  it. Worlds without the key keep their prior policy.
- Client foam reduction for broad rapids; clients remain optional for multiplayer water rules.
- **Bucket source transport is entirely a vanilla setting.** Hydrology's previous default override is removed.
  Strongly recommended: enable **Prevent liquid source transport by buckets** under **Survival challenges**.
- GPL-3.0 license text included in the package.

## Scope and compatibility

Requires Vintage Story 1.22 and the built-in Survival module. Intended for use with a compatible Earth release.
Do not combine with Realistic Water or Realistic Rapids. Earth is not added as a loader dependency; this does not
establish standalone gameplay support. River-query version remains 2. No player save or installation is changed.

Prior released/local test ZIPs remain untouched. Managed ponds, dam failure, complete downstream drainage after
diversion, and dedicated sluice/lock systems remain future goals.

## Verification

- Full Release build against Vintage Story **1.22.7**: **0 warnings, 0 errors**.
- Production H2/offline regressions: **14,315/14,315 assertions pass**, covering canals, natural-river protections and
  per-world flooding overrides. The offline harness uses a fake game API and does not exercise networking or the UI.
- Three isolated dedicated-server bucket/config probes, all exit **0**, no server errors:

  | Vanilla bucket setting | Full bucket spill |
  | --- | --- |
  | Default, no explicit override | `water-still-7` (source) |
  | Explicit false | `water-still-7` (source) |
  | Explicit true | `water-still-3` (flowing water) |

- Final ZIP inspected: version **0.6.0**, expected client/server flags, no `noLiquidSourceTransport` declaration,
  only the preserved `hydrologyFlooding` world option, no H6 reporter in the assembly, GPL-3.0 LICENSE present, and
  no bundled game/library DLLs. Publication HTML structure and the corrected Earth/bucket wording pass checks.
- Package: [`release/hydrology-0.6.0.zip`](../release/hydrology-0.6.0.zip), SHA-256
  `c48a1272b711d0dc840eac0fe0c204787cc769616bc7e3bd0cd0b41b72d455d2`.

The [verification receipt](verification/release-0.6.0.json) retains the probe results and package details. These are
synthetic server controls of bucket/config behavior, not evidence of standalone gameplay compatibility. New client
UI/network checks, a current Earth world playthrough and broader mod combinations remain pending. No installation,
player save edit, graphical-client playthrough or ModDB upload was performed. Historical reports retain their
original limitations.
