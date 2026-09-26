#nullable enable

using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace ScrewTweaks.Steering
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.steering";
        public const string Name = "Screw Tweaks - Steering";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private void Start()
        {
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }
    }
}
