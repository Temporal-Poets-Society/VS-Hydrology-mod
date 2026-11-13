using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Hydrology.Rules
{
    /// <summary>
    /// Not a water rule: a workaround for an engine bug that loses block edits (VintageStory-Issues#10177; Hydrology
    /// docs/upstream/chunk-pack-commit-lost-edits.md). The server packs an idle chunk on its compression thread
    /// (<c>WorldChunk.Pack</c> fills <c>blocksCompressedTmp</c>) and commits the packed copy later, mostly on the main thread
    /// (<c>TryCommitPackAndFree</c>; the chunk database thread commits too, and in singleplayer the patch also sees the
    /// client's chunks, whose refusals the counter includes), which only checks that the chunk has been idle for
    /// <c>UncompressedChunkTTL</c>. A
    /// write between the two leaves the pending copy in place, and if the commit comes late (a long compression queue) the
    /// copy made before the write is installed: the edit is gone, silently, and saved so.
    ///
    /// The patch drops a pending copy at commit if the chunk changed or was used after its pack began:
    /// - a per-chunk write count goes up on every <c>WorldChunk.MarkModified</c> (the server marks a chunk modified before it
    ///   raises ChunkDirty, so block writes, bulk commits, exchanges and lighting all count) and on every ChunkDirty event
    ///   (which adds newly loaded chunks, and still counts block writes if a game no longer has MarkModified);
    /// - <c>Pack</c>, holding the chunk's own pack lock, notes the count and the time it began;
    /// - <c>TryCommitPackAndFree</c>, holding the same lock through the commit, drops the copy (and refuses the commit) if
    ///   the count has moved, or if the chunk was read or written since a second before the pack began (vanilla's
    ///   lastReadOrWrite; a margin, since writes happen outside the lock). The chunk then stays unpacked and is queued again
    ///   at a later scan. With no pending copy, or a pack this patch did not see, vanilla's commit runs unchanged.
    ///
    /// Not covered (as in vanilla): a write by another thread still under way while the main thread commits, and worldgen
    /// writing into an already loaded chunk without marking it. Gameplay edits run on the main thread, which also commits.
    ///
    /// When it applies (config <c>ChunkPackFix</c>): "auto" (default) on game versions up to 1.22.x, read from the running
    /// game; "on" on any version; "off" never. The rule is a version cap rather than detecting the game's own fix, which
    /// cannot be checked reliably from its code shape; the patch is harmless beside a fixed engine (it only drops copies of
    /// chunks that changed after packing began), so a game that fixes the bug in 1.22.x loses nothing. Missing or changed
    /// targets: logged once, not patched (<see cref="GamePatches"/>).
    /// </summary>
    public static class EngineChunkPackFix
    {
        public const string IssueUrl = "https://github.com/anegostudios/VintageStory-Issues/issues/10177";
        /// <summary>A read or write this long before the pack began still drops its copy (writes happen outside the lock).</summary>
        public const int MarginMs = 1000;

        static AccessTools.FieldRef<object, object> lockRef;
        static AccessTools.FieldRef<object, long> lastReadOrWriteRef;
        static AccessTools.FieldRef<object, byte[]> blocksTmp, lightTmp, lightSatTmp, fluidsTmp;

        /// <summary>Per chunk: the write count (ChunkDirty), and the count and time noted when its last pack began.</summary>
        sealed class Stamp
        {
            public long Writes;
            public long PackWrites;
            public int PackTick;
            public bool Packed;
        }
        static readonly ConditionalWeakTable<object, Stamp> stamps = new();

        const string Feature = "the chunk pack fix (VintageStory-Issues#10177)";

        /// <summary>Patches the engine if the config and the game call for it, through <see cref="GamePatches"/>; never throws.
        /// Independent of the water rules: it guards every block edit, not only water's.</summary>
        public static void Apply(Harmony harmony, ICoreServerAPI api, string mode, ILogger log)
        {
            mode = (mode ?? "auto").Trim().ToLowerInvariant();
            if (mode is "false" or "no" or "0" or "disabled") mode = "off";
            else if (mode is "true" or "yes" or "1" or "enabled") mode = "on";
            else if (mode is not ("off" or "on" or "auto"))
            {
                log.Warning("Hydrology: config ChunkPackFix \"{0}\" is none of \"auto\", \"on\" or \"off\"; using \"auto\"", mode);
                mode = "auto";
            }
            if (mode == "off") { log.Notification("Hydrology: chunk pack fix off (config ChunkPackFix)"); return; }
            Breaker.Reset(log);
            try
            {
                string running = GamePatches.RunningGameVersion();
                if (mode == "auto" && (!Version.TryParse(running.Split('-')[0], out var version) || version.Major > 1 || version.Minor > 22))
                {
                    log.Notification("Hydrology: chunk pack fix not applied on game {0} (\"auto\" applies up to 1.22.x); set ChunkPackFix to \"on\" if {1} is still open",
                        running, IssueUrl);
                    return;
                }
                var t = GamePatches.TypeByName(log, Feature, "Vintagestory.Common.WorldChunk");
                if (t == null) return;
                var lockR = GamePatches.Field<object>(log, Feature, t, "packUnpackLock");
                var lastR = lockR == null ? null : GamePatches.Field<long>(log, Feature, t, "lastReadOrWrite");
                var blocksR = lastR == null ? null : GamePatches.Field<byte[]>(log, Feature, t, "blocksCompressedTmp");
                var lightR = blocksR == null ? null : GamePatches.Field<byte[]>(log, Feature, t, "lightCompressedTmp");
                var lightSatR = lightR == null ? null : GamePatches.Field<byte[]>(log, Feature, t, "lightSatCompressedTmp");
                var fluidsR = lightSatR == null ? null : GamePatches.Field<byte[]>(log, Feature, t, "fluidsCompressedTmp");
                if (fluidsR == null) return;
                // bound before the patches: the compression thread may enter them at once
                lockRef = lockR; lastReadOrWriteRef = lastR;
                blocksTmp = blocksR; lightTmp = lightR; lightSatTmp = lightSatR; fluidsTmp = fluidsR;
                var me = typeof(EngineChunkPackFix);
                // writes the ChunkDirty event does not report (exchanges, lighting); without it the fix still counts
                // ChunkDirty and checks vanilla's lastReadOrWrite
                GamePatches.Apply(harmony, log, "the chunk pack fix's count of MarkModified writes",
                    GamePatches.On(t, "MarkModified", Type.EmptyTypes, typeof(void), me, postfix: nameof(MarkModifiedPostfix)));
                if (!GamePatches.Apply(harmony, log, Feature,
                        GamePatches.On(t, "Pack", Type.EmptyTypes, typeof(void), me, prefix: nameof(PackPrefix), finalizer: nameof(PackFinalizer)),
                        GamePatches.On(t, "TryCommitPackAndFree", new[] { typeof(int) }, typeof(bool), me, prefix: nameof(CommitPrefix), finalizer: nameof(CommitFinalizer))))
                    return;
                api.Event.ChunkDirty += (coord, chunk, reason) =>
                {
                    if (chunk == null || Breaker.Off) return;
                    try { Interlocked.Increment(ref stamps.GetValue(chunk, _ => new Stamp()).Writes); }
                    catch (Exception e) { Breaker.Fault(e); }
                };
                log.Notification("Hydrology: chunk pack fix applied on game {0} ({1}): a packed chunk copy is dropped if the chunk changed or was used after packing began",
                    running, IssueUrl);
            }
            catch (Exception e)
            {
                log.Warning("Hydrology: chunk pack fix not applied: {0}", e.Message);
            }
        }

        // Its runtime breaker (GamePatches.Breaker): an error in these hooks, on the main thread, the compression thread or
        // while saving, is logged and leaves vanilla's pack and commit as they are; the finalizers still release the lock
        static readonly GamePatches.Breaker Breaker = new(Feature);

        /// <summary>A write the ChunkDirty event does not report (exchanges, lighting): MarkModified.</summary>
        static void MarkModifiedPostfix(object __instance)
        {
            if (Breaker.Off) return;
            try { Interlocked.Increment(ref stamps.GetValue(__instance, _ => new Stamp()).Writes); }
            catch (Exception e) { Breaker.Fault(e); }
        }

        // the compression thread (and saving): note the write count and the time, holding the chunk's pack lock through the
        // pack, so the note belongs to the copy this pack makes
        static void PackPrefix(object __instance, out bool __state)
        {
            __state = false;
            if (Breaker.Off) return;
            try
            {
                var gate = lockRef(__instance);
                if (gate == null) return;
                Monitor.Enter(gate, ref __state);
                var s = stamps.GetValue(__instance, _ => new Stamp());
                s.PackWrites = Interlocked.Read(ref s.Writes);
                s.PackTick = Environment.TickCount;
                s.Packed = true;
            }
            catch (Exception e) { Breaker.Fault(e); }
        }

        static Exception PackFinalizer(object __instance, bool __state, Exception __exception)
        {
            if (__state) Monitor.Exit(lockRef(__instance));
            return __exception;
        }

        // the commit (the main thread, and also the chunk database thread when it packs and commits a chunk itself, and in
        // singleplayer the client's chunks): hold the chunk's pack lock through the commit, and drop a copy the chunk has
        // moved on from
        static bool CommitPrefix(object __instance, ref bool __result, out bool __state)
        {
            __state = false;
            if (Breaker.Off) return true;
            try { return Commit(__instance, ref __result, ref __state); }
            catch (Exception e) { Breaker.Fault(e); return true; }
        }

        static bool Commit(object __instance, ref bool __result, ref bool __state)
        {
            var gate = lockRef(__instance);
            if (gate == null) return true;
            Monitor.Enter(gate, ref __state);
            if (blocksTmp(__instance) == null || !stamps.TryGetValue(__instance, out var s) || !s.Packed) return true;
            bool written = Interlocked.Read(ref s.Writes) != s.PackWrites;
            // Environment.TickCount wraps: compare as int (vanilla stores the int in a long)
            bool used = unchecked((int)lastReadOrWriteRef(__instance) - s.PackTick) > -MarginMs;
            if (!written && !used) return true;
            blocksTmp(__instance) = null;
            lightTmp(__instance) = null;
            lightSatTmp(__instance) = null;
            fluidsTmp(__instance) = null;
            s.Packed = false;
            Interlocked.Increment(ref HydrologyStats.PackCommitsRefused);
            __result = false;
            return false;
        }

        static Exception CommitFinalizer(object __instance, bool __state, Exception __exception)
        {
            if (__state) Monitor.Exit(lockRef(__instance));
            return __exception;
        }
    }
}
