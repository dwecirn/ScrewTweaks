#nullable enable

using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// Where the wheel's vertical numbers come from.
    ///
    /// They are <em>derived</em>, not tuned. The unsprung mass is the wheel part's own mass, the tyre
    /// stiffness is the suspension's wheel rate times a fixed ratio, and the damping is a fixed fraction
    /// of critical. None of it is in the config, because none of it is a preference: it is the best
    /// reading of the game's own part data, and three magic numbers next to a model selector invite
    /// tuning a car into a state that no longer describes a tyre.
    ///
    /// The one number that has to be computed rather than chosen is the substep count. An unsprung mode
    /// is fast, so the integration step follows from it - that is arithmetic, not a preference.
    /// </summary>
    internal static class TireVerticalTuning
    {
        /// <summary>Safety ceiling on the substepping, however stiff a tyre turns out to be.</summary>
        internal const int MaxSubsteps = 32;

        /// <summary>
        /// Tyre vertical stiffness as a multiple of the suspension's wheel rate.
        ///
        /// Real cars put a tyre's vertical rate at roughly 5 to 10 times the wheel rate, which is what
        /// keeps the unsprung mode well clear of the body mode. Taking the suspension's own
        /// `maxForce / maxLength` as that wheel rate means the tyre scales with the car exactly the way
        /// the game's springs already do, using part data that is already there - a heavy car with long
        /// travel gets a soft big tyre, a go-kart a stiff small one, with no car-specific constant.
        /// </summary>
        private const float RateRatio = 6f;

        /// <summary>
        /// Multiplier on the wheel part's mass to get the corner's unsprung mass.
        ///
        /// The game's `Mass` is the wheel on its own - tyre and rim. A real corner also carries the hub,
        /// the brake disc and caliper and half the suspension arms, so the unsprung mass is roughly
        /// twice what the wheel weighs, and the ratio between the two is what sets how prone the wheel
        /// is to hopping: too light, and a bump throws it clear of the ground.
        /// </summary>
        internal const float CornerMassFactor = 2f;

        /// <summary>
        /// Tyre vertical damping as a fraction of critical. Measured tyre vertical damping ratios sit
        /// around 0.1 to 0.3 - higher than most people expect, because the tyre is what has to control
        /// wheel hop: the damper is above the wheel's own frequency, and a digressive one has already
        /// blown off by the time the wheel is moving fast enough to matter.
        /// </summary>
        private const float DampingRatio = 0.2f;

        /// <summary>Fallback unsprung frequency, used only when there is no suspension part to read.</summary>
        private const float FallbackFrequency = 13f;

        /// <summary>Largest omega*dt a substep may reach. Explicit integration is unstable above 2;
        /// keeping it well under 1 leaves the damping room to work without ringing.</summary>
        private const float MaxOmegaDt = 0.5f;

        /// <summary>The unsprung mass for one wheel: the wheel part, plus the rest of the corner.</summary>
        internal static float UnsprungMass(WheelController wc)
        {
            float wheel = wc?.wheel != null ? wc.wheel.mass : 0f;
            return Mathf.Max(wheel * CornerMassFactor, 0.5f);
        }

        /// <summary>
        /// The wheel rate the suspension presents at its nominal compression, in [N/m]. This is the
        /// average rate over the travel, which is also the scale the game used to size `maxForce`.
        /// </summary>
        internal static float WheelRate(WheelController wc)
        {
            if (wc?.spring == null || wc.spring.maxLength <= 0f) return 0f;
            return Mathf.Max(wc.spring.maxForce, 0f) / wc.spring.maxLength;
        }

        /// <summary>Tyre vertical stiffness for one wheel, in [N/m].</summary>
        internal static float Stiffness(WheelController wc, float unsprungMass)
        {
            float mass = Mathf.Max(unsprungMass, 1e-3f);
            float rate = WheelRate(wc);
            if (rate > 0f) return rate * RateRatio;

            float omega = 2f * Mathf.PI * FallbackFrequency;
            return mass * omega * omega;
        }

        /// <summary>Tyre vertical damping for one wheel, in [N*s/m].</summary>
        internal static float Damping(WheelController wc, float unsprungMass)
        {
            float mass = Mathf.Max(unsprungMass, 1e-3f);
            return 2f * DampingRatio * Mathf.Sqrt(Stiffness(wc, mass) * mass);
        }

        /// <summary>The unsprung natural frequency this wheel ends up with, in [Hz].</summary>
        internal static float Frequency(WheelController wc, float unsprungMass)
        {
            float mass = Mathf.Max(unsprungMass, 1e-3f);
            return Mathf.Sqrt(Stiffness(wc, mass) / mass) / (2f * Mathf.PI);
        }

        /// <summary>
        /// Substeps for a step of <paramref name="dt"/>, from the stability limit rather than a setting,
        /// so a light wheel with a stiff tyre cannot turn into an explosion.
        /// </summary>
        internal static int SubstepsFor(WheelController wc, float unsprungMass, float dt)
        {
            float omega = 2f * Mathf.PI * Frequency(wc, unsprungMass);
            int needed = Mathf.CeilToInt(omega * Mathf.Max(dt, 1e-4f) / MaxOmegaDt);
            return Mathf.Clamp(needed, 1, MaxSubsteps);
        }
    }
}
