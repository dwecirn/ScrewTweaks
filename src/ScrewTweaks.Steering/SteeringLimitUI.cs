#nullable enable

using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SappUnityUtils.IO.SimpleSaveables;
using TMPro;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Adds a "Steering Limit Relax" slider to the controls settings page, right below the
    /// "Ignore Steer Angle Limit" toggle.
    ///
    /// The settings rows are laid out manually (no layout group), 60px apart, so we clone the
    /// "Steering Wheel Sensitivity" row, insert it below the toggle and push the following rows
    /// down by one row height. The value (0..1) is stored in Saveables.
    /// </summary>
    [HarmonyPatch]
    internal static class SteeringLimitUI
    {
        private const string Label = "Steering Limit Relax";
        private const float RowSpacing = 60f;

        private static readonly ConditionalWeakTable<SettingsControlsKeyboard, SappUI.Slider> Sliders = new();

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SettingsControlsKeyboard), "Start")]
        internal static void StartPostfix(SettingsControlsKeyboard __instance)
        {
            try
            {
                if (Sliders.TryGetValue(__instance, out _)) return;

                var sens = AccessTools.Field(typeof(SettingsControlsKeyboard), "sliderSteeringSensivity")?.GetValue(__instance) as SappUI.Slider;
                var toggle = AccessTools.Field(typeof(SettingsControlsKeyboard), "multiSelectIgnoreSteerAngleLimit")?.GetValue(__instance) as Component;
                if (sens == null || toggle == null) return;

                var content = sens.transform.parent;
                var clone = UnityEngine.Object.Instantiate(sens.gameObject, content);
                clone.name = "Slider - Steering Limit Relax";

                int insertIndex = toggle.transform.GetSiblingIndex() + 1;
                clone.transform.SetSiblingIndex(insertIndex);

                if (clone.transform is RectTransform cloneRt && toggle.transform is RectTransform toggleRt)
                    cloneRt.anchoredPosition = new Vector2(toggleRt.anchoredPosition.x, toggleRt.anchoredPosition.y - RowSpacing);

                for (int i = insertIndex + 1; i < content.childCount; i++)
                {
                    if (content.GetChild(i) is RectTransform rt)
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - RowSpacing);
                }
                if (content is RectTransform contentRt)
                    contentRt.sizeDelta = new Vector2(contentRt.sizeDelta.x, contentRt.sizeDelta.y + RowSpacing);

                PrepareClone(clone);

                var slider = clone.GetComponent<SappUI.Slider>();
                if (slider != null)
                {
                    slider.MinSliderValue = 0f;
                    slider.MaxSliderValue = 1f;
                    slider.IsNumeric = false;
                    AccessTools.Field(typeof(SappUI.Slider), "textAfterCommaDigits")?.SetValue(slider, 2);
                    slider.ActualValue = SteeringLimitRelax.Get();
                }

                if (slider != null) Sliders.Add(__instance, slider);
            }
            catch (Exception e)
            {
                Debug.Log("[ScrewTweaks.Steering] limit UI error: " + e);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SettingsControlsKeyboard), "ApplyValues")]
        internal static void ApplyValuesPostfix(SettingsControlsKeyboard __instance)
        {
            try
            {
                if (Sliders.TryGetValue(__instance, out var slider) && slider != null)
                    Saveables.SetValue(SteeringLimitRelax.Key, slider.ActualValue);
            }
            catch { }
        }

        /// <summary>Retitle the row and clear cloned neighbour/meta data so navigation recomputes.</summary>
        private static void PrepareClone(GameObject clone)
        {
            foreach (var comp in clone.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;

                if (typeName.IndexOf("LocalizeString", StringComparison.Ordinal) >= 0)
                {
                    var tmp = comp.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) tmp.text = Label;
                    UnityEngine.Object.Destroy(comp);
                }
                else if (typeName == "SettingsElementMetaInfo")
                {
                    // Cloned description would be wrong; drop it so nothing is shown.
                    UnityEngine.Object.Destroy(comp);
                }
            }

            var elem = clone.GetComponent<SappUI.ControllerNavigationElement>();
            if (elem == null) return;
            elem.ElementLeft = null;
            elem.ElementRight = null;
            elem.ElementUp = null;
            elem.ElementDown = null;

            foreach (var name in new[] { "elementLeft", "elementRight", "elementUp", "elementDown" })
                AccessTools.Field(typeof(SappUI.ControllerNavigationElement), name)?.SetValue(elem, null);
        }
    }
}
