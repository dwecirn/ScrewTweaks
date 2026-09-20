#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SappAudio;
using SkrilStudio;
using UnityEngine;
using ScrewTweaks.PowerFactor;

namespace ScrewTweaks.EngineSound
{
    /// Ensures that hybrid vehicles (combustion + electric motors) produce
    /// sound from both engine types. The stock selector picks only ONE
    /// CarEngineSoundConfiguration; if that config has no prefab for the
    /// other engine type, the corresponding sound is completely absent.
    ///
    /// Additionally assigns dynamic volume per engine-type group based on
    /// instantaneous torque at the current wheel speed, so that e.g.
    /// electric motors are louder off the line while combustion engines
    /// dominate at higher RPM.
    [HarmonyPatch]
    internal static class EngineSoundManager
    {
        // ====== SimpleCar2.initializeEngineSoundRealizer — logging ======

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimpleCar2), "initializeEngineSoundRealizer")]
        internal static void OnInitializeEngineSoundRealizer(
            CalculatedCar car,
            MechanicalOutputShifter moShifter,
            CarStats carStats)
        {
            try
            {
                var combustionTypes = new List<string>();
                var electricTypes = new List<string>();
                var gadgetTypes = new List<string>();

                for (int i = 0; i < car.Parts.Count; i++)
                {
                    var cp = car.Parts[i];
                    if (cp?.PartConfiguration == null) continue;

                    var partType = cp.PartConfiguration.partType;
                    var part = Part.MakePart(partType);
                    if (part == null) continue;

                    if (part.IsEngineImprovementGadget)
                    {
                        gadgetTypes.Add(partType.ToString());
                    }
                    else if (part.IsMotor || part.IsServoMotor)
                    {
                        var name = partType.ToString();
                        if (name.IndexOf("Electro", StringComparison.OrdinalIgnoreCase) >= 0)
                            electricTypes.Add(name);
                        else
                            combustionTypes.Add(name);
                    }
                }

                Debug.Log($"[ScrewTweaks] ===== Engine Sound Realizer Init =====");
                Debug.Log($"[ScrewTweaks]   Combustion: {string.Join(", ", combustionTypes)}");
                Debug.Log($"[ScrewTweaks]   Electric:   {string.Join(", ", electricTypes)}");
                Debug.Log($"[ScrewTweaks]   Gadgets:    {string.Join(", ", gadgetTypes)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScrewTweaks] Error in OnInitializeEngineSoundRealizer: {ex.Message}");
            }
        }

        // ====== CarEngineSoundSelector.GetCarEngineSoundConfiguration — logging ======

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CarEngineSoundSelector), "GetCarEngineSoundConfiguration")]
        internal static void OnGetCarEngineSoundConfigPrefix(PartType[] combustionEngines)
        {
            try
            {
                var types = string.Join(", ", combustionEngines.Select(pt => pt.ToString()));
                Debug.Log($"[ScrewTweaks] GetCarEngineSoundConfiguration: [{types}]");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScrewTweaks] Error in GetCarEngineSoundConfig prefix: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CarEngineSoundSelector), "GetCarEngineSoundConfiguration")]
        internal static void OnGetCarEngineSoundConfigPostfix(
            PartType[] combustionEngines,
            ref CarEngineSoundConfiguration __result)
        {
            try
            {
                var configsField = AccessTools.Field(typeof(CarEngineSoundSelector), "_carEngineSoundConfigurations");
                var configs = configsField?.GetValue(null) as CarEngineSoundConfiguration[];
                if (configs != null)
                {
                    foreach (var cfg in configs)
                    {
                        if (cfg == null) continue;
                        float dist = cfg.DistanceTo(combustionEngines);
                        var prefabCount = cfg.EngineSoundsPrefabs?.Length ?? 0;
                        int r = 0, e = 0;
                        if (cfg.EngineSoundsPrefabs != null)
                        {
                            foreach (var p in cfg.EngineSoundsPrefabs)
                            {
                                if (p == null) continue;
                                if (p.GetComponent<RealisticEngineSound>() != null) r++;
                                if (p.GetComponent<ElectricCarSounds>() != null) e++;
                            }
                        }
                        Debug.Log($"[ScrewTweaks]   {cfg.name}: dist={dist,8:F1}  prefabs={prefabCount} (R={r}, E={e}){(cfg == __result ? "  <-- SELECTED" : "")}");
                    }
                }

                if (__result == null)
                {
                    Debug.Log($"[ScrewTweaks] => NO matching config found!");
                }
                else
                {
                    var prefabNames = __result.EngineSoundsPrefabs?
                        .Select(g => g?.name ?? "null")
                        .ToArray() ?? Array.Empty<string>();
                    Debug.Log($"[ScrewTweaks] => SELECTED: \"{__result.name}\"  prefabs={string.Join(", ", prefabNames)}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScrewTweaks] Error in GetCarEngineSoundConfig postfix: {ex.Message}");
            }
        }

        // ====== Per-type engine sound groups + dynamic volume ======

        private static readonly FieldInfo FiRealisticEngineSounds =
            AccessTools.Field(typeof(EngineSoundRealizer), "_realisticEngineSounds");
        private static readonly FieldInfo FiElectricCarSounds =
            AccessTools.Field(typeof(EngineSoundRealizer), "_electricCarSounds");
        private static readonly FieldInfo FiCarEngineSoundConfig =
            AccessTools.Field(typeof(EngineSoundRealizer), "_carEngineSoundConfiguration");
        private static readonly FieldInfo FiInstShiftingSound =
            AccessTools.Field(typeof(EngineSoundRealizer), "_instShiftingSound");
        private static readonly FieldInfo FiStraightCutGearbox =
            AccessTools.Field(typeof(EngineSoundRealizer), "_straightCutGearbox");

        /// Estimate max power = max(torque × RPM) across the engine's operating range.
        /// For AnimationCurve-based functions reads keyframes directly (fast);
        /// otherwise samples at 100 points up to MotorTopspeed.
        private static float GetMaxPower(Part part)
        {
            var tf = part.TorqueFunction;
            if (tf == null) return 0f;

            if (tf is TorqueFunctionAnimationCurve curveTf)
            {
                var timesField  = AccessTools.Field(typeof(TorqueFunctionAnimationCurve), "animCurveTimes");
                var valuesField = AccessTools.Field(typeof(TorqueFunctionAnimationCurve), "animCurveValues");
                var times  = timesField?.GetValue(curveTf)  as float[];
                var values = valuesField?.GetValue(curveTf) as float[];
                if (times != null && values != null && times.Length > 0)
                {
                    float maxPower = 0f;
                    for (int i = 0; i < times.Length; i++)
                    {
                        float torque = values[i] * tf.YScale;
                        float rpm    = times[i]  * tf.XScale;
                        float power  = torque * rpm;
                        if (power > maxPower) maxPower = power;
                    }
                    return maxPower;
                }
            }

            // Fallback for TorqueFunctionCombustion and others: sample 100 points
            float topSpeed = Mathf.Max(part.MotorTopspeed, 1f);
            float maxP = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float rpm    = topSpeed * i / 100f;
                float torque = tf.f(rpm);
                float p      = torque * rpm;
                if (p > maxP) maxP = p;
            }
            return maxP;
        }

        /// Power rating for one engine type (proxied as peakTorque * MotorTopspeed).
        private readonly struct EnginePower
        {
            public readonly PartType Type;
            public readonly int Count;
            public readonly float Power;     // horsepower proxy

            public EnginePower(PartType type, int count, float power)
            {
                Type = type; Count = count; Power = power;
            }
        }

        /// A group of engine types that share one CarEngineSoundConfiguration.
        private sealed class SoundGroup
        {
            public EnginePower[] Engines = Array.Empty<EnginePower>();
            public List<RealisticEngineSound> RealisticSounds = new();
            public List<ElectricCarSounds> ElectricSounds = new();
            public float TotalPower; // Sum(Engines[i].Power * Engines[i].Count)
        }

        private static readonly Dictionary<EngineSoundRealizer, SoundGroup[]> _instanceGroups = new();

        // --- SetMotorPartConfigurations -------------------------------------------
        // Replaces the stock sound instantiation: for each distinct engine
        // PartType, queries its own CarEngineSoundConfiguration, instantiates
        // its prefabs, and groups the resulting components by config.

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EngineSoundRealizer), "SetMotorPartConfigurations")]
        internal static void FixHybridEngineSounds(
            EngineSoundRealizer __instance,
            List<PartConfiguration> partConfigurations)
        {
            try
            {
                // 1 - count engines by distinct PartType
                var engineCounts = new Dictionary<PartType, int>();
                foreach (var pc in partConfigurations)
                {
                    if (pc == null) continue;
                    var part = Part.MakePart(pc.partType);
                    if (part == null || !part.IsMotor || part.IsEngineImprovementGadget) continue;
                    engineCounts.TryGetValue(pc.partType, out int c);
                    engineCounts[pc.partType] = c + 1;
                }

                // Single type - stock handles it fine
                if (engineCounts.Count <= 1) return;

                // 2 - for each PartType find its matching config; group by config
                var configGroups = new Dictionary<CarEngineSoundConfiguration, List<EnginePower>>();
                foreach (var kv in engineCounts)
                {
                    var config = CarEngineSoundSelector.GetCarEngineSoundConfiguration(new[] { kv.Key });
                    if (config == null) continue;
                    var part = Part.MakePart(kv.Key);
                    float basePower = GetMaxPower(part);
                    float pfMultiplier = PowerFactorSync.GetMultiplier(kv.Key);
                    float power = basePower * pfMultiplier;
                    var ep = new EnginePower(kv.Key, kv.Value, power);

                    if (!configGroups.TryGetValue(config, out var list))
                        configGroups[config] = list = new List<EnginePower>();
                    list.Add(ep);
                }
                if (configGroups.Count == 0) return;

                // 3 - save old sound GOs so we can destroy them after replacement
                var oldGOs = new HashSet<GameObject>();
                void CollectOld(Array? arr)
                {
                    if (arr == null) return;
                    foreach (var obj in arr)
                    {
                        if (obj is RealisticEngineSound res && res != null)
                            oldGOs.Add(res.gameObject);
                        else if (obj is ElectricCarSounds ecs && ecs != null)
                            oldGOs.Add(ecs.gameObject);
                    }
                }
                CollectOld(FiRealisticEngineSounds?.GetValue(__instance) as Array);
                CollectOld(FiElectricCarSounds?.GetValue(__instance) as Array);

                // 4 - instantiate prefabs per config group
                var groups = new List<SoundGroup>();
                var allRealistic = new List<RealisticEngineSound>();
                var allElectric  = new List<ElectricCarSounds>();

                foreach (var kv in configGroups)
                {
                    var config  = kv.Key;
                    var engines = kv.Value;
                    float groupPower = 0f;
                    foreach (var ep in engines)
                        groupPower += ep.Power * ep.Count;
                    var sg      = new SoundGroup { Engines = engines.ToArray(), TotalPower = groupPower };

                    if (config.EngineSoundsPrefabs != null)
                    {
                        foreach (var prefab in config.EngineSoundsPrefabs)
                        {
                            if (prefab == null) continue;
                            var go = UnityEngine.Object.Instantiate(prefab, __instance.transform);
                            go.transform.localPosition = Vector3.zero;

                            var res = go.GetComponent<RealisticEngineSound>();
                            if (res != null)
                            {
                                res.maxRPMLimit = config.MaxRPM;
                                res.minDistance = __instance.engineSoundsMinDistance;
                                sg.RealisticSounds.Add(res);
                                allRealistic.Add(res);
                                oldGOs.Remove(go);    // don't destroy our own instantiations
                                continue;
                            }

                            var ecs = go.GetComponent<ElectricCarSounds>();
                            if (ecs != null)
                            {
                                ecs.minDistance = __instance.engineSoundsMinDistance;
                                sg.ElectricSounds.Add(ecs);
                                allElectric.Add(ecs);
                                oldGOs.Remove(go);
                            }
                        }
                    }

                    groups.Add(sg);
                }

                // 5 - replace sound arrays on the instance
                FiRealisticEngineSounds?.SetValue(__instance, allRealistic.ToArray());
                FiElectricCarSounds?.SetValue(__instance, allElectric.ToArray());

                // 6 - destroy old GameObjects (no longer referenced)
                foreach (var go in oldGOs)
                    UnityEngine.Object.Destroy(go);

                // 7 - reparent shifting / gearbox sounds to our first realistic engine
                if (allRealistic.Count > 0)
                {
                    var parent = allRealistic[0].transform;
                    var shiftGO = FiInstShiftingSound?.GetValue(__instance) as GameObject;
                    if (shiftGO != null)
                        shiftGO.transform.SetParent(parent, false);
                    var gearbox = FiStraightCutGearbox?.GetValue(__instance) as Component;
                    if (gearbox != null)
                        gearbox.transform.SetParent(parent, false);
                }

                // 8 - store for Update-time dynamic volume
                _instanceGroups[__instance] = groups.ToArray();

                // 9 - log summary
                var desc = string.Join(", ", configGroups.Select(kv =>
                    $"\"{kv.Key.name}\" [{string.Join(", ", kv.Value.Select(e => $"{e.Type}x{e.Count} ({e.Power:F0})"))}]"));
                Debug.Log($"[ScrewTweaks] Per-type sound groups: {desc}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScrewTweaks] Error in FixHybridEngineSounds: {ex.Message}");
            }
        }

        // --- UpdateSounds ---------------------------------------------------------
        // Every frame: assign volume proportional to each group's pre-computed
        // power rating, so high-power engines always dominate regardless of RPM.

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EngineSoundRealizer), "UpdateSounds")]
        internal static void DynamicVolumeByPower(
            EngineSoundRealizer __instance)
        {
            if (!_instanceGroups.TryGetValue(__instance, out var groups))
                return;
            if (groups == null || groups.Length == 0)
                return;

            // Compute base volume (matches setVolumeOfAllComponents logic)
            float masterVol = __instance.engineSoundsMasterVolume;
            var config = FiCarEngineSoundConfig?.GetValue(__instance) as CarEngineSoundConfiguration;
            if (config != null && config.OverrideEngineVolume > 0.0)
                masterVol = config.OverrideEngineVolume;
            float baseVol = VolumeManager.GetVolume(SoundCategory.Engines) * __instance.VolumeFactor * masterVol;

            // Total power across all groups
            float totalPower = 0f;
            for (int i = 0; i < groups.Length; i++)
                totalPower += groups[i].TotalPower;
            if (totalPower <= 0f) return;

            // Set volume per group proportional to its power share
            for (int i = 0; i < groups.Length; i++)
            {
                float ratio = groups[i].TotalPower / totalPower;
                float vol   = baseVol * ratio;

                foreach (var res in groups[i].RealisticSounds)
                {
                    if (res != null)
                    {
                        res.masterVolume     = vol;
                        res.windMasterVolume = vol;
                    }
                }
                foreach (var ecs in groups[i].ElectricSounds)
                {
                    if (ecs != null)
                    {
                        ecs.masterVolume     = vol;
                        ecs.windMasterVolume = vol;
                    }
                }
            }
        }

        // --- OnDestroy ------------------------------------------------------------
        // Prevent dictionary leak when the car is destroyed.

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EngineSoundRealizer), "OnDestroy")]
        internal static void OnDestroyCleanup(EngineSoundRealizer __instance)
        {
            _instanceGroups.Remove(__instance);
        }
    }
}
