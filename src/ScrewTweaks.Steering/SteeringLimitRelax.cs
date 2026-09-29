#nullable enable

using System.Runtime.CompilerServices;
using HarmonyLib;
using SappUnityUtils.IO.SimpleSaveables;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Softens the game's speed-sensitive steering limit.
    ///
    /// FrontWheelsSteerer computes the maximum wheel angle as
    ///   baseAngle * maxSteerAngleBySpeed.Evaluate(speed)
    /// where the curve returns a 0..1 factor that drops with speed. Vanilla values are quite
    /// aggressive, which reads as understeer. We blend that curve toward 1 ("no limit") by a
    /// user-tweakable amount:
    ///   newValue = value + (1 - value) * relax      (relax 0..1)
    /// relax = 0 -> vanilla, relax = 1 -> same as the game's "ignore steer angle limit".
    ///
    /// The value lives in the mod's own config (<see cref="SteeringSettings"/>) and is edited from the
    /// game's controls page or from the panel.
    /// </summary>
    [HarmonyPatch]
    internal static class SteeringLimitRelax
    {
        private sealed class State
        {
            internal AnimationCurve? Original;
            internal float MaxValue;
            internal float Applied = -1f;
        }

        private static readonly ConditionalWeakTable<FrontWheelsSteerer, State> States = new();

        private static readonly AccessTools.FieldRef<FrontWheelsSteerer, AnimationCurve> CurveRef =
            AccessTools.FieldRefAccess<FrontWheelsSteerer, AnimationCurve>("maxSteerAngleBySpeed");

        internal static float Get()
        {
            return SteeringSettings.LimitRelax;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FrontWheelsSteerer), "steerWheels")]
        internal static void SteerWheelsPrefix(FrontWheelsSteerer __instance)
        {
            try
            {
                var state = States.GetOrCreateValue(__instance);
                if (state.Original == null)
                {
                    state.Original = CurveRef(__instance);
                    state.Applied = -1f;
                    state.MaxValue = MaxValueOf(state.Original);
                    LogCurve(state.Original);
                }

                float relax = Get();
                if (Mathf.Approximately(relax, state.Applied)) return;
                state.Applied = relax;

                var original = state.Original;
                if (original == null) return;

                if (relax <= 0f)
                {
                    CurveRef(__instance) = original;
                    return;
                }

                float max = state.MaxValue;
                var relaxed = new AnimationCurve();
                foreach (var k in original.keys)
                {
                    float v = k.value + (max - k.value) * relax;
                    relaxed.AddKey(new Keyframe(k.time, v, k.inTangent, k.outTangent));
                }
                CurveRef(__instance) = relaxed;
            }
            catch
            {
                // Never let this break steering.
            }
        }

        private static float MaxValueOf(AnimationCurve? curve)
        {
            if (curve == null || curve.keys.Length == 0) return 1f;
            float max = float.MinValue;
            foreach (var k in curve.keys)
                if (k.value > max) max = k.value;
            return max;
        }

        private static void LogCurve(AnimationCurve? curve)
        {
            if (curve == null) { Debug.Log("[ScrewTweaks.Steering] maxSteerAngleBySpeed: null"); return; }
            var sb = new System.Text.StringBuilder("[ScrewTweaks.Steering] maxSteerAngleBySpeed keys:");
            foreach (var k in curve.keys)
                sb.Append($" ({k.time:0.##}->{k.value:0.###})");
            Debug.Log(sb.ToString());
        }
    }
}
