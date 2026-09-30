#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Views
{
    /// <summary>The module's config, and the only place that knows a key from a value.</summary>
    internal static class ViewSettings
    {
        internal static ConfigEntry<bool>? EnabledConfig;
        internal static ConfigEntry<int>? ModeConfig;
        internal static ConfigEntry<string>? PoseConfig;
        internal static ConfigEntry<float>? DistanceConfig;
        internal static ConfigEntry<float>? HeightConfig;
        internal static ConfigEntry<float>? AimHeightConfig;

        internal static bool Enabled
        {
            get => EnabledConfig?.Value ?? false;
            set { if (EnabledConfig != null) EnabledConfig.Value = value; }
        }

        /// <summary>The game camera mode this patches. Read from the game, written from the panel.</summary>
        internal static int Mode
        {
            get => ModeConfig?.Value ?? 10;
            set { if (ModeConfig != null) ModeConfig.Value = value; }
        }

        internal static string Pose => PoseConfig?.Value ?? RigidChase.Name;

        // Multipliers on the car's own size radius, so a go-kart and a truck frame the same way.
        internal static float Distance => DistanceConfig?.Value ?? 3.0f;
        internal static float Height => HeightConfig?.Value ?? 1.4f;
        internal static float AimHeight => AimHeightConfig?.Value ?? 0.3f;
    }
}
