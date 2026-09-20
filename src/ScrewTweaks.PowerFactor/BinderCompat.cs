#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ScrewTweaks.PowerFactor
{
    /// <summary>
    /// The game ships a SafeSerializationBinder that refuses to deserialize any type
    /// that does not live in the game's own assembly. This hardening was added after
    /// this mod was written.
    ///
    /// Our PowerFactorFunction is serialized into the .ccc car caches, so without this
    /// patch the binder refuses it, CalculatedCar.FromBytes() returns null and cars
    /// fail to spawn / the car selection UI throws.
    ///
    /// This allowlists only this mod's own assembly; the game's security checks stay
    /// in place for every other (untrusted) type.
    /// </summary>
    internal static class BinderCompat
    {
        internal static void Apply(ManualLogSource log)
        {
            try
            {
                var binderType = AccessTools.TypeByName("SafeSerializationBinder");
                var isAllowed = binderType == null ? null : AccessTools.Method(binderType, "IsAllowed");
                if (isAllowed == null)
                {
                    log.LogWarning("BinderCompat: SafeSerializationBinder.IsAllowed not found; skipping.");
                    return;
                }

                var postfix = AccessTools.Method(typeof(BinderCompat), nameof(Postfix));
                var harmony = new Harmony(PluginInfo.GUID + ".binder");
                harmony.Patch(isAllowed, postfix: new HarmonyMethod(postfix!));

                log.LogInfo("BinderCompat: allowlisted ScrewTweaks types for deserialization.");
            }
            catch (Exception ex)
            {
                log.LogError($"BinderCompat failed: {ex}");
            }
        }

        private static void Postfix(Type type, ref bool __result)
        {
            try
            {
                if (!__result && type != null && type.Assembly == typeof(Plugin).Assembly)
                    __result = true;
            }
            catch
            {
                // Never let the guard itself break the serializer.
            }
        }
    }
}
