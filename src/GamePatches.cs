using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Hydrology
{
    /// <summary>
    /// The one place where Hydrology reaches into the game's own code: every Harmony patch, and every reflection on a
    /// non-public game member, goes through here. (Earth's river query is Earth's public interface and is bound in
    /// <see cref="EarthRiverQuery"/>.) A game update may rename, move or change any of these targets. Each feature names
    /// all the targets it needs; they are all looked up, by name, exact parameter types and return type, before any of
    /// them is patched, so a feature is patched whole or not at all. A missing or changed target is logged once, naming
    /// the feature it switches off, and only that feature is off: nothing throws, and the rest of Hydrology runs on.
    /// </summary>
    public static class GamePatches
    {
        // HY-0018: the one invariant every rule's scratch (statics, [ThreadStatic] fields, H2Release's reused search, H4's
        // wake sets) relies on: Hydrology's server code runs on the server's main thread, one liquid update, event or tick
        // at a time (checked against 1.22.7: liquid updates are main-thread callbacks; the break and place events and the
        // tick listener run there too). Debug builds (the labs) check it and log the first call from another thread.
        static int mainThread;
        static ILogger threadLog;
        static int threadWarned;

        /// <summary>The server's main thread is the calling one (the first tick).</summary>
        public static void SetMainThread(ILogger log)
        {
            mainThread = Environment.CurrentManagedThreadId;
            threadLog = log;
            threadWarned = 0;
        }

        /// <summary>Debug builds: logs, once, a call from a thread other than the server's main thread.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void AssertMainThread(string where)
        {
            if (mainThread == 0 || Environment.CurrentManagedThreadId == mainThread) return;
            if (System.Threading.Interlocked.Exchange(ref threadWarned, 1) == 0)
                threadLog?.Error("Hydrology: {0} ran on thread {1}, not the server's main thread ({2}); the rules' scratch assumes the main thread", where, Environment.CurrentManagedThreadId, mainThread);
        }
        /// <summary>One patch of a feature: a method of the game and Hydrology's prefix, postfix and finalizer for it
        /// (static methods of <see cref="Patches"/>, by name).</summary>
        public sealed class Hook
        {
            public Type Type;
            public string Method;
            public Type[] Args;
            public Type Returns;
            public Type Patches;
            public string Prefix, Postfix, Finalizer;

            public override string ToString() =>
                $"{Type?.FullName ?? "?"}.{Method}({string.Join(", ", Args.Select(a => a.IsByRef ? "ref " + a.GetElementType().Name : a.Name))})";
        }

        /// <summary>A hook on <paramref name="method"/> of <paramref name="type"/> (declared there, not inherited) with exactly
        /// these parameter types and return type.</summary>
        public static Hook On(Type type, string method, Type[] args, Type returns, Type patches,
            string prefix = null, string postfix = null, string finalizer = null) =>
            new() { Type = type, Method = method, Args = args, Returns = returns, Patches = patches, Prefix = prefix, Postfix = postfix, Finalizer = finalizer };

        /// <summary>
        /// Patches every hook of <paramref name="feature"/>, or none. False (logged once) when a target is missing or
        /// changed, or when Harmony refuses a patch (a parameter the patch binds by name was renamed); the hooks already
        /// patched for this feature are then removed again.
        /// </summary>
        public static bool Apply(Harmony harmony, ILogger log, string feature, params Hook[] hooks)
        {
            var targets = new MethodInfo[hooks.Length];
            for (int i = 0; i < hooks.Length; i++)
            {
                var h = hooks[i];
                MethodInfo m = null;
                try { m = h.Type == null ? null : AccessTools.DeclaredMethod(h.Type, h.Method, h.Args); }
                catch (Exception) { } // an ambiguous match counts as changed
                if (m == null || m.ReturnType != h.Returns)
                {
                    Off(log, feature, h + (m == null ? " was not found" : " returns " + m.ReturnType.Name + ", not " + h.Returns.Name));
                    return false;
                }
                targets[i] = m;
            }
            var done = new List<(MethodInfo target, MethodInfo patch)>();
            try
            {
                for (int i = 0; i < hooks.Length; i++)
                {
                    var h = hooks[i];
                    var prefix = PatchMethod(h, h.Prefix);
                    var postfix = PatchMethod(h, h.Postfix);
                    var finalizer = PatchMethod(h, h.Finalizer);
                    harmony.Patch(targets[i], prefix: Wrap(prefix), postfix: Wrap(postfix), finalizer: Wrap(finalizer));
                    foreach (var p in new[] { prefix, postfix, finalizer }) if (p != null) done.Add((targets[i], p));
                }
                return true;
            }
            catch (Exception e)
            {
                foreach (var (target, patch) in done)
                    try { harmony.Unpatch(target, patch); } catch (Exception) { }
                Off(log, feature, "patching failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// A public game field the feature's own compiled code reads or writes directly (not through reflection). A game
        /// that no longer has it would fail only when that code first runs, inside the game's tick, so it is checked here
        /// before the feature is patched. False (logged once) when it is missing or of another type.
        /// </summary>
        public static bool HasField(ILogger log, string feature, Type type, string name, Type fieldType, bool isStatic)
        {
            var f = AccessTools.Field(type, name);
            if (f != null && f.FieldType == fieldType && f.IsStatic == isStatic) return true;
            Off(log, feature, type.FullName + "." + name + (f == null ? " was not found" : " is not a" + (isStatic ? " static" : "n instance") + " field of type " + fieldType.Name));
            return false;
        }

        /// <summary>A public game method the feature's own compiled code calls directly (see <see cref="HasField"/>).</summary>
        public static bool HasMethod(ILogger log, string feature, Type type, string name, Type[] args, Type returns)
        {
            MethodInfo m = null;
            try { m = AccessTools.Method(type, name, args); } catch (Exception) { }
            if (m != null && m.ReturnType == returns) return true;
            Off(log, feature, On(type, name, args, returns, null) + (m == null ? " was not found" : " returns " + m.ReturnType.Name));
            return false;
        }

        static MethodInfo PatchMethod(Hook h, string name) =>
            name == null ? null : AccessTools.DeclaredMethod(h.Patches, name) ?? throw new MissingMethodException(h.Patches.FullName, name);

        static HarmonyMethod Wrap(MethodInfo m) => m == null ? null : new HarmonyMethod(m);

        /// <summary>A reference to the game's field <paramref name="name"/> of <typeparamref name="T"/> (any visibility, declared
        /// there or inherited) of type <typeparamref name="F"/>; null (logged once) when it is missing or of another type.</summary>
        public static AccessTools.FieldRef<T, F> Field<T, F>(ILogger log, string feature, string name)
        {
            var f = FindField(log, feature, typeof(T), name, typeof(F));
            if (f == null) return null;
            try { return AccessTools.FieldRefAccess<T, F>(f); }
            catch (Exception e) { Off(log, feature, typeof(T).FullName + "." + name + ": " + e.Message); return null; }
        }

        /// <summary><see cref="Field{T, F}"/> for a game type known only at run time (an internal type).</summary>
        public static AccessTools.FieldRef<object, F> Field<F>(ILogger log, string feature, Type type, string name)
        {
            var f = FindField(log, feature, type, name, typeof(F));
            if (f == null) return null;
            try { return AccessTools.FieldRefAccess<object, F>(f); }
            catch (Exception e) { Off(log, feature, type.FullName + "." + name + ": " + e.Message); return null; }
        }

        static FieldInfo FindField(ILogger log, string feature, Type type, string name, Type fieldType)
        {
            var f = type == null ? null : AccessTools.Field(type, name);
            if (f != null && f.FieldType == fieldType && !f.IsStatic) return f;
            Off(log, feature, (type?.FullName ?? "?") + "." + name + (f == null ? " was not found" : " is not an instance field of type " + fieldType.Name));
            return null;
        }

        /// <summary>A game type by its full name (an internal type of the engine); null (logged once) when it is gone.</summary>
        public static Type TypeByName(ILogger log, string feature, string name)
        {
            var t = AccessTools.TypeByName(name);
            if (t == null) Off(log, feature, name + " was not found");
            return t;
        }

        /// <summary>Errors a feature's hooks may throw within <see cref="FaultWindowMs"/> before the feature is switched off.</summary>
        public const int MaxFaults = 50;
        /// <summary>rc.4 (audits): the window the faults are counted in. Counted over a whole session, rare errors on a server
        /// that runs for days added up to 50 and switched the water rules off.</summary>
        public const long FaultWindowMs = 60000;

        /// <summary>
        /// A feature's runtime breaker. Its hooks run their work inside a try and hand an exception here, then let vanilla's
        /// code have its way for that call: an exception escaping a hook would end the server's whole tick (logged as fatal,
        /// the rest of the tick skipped) on every call. The first error is logged with its stack trace; after
        /// <see cref="MaxFaults"/> within a minute the feature is switched off until the world restarts (logged once), which
        /// a game update that breaks a hook at run time reaches within seconds, and scattered odd cases never do.
        /// </summary>
        public sealed class Breaker : Rules.ReleaseDriver.IFaults
        {
            public readonly string Feature;
            readonly Action onOff;
            ILogger log;
            int faults, total;
            long windowStart;
            volatile bool off;

            /// <param name="onOff">Run once when the feature is switched off (a rule switch that must agree with vanilla).</param>
            public Breaker(string feature, Action onOff = null) { Feature = feature; this.onOff = onOff; }

            /// <summary>The feature has been switched off: its hooks do nothing.</summary>
            public bool Off => off;

            /// <summary>A new world: on again, with the world's logger.</summary>
            public void Reset(ILogger logger) { log = logger; faults = 0; total = 0; windowStart = Environment.TickCount64; off = false; lock (logged) logged.Clear(); }

            // Fable's pass: each distinct error (type and message) is logged once, five at most, not only the first one
            readonly HashSet<string> logged = new();

            public void Fault(Exception e)
            {
                long now = Environment.TickCount64;
                // a new window: the count starts again (a race here only shifts the window by a fault)
                if (now - System.Threading.Interlocked.Read(ref windowStart) > FaultWindowMs)
                {
                    System.Threading.Interlocked.Exchange(ref windowStart, now);
                    System.Threading.Interlocked.Exchange(ref faults, 0);
                }
                int n = System.Threading.Interlocked.Increment(ref faults);
                System.Threading.Interlocked.Increment(ref total);
                bool first;
                lock (logged) first = logged.Count < 5 && logged.Add(e.GetType().FullName + ": " + e.Message);
                if (first) log?.Error("Hydrology: {0} threw an error; vanilla's code ran on for that call: {1}", Feature, e);
                if (n == MaxFaults && !off)
                {
                    off = true;
                    try { onOff?.Invoke(); } catch (Exception) { }
                    log?.Error("Hydrology: {0} switched off after {1} errors within a minute ({2} in all); vanilla's code runs instead until the world restarts", Feature, n, total);
                }
            }
        }

        /// <summary>The running game's version. The API's GameVersion constants are compiled into this assembly, so the
        /// constant is read from the loaded API assembly instead.</summary>
        public static string RunningGameVersion()
        {
            try
            {
                var f = typeof(GameVersion).GetField(nameof(GameVersion.ShortGameVersion), BindingFlags.Public | BindingFlags.Static);
                return f?.GetRawConstantValue() as string ?? GameVersion.ShortGameVersion;
            }
            catch (Exception) { return GameVersion.ShortGameVersion; }
        }

        static readonly HashSet<string> loggedOff = new();

        /// <summary>A new world in the same process (singleplayer): its log says again which features are off (rc.4, audit:
        /// the second world's log had no warning, yet said "see the warning above").</summary>
        public static void NewWorld() { lock (loggedOff) loggedOff.Clear(); }

        /// <summary>Logs, once per feature and world, that a feature is off because the game no longer offers its target.</summary>
        public static void Off(ILogger log, string feature, string why)
        {
            lock (loggedOff) if (!loggedOff.Add(feature)) return;
            log.Warning("Hydrology: {0} switched off: {1} (game {2}; a game update may have changed it)", feature, why, RunningGameVersion());
        }
    }
}
