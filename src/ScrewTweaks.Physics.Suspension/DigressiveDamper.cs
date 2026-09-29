#nullable enable

using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// The damper that ships: a four-way one, bump and rebound each with a low-speed and a high-speed
    /// coefficient.
    ///
    /// The game's damper is `C * |v|` with one C for both directions, so it is linear everywhere and
    /// symmetric. A real damper is neither, and the two things that make it real are independent:
    ///
    ///   - rebound is stiffer than bump, in roughly a 2:1 ratio on a road car and more on a race car.
    ///     Rebound is what arrests the body once the bump has passed, so it is what decides whether the
    ///     car feels planted or pogoes.
    ///   - force rises steeply up to a knee (the bleed) and much more slowly after it, once the shim
    ///     stack is open. Without that knee a sharp hit - a kerb, a crack, a landing - sends the whole
    ///     velocity spike into the chassis, while slow inputs like roll are left under-damped.
    ///
    /// So the law is piecewise linear, continuous at the knee, and each of the four coefficients is
    /// independent:
    ///
    ///     F = C * ( v <= knee ? low * v : low * knee + high * (v - knee) )
    ///
    /// with `low` and `high` taken from whichever half of the table matches the direction of travel.
    /// Every coefficient is a multiple of the game's own C, so the mass, wheel-count and `damperforce`
    /// scaling the game already does is preserved, and all four at 1.00 reproduces the game exactly -
    /// which is the A/B switch to compare against.
    /// </summary>
    internal sealed class DigressiveDamper : IDamperModel
    {
        public string Name => "Four way";

        public string Description =>
            "Bump and rebound, each with its own low-speed (bleed) and high-speed (blow-off) coefficient. " +
            "Stiffer when slow so the body is controlled, softer on sharp hits so kerbs are absorbed. " +
            "All four coefficients at 1.00 reproduces the game exactly.";

        public float Evaluate(in DamperState state)
        {
            float v = state.Velocity;
            float knee = Mathf.Max(DamperTuning.KneeVelocity, 1e-4f);

            float low = state.Compressing ? DamperTuning.BumpLow : DamperTuning.ReboundLow;
            float high = state.Compressing ? DamperTuning.BumpHigh : DamperTuning.ReboundHigh;

            float coefficient = v <= knee
                ? low * v
                : low * knee + high * (v - knee);

            return state.GameCoefficient * coefficient;
        }
    }
}
