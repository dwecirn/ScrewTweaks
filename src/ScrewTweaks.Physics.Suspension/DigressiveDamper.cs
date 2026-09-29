#nullable enable

using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// The damper that ships with the suite: a bump/rebound split plus a digressive force curve.
    ///
    /// The game's damper is `C * |v|`, with one C for both directions, so it is linear everywhere and
    /// symmetric. A real damper is neither:
    ///
    ///   - rebound is stiffer than bump, in roughly a 2:1 ratio on a road car and more on a race car.
    ///     Rebound is what arrests the body once the bump has passed, so it is the channel that decides
    ///     whether the car feels planted or pogoes.
    ///   - force rises steeply up to a knee (the bleed), then much more slowly once the shim stack
    ///     opens. Without that knee, a sharp hit - a kerb, a crack, a landing - sends the whole velocity
    ///     spike straight into the chassis, while slow inputs like roll are left under-damped.
    ///
    /// The law is
    ///
    ///     g(v) = v                                    v <= knee
    ///     g(v) = knee + (v - knee) * blowOff          v >  knee
    ///     F    = C * lowSpeedGain * g(v) * (bump ? 1 : reboundRatio)
    ///
    /// Everything is a multiple of the game's own coefficient C, so the mass, wheel-count and
    /// `damperforce` property scaling the game already does is preserved. Setting
    /// `LowSpeedGain = 1`, `BlowOffRatio = 1` and `ReboundRatio = 1` reproduces the game exactly, which
    /// is the A/B switch to compare against.
    /// </summary>
    internal sealed class DigressiveDamper : IDamperModel
    {
        public string Name => "Digressive";

        public string Description =>
            "Bump/rebound split with a digressive (blow-off) curve. Stiffer when slow, softer on sharp " +
            "hits, so the body is controlled without the kerbs being harsh. Back off the sliders to " +
            "reproduce the game's linear damper.";

        public float Evaluate(in DamperState state)
        {
            float v = state.Velocity;

            float knee = Mathf.Max(DamperTuning.KneeVelocity, 1e-4f);
            float blowOff = Mathf.Clamp(DamperTuning.BlowOffRatio, 0f, 1f);
            float gain = Mathf.Max(DamperTuning.LowSpeedGain, 0f);
            float direction = state.Compressing ? 1f : Mathf.Max(DamperTuning.ReboundRatio, 0f);

            float shape = v <= knee ? v : knee + (v - knee) * blowOff;
            return state.GameCoefficient * gain * shape * direction;
        }
    }
}
