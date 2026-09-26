#nullable enable

using HarmonyLib;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.ECU
{
    /// <summary>
    /// Hooks the two wheel types that carry ABS/TCS and routes the brake channel through the slot.
    /// The neutralisation itself happens in <see cref="AidApplier"/>; here we only run the plugged
    /// algorithm for modes that need it (currently ABS = Progressive).
    /// </summary>
    [HarmonyPatch]
    internal static class AidPatches
    {
        private static readonly AccessTools.FieldRef<MechanicalOutputWheel, float> WheelBrakeRef =
            AccessTools.FieldRefAccess<MechanicalOutputWheel, float>("currentBrake");

        private static readonly AccessTools.FieldRef<MechanicalOutputWheelBrake, float> BrakeWheelBrakeRef =
            AccessTools.FieldRefAccess<MechanicalOutputWheelBrake, float>("currentBrake");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechanicalOutputWheel), "Start")]
        internal static void WheelStart(MechanicalOutputWheel __instance) => AidApplier.Apply(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechanicalOutputWheelBrake), "Start")]
        internal static void BrakeWheelStart(MechanicalOutputWheelBrake __instance) => AidApplier.Apply(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechanicalOutputWheel), "FixedUpdate")]
        internal static void WheelFixedUpdate(MechanicalOutputWheel __instance)
        {
            try
            {
                if (Aids.Abs != AidMode.Progressive) return;
                if (Skip(__instance.DrivingCar) || InAir(__instance.UseNWH, __instance.WheelController)) return;

                float raw = WheelBrakeRef(__instance);
                if (raw <= 0f) return;

                float modified = Aids.ProgressiveAbs.Apply(Context(__instance, raw), raw);
                if (Mathf.Approximately(modified, raw)) return;

                WheelBrakeRef(__instance) = modified;
                ApplyBrake(__instance, modified);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechanicalOutputWheelBrake), "FixedUpdate")]
        internal static void BrakeWheelFixedUpdate(MechanicalOutputWheelBrake __instance)
        {
            try
            {
                if (Aids.Abs != AidMode.Progressive) return;
                if (Skip(__instance.DrivingCar) || InAir(__instance.UseNWH, __instance.WheelController)) return;

                float raw = BrakeWheelBrakeRef(__instance);
                if (raw <= 0f) return;

                float modified = Aids.ProgressiveAbs.Apply(Context(__instance, raw), raw);
                if (Mathf.Approximately(modified, raw)) return;

                BrakeWheelBrakeRef(__instance) = modified;
                ApplyBrake(__instance, modified);
            }
            catch { }
        }

        private static bool Skip(DrivingCar? car)
        {
            if (car == null) return true;
            return car.IsHandbraking
                || car.FullBrake
                || car.FullBrakeBecauseOfSpawning
                || car.FullBrakeBecauseOfFirstPerson
                || car.FullBrakeBecauseOfMapBuilding;
        }

        private static bool InAir(bool useNwh, WheelController? controller)
            => useNwh && controller != null && controller.isInAirOverride;

        private static AidContext Context(MechanicalOutputWheel wheel, float desiredBrake) => new AidContext
        {
            Wheel = wheel,
            ForwardSlip = ForwardSlip(wheel.UseNWH, wheel.WheelController, wheel.WheelCollider),
            SidewaySlip = SidewaySlip(wheel.UseNWH, wheel.WheelController),
            WheelRpm = WheelRpm(wheel.UseNWH, wheel.WheelController, wheel.WheelCollider),
            CarSpeed = wheel.DrivingCar != null ? wheel.DrivingCar.CurrentSpeed : 0f,
            DesiredBrake = desiredBrake
        };

        private static AidContext Context(MechanicalOutputWheelBrake wheel, float desiredBrake) => new AidContext
        {
            ForwardSlip = ForwardSlip(wheel.UseNWH, wheel.WheelController, wheel.WheelCollider),
            SidewaySlip = SidewaySlip(wheel.UseNWH, wheel.WheelController),
            WheelRpm = WheelRpm(wheel.UseNWH, wheel.WheelController, wheel.WheelCollider),
            CarSpeed = wheel.DrivingCar != null ? wheel.DrivingCar.CurrentSpeed : 0f,
            DesiredBrake = desiredBrake
        };

        private static float ForwardSlip(bool useNwh, WheelController? controller, WheelCollider? collider)
        {
            if (useNwh && controller != null) return controller.wheelHit.forwardSlip;
            if (collider != null && collider.GetGroundHit(out var hit)) return hit.forwardSlip;
            return 0f;
        }

        private static float SidewaySlip(bool useNwh, WheelController? controller)
            => useNwh && controller != null ? controller.SidewaySlipSpeed : 0f;

        private static float WheelRpm(bool useNwh, WheelController? controller, WheelCollider? collider)
        {
            if (useNwh && controller != null) return controller.rpm;
            if (collider != null) return collider.rpm;
            return 0f;
        }

        // Mirrors how the game pushes the computed brake to the wheel.
        private static void ApplyBrake(MechanicalOutputWheel wheel, float brake)
        {
            if (wheel.UseNWH)
            {
                if (wheel.WheelController != null) wheel.WheelController.brakeTorque = brake * 4f * wheel.brakeFactor;
            }
            else if (wheel.UseCW)
            {
                if (wheel.WheelColliderCW != null) wheel.WheelColliderCW.CWWheelTorqueDistr.Brake(brake);
            }
            else if (wheel.WheelCollider != null)
            {
                wheel.WheelCollider.brakeTorque = brake;
            }
        }

        private static void ApplyBrake(MechanicalOutputWheelBrake wheel, float brake)
        {
            if (wheel.UseNWH)
            {
                if (wheel.WheelController != null) wheel.WheelController.brakeTorque = brake * 4f * wheel.brakeFactor;
            }
            else if (wheel.UseCW)
            {
                if (wheel.WheelColliderCW != null) wheel.WheelColliderCW.CWWheelTorqueDistr.Brake(brake);
            }
            else if (wheel.WheelCollider != null)
            {
                wheel.WheelCollider.brakeTorque = brake;
            }
        }
    }
}
