# 0002: Per-world flooding

Date: 2026-10-31. Status: implemented in Hydrology 0.1.13; coordinator build/tests and runtime verification pending.
Later: since 0.6.1 Hydrology is required on clients (`requiredOnClient: true`), so the "preserving `requiredOnClient:
false`" below is history; since 0.6.2-rc.4 an Earth world that asks for vessels only with its river query keeps them off
while the query is not bound, whatever this option says ([0004](0004-release-0.6.2.md) item 8).

## Evidence and decision

The user's suite decisions in `.local/world-options-spec.md` specify `hydrologyFlooding`: a boolean, default true,
owned by Hydrology and displayed in category `earth`. Declare it in this mod's `worldconfig.json`, retaining the
non-null empty `playStyles` list and existing bucket setting. Ship English labels with the mod's assets.

The user-facing choice is whether **communicating vessels are active**, the behavior represented by `FillVessels`.
`WorldMayFillVessels` is a legacy permission for Earth to enable that behavior, not a separate user-facing water rule.
When the new key exists, its true/false value wins over both switches and both Earth flags. Keep `Fill` as the H2
master switch; preserve all depth/containment and natural-river protections and all other rules.

Read presence and value from the server's `api.World.Config` through H2's `IWorldAccessor`. Check the new key before
the existing fast paths so explicit false also wins over a previously cached true. Do not write a default into saves
or modify/store the mod config with a world override. Missing key runs the existing path exactly:

`FillVessels || (WorldMayFillVessels && (hydrologyFillVessels || (riverQueryActive && hydrologyFillVesselsWithRiverQuery)))`

That path retains its lazy read, one-second retry and positive cache because Earth sets its flags after Hydrology
starts. New-world default true is supplied by the game's worldconfig declaration, not by a missing-key fallback.

The attribute has `onlyDuringWorldCreate: false`. Administrators may change it later and reload the world; this does
not schedule a global water update or reverse already created source water. Ordinary subsequent liquid updates use
the chosen rule. Old Earth layouts without a river query lack the natural-river exemption, so enabling flooding there
is an explicit change from the preserved fallback policy.

## Client synchronization

Inspection of this installed-build branch found **no existing config network channel**: the client's only config read
sets local rapid foam shares. Add `hydrology-world-settings` with a protobuf request and response containing the
server's effective flooding value. The client asks on `LevelFinalize`; the server answers using the same production
`VesselsActive` method. Expose the received value as `HydrologySystem.Flooding`. No client rule decides water behavior.
Send only in response to a registered client request, preserving `requiredOnClient: false`. Leave foam behavior alone.
Reference the game's protobuf assembly without bundling it. Copy language assets into build/package output.

## Verification

Extend the existing offline harness that compiles production H2: all 96 combinations of missing/false/true and the
five legacy/config/query booleans; explicit false overriding a populated legacy cache; canals with explicit true and
false; `Fill=false` still winning; natural-river protections with explicit true. Existing issue #20 cases remain.
The fake world config now distinguishes keys and presence instead of returning one value for every key.

Build and tests are left to the coordinator per the task handoff. Run `./tests/offline/run.ps1`, then build against
`.local/vs` and inspect output for `worldconfig.json` and `assets/hydrology/lang/en.json`. Runtime follow-up should
check new-world Earth-tab default/labels, old saves with absent keys and both legacy policies, explicit false with
legacy true, changed settings after reload, client/server disagreement resolved by sync, and clients without the mod.
No game, installation, player-data access, commit or push was performed for this change.
