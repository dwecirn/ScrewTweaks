#nullable enable

using System.Runtime.CompilerServices;
using HarmonyLib;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// Gives the wheel a vertical degree of freedom.
    ///
    /// `WheelController.SuspensionUpdate` is the game's entire vertical solver, and it is a one-mass
    /// one: it reads the ground out of the raycast and *assigns* `spring.length` so that the tyre sits
    /// exactly on it. The tyre is therefore rigid and the wheel has no inertia of its own. That cannot
    /// be fixed from outside, because the game's own bottom-out test, its force and the wheel's visible
    /// position all fall out of the length it has just written. So with a non-Native model selected this
    /// replaces the method outright - the same patch point the damper module corrects, grown into a
    /// full solver, as docs/suspension-model-spec.md predicted it would have to.
    ///
    /// Kept as the game has it: the hit test, the geometry that finds the wheel centre a tyre would
    /// need in order to just touch, the force application point, the contact-normal cosine.
    /// Deliberately different: the length is an integrated state, the tyre pushes back through
    /// <see cref="ITireVerticalModel"/>, the chassis is pulled by the wheels while airborne instead of
    /// the suspension going quiet, and the travel stops transmit whatever they have to instead of the
    /// game's separate bottom-out force term (the tyre itself is a progressive bump stop now).
    ///
    /// `Native` leaves all of this alone; the original method runs untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class WheelVerticalSlot
    {
        private sealed class State
        {
            internal bool Ready;
            internal float Length;              // suspension length [m]: the wheel's vertical position
            internal float Velocity;            // dLength/dt [m/s], positive while extending
            internal float TireForce;           // last computed tyre force [N], for the load and telemetry
            internal float Deflection;          // last tyre deflection [m]
            internal int Substeps = 1;

            internal float LastGroundLength;
            internal bool HasLastGround;
        }

        private static readonly ConditionalWeakTable<WheelController, State> States =
            new ConditionalWeakTable<WheelController, State>();

        private static readonly AccessTools.FieldRef<WheelController, float> FixedDeltaTime =
            AccessTools.FieldRefAccess<WheelController, float>("_fixedDeltaTime");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WheelController), "SuspensionUpdate")]
        internal static bool SuspensionUpdatePrefix(WheelController __instance)
        {
            if (TireVerticalModels.IsNative) return true;

            var wc = __instance;
            if (wc == null) return true;

            var spring = wc.spring;
            var damper = wc.damper;
            var body = wc.ActiveRigidbody;
            if (spring == null || damper == null || body == null) return true;

            float dt = FixedDeltaTime(wc);
            if (dt <= 0f) dt = Time.fixedDeltaTime;

            try
            {
                Solve(wc, spring, damper, body, dt);
            }
            catch
            {
                return true; // never leave a wheel with no suspension at all
            }

            return false; // the game's own solver must not run on top of ours
        }

        private static void Solve(WheelController wc, Spring spring, Damper damper, Rigidbody body, float dt)
        {
            var state = States.GetOrCreateValue(wc);
            Vector3 axis = wc.cachedTransform.up;

            // --- where the wheel centre would have to be for the tyre to just touch ----------------
            // Exactly the game's geometry, minus the assignment of spring.length that throws the gap away.
            float groundLength = float.NaN;
            if (wc.hasHit)
            {
                Vector3 point = wc.wheelHit.raycastHit.point;
                float rim = wc.wheel.rimOffset * (float)wc.vehicleSide;

                if (wc.singleRay)
                {
                    Vector3 biased = point - axis * (wc.wheel.radius * 0.06f);
                    spring.targetPoint = biased - wc.wheel.right * rim;
                }
                else
                {
                    spring.targetPoint = point
                        - wc.wheel.forward * wc.wheelHit.offset.y
                        - wc.wheel.right * wc.wheelHit.offset.x
                        - wc.wheel.right * rim;
                }

                groundLength = -wc.cachedTransform.InverseTransformPoint(spring.targetPoint).y
                               * wc.cachedTransform.lossyScale.y;
            }

            if (!state.Ready)
            {
                // Start where the game would have put it, so a freshly spawned car does not jump.
                float start = float.IsNaN(groundLength) ? spring.length : groundLength;
                state.Length = Mathf.Clamp(start, 0f, spring.maxLength);
                state.Velocity = 0f;
                state.Ready = true;
            }

            // How fast the geometry under the wheel is moving: the chassis's own motion, and the terrain's.
            float groundRate = 0f;
            if (state.HasLastGround && !float.IsNaN(groundLength) && dt > 0f)
                groundRate = (groundLength - state.LastGroundLength) / dt;
            state.LastGroundLength = groundLength;
            state.HasLastGround = true;

            float unsprungMass = TireVerticalTuning.UnsprungMass(wc);
            int substeps = TireVerticalTuning.SubstepsFor(wc, unsprungMass, dt);
            float sub = dt / substeps;
            float gravityDown = Vector3.Dot(UnityEngine.Physics.gravity, -axis);

            float length = state.Length;
            float velocity = state.Velocity;
            float tireForce = 0f;
            float deflection = 0f;
            float suspensionTotal = 0f;
            float stopReaction = 0f;

            for (int i = 0; i < substeps; i++)
            {
                // Publish the wheel's own position before asking the models about it, so what they read
                // off the DamperState is this substep's state and not the previous step's.
                spring.length = length;
                spring.velocity = velocity;
                spring.forceCurve ??= new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
                spring.compressionPercent = spring.maxLength > 0f
                    ? Mathf.Clamp01((spring.maxLength - length) / spring.maxLength)
                    : 0f;
                spring.force = spring.maxForce * spring.forceCurve.Evaluate(spring.compressionPercent);
                damper.force = 0f;

                float damperForce = DamperForce(wc, spring, damper, velocity);
                suspensionTotal = spring.force + damperForce; // up on the chassis, down on the wheel

                tireForce = EvaluateTire(wc, length, velocity, groundRate, groundLength, unsprungMass,
                    out deflection);

                // The unsprung mass: the suspension pushes it down, the ground pushes it up.
                float accel = (suspensionTotal + unsprungMass * gravityDown - tireForce) / unsprungMass;
                velocity += accel * sub;
                length += velocity * sub;

                // Travel stops. They are inelastic, and whatever they have to carry has to reach the
                // chassis as well - otherwise a fully bottomed-out suspension would let the car sink
                // through its own wheel.
                stopReaction = 0f;
                if (length <= 0f)
                {
                    length = 0f;
                    if (velocity < 0f) velocity = 0f;
                    stopReaction = -(suspensionTotal + unsprungMass * gravityDown - tireForce);
                }
                else if (length >= spring.maxLength)
                {
                    length = spring.maxLength;
                    if (velocity > 0f) velocity = 0f;
                    stopReaction = -(suspensionTotal + unsprungMass * gravityDown - tireForce);
                }
            }

            spring.length = length;
            spring.velocity = velocity;
            spring.prevLength = length;
            spring.compressionPercent = spring.maxLength > 0f
                ? Mathf.Clamp01((spring.maxLength - length) / spring.maxLength)
                : 0f;
            spring.bottomedOut = length <= 0f;
            spring.overExtended = length >= spring.maxLength;
            // spring.force and damper.force already hold the last substep's values.

            state.Length = length;
            state.Velocity = velocity;
            state.TireForce = wc.hasHit ? tireForce : 0f;
            state.Deflection = deflection;
            state.Substeps = substeps;

            // No rebound clamp here, deliberately. The game's `clamp(spring + damper, 0, +inf)` exists
            // because a rigid wheel has no inertia, so a damper pulling the body down would have nothing
            // behind it. With a wheel that can hang, a negative suspension force is the real reaction to
            // the wheel's mass, and the bound is the tyre's own one-sided force - it can push, never
            // pull. Leaving the clamp in place would silently throw away the whole rebound half of every
            // wheel hop, which is what made the wheels bounce twice off a small bump no matter how hard
            // the damper was set.
            //
            // It is still bounded, though: the body cannot be pulled down harder than the ground is
            // pushing the wheel up, because past that the wheel would simply leave the ground. Without
            // this, one bad damper coefficient - a value rescaled wrong, a hand-edited save - turns into
            // tens of kN of downward force and the car is thrown into the air on spawn.
            float support = tireForce + unsprungMass * gravityDown;
            if (support < 0f) support = 0f;

            float applied = suspensionTotal + stopReaction;
            if (applied < -support) applied = -support;

            Vector3 normal = wc.wheelHit.raycastHit.normal;
            float cos = Mathf.Cos(Vector3.Angle(-wc.wheel.up, -normal) * Mathf.Deg2Rad);
            body.AddForceAtPosition(applied * normal * cos, wc.transform.position);
        }

        private static float EvaluateTire(WheelController wc, float length, float velocity, float groundRate,
            float groundLength, float unsprungMass, out float deflection)
        {
            deflection = 0f;
            if (!wc.hasHit || float.IsNaN(groundLength)) return 0f;

            // The wheel is pressed into the ground by however far past the touching position it is.
            deflection = length - groundLength;

            var model = TireVerticalModels.Current;
            if (model == null) return 0f;

            float force = model.Evaluate(new TireVerticalState(
                wc, deflection, velocity - groundRate, unsprungMass));

            return force > 0f ? force : 0f;
        }

        /// <summary>
        /// The damper term for one substep. Goes through the selected model when there is one, and
        /// reproduces the game's own law - one coefficient, the identity curve - when there is not.
        /// </summary>
        private static float DamperForce(WheelController wc, Spring spring, Damper damper, float velocity)
        {
            bool compressing = velocity <= 0f;
            float magnitude = Mathf.Abs(velocity);

            var model = DamperModels.Current;
            if (model == null || model is NativeDamperModel)
            {
                float shape = damper.curve != null ? damper.curve.Evaluate(magnitude) : magnitude;
                return compressing
                    ? damper.bumpForce * shape
                    : -damper.reboundForce * shape;
            }

            float force = model.Evaluate(new DamperState(wc, compressing, magnitude, damper.bumpForce,
                DamperProperties.For(wc.PartConfigurationSuspension)));
            if (!(force >= 0f)) force = 0f;
            return compressing ? force : -force;
        }

        /// <summary>
        /// The load the tyre is carrying is the tyre's own vertical force now, not the suspension's -
        /// the two are no longer the same thing, and grip follows the contact patch. Runs after the game
        /// has written its own value and before FrictionUpdate reads it.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(WheelController), "WheelUpdate")]
        internal static void WheelUpdatePostfix(WheelController __instance)
        {
            if (TireVerticalModels.IsNative) return;

            var wc = __instance;
            if (wc?.wheel == null) return;
            if (!States.TryGetValue(wc, out var state) || !state.Ready) return;

            wc.wheel.load = state.TireForce;
            if (wc.hasHit) wc.wheelHit.force = state.TireForce;
        }

        /// <summary>Live values for the panel, or null when this wheel is not running the model.</summary>
        internal static (float Length, float Deflection, float TireForce, float Frequency, int Substeps)?
            Describe(WheelController wc)
        {
            if (wc == null || !States.TryGetValue(wc, out var state) || !state.Ready) return null;
            return (state.Length, state.Deflection, state.TireForce,
                TireVerticalTuning.Frequency(wc, TireVerticalTuning.UnsprungMass(wc)), state.Substeps);
        }
    }
}
