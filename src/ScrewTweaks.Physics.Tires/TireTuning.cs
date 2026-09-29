#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Physics.Tires
{
    internal static class TireTuning
    {
        internal static ConfigEntry<float>? GripScaleConfig;
        internal static ConfigEntry<float>? CamberThrustConfig;

        internal static float GripScale => GripScaleConfig?.Value ?? 1f;
        internal static float CamberThrust => CamberThrustConfig?.Value ?? 0.015f;
    }
}
