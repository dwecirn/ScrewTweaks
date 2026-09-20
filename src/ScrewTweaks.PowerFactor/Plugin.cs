#nullable enable

using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace ScrewTweaks.PowerFactor
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.powerfactor";
        public const string Name = "Screw Tweaks - Power Factor";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private void Start()
        {
            // PowerFactor is fully automatic (Harmony patches drive injection, sync and
            // persistence); it does not need a keybind.

            // PowerFactor serializes a custom TorqueFunction into the .ccc car cache,
            // so this plugin owns the game-binder allowlist patch.
            BinderCompat.Apply(Logger);
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }
    }
}
