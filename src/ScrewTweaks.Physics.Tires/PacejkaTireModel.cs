#nullable enable

using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// First real model. Keeps the game's per-surface `FrictionPreset` curve (a Magic-Formula
    /// shape, `D*sin(C*atan(Bx - E*(Bx - atan(Bx))))`) as the pure-slip shape, and adds the
    /// physics the native model is missing:
    ///
    /// - proper slip ratio / slip angle (own wheel-spin integration),
    /// - friction ellipse for combined slip (native only clamps the vector magnitude),
    /// - camber thrust,
    /// - load sensitivity reused from the game (`loadGripCurve` / `maximumTireGripForce`).
    ///
    /// Peak grip is intentionally matched to the native model so handling magnitude stays
    /// comparable: `peak = |D| * loadGripCurve(Fz/Fz0) * maximumTireGripForce * forceCoefficient`.
    /// </summary>
    internal sealed class PacejkaTireModel : ITireModel
    {
        private static readonly Vector4 FallbackBcde = new Vector4(11f, 2.05f, 0.925f, 0.97f);

        public string Name => "Pacejka";
        public string Description => "Game BCDE as pure-slip shape + load sensitivity + friction ellipse + camber thrust + own wheel-spin integration.";

        public bool Apply(WheelController wc, float dt)
        {
            var wheel = wc.wheel;
            if (wheel == null || dt <= 0f) return false;

            float radius = wheel.radius;
            float inertia = Mathf.Max(wheel.inertia, 1e-4f);
            float fz = Mathf.Max(wheel.load, 0f);

            // Contact speeds are filled in by the slot before Apply() is called.
            float vx = wc.forwardFriction.speed;
            float vy = wc.sideFriction.speed;

            // --- wheel spin: drive + brake first (gives the slip-producing omega) ---
            float omega = wheel.angularVelocity;
            omega += wheel.motorTorque / inertia * dt;

            float maxBrakeTorque = Mathf.Abs(omega) * inertia / dt;
            float brakeTorque = Mathf.Min(Mathf.Abs(wheel.brakeTorque), maxBrakeTorque);
            if (Mathf.Abs(omega) > 1e-5f)
                omega -= Mathf.Sign(omega) * brakeTorque / inertia * dt;

            // --- slip ratio / slip angle ---
            const float minSpeed = 0.6f;
            float denom = Mathf.Max(Mathf.Abs(vx), minSpeed);
            float kappa = Mathf.Clamp((omega * radius - vx) / denom, -1f, 1f);
            float alpha = Mathf.Clamp(Mathf.Atan2(vy, denom), -Mathf.PI * 0.5f + 0.01f, Mathf.PI * 0.5f - 0.01f);

            // --- parameters from the game's data ---
            Vector4 bcde = wc.activeFrictionPreset != null ? wc.activeFrictionPreset.BCDE : FallbackBcde;
            float b = bcde.x, c = bcde.y, d = bcde.z, e = bcde.w;

            float maxLoad = Mathf.Max(wc.maximumTireLoad, 1f);
            float loadGrip = wc.loadGripCurve != null
                ? wc.loadGripCurve.Evaluate(Mathf.Clamp01(fz / maxLoad))
                : 1f;

            float loadCoeff = loadGrip * wc.maximumTireGripForce * TireTuning.GripScale;
            float fwdMax = Mathf.Abs(d) * loadCoeff * wc.forwardFriction.forceCoefficient;
            float sideMax = Mathf.Abs(d) * loadCoeff * wc.sideFriction.forceCoefficient;

            // --- pure slip (the game's BCDE is the shape; D is factored into the max) ---
            float kappaEff = kappa * wc.forwardFriction.slipCoefficient;
            float alphaEff = alpha * wc.sideFriction.slipCoefficient;
            float fx = fwdMax * Mf(b, c, e, kappaEff);
            float fy = sideMax * Mf(b, c, e, alphaEff);

            // --- combined slip: friction ellipse ---
            float sx = fwdMax > 1e-4f ? Mathf.Abs(fx) / fwdMax : 0f;
            float sy = sideMax > 1e-4f ? Mathf.Abs(fy) / sideMax : 0f;
            float combined = Mathf.Sqrt(sx * sx + sy * sy);
            if (combined > 1f)
            {
                fx /= combined;
                fy /= combined;
            }

            // --- camber thrust (native has none) ---
            fy += TireTuning.CamberThrust * wheel.camberAngle * fz;

            // --- tire reaction on the wheel spin ---
            omega -= fx * radius / inertia * dt;
            wheel.angularVelocity = omega;

            // --- output (game sign conventions: +forward, sideFriction applies along -sidewaysDir) ---
            wc.forwardFriction.force = fx;
            wc.sideFriction.force = fy;
            wc.forwardFriction.slip = kappa;
            wc.sideFriction.slip = alpha;
            return true;
        }

        private static float Mf(float b, float c, float e, float x)
        {
            float bx = b * x;
            return Mathf.Sin(c * Mathf.Atan(bx - e * (bx - Mathf.Atan(bx))));
        }
    }
}
