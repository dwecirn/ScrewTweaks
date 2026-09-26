#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Makes the injected "instantsteering" toggle usable in the suspension properties panel.
    ///
    /// GUIPropertiesManager builds one element per property, choosing the prefab by property
    /// name. Unknown names fall through to "prefabElement", which in the shipping game is a
    /// placeholder that does not render. We replace the element created for our property with
    /// the same toggle prefab the game uses for "issteerable".
    /// </summary>
    [HarmonyPatch(typeof(GUIPropertiesManager))]
    internal static class SteeringSpeedGui
    {
        [HarmonyPrefix]
        [HarmonyPatch("GUIShown", MethodType.Setter)]
        internal static void OnShownPrefix(GUIPropertiesManager __instance, bool value)
        {
            try
            {
                if (!value || __instance.PartProperties == null) return;
                if (!SteeringSpeed.IsSteerSusp(__instance.PartType)) return;

                bool hasNew = SteeringSpeed.HasProperty(__instance.PartProperties);
                bool hasLegacy = SteeringSpeed.HasLegacyProperty(__instance.PartProperties);
                if (hasNew && !hasLegacy) return;

                var newProps = SteeringSpeed.Rebuild(__instance.PartProperties, SteeringSpeed.Create());
                __instance.PartProperties = newProps;
                MirrorToStorage(__instance, newProps);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GUIShown", MethodType.Setter)]
        internal static void OnShownPostfix(GUIPropertiesManager __instance)
        {
            try
            {
                if (!__instance.GUIShown || __instance.PartProperties == null) return;

                int index = Array.FindIndex(__instance.PartProperties, p => p.PropertyName == SteeringSpeed.PropertyName);
                if (index < 0 || index >= __instance.InstObjects.Count) return;

                var prefab = AccessTools.Field(typeof(GUIPropertiesManager), "prefabElementSteering")?.GetValue(__instance) as GameObject;
                if (prefab == null) return;

                var oldGo = __instance.InstObjects[index];
                var parent = oldGo != null ? oldGo.transform.parent : null;
                var newGo = UnityEngine.Object.Instantiate(prefab, parent);
                if (oldGo != null)
                {
                    newGo.transform.localScale = oldGo.transform.localScale;
                    newGo.transform.localPosition = oldGo.transform.localPosition;
                }

                var oldElem = oldGo != null ? oldGo.GetComponent<GUIPropertiesElement>() : null;
                var newElem = newGo.GetComponent<GUIPropertiesElement>();
                if (newElem != null)
                {
                    newElem.DisplayName = oldElem != null ? oldElem.DisplayName : SteeringSpeed.DisplayName;
                    newElem.PropertyName = SteeringSpeed.PropertyName;
                    newElem.InputValue = __instance.PartProperties[index].PropertyType.ToString();
                }

                __instance.InstObjects[index] = newGo;
                if (oldGo != null) UnityEngine.Object.Destroy(oldGo);
            }
            catch { }
        }

        private static void MirrorToStorage(GUIPropertiesManager gui, PartProperty[] props)
        {
            try
            {
                var manager = AccessTools.Field(typeof(GUIPropertiesManager), "manager")?.GetValue(gui) as ManagerBuilding;
                var settings = manager?.CarPropertiesSetting;
                if (settings == null) return;

                foreach (var cfg in settings.PartConfigs)
                {
                    if (cfg.partType != gui.PartType) continue;
                    settings.SetProperties(cfg, props);
                    break;
                }
            }
            catch { }
        }
    }
}
