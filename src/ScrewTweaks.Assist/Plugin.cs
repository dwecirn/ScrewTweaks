#nullable enable

using BepInEx;
using BepInEx.Configuration;
using ScrewTweaks.Assist.Generated;
using UnityEngine;

namespace ScrewTweaks.Assist
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.assist";
        public const string Name = "Screw Tweaks - Assist Disabler";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private AssistDisabler? _assistDisabler;
        private ConfigEntry<KeyCode>? _assistKey;

        private void Awake()
        {
            // Default comes from keybinds.props (compile time); players can override
            // it in BepInEx/config/dev.dwecirn.screwtweaks.assist.cfg (run time).
            _assistKey = Config.Bind(
                "Keys",
                "Assist",
                KeyBinds.Assist,
                "Key that disables TCS and ABS for the current car.");
        }

        private void Start()
        {
            _assistDisabler = new AssistDisabler(
                msg => Logger.LogInfo(msg),
                msg => Logger.LogWarning(msg),
                msg => Logger.LogError(msg));

            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void Update()
        {
            if (_assistKey != null && Input.GetKeyDown(_assistKey.Value))
                _assistDisabler?.ProbeAndDisable();
        }
    }
}
