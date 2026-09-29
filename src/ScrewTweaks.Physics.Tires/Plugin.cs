#nullable enable

using System.Reflection;
using BepInEx;
using HarmonyLib;
using ScrewTweaks.UI;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.physics.tires";
        public const string Name = "Screw Tweaks - Tire Physics";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _open;

        private void Awake()
        {
            TireTuning.GripScaleConfig = Config.Bind(
                "Pacejka",
                "GripScale",
                1.0f,
                "Multiplier on the peak grip. 1.0 matches the native model's peak force.");

            TireTuning.CamberThrustConfig = Config.Bind(
                "Pacejka",
                "CamberThrust",
                0.015f,
                "Camber thrust as a fraction of wheel load per degree of camber. Flip the sign if it pushes the wrong way.");
        }

        private void Start()
        {
            TireModels.Init();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            Panel.Register("Tires", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void DrawSection()
        {
            var current = TireModels.Current;
            GUILayout.Label($"Tyre model: {(current != null ? current.Name : "none")}");
            if (current != null)
            {
                GUILayout.Label(current.Description);
            }

            GUILayout.Space(6f);
            if (GUILayout.Button($"{current?.Name ?? "-"} ▼", GUILayout.Width(240f)))
                _open = !_open;

            if (_open)
            {
                foreach (var model in TireModels.All)
                {
                    string text = model == current ? $"● {model.Name}" : $"     {model.Name}";
                    if (GUILayout.Button(text, GUILayout.Width(236f)))
                    {
                        TireModels.Select(model);
                        _open = false;
                    }
                }
            }

            if (current is PacejkaTireModel)
            {
                GUILayout.Space(10f);
                DrawSlider("Grip scale", TireTuning.GripScaleConfig, 0.5f, 2f, "0.00");
                DrawSlider("Camber thrust / deg", TireTuning.CamberThrustConfig, -0.08f, 0.08f, "0.000");
            }

            GUILayout.Space(10f);
            GUILayout.Label("The slot replaces WheelController.FrictionUpdate;");
            GUILayout.Label("Native keeps the game's original friction.");
        }

        private static void DrawSlider(string label, BepInEx.Configuration.ConfigEntry<float>? entry, float min, float max, string format)
        {
            if (entry == null) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {entry.Value.ToString(format)}", GUILayout.Width(190f));
            float value = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(value, entry.Value))
                entry.Value = value;
        }
    }
}
