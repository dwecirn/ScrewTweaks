#nullable enable

using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Applies the "Instant Steering" toggle to the car's FrontWheelsSteerer.
    ///
    /// The game ramps the wheel angle with Mathf.MoveTowards using the private fields
    /// "steerSpeedBySpeed" (degrees/second curve) and "steerBackSpeed", but only for binary
    /// (keyboard / d-pad) input; analog sticks bypass them via the "_useDirectDrive" path.
    ///
    /// When any steerable suspension on the car has the toggle on, we scale both fields by a
    /// huge factor so the wheels reach the target in a single frame - effectively the same as
    /// analog direct drive. The originals are captured once per steerer so nothing compounds.
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

        private static readonly ConditionalWeakTable<FrontWheelsSteerer, State> States = new();

        private static readonly AccessTools.FieldRef<FrontWheelsSteerer, AnimationCurve> TurnCurveRef =
            AccessTools.FieldRefAccess<FrontWheelsSteerer, AnimationCurve>("steerSpeedBySpeed");

        private static readonly AccessTools.FieldRef<FrontWheelsSteerer, float> BackSpeedRef =
            AccessTools.FieldRefAccess<FrontWheelsSteerer, float>("steerBackSpeed");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimpleCar2), "ApplyCar", typeof(CalculatedCar), typeof(byte[]), typeof(byte[]))]
        internal static void ApplyCarPostfix(SimpleCar2 __instance, CalculatedCar car, byte[] baguetteBytes)
        {
            try
            {
                var steerer = __instance.FrontWheelsSteerer;
                if (steerer == null) return;

                bool enabled = ReadEnabledFromCar(car) || ReadEnabledFromBytes(baguetteBytes);
                Apply(steerer, enabled ? InstantMultiplier : 1f);
            }
            catch
            {
                // Never let this break car spawning.
            }
        }

        /// <summary>Reads the toggle from the freshly analysed car (preferred source).</summary>
        private static bool ReadEnabledFromCar(CalculatedCar car)
        {
            if (car == null) return false;
            try
            {
                foreach (var part in car.Parts)
                {
                    if (part?.PartConfiguration == null) continue;
                    if (!SteeringSpeed.IsSteerSusp(part.PartConfiguration.partType)) continue;

                    var props = part.Properties;
                    if (props == null) continue;

                    foreach (var prop in props)
                    {
                        if (prop.PropertyName != SteeringSpeed.PropertyName) continue;
                        try { if (Convert.ToInt32(prop.PropertyType?.Value) == 1) return true; } catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Fallback: reads the toggle straight from the saved .baguette bytes.</summary>
        private static bool ReadEnabledFromBytes(byte[]? baguetteBytes)
        {
            if (baguetteBytes == null || baguetteBytes.Length == 0) return false;
            try
            {
                var properties = FileLoader.LoadSettingsFromBytes(baguetteBytes);
                if (properties == null) return false;

                foreach (var cfg in properties.PartConfigs)
                {
                    if (!SteeringSpeed.IsSteerSusp(cfg.partType)) continue;
                    var props = properties.GetProperties(cfg);
                    if (props == null) continue;

                    foreach (var prop in props)
                    {
                        if (prop.PropertyName != SteeringSpeed.PropertyName) continue;
                        try { if (Convert.ToInt32(prop.PropertyType?.Value) == 1) return true; } catch { }
                    }
                }
            }
            catch { }
            return false;
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
