using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent.Mechanics;

namespace Hydrology.Rules
{
    /// <summary>
    /// H8: water wheels on natural rivers (0.1.8, decision Earth 0027). Vanilla's wheel measures, once a second
    /// (<c>BEBehaviorMPWaterWheel.CheckWater</c>), how the moving water around its rim pushes it, and keeps that as
    /// <c>flowRate</c>, its torque. On a wheel whose axle stands over natural river water (Earth's river query), the
    /// river's slope × discharge raises it: × (1 + slope · discharge / <see cref="PowerRef"/>), at most
    /// <see cref="MaxBoost"/>. Only a wheel that vanilla already turns (flowRate &gt; 0) is raised, never lowered, and
    /// nothing changes without the query. Server side; the client keeps vanilla's own reading for the wheel's sound.
    /// </summary>
    public static class H8Wheels
    {
        public static bool Enabled = true;
        /// <summary>slope (block per block) × discharge (m³/s) that doubles a wheel's torque.</summary>
        public static double PowerRef = 10;
        /// <summary>The most a river raises a wheel's torque.</summary>
        public static double MaxBoost = 3;

        const string Feature = "H8 (wheels on rivers)";

        // the wheel's protected flowRate, bound in Apply (never in a static initializer: a game without the field must not
        // break the class, and with it the config's assignments in StartPre); null while H8 is not patched
        static AccessTools.FieldRef<BEBehaviorMPWaterWheel, float> flowRate;

        /// <summary>Patches the wheel's water check if the game still has it and its flowRate (<see cref="GamePatches"/>).</summary>
        static readonly GamePatches.Breaker Breaker = new(Feature);

        public static void Apply(Harmony harmony, ILogger log)
        {
            Breaker.Reset(log);
            var field = GamePatches.Field<BEBehaviorMPWaterWheel, float>(log, Feature, "flowRate");
            if (field == null) return;
            flowRate = field;
            if (!GamePatches.Apply(harmony, log, Feature,
                    GamePatches.On(typeof(BEBehaviorMPWaterWheel), "CheckWater", new[] { typeof(float) }, typeof(void), typeof(H8Wheels), postfix: nameof(CheckWaterPostfix))))
                flowRate = null;
        }

        /// <summary>Blocks from a wheel's axle its rim may reach, for the river's span (rc.4).</summary>
        const int WheelReach = 3;

        /// <summary>The patch: its work runs inside the feature's breaker (<see cref="GamePatches.Breaker"/>).</summary>
        static void CheckWaterPostfix(BEBehaviorMPWaterWheel __instance)
        {
            var rate = flowRate;
            if (rate == null || Breaker.Off || !Enabled || !EarthRiverQuery.Active) return;
            try { Raise(__instance, rate); }
            catch (Exception e) { Breaker.Fault(e); }
        }

        static void Raise(BEBehaviorMPWaterWheel __instance, AccessTools.FieldRef<BEBehaviorMPWaterWheel, float> rate)
        {
            if (__instance.Api?.Side != EnumAppSide.Server) return;
            ref float flow = ref rate(__instance);
            if (flow <= 0f) return;
            // the wheel's axle stands above the water its rim dips into: its rim must reach the river's own water (a 3 m
            // wheel's rim lies within 2 blocks of the axle; 3 leaves room for larger ones)
            if (!EarthRiverQuery.AtColumn(__instance.Pos, WheelReach, out _, out double slope, out double discharge)) return;
            double boost = 1 + Math.Clamp(slope * discharge / Math.Max(1e-9, PowerRef), 0, Math.Max(0, MaxBoost - 1));
            if (boost <= 1) return;
            flow = (float)(flow * boost);
            HydrologyStats.WheelsRaised++;
        }
    }
}
