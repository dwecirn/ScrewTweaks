#nullable enable

using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Model parameters derived from the wheel's own data, so different tires (slick, off-road,
    /// rigid rim) get different values instead of one global setting.
    ///
    /// Two orthogonal inputs:
    ///   - size        (radius, width)  -> relaxation length, camber thrust, contact patch
    ///   - identity    (PartType, Grip) -> available for per-tire authoring
    ///
    /// The tuning sliders are expressed against a reference wheel (0.30 m / 0.25 m), so a small
    /// rigid wheel ends up with a short relaxation length and weak camber thrust while a big soft
    /// off-road tire gets a long / strong one.
    /// </summary>
    internal readonly struct TireParameters
    {
        internal const float ReferenceRadius = 0.30f;
        internal const float ReferenceWidth = 0.25f;

        /// <summary>Longitudinal relaxation length [m].</summary>
        internal readonly float SigmaK;

        /// <summary>Lateral relaxation length [m].</summary>
        internal readonly float SigmaA;

        /// <summary>Camber thrust as a fraction of wheel load per degree.</summary>
        internal readonly float CamberThrust;

        /// <summary>
        /// Multiplier on the slip fed to the shape curve. Above 1 the curve peaks at a smaller
        /// physical slip (sharper tire), below 1 it peaks later (softer tire).
        /// </summary>
        internal readonly float PeakSlipScale;

        private TireParameters(float sigmaK, float sigmaA, float camberThrust, float peakSlipScale)
        {
            SigmaK = sigmaK;
            SigmaA = sigmaA;
            CamberThrust = camberThrust;
            PeakSlipScale = peakSlipScale;
        }

        internal static TireParameters From(Wheel wheel)
        {
            float sizeRatio = Mathf.Clamp(wheel.radius, 0.02f, 2f) / ReferenceRadius;
            float widthRatio = Mathf.Clamp(wheel.width, 0.02f, 2f) / ReferenceWidth;

            // Brush model: cornering / slip stiffness grows with the contact patch, i.e. roughly
            // with sqrt(radius) * width. A stiffer patch reaches its peak at a smaller slip, so the
            // curve input is scaled up. This is geometry only; the carcass can dominate in reality,
            // which is why the coupling is a tunable strength (0 disables it).
            float contactRatio = Mathf.Clamp(Mathf.Sqrt(sizeRatio) * widthRatio, 0.5f, 2f);
            float peakSlipScale = Mathf.Pow(contactRatio, TireTuning.GeometryShapeCoupling);

            return new TireParameters(
                TireTuning.RelaxationLength * sizeRatio,
                TireTuning.RelaxationLength * sizeRatio,
                TireTuning.CamberThrust * sizeRatio,
                peakSlipScale);
        }
    }
}
