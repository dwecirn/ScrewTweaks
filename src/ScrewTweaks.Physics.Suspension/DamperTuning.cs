#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// The damper's four numbers, plus the velocity where the two halves of each meet.
    ///
    /// Every coefficient is a multiple of the one the game computed for that wheel (see
    /// <see cref="DamperState.GameCoefficient"/>), which is why they are dimensionless: 1.00 is the game's
    /// own damping at that wheel, and all four at 1.00 is the game exactly. Absolute numbers in N*s/m
    /// would be unusable across cars - a truck and a go-kart are two orders of magnitude apart.
    /// </summary>
    internal static class DamperTuning
    {
        internal static ConfigEntry<float>? KneeVelocityConfig;
        internal static ConfigEntry<float>? BumpLowConfig;
        internal static ConfigEntry<float>? BumpHighConfig;
        internal static ConfigEntry<float>? ReboundLowConfig;
        internal static ConfigEntry<float>? ReboundHighConfig;
        internal static ConfigEntry<float>? ReboundFloorConfig;

        /// <summary>
        /// Velocity [m/s] at which each half's shim stack opens and the force stops rising as fast. Real
        /// dampers knee somewhere between about 0.05 and 0.3 m/s, and it is a shaft property, so it does
        /// not scale with the car. It is shared by bump and rebound; real dampers have separate ones, and
        /// the model API does not stop anyone from writing that.
        /// </summary>
        internal static float KneeVelocity => KneeVelocityConfig?.Value ?? 0.10f;

        /// <summary>
        /// Bump below the knee. This is the slope of the steep part of the plot, the bleed: it is what
        /// controls the body over slow inputs like roll and pitch, so it is the number that decides how
        /// planted the car feels.
        /// </summary>
        internal static float BumpLow => BumpLowConfig?.Value ?? 1.6f;

        /// <summary>
        /// Bump above the knee, where the shim stack is open. This is the slope of the shallow part, and
        /// it is what a kerb or an expansion joint sees - lower means the hit is absorbed instead of
        /// being passed into the chassis.
        /// </summary>
        internal static float BumpHigh => BumpHighConfig?.Value ?? 0.4f;

        /// <summary>
        /// Rebound below the knee. Rebound is what arrests the body once a bump has passed, which is why
        /// it is normally set well above bump.
        /// </summary>
        internal static float ReboundLow => ReboundLowConfig?.Value ?? 3.2f;

        /// <summary>
        /// Rebound above the knee. Raising this is what stops a car launching off its springs after a
        /// landing, at the cost of making sharp edges harsher.
        /// </summary>
        internal static float ReboundHigh => ReboundHighConfig?.Value ?? 0.8f;

        /// <summary>
        /// How far the damper is allowed to pull the body *down*, as a fraction of the nominal static
        /// wheel load. Only applies while the wheel is rigid: with a vertical model the tyre's one-sided
        /// force is the real bound and this is not used at all.
        /// </summary>
        internal static float ReboundFloor => ReboundFloorConfig?.Value ?? 0f;
    }
}
