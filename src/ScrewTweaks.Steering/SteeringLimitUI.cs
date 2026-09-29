#nullable enable

using System;
using HarmonyLib;
using SappUI;
using TMPro;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Adds the two steering rows to the game's own controls settings page, right below the
    /// "Ignore Steer Angle Limit" selector:
    ///
    ///   - **Steering Limit Relax**, a slider that blends the game's speed-sensitive steering limit away;
    ///   - **Instant Steering**, the same kind of on/off selector the game uses for the ignore-limit
    ///     setting, so binary (keyboard / d-pad) steering is applied in one frame.
    ///
    /// Both are stored in Saveables, which is where the game keeps its own controls settings, so the page
    /// and the panel read and write the same values.
    ///
    /// The settings rows are laid out manually (no layout group), 60px apart, so two rows are cloned from
    /// the game's own widgets, inserted below the selector, and the rows that follow are pushed down.
    /// </summary>
    [HarmonyPatch]
    internal static class SteeringLimitUI
    {
        private const string RelaxLabel = "Steering Limit Relax";
        private const string InstantLabel = "Instant Steering";
        private const float RowSpacing = 60f;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SettingsControlsKeyboard), "Start")]
        internal static void StartPostfix(SettingsControlsKeyboard __instance)
        {
            try
            {
                var slider = AccessTools.Field(typeof(SettingsControlsKeyboard), "sliderSteeringSensivity")
                    ?.GetValue(__instance) as Slider;
                var toggle = AccessTools.Field(typeof(SettingsControlsKeyboard), "multiSelectIgnoreSteerAngleLimit")
                    ?.GetValue(__instance) as MultiSelect;
                if (slider == null || toggle == null) return;

                var content = slider.transform.parent;
                int insertIndex = toggle.transform.GetSiblingIndex() + 1;

                var relax = Clone(slider.gameObject, content, insertIndex, toggle, 1, RelaxLabel);
                if (relax != null)
                {
                    relax.transform.SetSiblingIndex(insertIndex);
                    var relaxSlider = relax.GetComponent<Slider>();
                    if (relaxSlider != null)
                    {
                        relaxSlider.MinSliderValue = 0f;
                        relaxSlider.MaxSliderValue = 1f;
                        relaxSlider.IsNumeric = false;
                        AccessTools.Field(typeof(Slider), "textAfterCommaDigits")?.SetValue(relaxSlider, 2);
                        relaxSlider.ActualValue = SteeringSettings.LimitRelax;
                    }
                }

                var instant = Clone(toggle.gameObject, content, insertIndex + 1, toggle, 2, InstantLabel);
                if (instant != null)
                {
                    instant.transform.SetSiblingIndex(insertIndex + 1);
                    instant.GetComponent<MultiSelect>()?.ForceSelectElement(SteeringSettings.InstantSteering ? 1 : 0);
                }

                PushRowsDown(content, insertIndex + 2, RowSpacing * 2f);
            }
            catch (Exception e)
            {
                Debug.Log("[ScrewTweaks.Steering] settings rows error: " + e);
            }
        }

        /// <summary>
        /// The rows are positioned by hand, so inserting two of them means moving everything after them -
        /// and the page's content is what provides the scroll room.
        /// </summary>
        private static void PushRowsDown(Transform content, int from, float amount)
        {
            for (int i = from; i < content.childCount; i++)
            {
                if (content.GetChild(i) is RectTransform rt)
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - amount);
            }

            if (content is RectTransform contentRect)
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, contentRect.sizeDelta.y + amount);
        }

        /// <summary>
        /// Clone one of the game's rows in below the selector, push everything after it down, and give it
        /// our label. <paramref name="rowsDown"/> is how many rows this one sits below the selector, which
        /// is what the rows after both clones get shifted by.
        /// </summary>
        private static GameObject? Clone(GameObject source, Transform content, int index, Component anchor,
            int rowsDown, string label)
        {
            var clone = UnityEngine.Object.Instantiate(source, content);
            clone.name = "Row - " + label;

            if (clone.transform is RectTransform cloneRect && anchor.transform is RectTransform anchorRect)
                cloneRect.anchoredPosition = new Vector2(anchorRect.anchoredPosition.x,
                    anchorRect.anchoredPosition.y - RowSpacing * rowsDown);

            PrepareClone(clone, label);
            return clone;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SettingsControlsKeyboard), "ApplyValues")]
        internal static void ApplyValuesPostfix(SettingsControlsKeyboard __instance)
        {
            try
            {
                var instant = FindRow(__instance, InstantLabel)?.GetComponent<MultiSelect>();
                if (instant != null)
                    SteeringSettings.InstantSteering = instant.CurrentlySelectedIndex == 1;

                var relax = FindRow(__instance, RelaxLabel)?.GetComponent<Slider>();
                if (relax != null)
                    SteeringSettings.LimitRelax = relax.ActualValue;
            }
            catch
            {
                // ignore
            }
        }

        private static GameObject? FindRow(SettingsControlsKeyboard instance, string label)
        {
            var slider = AccessTools.Field(typeof(SettingsControlsKeyboard), "sliderSteeringSensivity")
                ?.GetValue(instance) as Slider;
            var content = slider != null ? slider.transform.parent : null;
            if (content == null) return null;

            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                if (child != null && child.name == "Row - " + label) return child.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Retitle the row and clear cloned neighbour/meta data so navigation recomputes.
        /// </summary>
        private static void PrepareClone(GameObject clone, string label)
        {
            foreach (var comp in clone.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;

                if (typeName.IndexOf("LocalizeString", StringComparison.Ordinal) >= 0)
                {
                    var tmp = comp.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) tmp.text = label;
                    UnityEngine.Object.Destroy(comp);
                }
                else if (typeName == "SettingsElementMetaInfo")
                {
                    // Cloned description would be wrong; drop it so nothing is shown.
                    UnityEngine.Object.Destroy(comp);
                }
            }

            var elem = clone.GetComponent<ControllerNavigationElement>();
            if (elem == null) return;
            elem.ElementLeft = null;
            elem.ElementRight = null;
            elem.ElementUp = null;
            elem.ElementDown = null;

            foreach (var name in new[] { "elementLeft", "elementRight", "elementUp", "elementDown" })
                AccessTools.Field(typeof(ControllerNavigationElement), name)?.SetValue(elem, null);
        }
    }
}
