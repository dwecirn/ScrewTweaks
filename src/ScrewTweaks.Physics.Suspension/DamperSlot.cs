#nullable enable

using HarmonyLib;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// Routes the game's damper term through the selected <see cref="IDamperModel"/>.
    ///
    /// `WheelController.SuspensionUpdate` computes, for every grounded wheel, every physics step:
    ///
    ///     damper.force = ±(coefficient * curve(|velocity|))          // + bump, - rebound
    ///     applied      = clamp(spring.force + damper.force, 0, +inf)
    ///     rigidbody.AddForceAtPosition(applied * normal * cos(angle), mountPoint)
    ///
    /// We let the game do all of that - the geometry, the bottom-out path, the hit test and the timings
    /// are easy to get subtly wrong and are not what this module is about - and then correct the damper
    /// term only:
    ///
    ///   1. write our force into `damper.force`, so the tyre load, the chassis force and the bump sound
    ///      all see the same number (`WheelUpdate` runs after this and recomputes `wheel.load` from it);
    ///   2. add the difference between what the game applied and what we want applied, at the same point
    ///      and along the same normal.
    ///
    /// The force is recomputed from scratch every step, so nothing accumulates and step 2 costs nothing
    /// when the two agree. With `Native` selected the original method is not touched at all.
    ///
    /// Note that the game freezes `damper.force` while the suspension is bottomed out instead of
    /// re-evaluating it. We do evaluate there, which is the only place this changes the game's
    /// behaviour beyond the damper law itself.
    /// </summary>
    [HarmonyPatch]
    internal static class DamperSlot
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(WheelController), "SuspensionUpdate")]
        internal static void SuspensionUpdatePostfix(WheelController __instance)
        {
            // With a vertical model selected, WheelVerticalSlot replaces SuspensionUpdate and evaluates
            // the damper itself inside its substeps. This postfix should not even run then (a prefix
            // returning false skips postfixes), but saying so here keeps it true if that ever changes.
            if (!TireVerticalModels.IsNative) return;

            var model = DamperModels.Current;
            if (model == null || model is NativeDamperModel) return;

            var wc = __instance;
            if (wc == null || !wc.hasHit) return;

            var body = wc.ActiveRigidbody;
            var spring = wc.spring;
            var damper = wc.damper;
            if (body == null || spring == null || damper == null) return;

            // What the game put into the total it just applied, stale or not.
            float gameDamperForce = damper.force;

            // Matches the game's own branch: it compares lengths, and velocity is (length - prevLength)/dt.
            bool compressing = spring.velocity <= 0f;

            float force;
            try
            {
                force = model.Evaluate(new DamperState(
                    wc, compressing, Mathf.Abs(spring.velocity), damper.bumpForce));
            }
            catch
            {
                return; // never break the suspension
            }

            if (!(force >= 0f)) force = 0f; // also catches NaN from a bad model
            damper.force = compressing ? force : -force;

            float applied = Mathf.Clamp(spring.force + gameDamperForce, 0f, float.PositiveInfinity);
            float wanted = Mathf.Clamp(spring.force + damper.force, -ReboundFloorFor(wc), float.PositiveInfinity);
            float delta = wanted - applied;
            if (Mathf.Abs(delta) < 0.01f) return;

            // Public fields only, so nothing here breaks if the game's privates get renamed. The normal
            // is the one just averaged in HitUpdate - the game's own force uses the previous step's copy,
            // which is one physics step stale. Both point roughly the same way and the correction is a
            // fraction of the total, so this is the more correct of the two rather than a compromise.
            Vector3 normal = wc.wheelHit.raycastHit.normal;
            float cos = Mathf.Cos(Vector3.Angle(-wc.wheel.up, -normal) * Mathf.Deg2Rad);
            body.AddForceAtPosition(delta * normal * cos, wc.transform.position);
        }

        /// <summary>
        /// How far below zero the total suspension force may go, in [N]. 0 is the game's own clamp, which
        /// stops the damper from ever pulling the body down.
        /// </summary>
        internal static float ReboundFloorFor(WheelController wc)
        {
            float fraction = DamperTuning.ReboundFloor;
            if (fraction <= 0f) return 0f;
            return fraction * StaticWheelLoad(wc);
        }

        /// <summary>
        /// The load this wheel carries at rest, in [N].
        ///
        /// `maxForce` is chosen so that the spring sits at roughly 55% of its travel under the car's own
        /// weight (the mass term cancels out of the equilibrium - see docs/suspension-model-spec.md), so
        /// the spring force at that compression is the nominal static load.
        /// </summary>
        private static float StaticWheelLoad(WheelController wc)
        {
            var spring = wc.spring;
            var curve = spring.forceCurve;
            float ratio = curve != null && curve.length > 0 ? curve.Evaluate(0.55f) : 0.55f;
            return Mathf.Max(spring.maxForce * ratio, 1f);
        }
    }
}
