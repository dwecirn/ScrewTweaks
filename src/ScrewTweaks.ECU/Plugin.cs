#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ScrewTweaks.Panel;
using UnityEngine;

using ScrewTweaks.ECU.Generated;

namespace ScrewTweaks.ECU
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.ecu";
        public const string Name = "Screw Tweaks - ECU";
        public const string Version = PluginVersion.Value;
    }

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _absOpen;
        private bool _tractionOpen;

        private void Awake()
        {
            Aids.Init();

            Aids.AbsConfig = Config.Bind(
                "Channels",
                "Abs",
                Aids.NativeName,
                "ABS algorithm. Native = game default, Off = no ABS, otherwise the name of a registered algorithm (e.g. Progressive).");

            Aids.TractionConfig = Config.Bind(
                "Channels",
                "Traction",
                Aids.NativeName,
                "Traction control algorithm. Native = game default, Off = no TCS, otherwise the name of a registered algorithm.");

            Aids.AbsTargetConfig = Config.Bind("Abs", "TargetSlip", 0.12f, "Slip ratio the progressive ABS aims to hold. The game's asphalt curve peaks at ~0.125, so a target above that is already past peak grip.");
            Aids.AbsGainConfig = Config.Bind("Abs", "Gain", 6f, "How hard the brake is cut as slip exceeds the target. At gain 6 a slip of ~0.29 releases the brake fully.");
            Aids.AbsFloorConfig = Config.Bind("Abs", "Floor", 0.10f, "Minimum fraction of the requested brake kept while slipping (0..1). Lower releases more, like a real ABS.");

            Aids.TractionTargetConfig = Config.Bind("Traction", "TargetSlip", 0.12f, "Slip ratio the progressive traction control aims to hold.");
            Aids.TractionGainConfig = Config.Bind("Traction", "Gain", 3f, "How hard the drive torque is cut as slip exceeds the target.");
        }

        private void Start()
        {
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            PanelHost.Register("ECU", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void Update() => TireDataDump.Update();

        private void DrawSection()
        {
            DrawDropdown("ABS", Aids.BrakeSelection, ref _absOpen, SetAbs, Aids.BrakeNames());
            if (Aids.BrakeAid != null)
            {
                DrawSlider("  Target slip", Aids.AbsTargetConfig, 0.02f, 0.60f, "0.00");
                DrawSlider("  Gain", Aids.AbsGainConfig, 0.5f, 12f, "0.0");
                DrawSlider("  Brake floor", Aids.AbsFloorConfig, 0f, 0.95f, "0.00");
            }

            GUILayout.Space(10f);
            DrawDropdown("Traction", Aids.DriveSelection, ref _tractionOpen, SetTraction, Aids.DriveNames());
            if (Aids.DriveAid != null)
            {
                DrawSlider("  Target slip", Aids.TractionTargetConfig, 0.02f, 0.60f, "0.00");
                DrawSlider("  Cut gain", Aids.TractionGainConfig, 0.5f, 12f, "0.0");
            }

            GUILayout.Space(10f);
            GUILayout.Label("Changes apply live and are saved to the config file.");
            GUILayout.Label("Other plugins can add algorithms: EcuAids.Register(...).");
        }

        private static void DrawDropdown(string label, string current, ref bool open, Action<string> onChange, List<string> modes)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80f));
            if (GUILayout.Button($"{current} ▼", GUILayout.Width(160f)))
                open = !open;
            GUILayout.EndHorizontal();

            if (!open) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(84f);
            GUILayout.BeginVertical();
            foreach (var mode in modes)
            {
                string text = mode == current ? $"● {mode}" : $"     {mode}";
                if (GUILayout.Button(text, GUILayout.Width(156f)))
                {
                    onChange(mode);
                    open = false;
                }
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void DrawSlider(string label, ConfigEntry<float>? entry, float min, float max, string format)
        {
            if (entry == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {entry.Value.ToString(format)}", GUILayout.Width(170f));
            float value = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(value, entry.Value))
                entry.Value = value;
        }

        private void SetAbs(string name)
        {
            if (Aids.AbsConfig != null) Aids.AbsConfig.Value = name;
            Aids.ReapplyAll();
        }

        private void SetTraction(string name)
        {
            if (Aids.TractionConfig != null) Aids.TractionConfig.Value = name;
            Aids.ReapplyAll();
        }
    }
}
