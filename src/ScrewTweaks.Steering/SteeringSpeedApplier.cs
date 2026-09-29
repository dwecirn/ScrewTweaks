#nullable enable

using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Applies the Instant Steering setting to every car's FrontWheelsSteerer.
    ///
    /// The game ramps the wheel angle with Mathf.MoveTowards using the private fields
    /// "steerSpeedBySpeed" (degrees/second curve) and "steerBackSpeed", but only for binary
    /// (keyboard / d-pad) input; analog sticks bypass them via the "_useDirectDrive" path.
    ///
    /// When the setting is on we scale both fields by a huge factor so the wheels reach the target in a
    /// single frame - effectively the same as analog direct drive. The originals are captured once per
    /// steerer so nothing compounds.
    ///
    /// The setting is global and lives in Saveables, so a change made in the panel or in the game's
    /// controls page is picked up by <see cref="Plugin"/> and pushed to the cars in the world.
    /// </summary>
    [HarmonyPatch]
    internal static class SteeringSpeedApplier
    {
        private const float InstantMultiplier = 1_000_000f;

        private sealed class State
        {
            internal AnimationCurve? OriginalTurnCurve;
            internal float OriginalBackSpeed;
            internal float Applied = -1f;
        }

        private static readonly ConditionalWeakTable<FrontWheelsSteerer, State> States =
            new ConditionalWeakTable<FrontWheelsSteerer, State>();

        private static readonly AccessTools.FieldRef<FrontWheelsSteerer, AnimationCurve> TurnCurveRef =
            AccessTools.FieldRefAccess<FrontWheelsSteerer, AnimationCurve>("steerSpeedBySpeed");

        private static readonly AccessTools.FieldRef<FrontWheelsSteerer, float> BackSpeedRef =
            AccessTools.FieldRefAccess<FrontWheelsSteerer, float>("steerBackSpeed");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimpleCar2), "ApplyCar", typeof(CalculatedCar), typeof(byte[]), typeof(byte[]))]
        internal static void ApplyCarPostfix(SimpleCar2 __instance)
        {
            try
            {
                var steerer = __instance.FrontWheelsSteerer;
                if (steerer == null) return;

                Apply(steerer, SteeringSettings.InstantSteering ? InstantMultiplier : 1f);
            }
            catch
            {
                // never let this break car spawning
            }
        }

        /// <summary>Push the current setting onto every car in the world.</summary>
        internal static void ApplyToAll()
        {
            try
            {
                float multiplier = SteeringSettings.InstantSteering ? InstantMultiplier : 1f;
                foreach (var steerer in UnityEngine.Object.FindObjectsByType<FrontWheelsSteerer>(
                             FindObjectsSortMode.None))
                {
                    if (steerer != null) Apply(steerer, multiplier);
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void Apply(FrontWheelsSteerer steerer, float multiplier)
        {
            var state = States.GetOrCreateValue(steerer);

            // Capture the untouched originals on first contact so we never compound.
            if (state.Applied < 0f)
            {
                state.OriginalTurnCurve = TurnCurveRef(steerer);
                state.OriginalBackSpeed = BackSpeedRef(steerer);
                state.Applied = 1f;
            }

            if (Mathf.Approximately(multiplier, state.Applied)) return;
            state.Applied = multiplier;

            var original = state.OriginalTurnCurve;
            if (original != null)
            {
                var scaled = new AnimationCurve();
                foreach (var key in original.keys)
                {
                    scaled.AddKey(new Keyframe(
                        key.time,
                        key.value * multiplier,
                        key.inTangent * multiplier,
                        key.outTangent * multiplier));
                }
                TurnCurveRef(steerer) = scaled;
            }

            BackSpeedRef(steerer) = state.OriginalBackSpeed * multiplier;
        }
    }
}
