#nullable enable

using HarmonyLib;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Captures which tire part each wheel was built from. `SetWheelProperties` is the single point
    /// where the game pairs a WheelController with its CalculatedPartWheel, and `Part.MakePart`
    /// exposes the compound's Grip / WheelRadius / WheelWidth, so this is where per-tire data has
    /// to come from.
    /// </summary>
    [HarmonyPatch]
    internal static class TireIdentityPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(WheelPropertiesSetter), nameof(WheelPropertiesSetter.SetWheelProperties))]
        internal static void AfterSetWheelProperties(WheelPropertiesSetter __instance, CalculatedPartWheel calculatedPartWheel)
        {
            Capture(__instance, calculatedPartWheel.PartConfiguration.partType);
        }

        /// <summary>
        /// Tracks / tracked vehicles go through a separate setter. There are two overloads of it
        /// (the 7-argument one just forwards to this one), so the parameter list is given
        /// explicitly instead of letting Harmony resolve the overload by name.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(
            typeof(WheelPropertiesSetter),
            nameof(WheelPropertiesSetter.SetTankWheelProperties),
            new[]
            {
                typeof(CalculatedGearChain),
                typeof(float),
                typeof(int),
                typeof(GameObject),
                typeof(Rigidbody),
                typeof(float),
                typeof(float),
                typeof(TankTrackSuspensionSettings),
            })]
        internal static void AfterSetTankWheelProperties(WheelPropertiesSetter __instance, CalculatedGearChain calculatedGearChain)
        {
            Capture(__instance, calculatedGearChain.PartConfiguration.partType);
        }

        private static void Capture(WheelPropertiesSetter setter, PartType type)
        {
            try
            {
                var wheel = setter.transform.GetComponentInChildren<WheelController>();
                if (wheel == null) return;

                var part = Part.MakePart(type);

                TireIdentities.Set(wheel, new TireIdentity
                {
                    Type = type,
                    Grip = part.Grip,
                    PartRadius = part.WheelRadius,
                    PartWidth = part.WheelWidth,
                    Captured = true,
                });
            }
            catch
            {
                // Identity is a nice-to-have; never break spawning.
            }
        }
    }
}
