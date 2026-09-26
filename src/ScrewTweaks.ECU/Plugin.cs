#nullable enable

using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ScrewTweaks.ECU.Generated;
using UnityEngine;

namespace ScrewTweaks.ECU
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.ecu";
        public const string Name = "Screw Tweaks - ECU";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _shown;
        private bool _absOpen;
        private bool _tractionOpen;
        private Rect _window = new Rect(24f, 24f, 340f, 220f);

        private void Awake()
        {
            Aids.AbsConfig = Config.Bind(
                "Channels",
                "Abs",
                nameof(AidMode.Native),
                "ABS algorithm. Native = game default, Off = no ABS, Progressive = hold a target slip.");

            Aids.TractionConfig = Config.Bind(
                "Channels",
                "Traction",
                nameof(AidMode.Native),
                "Traction control algorithm. Native = game default, Off = no TCS.");

            Aids.AbsTargetConfig = Config.Bind("Abs", "TargetSlip", 0.20f, "Slip ratio the progressive ABS aims to hold.");
            Aids.AbsGainConfig = Config.Bind("Abs", "Gain", 4f, "How hard the brake is cut as slip exceeds the target.");
            Aids.AbsFloorConfig = Config.Bind("Abs", "Floor", 0.40f, "Minimum fraction of the requested brake kept while slipping (0..1).");
        }

        private void Start()
        {
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyBinds.Ecu))
                _shown = !_shown;

            if (_shown)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            if (!_shown) return;
            _window = GUILayout.Window(0x5EC0, _window, DrawWindow, "ScrewTweaks ECU  (F7)");
        }

        private void DrawWindow(int id)
        {
            DrawDropdown("ABS", Aids.Abs, ref _absOpen, SetAbs, new[] { AidMode.Native, AidMode.Off, AidMode.Progressive });
            if (Aids.Abs == AidMode.Progressive)
            {
                DrawSlider("  Target slip", Aids.AbsTargetConfig, 0.02f, 0.60f, "0.00");
                DrawSlider("  Gain", Aids.AbsGainConfig, 0.5f, 12f, "0.0");
                DrawSlider("  Brake floor", Aids.AbsFloorConfig, 0f, 0.95f, "0.00");
            }

            GUILayout.Space(10f);
            DrawDropdown("Traction", Aids.Traction, ref _tractionOpen, SetTraction, new[] { AidMode.Native, AidMode.Off });

            GUILayout.Space(10f);
            GUILayout.Label("Changes apply live and are saved to the config file.");
            GUI.DragWindow();
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

        private static void DrawDropdown(string label, AidMode current, ref bool open, Action<AidMode> onChange, AidMode[] modes)
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

        private void SetAbs(AidMode mode)
        {
            if (Aids.AbsConfig != null) Aids.AbsConfig.Value = mode.ToString();
            Aids.ReapplyAll();
        }

        private void SetTraction(AidMode mode)
        {
            if (Aids.TractionConfig != null) Aids.TractionConfig.Value = mode.ToString();
            Aids.ReapplyAll();
        }
    }
}
