#nullable enable

using HarmonyLib;
using NWH.WheelController3D;

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

        /// <summary>Tracks / tracked vehicles go through a separate setter.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(WheelPropertiesSetter), nameof(WheelPropertiesSetter.SetTankWheelProperties))]
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
