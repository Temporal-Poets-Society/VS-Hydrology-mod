# 0003: Public release and vanilla bucket settings

Date: 2026-10-31. Status: implemented for 0.6.0; see [release verification](../release-0.6.0.md).

The author approved **0.6.0** for the first public development release, requested release notes and a push to the
existing private GitHub repository. The source basis is candidate `10ce503`, including the 0.1.10 river-flow labels,
0.1.11 foam changes, 0.1.12 uneven-bed filling and 0.1.13 per-world flooding option. Main is fast-forwarded to that
candidate before the release changes; earlier ZIPs and other worktrees are preserved.

Publication text must lead with the water mechanics for [Earth's rivers](https://mods.vintagestory.at/earth).
Standalone gameplay outside Earth has not been tested for this release. Earlier synthetic vanilla controls remain
valid historical evidence, but do not establish current standalone gameplay support. No Earth loader dependency is
added by this wording change and the existing reflection fallback remains; the advertised/tested scope is distinct
from the manifest's declared dependencies.

The supplied screenshot shows vanilla's **Prevent liquid source transport by buckets** unchecked. Inspection found
no independent bucket-blocking mechanic: Hydrology declared `noLiquidSourceTransport=true` in `worldconfig.json`,
and `H6Buckets` only logged the resulting world setting. At the author's request, remove that declaration, the
reporting-only class and its call. Do not replace them with a bucket patch or rewrite world/server configurations.
Vanilla's own setting and asset patch decide source transport. **Strongly recommend enabling that setting manually**
under Survival challenges; do not claim Hydrology enables or enforces it.

Keep the independent `hydrologyFlooding` setting and its default unchanged. Release number and publication wording
do not change river-query version 2, saved pool layouts, block IDs or the other water rules. Include GPL-3.0's LICENSE
in the package. Preserve existing releases and player saves; no installation or ModDB upload is requested here.
