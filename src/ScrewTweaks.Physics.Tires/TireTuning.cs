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
        internal static ConfigEntry<float>? GeometryShapeCouplingConfig;
        internal static ConfigEntry<float>? CamberDynamicsConfig;

        internal static float GripScale => GripScaleConfig?.Value ?? 1f;
        internal static float CamberThrust => CamberThrustConfig?.Value ?? 0.015f;

        /// <summary>0 = ignore combined slip (longitudinal and lateral independent), 1 = full ADAMS ellipse.</summary>
        internal static float CombinedSlip => CombinedSlipConfig?.Value ?? 1f;

        /// <summary>Relaxation length [m]. 0 = no lag (force follows the instantaneous slip).</summary>
        internal static float RelaxationLength => RelaxationLengthConfig?.Value ?? 0.30f;

        /// <summary>
        /// How strongly the contact patch (radius x width) moves the slip-curve peak. Brush-model
        /// geometry puts the peak earlier for a bigger patch; the carcass also matters, which we
        /// cannot see, so this is exposed as a strength and set to 0 to disable.
        /// </summary>
        internal static float GeometryShapeCoupling => GeometryShapeCouplingConfig?.Value ?? 1f;

        /// <summary>
        /// Strength of the camber effects other than thrust: how much camber softens the tyre
        /// (cornering stiffness down -> the curve peaks later) and lowers the lateral peak.
        /// 0 = camber contributes thrust only. 1 = typical MF coefficients (PKY3 1.2, PDY3 3).
        /// </summary>
        internal static float CamberDynamics => CamberDynamicsConfig?.Value ?? 1f;
    }
}
