#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ScrewTweaks.Panel;
using TMPro;
using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// One suspension part's damper setup: the four coefficients of a four-way damper.
    ///
    /// These live on the part, not in the mod's config, because they are a property of the car - like
    /// `springforce` and `damperforce`, which the game already stores per part in the car's own save
    /// file. A car shared with someone else carries its damper setup with it.
    ///
    /// They are a multiple of the game's own coefficient, which `damperforce` still sets: the game's
    /// slider is the overall stiffness, these four are its shape. 1.00 is the game exactly, so
    /// 100/100/100/100 on every part is the same as not having the model selected at all.
    /// </summary>
    public readonly struct DamperSetup
    {
        internal DamperSetup(float bumpLow, float bumpHigh, float reboundLow, float reboundHigh)
        {
            BumpLow = bumpLow;
            BumpHigh = bumpHigh;
            ReboundLow = reboundLow;
            ReboundHigh = reboundHigh;
        }

        /// <summary>Bump below the knee: the bleed, which controls the body over slow inputs.</summary>
        public float BumpLow { get; }

        /// <summary>Bump above the knee, shim stack open. What a kerb sees.</summary>
        public float BumpHigh { get; }

        /// <summary>Rebound below the knee. What arrests the body after a bump.</summary>
        public float ReboundLow { get; }

        /// <summary>Rebound above the knee. Raising it stops the car launching after a landing.</summary>
        public float ReboundHigh { get; }

        /// <summary>What the game does on its own: all four at 1.00.</summary>
        internal static DamperSetup Default => new DamperSetup(1.6f, 0.4f, 3.2f, 0.8f);
    }

    /// <summary>
    /// The four damper properties, their injection into every suspension part, and the widget they are
    /// edited with. Modelled on ScrewTweaks.Steering's `instantsteering`, which does the same thing for a
    /// single toggle.
    /// </summary>
    internal static class DamperProperties
    {
        internal const string BumpLowName = "damperbumplow";
        internal const string BumpHighName = "damperbumphigh";
        internal const string ReboundLowName = "damperreboundlow";
        internal const string ReboundHighName = "damperreboundhigh";

        // Stored as a percentage of the game's own coefficient, like every other suspension property in
        // the game is stored as a percentage, so the builder's existing slider prefab works unchanged.
        private const int BumpLowDefault = 160;
        private const int BumpHighDefault = 40;
        private const int ReboundLowDefault = 320;
        private const int ReboundHighDefault = 80;
        private const int MaxPercent = 500;

        internal static readonly string[] Names =
        {
            BumpLowName, BumpHighName, ReboundLowName, ReboundHighName
        };

        /// <summary>
        /// Display captions. Read by the game's own builder panel, so they go through the panel's
        /// localisation rather than the game's; switching language needs the car reopened to take effect.
        /// </summary>
        private static string Caption(string key)
        {
            return Loc.T(key);
        }

        internal static void Localize()
        {
            Loc.Add(PanelLanguage.ChineseSimplified,
                ("Damper bump, slow", "阻尼·压缩 慢速"),
                ("Damper bump, fast", "阻尼·压缩 快速"),
                ("Damper rebound, slow", "阻尼·回弹 慢速"),
                ("Damper rebound, fast", "阻尼·回弹 快速"));

            Loc.Add(PanelLanguage.Japanese,
                ("Damper bump, slow", "ダンパー 圧縮 低速"),
                ("Damper bump, fast", "ダンパー 圧縮 高速"),
                ("Damper rebound, slow", "ダンパー 伸張 低速"),
                ("Damper rebound, fast", "ダンパー 伸張 高速"));
        }

        private static PartProperty Create(string propertyName, string captionKey, int percent)
        {
            var property = new PartProperty
            {
                DisplayName = Caption(captionKey),
                PropertyName = propertyName,
                PropertyType = new PropString()
            };
            property.PropertyType.SetFromString(percent.ToString());
            return property;
        }

        internal static PartProperty[] CreateAll()
        {
            return new[]
            {
                Create(BumpLowName, "Damper bump, slow", BumpLowDefault),
                Create(BumpHighName, "Damper bump, fast", BumpHighDefault),
                Create(ReboundLowName, "Damper rebound, slow", ReboundLowDefault),
                Create(ReboundHighName, "Damper rebound, fast", ReboundHighDefault)
            };
        }

        internal static bool Has(PartProperty[] props)
        {
            return props.Any(p => p.PropertyName == BumpLowName);
        }

        /// <summary>
        /// Whether this part is one the damping applies to. Asked behaviourally - a suspension is a part
        /// that has the game's own damper property - rather than by type, since the parts have no common
        /// base class for "is a suspension".
        /// </summary>
        internal static bool Applies(PartProperty[]? props)
        {
            return props != null && props.Any(p => p.PropertyName == "damperforce");
        }

        /// <summary>Insert the four right after the game's own damper row, or repoint existing ones.</summary>
        internal static PartProperty[] Rebuild(PartProperty[] props)
        {
            var existing = new Dictionary<string, PartProperty>();
            foreach (var prop in props)
            {
                if (Names.Contains(prop.PropertyName)) existing[prop.PropertyName] = prop;
            }

            var list = props.Where(p => !Names.Contains(p.PropertyName)).ToList();

            int at = list.FindIndex(p => p.PropertyName == "damperforce");
            int insertAt = at >= 0 ? at + 1 : list.Count;

            foreach (var created in CreateAll())
            {
                // A property loaded from a car keeps its saved *value*, but its caption is taken from the
                // current language: the caption itself is part of what the game writes to the car file,
                // so without this a car saved in one language would keep that language's labels.
                if (existing.TryGetValue(created.PropertyName, out var kept))
                {
                    kept.DisplayName = created.DisplayName;
                    list.Insert(insertAt++, kept);
                }
                else
                {
                    list.Insert(insertAt++, created);
                }
            }

            return list.ToArray();
        }

        /// <summary>Read the setup out of a part's properties, or false when the part does not carry one.</summary>
        internal static bool TryRead(PartProperty[]? props, out DamperSetup setup)
        {
            setup = default;
            if (props == null) return false;

            bool found = false;
            float bumpLow = 1f, bumpHigh = 1f, reboundLow = 1f, reboundHigh = 1f;

            foreach (var prop in props)
            {
                int value;
                try
                {
                    value = Convert.ToInt32(prop.PropertyType?.Value);
                }
                catch
                {
                    continue;
                }

                if (prop.PropertyName == BumpLowName) { bumpLow = value / 100f; found = true; }
                else if (prop.PropertyName == BumpHighName) { bumpHigh = value / 100f; found = true; }
                else if (prop.PropertyName == ReboundLowName) { reboundLow = value / 100f; found = true; }
                else if (prop.PropertyName == ReboundHighName) { reboundHigh = value / 100f; found = true; }
            }

            if (!found) return false;

            setup = new DamperSetup(bumpLow, bumpHigh, reboundLow, reboundHigh);
            return true;
        }

        // ---------------------------------------------------------------------------------------------
        // The setup a running car is using, keyed by the part it belongs to and filled when the car is
        // applied. Empty means "not seen yet", in which case the defaults are used - the same
        // lazy-identity caveat the tire module has.
        // ---------------------------------------------------------------------------------------------

        private static readonly Dictionary<PartConfiguration, DamperSetup> Cache =
            new Dictionary<PartConfiguration, DamperSetup>();

        /// <summary>
        /// The car currently being built or driven. Filled from the patches that already have it (they run
        /// whenever the game adds a part or asks for its properties), so the per-step physics never has to
        /// search the scene.
        /// </summary>
        internal static CarPropertiesSetting? Settings;

        /// <summary>Last resort, for a car that was already in the world before this plugin loaded.</summary>
        internal static void FindSettings()
        {
            try
            {
                Settings ??= UnityEngine.Object.FindAnyObjectByType<ManagerBuilding>()?.CarPropertiesSetting;
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>The setup of a running car's suspension part, or the defaults.</summary>
        internal static DamperSetup For(PartConfiguration? suspension)
        {
            if (suspension == null) return DamperSetup.Default;
            if (Cache.TryGetValue(suspension, out var setup)) return setup;

            var settings = Settings;
            if (settings != null && TryRead(settings.GetProperties(suspension), out setup)) return setup;

            return DamperSetup.Default;
        }

        internal static void ClearCache()
        {
            Cache.Clear();
        }

        internal static void Remember(PartConfiguration suspension, DamperSetup setup)
        {
            Cache[suspension] = setup;
        }
    }

    /// <summary>Adds the four properties to every suspension part, in cars already saved as well.</summary>
    [HarmonyPatch]
    internal static class DamperPropertyInjection
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarPropertiesSetting), "AddPart", typeof(PartConfiguration))]
        internal static void AddPartPostfix(CarPropertiesSetting __instance, PartConfiguration partConfig)
            => Inject(__instance, partConfig);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarPropertiesSetting), "AddPart", typeof(PartConfiguration), typeof(PartProperty[]))]
        internal static void AddPartWithPropsPostfix(CarPropertiesSetting __instance, PartConfiguration partConfig)
            => Inject(__instance, partConfig);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarPropertiesSetting), "GetProperties", typeof(PartConfiguration))]
        internal static void GetPropertiesPostfix(
            CarPropertiesSetting __instance,
            PartConfiguration partConfig,
            ref PartProperty[] __result)
        {
            DamperProperties.Settings = __instance;
            if (!TryRebuild(__result, out var rebuilt)) return;
            __instance.SetProperties(partConfig, rebuilt);
            __result = rebuilt;
        }

        private static void Inject(CarPropertiesSetting instance, PartConfiguration partConfig)
        {
            if (instance == null || partConfig == null) return;
            DamperProperties.Settings = instance;
            if (!TryRebuild(instance.GetProperties(partConfig), out var rebuilt)) return;
            instance.SetProperties(partConfig, rebuilt);
        }

        private static bool TryRebuild(PartProperty[]? props, out PartProperty[] rebuilt)
        {
            rebuilt = Array.Empty<PartProperty>();
            if (!DamperProperties.Applies(props)) return false;
            if (props != null && DamperProperties.Has(props)) return false;

            rebuilt = DamperProperties.Rebuild(props!);
            return true;
        }
    }

    /// <summary>
    /// Gives the injected properties the game's own slider widget. GUIPropertiesManager builds one
    /// element per property and has no prefab for names it does not know, so ours would not render at
    /// all; this puts the damper slider prefab where each of them belongs.
    /// </summary>
    [HarmonyPatch(typeof(GUIPropertiesManager))]
    internal static class DamperPropertyGui
    {
        [HarmonyPostfix]
        [HarmonyPatch("GUIShown", MethodType.Setter)]
        internal static void OnShownPostfix(GUIPropertiesManager __instance)
        {
            try
            {
                if (!__instance.GUIShown || __instance.PartProperties == null) return;

                var prefab = AccessTools.Field(typeof(GUIPropertiesManager), "prefabElementDamperForce")
                    ?.GetValue(__instance) as GameObject;
                if (prefab == null) return;

                for (int index = 0; index < __instance.PartProperties.Length; index++)
                {
                    if (index >= __instance.InstObjects.Count) break;

                    var property = __instance.PartProperties[index];
                    if (!DamperProperties.Names.Contains(property.PropertyName)) continue;

                    var oldGo = __instance.InstObjects[index];
                    var parent = oldGo != null ? oldGo.transform.parent : null;
                    var newGo = UnityEngine.Object.Instantiate(prefab, parent);
                    if (newGo == null) continue;
                    if (oldGo != null)
                    {
                        newGo.transform.localScale = oldGo.transform.localScale;
                        newGo.transform.localPosition = oldGo.transform.localPosition;
                    }

                    // Range before value: the prefab is the Damper Force slider and stops at 100, so a
                    // coefficient of 320 would be clamped on the way in.
                    Widen(newGo);
                    Caption(newGo, property.DisplayName);

                    var newElem = newGo.GetComponent<GUIPropertiesElement>();
                    if (newElem != null)
                    {
                        newElem.PropertyName = property.PropertyName;
                        newElem.InputValue = property.PropertyType.ToString();
                    }

                    __instance.InstObjects[index] = newGo;
                    if (oldGo != null) UnityEngine.Object.Destroy(oldGo);
                }
            }
            catch
            {
                // never break the builder
            }
        }

        /// <summary>
        /// Replace the prefab's own caption. It is the game's "Damper Force" label, carried by a
        /// localisation component on a child text; left alone, every injected row would read "Damper
        /// Force" as well - which looks exactly like the game's row having been duplicated.
        /// </summary>
        private static void Caption(GameObject element, string caption)
        {
            foreach (var component in element.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                if (component.GetType().Name.IndexOf("LocalizeString", StringComparison.Ordinal) < 0) continue;

                var text = component.GetComponent<TextMeshProUGUI>();
                if (text != null) text.text = caption;
                UnityEngine.Object.Destroy(component);
            }
        }

        /// <summary>
        /// The damper slider stops at 100 because the game's own property does; a coefficient can be up
        /// to 500% of it.
        /// </summary>
        private static void Widen(GameObject element)
        {
            foreach (var slider in element.GetComponentsInChildren<SappUI.Slider>(true))
            {
                slider.MinSliderValue = 0f;
                slider.MaxSliderValue = 500f;
                AccessTools.Field(typeof(SappUI.Slider), "textAfterCommaDigits")?.SetValue(slider, 0);
            }
        }
    }

    /// <summary>Fills the per-part cache whenever a car is applied to the world.</summary>
    [HarmonyPatch]
    internal static class DamperSetupReader
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimpleCar2), "ApplyCar", typeof(CalculatedCar), typeof(byte[]), typeof(byte[]))]
        internal static void ApplyCarPostfix(SimpleCar2 __instance, CalculatedCar car)
        {
            try
            {
                DamperProperties.ClearCache();
                if (car?.Parts == null) return;

                foreach (var part in car.Parts)
                {
                    if (part == null) continue;
                    var configuration = part.PartConfiguration;
                    if (configuration == null) continue;
                    if (!DamperProperties.TryRead(part.Properties, out var setup)) continue;

                    DamperProperties.Remember(configuration, setup);
                }
            }
            catch
            {
                // a car without our properties just uses the defaults
            }
        }
    }
}
