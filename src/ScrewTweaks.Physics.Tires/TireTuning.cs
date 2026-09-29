#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Physics.Tires
{
    internal static class TireTuning
    {
        internal static ConfigEntry<float>? GripScaleConfig;
        internal static ConfigEntry<float>? CamberThrustConfig;
        internal static ConfigEntry<float>? CombinedSlipConfig;
        internal static ConfigEntry<float>? RelaxationLengthConfig;

        internal static float GripScale => GripScaleConfig?.Value ?? 1f;
        internal static float CamberThrust => CamberThrustConfig?.Value ?? 0.015f;

        /// <summary>0 = ignore combined slip (longitudinal and lateral independent), 1 = full ADAMS ellipse.</summary>
        internal static float CombinedSlip => CombinedSlipConfig?.Value ?? 1f;

        /// <summary>Relaxation length [m]. 0 = no lag (force follows the instantaneous slip).</summary>
        internal static float RelaxationLength => RelaxationLengthConfig?.Value ?? 0.30f;
    }
}
