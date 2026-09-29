#nullable enable

using BepInEx.Configuration;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// The two steering settings, stored in the mod's own config like everything else the player tunes.
    ///
    /// Both are also edited from the game's controls page, which is why they live behind this one class:
    /// the page's two rows and the panel's controls read and write the same entries, so the two views
    /// cannot disagree.
    /// </summary>
    internal static class SteeringSettings
    {
        internal static ConfigEntry<float>? LimitRelaxConfig;
        internal static ConfigEntry<bool>? InstantConfig;

        /// <summary>How much of the game's speed-sensitive steering limit to blend away, 0..1.</summary>
        internal static float LimitRelax => Mathf.Clamp01(LimitRelaxConfig?.Value ?? 0f);

        internal static void SetLimitRelax(float value)
        {
            if (LimitRelaxConfig != null) LimitRelaxConfig.Value = Mathf.Clamp01(value);
        }

        /// <summary>Whether binary steering input is applied instantly instead of being ramped.</summary>
        internal static bool InstantSteering => InstantConfig?.Value ?? false;

        internal static void SetInstant(bool value)
        {
            if (InstantConfig != null) InstantConfig.Value = value;
        }

        private static bool _cachedInstant;

        /// <summary>
        /// True when Instant Steering changed since the last check, whoever changed it - the panel, the
        /// game's controls page, or a hand-edited config. The caller reapplies it to the running cars.
        /// </summary>
        internal static bool ConsumeInstantChange()
        {
            bool now = InstantSteering;
            if (now == _cachedInstant) return false;
            _cachedInstant = now;
            return true;
        }
    }
}
