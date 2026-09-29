#nullable enable

using HarmonyLib;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Routes the game's per-wheel friction through the selected <see cref="ITireModel"/>.
    ///
    /// `WheelController.FrictionUpdate` is private and does more than compute forces: it also
    /// publishes the contact speeds, the wheel RPM and the wheel-hit slip values that the game's
    /// drivetrain (`MechanicalOutputWheel`) and its TCS/ABS read back. Skipping it without
    /// reproducing that bookkeeping decouples the engine from the wheels, so the host does it here
    /// for every model.
    ///
    /// Contract for a model: fill `forwardFriction` / `sideFriction` `force` and `slip`, and
    /// integrate `wheel.angularVelocity`. The contact speeds are already filled in before the call.
    /// </summary>
    [HarmonyPatch]
    internal static class TireSlot
    {
        private static readonly AccessTools.FieldRef<WheelController, float> FixedDeltaTime =
            AccessTools.FieldRefAccess<WheelController, float>("_fixedDeltaTime");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WheelController), "FrictionUpdate")]
        internal static bool FrictionUpdatePrefix(WheelController __instance)
        {
            var model = TireModels.Current;
            if (model == null || model is NativeTireModel)
                return true; // run the original

            var wc = __instance;

            float dt = FixedDeltaTime(wc);
            if (dt <= 0f)
                dt = Time.fixedDeltaTime;

            UpdateContactSpeeds(wc);

            bool handled;
            try
            {
                handled = model.Apply(wc, dt);
            }
            catch
            {
                return true; // never break the wheel
            }

            if (!handled)
                return true;

            // The tail of the native FrictionUpdate: drivetrain + TCS feedback.
            wc.wheel.RPM = wc.wheel.angularVelocity * 9.55f;
            if (wc.hasHit)
            {
                wc.wheelHit.forwardSlip = wc.forwardFriction.slip;
                wc.wheelHit.sidewaysSlip = wc.sideFriction.slip;
            }

            return false; // skip the original body
        }

        /// <summary>Native-equivalent contact speeds, read back by the drivetrain and TCS.</summary>
        private static void UpdateContactSpeeds(WheelController wc)
        {
            if (!wc.hasHit || wc.ActiveRigidbody == null)
            {
                wc.forwardFriction.speed = 0f;
                wc.sideFriction.speed = 0f;
                return;
            }

            var wheel = wc.wheel;
            Vector3 velocity = wc.ActiveRigidbody.GetPointVelocity(wheel.worldPosition - wheel.up * wheel.radius);
            var ground = wc.wheelHit.raycastHit.rigidbody;
            if (ground != null)
                velocity -= ground.GetPointVelocity(wc.wheelHit.raycastHit.point);

            wc.forwardFriction.speed = Vector3.Dot(velocity, wc.wheelHit.forwardDir);
            wc.sideFriction.speed = Vector3.Dot(velocity, wc.wheelHit.sidewaysDir);
        }
    }
}
