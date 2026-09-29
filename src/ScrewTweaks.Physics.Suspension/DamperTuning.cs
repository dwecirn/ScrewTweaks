#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Physics.Suspension
{
    internal static class DamperTuning
    {
        internal static ConfigEntry<float>? ReboundRatioConfig;
        internal static ConfigEntry<float>? LowSpeedGainConfig;
        internal static ConfigEntry<float>? KneeVelocityConfig;
        internal static ConfigEntry<float>? BlowOffRatioConfig;
        internal static ConfigEntry<float>? ReboundFloorConfig;

        /// <summary>
        /// Rebound coefficient as a multiple of the bump coefficient. The game writes the same number to
        /// both; real dampers are 2 to 3 times stiffer in rebound, because rebound is what controls the
        /// body after a bump instead of the spring pushing it back.
        /// </summary>
        internal static float ReboundRatio => ReboundRatioConfig?.Value ?? 2f;

        /// <summary>
        /// Multiplier on the damping coefficient in the low-speed region. 1.0 = the game's own
        /// coefficient. Above 1 the car is held down more over slow inputs (roll, pitch, weight
        /// transfer) without making sharp hits harsher - that is what the blow-off below is for.
        /// </summary>
        internal static float LowSpeedGain => LowSpeedGainConfig?.Value ?? 1.6f;

        /// <summary>
        /// Velocity [m/s] at which the shim stack opens and the force stops rising as fast. Real
        /// dampers knee somewhere around 0.05 to 0.3 m/s; it is a shaft property, so it does not scale
        /// with the car.
        /// </summary>
        internal static float KneeVelocity => KneeVelocityConfig?.Value ?? 0.1f;

        /// <summary>
        /// Slope above the knee, as a fraction of the low-speed slope. 1 = no blow-off at all (a plain
        /// linear damper, i.e. the game), 0 = a hard plateau. Real dampers sit around 0.1 to 0.3.
        /// </summary>
        internal static float BlowOffRatio => BlowOffRatioConfig?.Value ?? 0.25f;

        /// <summary>
        /// How far the damper is allowed to pull the body *down*, as a fraction of the nominal static
        /// wheel load. The game clamps the total suspension force at zero, so a rebound force larger
        /// than the spring force is truncated and stops doing anything; this is the knob that decides
        /// whether that is relaxed.
        ///     0    = the game's behaviour, the damper can never pull the body down (default)
        ///     1    = it may pull down as hard as the wheel's static load, i.e. about 1 g
        /// Anything above 1 risks dragging the chassis into the terrain on a crest.
        /// </summary>
        internal static float ReboundFloor => ReboundFloorConfig?.Value ?? 0f;
    }
}
