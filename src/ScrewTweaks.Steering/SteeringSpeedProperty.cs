#nullable enable

using System;
using System.Linq;
using HarmonyLib;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// Adds an "Instant Steering" on/off property to every steerable suspension part,
    /// inserted directly below the game's "issteerable" property.
    ///
    /// The value is a plain 0/1 game PropInt, so it travels with the car's .baguette/.ccc
    /// files and needs no custom serialized type. When enabled, binary (keyboard / d-pad)
    /// steering is applied instantly instead of being ramped by FrontWheelsSteerer.
    /// </summary>
    internal static class SteeringSpeed
    {
        internal const string PropertyName = "instantsteering";
        internal const string LegacyPropertyName = "steerspeed";
        internal const string DisplayName = "Instant Steering";
        internal const string DefaultValue = "0";

        internal static PartProperty Create()
        {
            var prop = new PartProperty
            {
                DisplayName = DisplayName,
                PropertyName = PropertyName,
                PropertyType = new PropInt()
            };
            prop.PropertyType.SetFromString(DefaultValue);
            return prop;
        }

        internal static bool IsSteerSusp(PartType partType)
        {
            try { return Part.MakePart(partType).IsSteerSusp; }
            catch { return false; }
        }

        internal static bool HasProperty(PartProperty[] props)
            => props.Any(p => p.PropertyName == PropertyName);

        internal static bool HasLegacyProperty(PartProperty[] props)
            => props.Any(p => p.PropertyName == LegacyPropertyName);

        /// <summary>
        /// Rebuild the property list: drop the legacy "steerspeed" entry and insert the
        /// "instantsteering" toggle right after "issteerable".
        /// </summary>
        internal static PartProperty[] Rebuild(PartProperty[] props, PartProperty prop)
        {
            var list = props.Where(p => p.PropertyName != LegacyPropertyName).ToList();

            int at = list.FindIndex(p => p.PropertyName == "issteerable");
            int insertAt = at >= 0 ? at + 1 : list.Count;
            list.Insert(insertAt, prop);

            return list.ToArray();
        }
    }

    [HarmonyPatch]
    internal static class SteeringSpeedInjection
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
            if (!TryRebuild(partConfig, __result, out var rebuilt)) return;
            __instance.SetProperties(partConfig, rebuilt);
            __result = rebuilt;
        }

        private static void Inject(CarPropertiesSetting instance, PartConfiguration partConfig)
        {
            if (instance == null || partConfig == null) return;
            if (!SteeringSpeed.IsSteerSusp(partConfig.partType)) return;

            var props = instance.GetProperties(partConfig);
            if (!TryRebuild(partConfig, props, out var rebuilt)) return;

            instance.SetProperties(partConfig, rebuilt);
        }

        private static bool TryRebuild(PartConfiguration partConfig, PartProperty[]? props, out PartProperty[] rebuilt)
        {
            rebuilt = Array.Empty<PartProperty>();
            if (props == null || partConfig == null) return false;
            if (!SteeringSpeed.IsSteerSusp(partConfig.partType)) return false;

            bool hasNew = SteeringSpeed.HasProperty(props);
            bool hasLegacy = SteeringSpeed.HasLegacyProperty(props);
            if (hasNew && !hasLegacy) return false;

            rebuilt = SteeringSpeed.Rebuild(props, SteeringSpeed.Create());
            return true;
        }
    }
}
