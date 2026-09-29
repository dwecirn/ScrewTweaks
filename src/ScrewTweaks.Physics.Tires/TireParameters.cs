#nullable enable

using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Model parameters derived from the wheel's own data, so different tires (slick, off-road,
    /// rigid rim) get different values instead of one global setting.
    ///
    /// The tuning sliders are expressed against a reference wheel size; each wheel is then scaled
    /// by its radius relative to that, which is the first-order dependency of both the relaxation
    /// length and the camber response on tire size. A small rigid wheel therefore ends up with a
    /// short relaxation length and weak camber thrust, a big soft off-road tire with a long one.
    /// </summary>
    internal readonly struct TireParameters
    {
        internal const float ReferenceRadius = 0.30f;

        /// <summary>Longitudinal relaxation length [m].</summary>
        internal readonly float SigmaK;

        /// <summary>Lateral relaxation length [m].</summary>
        internal readonly float SigmaA;

        /// <summary>Camber thrust as a fraction of wheel load per degree.</summary>
        internal readonly float CamberThrust;

        private TireParameters(float sigmaK, float sigmaA, float camberThrust)
        {
            SigmaK = sigmaK;
            SigmaA = sigmaA;
            CamberThrust = camberThrust;
        }

        internal static TireParameters From(Wheel wheel)
        {
            float sizeRatio = Mathf.Clamp(wheel.radius, 0.02f, 2f) / ReferenceRadius;

            return new TireParameters(
                TireTuning.RelaxationLength * sizeRatio,
                TireTuning.RelaxationLength * sizeRatio,
                TireTuning.CamberThrust * sizeRatio);
        }
    }
}
