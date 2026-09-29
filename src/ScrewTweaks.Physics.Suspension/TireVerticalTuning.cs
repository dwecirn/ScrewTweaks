#nullable enable

using BepInEx.Configuration;
using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    internal static class TireVerticalTuning
    {
        /// <summary>Safety ceiling on the substepping, however stiff a model asks for.</summary>
        internal const int MaxSubsteps = 32;

        /// <summary>Largest omega*dt a substep is allowed to reach. Explicit integration is unstable
        /// above 2; keeping it well under 1 leaves the damper room to work without ringing.</summary>
        private const float MaxOmegaDt = 0.5f;

        internal static ConfigEntry<float>? FrequencyConfig;
        internal static ConfigEntry<float>? DampingRatioConfig;
        internal static ConfigEntry<float>? MassScaleConfig;
        internal static ConfigEntry<int>? SubstepsConfig;

        /// <summary>
        /// Target unsprung natural frequency in [Hz]. Real cars sit around 10 to 15 Hz, which is why
        /// 13 is the default; it also sets how stiff the tyre is relative to the car (a real tyre is
        /// roughly 5 to 10 times the wheel rate).
        /// </summary>
        internal static float Frequency => FrequencyConfig?.Value ?? 13f;

        /// <summary>
        /// Tyre vertical damping as a fraction of critical. Real tyres are lightly damped, around
        /// 0.05 to 0.1; the damping also has to keep the substepped integration clean.
        /// </summary>
        internal static float DampingRatio => DampingRatioConfig?.Value ?? 0.07f;

        /// <summary>
        /// Multiplier on the wheel part's own mass, which is the unsprung mass. The game only ever
        /// used that number for rotational inertia; a real corner also has the hub and half the arms
        /// in it, so this is where that is added.
        /// </summary>
        internal static float MassScale => MassScaleConfig?.Value ?? 1f;

        /// <summary>Requested substeps. The host raises it if the frequency and step need more.</summary>
        internal static int Substeps => SubstepsConfig?.Value ?? 4;

        private static float Omega => 2f * Mathf.PI * Mathf.Max(Frequency, 0.1f);

        /// <summary>
        /// Tyre stiffness for one wheel, in [N/m], chosen so the unsprung frequency is the same
        /// whatever the wheel's mass is.
        /// </summary>
        internal static float Stiffness(float unsprungMass)
        {
            return Mathf.Max(unsprungMass, 1e-3f) * Omega * Omega;
        }

        /// <summary>Tyre vertical damping for one wheel, in [N*s/m].</summary>
        internal static float Damping(float unsprungMass)
        {
            return 2f * Mathf.Max(DampingRatio, 0f) * Mathf.Sqrt(Stiffness(unsprungMass) * Mathf.Max(unsprungMass, 1e-3f));
        }

        /// <summary>
        /// Substeps actually used for a step of <paramref name="dt"/>. Derived from the stability
        /// limit rather than trusted to the setting, so a high frequency or a slow physics rate cannot
        /// turn into an explosion; the setting can only ask for more than the minimum.
        /// </summary>
        internal static int SubstepsFor(float dt)
        {
            int needed = Mathf.CeilToInt(Omega * Mathf.Max(dt, 1e-4f) / MaxOmegaDt);
            return Mathf.Clamp(Mathf.Max(Substeps, needed), 1, MaxSubsteps);
        }
    }
}
