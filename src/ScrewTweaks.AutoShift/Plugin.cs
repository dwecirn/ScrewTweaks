#nullable enable

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ScrewTweaks.AutoShift.Generated;
using ScrewTweaks.Panel;
using UnityEngine;

namespace ScrewTweaks.AutoShift
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.autoshift";
        public const string Name = "Screw Tweaks - Auto Shift";
        public const string Version = PluginVersion.Value;
    }

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool>? _enabled;

        private void Awake()
        {
            _enabled = Config.Bind(
                "General",
                "Enabled",
                false,
                "Faster automatic shifting: shift after 0.2 s over the RPM threshold instead of 1 s, " +
                "with a shorter cooldown and torque cut. False = the game's own shift timing. " +
                "Also switchable from the panel (F7 -> Auto Shift).");
        }

        private void Start()
        {
            AutoShiftFeature.Init(msg => Logger.LogInfo(msg));
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            PanelHost.Register("Auto Shift", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        /// <summary>
        /// Keeps the feature in step with the config file, whichever side changed: the checkbox writes
        /// the entry, and editing the .cfg by hand makes BepInEx reload it.
        /// </summary>
        private void Update()
        {
            if (_enabled == null) return;
            if (AutoShiftFeature.Enabled != _enabled.Value)
                AutoShiftFeature.SetEnabled(_enabled.Value);
        }

        private void DrawSection()
        {
            if (_enabled == null) return;

            bool enabled = _enabled.Value;
            bool wanted = GUILayout.Toggle(enabled, " Faster automatic shifting");
            if (wanted != enabled)
                _enabled.Value = wanted;

            GUILayout.Space(4f);
            var game = AutoShiftFeature.GameValues;
            TimingRow("RPM threshold held for", AutoShiftFeature.FastOverThreshFor, game?.OverThreshFor);
            TimingRow("Minimum time between shifts", AutoShiftFeature.FastShiftMinCooldownTime, game?.MinCooldown);
            TimingRow("Torque cut while engaging", AutoShiftFeature.FastShiftDeadTime, game?.DeadTime);

            GUILayout.Space(10f);
            GUILayout.Label("The box is saved to the config file and applies to every car");
            GUILayout.Label("immediately; unticking it restores the game's own shift timing.");
        }

        /// <summary>
        /// One line of the comparison. The game's value only shows once a shifter has been seen, which
        /// is also when the feature learns it.
        /// </summary>
        private static void TimingRow(string label, float fast, float? game)
        {
            string value = game.HasValue
                ? $"{fast:0.00} s   (game: {game.Value:0.00} s)"
                : $"{fast:0.00} s";
            GUILayout.Label($"{label}   {value}");
        }
    }
}
