#nullable enable

using BepInEx.Configuration;
using UnityEngine;

namespace ScrewTweaks.ECU
{
    internal enum AidMode
    {
        Native,
        Off,
        Progressive
    }

    /// <summary>Everything an aid algorithm gets to see for one wheel on one physics step.</summary>
    internal struct AidContext
    {
        public MechanicalOutputWheel Wheel;
        public float ForwardSlip;
        public float SidewaySlip;
        public float WheelRpm;
        public float CarSpeed;
        public float DesiredBrake;
    }

    /// <summary>Brake-side electronic aid slot (ABS).</summary>
    internal interface IBrakeAid
    {
        float Apply(in AidContext ctx, float desiredBrake);
    }

    /// <summary>
    /// Proportional ABS. Below the target slip it applies the full brake; above it, the brake is
    /// scaled down by the slip error but never below <see cref="Aids.AbsFloor"/>, so deceleration
    /// (and therefore front weight transfer) is preserved while the wheel is kept from locking.
    /// </summary>
    internal sealed class ProgressiveAbs : IBrakeAid
    {
        public float Apply(in AidContext ctx, float desiredBrake)
        {
            if (desiredBrake <= 0f) return desiredBrake;

            float target = Aids.AbsTarget;
            float slip = Mathf.Abs(ctx.ForwardSlip);
            if (slip <= target) return desiredBrake;

            float factor = 1f - (slip - target) * Aids.AbsGain;
            factor = Mathf.Clamp(factor, Aids.AbsFloor, 1f);
            return desiredBrake * factor;
        }
    }

    /// <summary>Central registry: which algorithm is plugged into each channel.</summary>
    internal static class Aids
    {
        internal static ConfigEntry<string>? AbsConfig;
        internal static ConfigEntry<string>? TractionConfig;

        internal static ConfigEntry<float>? AbsTargetConfig;
        internal static ConfigEntry<float>? AbsGainConfig;
        internal static ConfigEntry<float>? AbsFloorConfig;

        internal static readonly ProgressiveAbs ProgressiveAbs = new ProgressiveAbs();

        internal static AidMode Abs => Parse(AbsConfig?.Value);
        internal static AidMode Traction => Parse(TractionConfig?.Value);

        internal static float AbsTarget => AbsTargetConfig?.Value ?? 0.20f;
        internal static float AbsGain => AbsGainConfig?.Value ?? 4f;
        internal static float AbsFloor => AbsFloorConfig?.Value ?? 0.40f;

        internal static AidMode Parse(string? value)
            => value == nameof(AidMode.Off) ? AidMode.Off
             : value == nameof(AidMode.Progressive) ? AidMode.Progressive
             : AidMode.Native;

        /// <summary>Re-apply the current channel selection to every wheel currently in the scene.</summary>
        internal static void ReapplyAll()
        {
            foreach (var wheel in Object.FindObjectsByType<MechanicalOutputWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AidApplier.Apply(wheel);

            foreach (var wheel in Object.FindObjectsByType<MechanicalOutputWheelBrake>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AidApplier.Apply(wheel);
        }
    }
}
