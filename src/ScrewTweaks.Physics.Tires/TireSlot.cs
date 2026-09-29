#nullable enable

using HarmonyLib;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Routes the game's per-wheel friction through the selected <see cref="ITireModel"/>.
    ///
    /// `WheelController.FrictionUpdate` is private and is called from Step() right after the
    /// contact/slip inputs are prepared and before `UpdateForces` applies the forces. A prefix
    /// that returns false skips the original body; the selected model is then responsible for
    /// setting `forwardFriction.force`, `sideFriction.force` (and integrating wheel spin).
    /// </summary>
    [HarmonyPatch]
    internal static class TireSlot
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WheelController), "FrictionUpdate")]
        internal static bool FrictionUpdatePrefix(WheelController __instance)
        {
            var model = TireModels.Current;
            if (model == null || model is NativeTireModel)
                return true; // run the original

            try
            {
                return !model.Apply(__instance, Time.fixedDeltaTime);
            }
            catch
            {
                return true; // never break the wheel
            }
        }
    }
}
