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
    /// - relaxation length (transient slip, so the force builds over distance),
    /// - combined slip via the ADAMS friction ellipse,
    /// - camber thrust,
    /// - load sensitivity reused from the game (`loadGripCurve` / `maximumTireGripForce`).
    ///
    /// Peak grip is kept close to the native model so handling magnitude stays comparable:
    /// `peak = |D| * loadGripCurve(Fz/Fz0) * maximumTireGripForce * forceCoefficient`.
    /// </summary>
    internal sealed class PacejkaTireModel : ITireModel
    {
        private static readonly Vector4 FallbackBcde = new Vector4(11f, 2.05f, 0.925f, 0.97f);

        public string Name => "Pacejka";
        public string Description => "Game BCDE + load sensitivity + relaxation length + ADAMS friction ellipse + camber thrust.";

        public bool Apply(WheelController wc, float dt)
        {
            var wheel = wc.wheel;
            if (wheel == null || dt <= 0f) return false;

            float radius = wheel.radius;
            float inertia = Mathf.Max(wheel.inertia, 1e-4f);
            float fz = Mathf.Max(wheel.load, 0f);

            // Model parameters come from this tire, not from a global setting.
            var parameters = TireParameters.From(wheel);

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

            // --- steady-state slip ratio / slip angle (kinematic) ---
            const float minSpeed = 0.6f;
            float denom = Mathf.Max(Mathf.Abs(vx), minSpeed);
            float kappaSs = Mathf.Clamp((omega * radius - vx) / denom, -1f, 1f);
            float alphaSs = Mathf.Clamp(Mathf.Atan2(vy, denom), -Mathf.PI * 0.5f + 0.01f, Mathf.PI * 0.5f - 0.01f);

            // --- relaxation length: the tyre needs distance, not time, to build slip ---
            var state = TireStates.Get(wc);
            float sigma = parameters.SigmaK;
            float kappa;
            float alpha;
            if (sigma <= 1e-4f || !wc.hasHit)
            {
                // No lag (or airborne): follow the kinematics exactly.
                state.KappaRelaxed = kappaSs;
                state.AlphaRelaxed = alphaSs;
                kappa = kappaSs;
                alpha = alphaSs;
            }
            else
            {
                // First-order lag, rate = |Vx| / sigma. Speed is floored so the slip cannot freeze
                // at a standstill; the exponential form is stable for any step size.
                float relaxSpeed = Mathf.Max(Mathf.Abs(vx), 3f);
                state.KappaRelaxed += (kappaSs - state.KappaRelaxed) * (1f - Mathf.Exp(-relaxSpeed / parameters.SigmaK * dt));
                state.AlphaRelaxed += (alphaSs - state.AlphaRelaxed) * (1f - Mathf.Exp(-relaxSpeed / parameters.SigmaA * dt));
                kappa = state.KappaRelaxed;
                alpha = state.AlphaRelaxed;
            }

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
            // PeakSlipScale moves where the curve peaks, per tire (contact patch geometry).
            float kappaEff = kappa * wc.forwardFriction.slipCoefficient * parameters.PeakSlipScale;
            float alphaEff = alpha * wc.sideFriction.slipCoefficient * parameters.PeakSlipScale;
            float fxPure = fwdMax * Mf(b, c, e, kappaEff);
            float fyPure = sideMax * Mf(b, c, e, alphaEff);

            // --- camber thrust (native has none) is part of the lateral force ---
            float camberThrust = parameters.CamberThrust * wheel.camberAngle * fz;
            fyPure += camberThrust;

            // --- combined slip: ADAMS friction ellipse ---
            CombineAdmsEllipse(fxPure, fyPure, fwdMax, sideMax, kappa, alpha, out float fxCombined, out float fyCombined);
            float combine = Mathf.Clamp01(TireTuning.CombinedSlip);
            float fx = Mathf.Lerp(fxPure, fxCombined, combine);
            float fy = Mathf.Lerp(fyPure, fyCombined, combine);

            // --- tire reaction on the wheel spin ---
            omega -= fx * radius / inertia * dt;
            wheel.angularVelocity = omega;

            // --- output (game sign conventions: +forward, sideFriction applies along -sidewaysDir) ---
            wc.forwardFriction.force = fx;
            wc.sideFriction.force = fy;
            wc.forwardFriction.slip = kappa;
            wc.sideFriction.slip = alpha;

            bool hasIdentity = TireIdentities.TryGet(wc, out var identity);

            TireTelemetry.Report(wc, new WheelSample
            {
                Name = wc.gameObject.name,
                Kappa = kappa,
                AlphaDeg = alpha * Mathf.Rad2Deg,
                KappaRaw = kappaSs,
                AlphaRawDeg = alphaSs * Mathf.Rad2Deg,
                Fx = fx,
                Fy = fy,
                Fz = fz,
                FxMax = fwdMax,
                FyMax = sideMax,
                Vx = vx,
                Omega = omega,
                Radius = radius,
                Sigma = parameters.SigmaA,
                PeakSlipScale = parameters.PeakSlipScale,
                CamberDeg = wheel.camberAngle,
                CamberThrustForce = camberThrust,
                HasIdentity = hasIdentity,
                TireType = hasIdentity ? identity.Type : PartType.NONE,
                TireGrip = hasIdentity ? identity.Grip : 0f,
                BcdeB = b,
                BcdeC = c,
                BcdeD = d,
                BcdeE = e,
                SlipXk = kappa,
                SlipYs = Mathf.Sin(alpha),
            });

            return true;
        }

        private static float Mf(float b, float c, float e, float x)
        {
            float bx = b * x;
            return Mathf.Sin(c * Mathf.Atan(bx - e * (bx - Mathf.Atan(bx))));
        }

        /// <summary>
        /// ADAMS friction ellipse, adapted from Project Chrono's
        /// <c>ChPac02Tire::CalcFxyMz</c> (mode 4 with <c>use_friction_ellipsis</c>,
        /// `src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`, BSD-3-Clause).
        ///
        /// Unlike a naive "scale the force vector if it leaves the ellipse" clamp, this ties the
        /// split to the direction of the *slip vector*: a wheel that is sliding mostly
        /// longitudinally (locked, spinning) loses lateral capacity and vice versa. That is what
        /// makes a locked rear actually step out.
        ///
        /// Chrono: beta is the slip-vector angle, then
        ///   mu_x_c = 1 / hypot(1/mu_x_act, tan(beta)/mu_y_max)
        ///   mu_y_c = tan(beta) / hypot(1/mu_x_max, tan(beta)/mu_y_act)
        /// and the forces are rescaled with those. Substituting the definitions collapses the
        /// Fz and tan(beta) terms away, giving the closed form below.
        /// </summary>
        private static void CombineAdmsEllipse(
            float fxPure, float fyPure, float fwdMax, float sideMax,
            float kappa, float alpha, out float fx, out float fy)
        {
            fx = fxPure;
            fy = fyPure;

            if (fwdMax <= 1e-4f || sideMax <= 1e-4f)
                return;

            float a = kappa;                 // longitudinal slip component
            float b = Mathf.Sin(alpha);      // lateral slip component

            // mu_x_act / mu_y_max and mu_y_act / mu_x_max; Fz cancels out.
            float r1 = Mathf.Abs(fxPure) / sideMax;
            float r2 = Mathf.Abs(fyPure) / fwdMax;

            float dx = Mathf.Sqrt(a * a + b * b * r1 * r1);
            float dy = Mathf.Sqrt(a * a * r2 * r2 + b * b);

            fx = dx > 1e-6f ? a * Mathf.Abs(fxPure) / dx : 0f;
            fy = dy > 1e-6f ? b * Mathf.Abs(fyPure) / dy : 0f;
        }
    }
}
