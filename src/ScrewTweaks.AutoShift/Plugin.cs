#nullable enable

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ScrewTweaks.AutoShift.Generated;
using UnityEngine;

namespace ScrewTweaks.AutoShift
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.autoshift";
        public const string Name = "Screw Tweaks - Auto Shift";
        public const string Version = PluginVersion.Value;
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<KeyCode>? _autoShiftKey;

        private void Awake()
        {
            // Default comes from keybinds.props (compile time); players can override
            // it in BepInEx/config/dev.dwecirn.screwtweaks.autoshift.cfg (run time).
            _autoShiftKey = Config.Bind(
                "Keys",
                "AutoShift",
                KeyBinds.AutoShift,
                "Key that applies the fast auto-shift tuning.");
        }

        private void Start()
        {
            AutoShiftFeature.Init(msg => Logger.LogInfo(msg));
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void Update()
        {
            if (_autoShiftKey != null && Input.GetKeyDown(_autoShiftKey.Value))
                AutoShiftFeature.Apply();
        }
    }
}
