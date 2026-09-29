#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// The one damper number that is not a property of the car.
    ///
    /// The four coefficients belong to the suspension part and are stored in the car's own save file
    /// (<see cref="DamperProperties"/>), because they are how *that car* is set up. The knee velocity is
    /// not: it is a property of the damper shaft, the same for every car, and is not something a driver
    /// changes. Neither is the pull-down floor, which is a decision about how much of the game's own
    /// integrator to keep rather than a property of a damper.
    /// </summary>
    internal static class DamperTuning
    {
        /// <summary>
        /// Velocity [m/s] at which each half's shim stack opens and the force stops rising as fast. Real
        /// dampers knee between about 0.05 and 0.3 m/s, and it is a shaft property, so it does not scale
        /// with the car. Shared by bump and rebound; real dampers have separate ones, which the model API
        /// does not prevent anyone from writing.
        /// </summary>
        internal const float KneeVelocity = 0.10f;

        internal static ConfigEntry<float>? ReboundFloorConfig;

        /// <summary>
        /// How far the damper is allowed to pull the body *down*, as a fraction of the nominal static wheel
        /// load. Only applies while the wheel is rigid: with a vertical model the tyre's one-sided force is
        /// the real bound and this is not used at all.
        /// </summary>
        internal static float ReboundFloor => ReboundFloorConfig?.Value ?? 0f;
    }
}
