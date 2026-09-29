#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SappUnityUtils.Graphs;

namespace ScrewTweaks.PowerFactor
{
    /// Entry point for Power Factor feature — called from Plugin.Update() (B key).
    public static class PowerFactorFeature
    {
        public static void SyncAll() => PowerFactorSync.SyncFromCarProperties();
    }

    // ====== POWER FACTOR CACHE ======

    /// Stores per-engine-type powerfactor. Read by PowerFactorFunction at evaluation time.
    public static class PowerFactorSync
    {
        internal const string PropertyName = "powerfactor";
        internal static readonly Dictionary<PartType, float> MultiplierCache = new Dictionary<PartType, float>();

        public static float GetMultiplier(PartType partType)
        {
            return MultiplierCache.TryGetValue(partType, out float m) ? m : 1f;
        }

        internal static void SetMultiplier(PartType partType, float multiplier)
        {
            MultiplierCache[partType] = Mathf.Max(0f, multiplier);
        }

        /// Rebuild cache from a given CarPropertiesSetting (analysis time).
        public static void SyncFromCarProperties(CarPropertiesSetting properties)
        {
            try
            {
                if (properties == null) return;

                foreach (var config in properties.PartConfigs)
                {
                    try
                    {
                        var part = Part.MakePart(config.partType);
                        if (!part.IsMotor) continue;

                        var props = properties.GetProperties(config);
                        if (props == null) continue;

                        foreach (var prop in props)
                        {
                            if (prop.PropertyName != PropertyName) continue;
                            float val = 100f;
                            float.TryParse(prop.PropertyType?.Value?.ToString(), out val);
                            SetMultiplier(config.partType, val / 100f);
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// Rebuild cache from ManagerBuilding (panel close / B key).
        public static void SyncFromCarProperties()
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<ManagerBuilding>(FindObjectsInactive.Exclude);
            if (manager?.CarPropertiesSetting != null)
                SyncFromCarProperties(manager.CarPropertiesSetting);
        }
    }

    // ====== TORQUE FUNCTION WRAPPER ======

    /// Wraps any TorqueFunction to multiply its output by the cached powerfactor.
    /// Survives Clone() because the PartType is preserved in the new instance.
    [Serializable]
    internal class PowerFactorFunction : TorqueFunction
    {
        // Set by the two-argument constructor, or populated by Unity's deserializer when a car cache
        // is loaded. The parameterless constructor exists only so that deserialization can run, which
        // is why the field is left unset there.
        private TorqueFunction _inner = null!;

        private PartType _partType;

        // Parameterless constructor for Unity serialization
        public PowerFactorFunction() { }

        public PowerFactorFunction(TorqueFunction inner, PartType partType)
        {
            _inner = inner;
            _partType = partType;
        }

        public override float f(float x)
        {
            return _inner.f(x) * PowerFactorSync.GetMultiplier(_partType);
        }

        public override TorqueFunction Clone()
        {
            return new PowerFactorFunction(_inner.Clone(), _partType);
        }

        public override float ProvideRelativeTransmission()
        {
            return _inner.ProvideRelativeTransmission();
        }
    }

    // ====== CAR ANALYZER PATCH ======

    /// Wrap the torque function returned by CarAnalyzer2 with our powerfactor multiplier.
    [HarmonyPatch(typeof(CarAnalyzer2))]
    internal static class CarAnalyzerPatches
    {
        /// Populate multiplier cache from CarPropertiesSetting before analysis runs.
        /// This ensures the cache is filled even when properties are loaded from save (Free Drive),
        /// where AddPart() patches may not fire during deserialization.
        [HarmonyPrefix]
        [HarmonyPatch("AnalyzeCar")]
        internal static void AnalyzeCarPrefix(CarPropertiesSetting properties)
        {
            PowerFactorSync.SyncFromCarProperties(properties);
        }

        [HarmonyPostfix]
        [HarmonyPatch("improveEngineTorqueFunctionWithGadgets")]
        internal static void Postfix(ref TorqueFunction __result, Node<PartConfiguration> engineNode)
        {
            try
            {
                // Always wrap so the PowerFactorFunction reads from cache at evaluation time.
                // This ensures Free Drive works even when cache wasn't populated at analysis time
                // (e.g. slider value changed after car was already analyzed).
                if (__result != null)
                    __result = new PowerFactorFunction(__result, engineNode.ID.partType);
            }
            catch { }
        }
    }

    // ====== CCC DESERIALIZATION PATCH ======

    /// After Deserializing a CalculatedCar from .ccc, wrap all engine torque
    /// functions that aren't already PowerFactorFunction wrappers. This ensures
    /// old .ccc files (created before the mod) also get wrapped functions.
    [HarmonyPatch(typeof(CalculatedCar))]
    internal static class CalculatedCarPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("FromBytes")]
        internal static void FromBytesPostfix(ref CalculatedCar __result)
        {
            if (__result == null) return;
            try
            {
                foreach (var part in __result.Parts)
                {
                    if (part.CalculatedAxe == null) continue;
                    var funcs = part.CalculatedAxe.CalculatedDependingPowerfunctions;
                    if (funcs == null) continue;
                    foreach (var cdpf in funcs)
                    {
                        if (cdpf?.torqueFunction == null) continue;
                        if (cdpf.torqueFunction is PowerFactorFunction) continue;
                        cdpf.torqueFunction = new PowerFactorFunction(cdpf.torqueFunction, part.PartConfiguration.partType);
                    }
                }
            }
            catch { }
        }
    }

    // ====== CAR SPAWN CACHE POPULATION ======

    /// When a car is spawned (Free Drive or Test Drive), populate the multiplier
    /// cache from the CarPropertiesSetting embedded in the baguette bytes.
    [HarmonyPatch(typeof(SimpleCar2))]
    internal static class SimpleCar2Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch("ApplyCar", typeof(CalculatedCar), typeof(byte[]), typeof(byte[]))]
        internal static void ApplyCarPostfix(byte[] baguetteBytes)
        {
            try
            {
                if (baguetteBytes == null || baguetteBytes.Length == 0) return;
                var properties = FileLoader.LoadSettingsFromBytes(baguetteBytes);
                PowerFactorSync.SyncFromCarProperties(properties);
            }
            catch { }
        }
    }

    // ====== PROPERTY INJECTION ======

    /// Injects powerfactor into engine parts' PartProperties on placement and load.
    [HarmonyPatch]
    internal static class EnginePropertyInjection
    {
        internal static PartProperty CreatePowerFactorProp()
        {
            var prop = new PartProperty
            {
                DisplayName = "Power Factor",
                PropertyName = PowerFactorSync.PropertyName,
                PropertyType = new PropString()
            };
            prop.PropertyType.SetFromString("100");
            return prop;
        }

        private static bool IsEnginePart(PartType partType)
        {
            try { return Part.MakePart(partType).IsMotor; }
            catch { return false; }
        }

        private static void Inject(CarPropertiesSetting instance, PartConfiguration partConfig)
        {
            try
            {
                if (!IsEnginePart(partConfig.partType)) return;
                var props = instance.GetProperties(partConfig);
                if (props == null) return;

                // If powerfactor already exists, update cache with saved value
                foreach (var prop in props)
                {
                    if (prop.PropertyName == PowerFactorSync.PropertyName)
                    {
                        float val = 100f;
                        float.TryParse(prop.PropertyType?.Value?.ToString(), out val);
                        PowerFactorSync.SetMultiplier(partConfig.partType, val / 100f);
                        return;
                    }
                }

                // Inject new powerfactor (default 100%)
                var newProps = new PartProperty[props.Length + 1];
                Array.Copy(props, newProps, props.Length);
                newProps[newProps.Length - 1] = CreatePowerFactorProp();
                instance.SetProperties(partConfig, newProps);
                PowerFactorSync.SetMultiplier(partConfig.partType, 1f);
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarPropertiesSetting), "AddPart", typeof(PartConfiguration))]
        internal static void AddPartPostfix(CarPropertiesSetting __instance, PartConfiguration partConfig)
            => Inject(__instance, partConfig);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarPropertiesSetting), "AddPart", typeof(PartConfiguration), typeof(PartProperty[]))]
        internal static void AddPartWithPropsPostfix(CarPropertiesSetting __instance, PartConfiguration partConfig, PartProperty[] props)
            => Inject(__instance, partConfig);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarPropertiesSetting), "GetProperties", typeof(PartConfiguration))]
        internal static void GetPropertiesPostfix(CarPropertiesSetting __instance, PartConfiguration partConfig, ref PartProperty[] __result)
        {
            if (__result == null) return;
            if (!IsEnginePart(partConfig.partType)) return;
            if (__result.Any(p => p.PropertyName == PowerFactorSync.PropertyName)) return;

            var newProps = new PartProperty[__result.Length + 1];
            Array.Copy(__result, newProps, __result.Length);
            newProps[newProps.Length - 1] = CreatePowerFactorProp();
            __instance.SetProperties(partConfig, newProps);
            __result = newProps;
            PowerFactorSync.SetMultiplier(partConfig.partType, 1f);
        }
    }

    // ====== GUI SLIDER ======

    /// Replaces the default text input with a slider for the powerfactor property.
    [HarmonyPatch(typeof(GUIPropertiesManager))]
    internal static class GUIPropertySlider
    {
        private static FieldInfo? _prefabField;
        private static readonly string[] PrefabFieldNames = {
            "prefabElementPistonSpeed", "prefabElementPercentage", "prefabElementBrakeForce",
            "prefabElementSpringForce", "prefabElementDamperForce", "prefabElementProgressiveness",
            "prefabElementCamberAngle", "prefabElementStepperSpeed", "prefabElementTurnDegree"
        };

        private static GameObject? FindPrefab(GUIPropertiesManager instance)
        {
            if (_prefabField != null)
                return _prefabField.GetValue(instance) as GameObject;

            foreach (var name in PrefabFieldNames)
            {
                var field = typeof(GUIPropertiesManager).GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) continue;
                if (field.GetValue(instance) is GameObject prefab)
                {
                    _prefabField = field;
                    return prefab;
                }
            }
            return null;
        }

        /// Ensure powerfactor exists in PartProperties before GUI creates elements.
        [HarmonyPrefix]
        [HarmonyPatch("GUIShown", MethodType.Setter)]
        internal static void OnGUIShownPrefix(GUIPropertiesManager __instance, bool value)
        {
            if (!value || __instance.PartProperties == null) return;

            try { if (!Part.MakePart(__instance.PartType).IsMotor) return; }
            catch { return; }

            // Update cache from the slider's PartProperties (may already be set from saved data)
            foreach (var prop in __instance.PartProperties)
            {
                if (prop.PropertyName == PowerFactorSync.PropertyName)
                {
                    float val = 100f;
                    float.TryParse(prop.PropertyType?.Value?.ToString(), out val);
                    PowerFactorSync.SetMultiplier(__instance.PartType, val / 100f);
                    return;
                }
            }

            // Inject into GUI copy if missing
            var old = __instance.PartProperties;
            var newProps = new PartProperty[old.Length + 1];
            Array.Copy(old, newProps, old.Length);
            newProps[newProps.Length - 1] = EnginePropertyInjection.CreatePowerFactorProp();
            __instance.PartProperties = newProps;
            PowerFactorSync.SetMultiplier(__instance.PartType, 1f);

            // Also persist into CarPropertiesSetting
            try
            {
                var manager = AccessTools.Field(typeof(GUIPropertiesManager), "manager")
                    ?.GetValue(__instance) as ManagerBuilding;
                if (manager?.CarPropertiesSetting != null)
                {
                    foreach (var cfg in manager.CarPropertiesSetting.PartConfigs)
                    {
                        if (cfg.partType != __instance.PartType) continue;
                        var stored = manager.CarPropertiesSetting.GetProperties(cfg);
                        if (stored == null || stored.Any(p => p.PropertyName == PowerFactorSync.PropertyName))
                            continue;
                        var storedNew = new PartProperty[stored.Length + 1];
                        Array.Copy(stored, storedNew, stored.Length);
                        storedNew[storedNew.Length - 1] = EnginePropertyInjection.CreatePowerFactorProp();
                        manager.CarPropertiesSetting.SetProperties(cfg, storedNew);
                        break;
                    }
                }
            }
            catch { }
        }

        /// Every frame while panel is open: sync slider → cache + persist to storage.
        [HarmonyPostfix]
        [HarmonyPatch("Update")]
        internal static void OnUpdatePostfix(GUIPropertiesManager __instance)
        {
            if (!__instance.GUIShown || __instance.PartProperties == null) return;

            foreach (var prop in __instance.PartProperties)
            {
                if (prop.PropertyName != PowerFactorSync.PropertyName) continue;
                float val = 100f;
                float.TryParse(prop.PropertyType?.Value?.ToString(), out val);
                PowerFactorSync.SetMultiplier(__instance.PartType, val / 100f);
                PersistToStored(__instance, prop);
                return;
            }
        }

        private static void PersistToStored(GUIPropertiesManager __instance, PartProperty guiProp)
        {
            try
            {
                var manager = AccessTools.Field(typeof(GUIPropertiesManager), "manager")
                    ?.GetValue(__instance) as ManagerBuilding;
                if (manager?.CarPropertiesSetting == null) return;

                foreach (var cfg in manager.CarPropertiesSetting.PartConfigs)
                {
                    if (cfg.partType != __instance.PartType) continue;
                    var stored = manager.CarPropertiesSetting.GetProperties(cfg);
                    if (stored == null) continue;
                    foreach (var storedProp in stored)
                    {
                        if (storedProp.PropertyName == PowerFactorSync.PropertyName)
                        {
                            storedProp.PropertyType.SetFromString(guiProp.PropertyType.ToString());
                            return;
                        }
                    }
                    return;
                }
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch("GUIShown", MethodType.Setter)]
        internal static void OnGUIShown(GUIPropertiesManager __instance)
        {
            // Panel closing — persist and rebuild cache
            if (!__instance.GUIShown)
            {
                if (__instance.PartProperties != null)
                {
                    foreach (var prop in __instance.PartProperties)
                    {
                        if (prop.PropertyName == PowerFactorSync.PropertyName)
                        {
                            PersistToStored(__instance, prop);
                            break;
                        }
                    }
                }
                PowerFactorSync.SyncFromCarProperties();
                return;
            }

            // Panel opening — replace text input with slider
            if (__instance.PartProperties == null) return;

            int idx = -1;
            for (int i = 0; i < __instance.PartProperties.Length; i++)
            {
                if (__instance.PartProperties[i].PropertyName == PowerFactorSync.PropertyName)
                { idx = i; break; }
            }
            if (idx < 0 || idx >= __instance.InstObjects.Count) return;

            var oldGO = __instance.InstObjects[idx];
            if (oldGO == null) return;

            var prefab = FindPrefab(__instance);
            if (prefab == null) return;

            var newGO = UnityEngine.Object.Instantiate(prefab, oldGO.transform.parent);
            newGO.transform.localScale = oldGO.transform.localScale;
            newGO.transform.localPosition = oldGO.transform.localPosition;

            var slider = newGO.GetComponentInChildren<Slider>(true);
            if (slider != null)
            {
                slider.minValue = 0f;
                slider.maxValue = 1000f;
            }

            var oldElem = oldGO.GetComponent<GUIPropertiesElement>();
            var newElem = newGO.GetComponent<GUIPropertiesElement>();
            if (oldElem != null && newElem != null)
            {
                newElem.DisplayName = oldElem.DisplayName;
                newElem.PropertyName = oldElem.PropertyName;
                newElem.InputValue = oldElem.InputValue;
            }

            __instance.InstObjects[idx] = newGO;
            UnityEngine.Object.Destroy(oldGO);
        }

        /// Persist on destroy (panel destroyed without formal close).
        [HarmonyPrefix]
        [HarmonyPatch("OnDestroy")]
        internal static void OnDestroyPrefix(GUIPropertiesManager __instance)
        {
            if (!__instance.GUIShown || __instance.PartProperties == null) return;

            foreach (var prop in __instance.PartProperties)
            {
                if (prop.PropertyName != PowerFactorSync.PropertyName) continue;
                float val = 100f;
                float.TryParse(prop.PropertyType?.Value?.ToString(), out val);
                PowerFactorSync.SetMultiplier(__instance.PartType, val / 100f);
                PersistToStored(__instance, prop);
                break;
            }
        }
    }
}
