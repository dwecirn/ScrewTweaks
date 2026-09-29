#nullable enable

using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NWH.WheelController3D;
using ScrewTweaks.Panel;
using ScrewTweaks.Physics.Suspension.Generated;
using UnityEngine;

namespace ScrewTweaks.Physics.Suspension
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.physics.suspension";
        public const string Name = "Screw Tweaks - Suspension Physics";
        public const string Version = PluginVersion.Value;
    }

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private const float ScanInterval = 2f;

        private bool _open;
        private WheelController[] _wheels = Array.Empty<WheelController>();
        private float _nextScan;

        private void Awake()
        {
            DamperModels.SelectedConfig = Config.Bind(
                "Model",
                "Selected",
                "Native",
                "Damper model in use. Saved when you pick one in the panel, so it is remembered across sessions.");

            DamperTuning.ReboundRatioConfig = Config.Bind(
                "Damper",
                "ReboundRatio",
                2.0f,
                "Rebound coefficient as a multiple of the bump coefficient. The game uses 1.0 for both.");

            DamperTuning.LowSpeedGainConfig = Config.Bind(
                "Damper",
                "LowSpeedGain",
                1.6f,
                "Damping multiplier below the knee velocity. 1.0 = the game's own coefficient. " +
                "1.0 together with BlowOffRatio 1.0 and ReboundRatio 1.0 reproduces the game exactly.");

            DamperTuning.KneeVelocityConfig = Config.Bind(
                "Damper",
                "KneeVelocity",
                0.10f,
                "Suspension velocity [m/s] at which the damper stops rising as fast. Real dampers knee " +
                "between about 0.05 and 0.3 m/s. Below it the low-speed gain applies in full.");

            DamperTuning.BlowOffRatioConfig = Config.Bind(
                "Damper",
                "BlowOffRatio",
                0.25f,
                "Slope above the knee as a fraction of the slope below it. 1.0 = no blow-off (linear, " +
                "i.e. the game), 0.0 = a hard plateau. Real dampers sit around 0.1 to 0.3.");

            DamperTuning.ReboundFloorConfig = Config.Bind(
                "Damper",
                "ReboundFloor",
                0.0f,
                "How far the damper may pull the body down, as a fraction of the wheel's static load. " +
                "The game clamps the total suspension force at zero, so a rebound force larger than the " +
                "spring force is truncated and stops doing anything. 0 = the game's behaviour, " +
                "1 = up to about 1 g of downward pull. Anything above 1 risks dragging the chassis " +
                "into the terrain on a crest.");
        }

        private void Start()
        {
            Init("damper models", DamperModels.Init);
            Init("suspension patch", () => Harmony.CreateAndPatchAll(typeof(DamperSlot), PluginInfo.GUID));

            PanelHost.Register("Suspension", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded (model: {DamperModels.Current?.Name ?? "none"}).");
        }

        private void Init(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Logger.LogError($"init failed ({what}): {e}");
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            try
            {
                _wheels = UnityEngine.Object.FindObjectsByType<WheelController>(FindObjectsSortMode.None);
            }
            catch
            {
                _wheels = Array.Empty<WheelController>();
            }
        }

        private void DrawSection()
        {
            var current = DamperModels.Current;
            GUILayout.Label($"Damper model: {(current != null ? current.Name : "none")}");
            if (current != null) GUILayout.Label(current.Description);

            GUILayout.Space(6f);
            if (GUILayout.Button($"{current?.Name ?? "-"} ▼", GUILayout.Width(240f)))
                _open = !_open;

            if (_open)
            {
                foreach (var model in DamperModels.All)
                {
                    string text = model == current ? $"● {model.Name}" : $"     {model.Name}";
                    if (GUILayout.Button(text, GUILayout.Width(236f)))
                    {
                        DamperModels.Select(model);
                        _open = false;
                    }
                }
            }

            if (!DamperModels.IsNative)
            {
                GUILayout.Space(10f);
                DrawSlider("Rebound / bump", DamperTuning.ReboundRatioConfig, 0.5f, 4f, "0.00");
                DrawSlider("Low-speed gain", DamperTuning.LowSpeedGainConfig, 0.5f, 3f, "0.00");
                DrawSlider("Knee velocity", DamperTuning.KneeVelocityConfig, 0.01f, 0.5f, "0.00");
                DrawSlider("Blow-off slope", DamperTuning.BlowOffRatioConfig, 0f, 1f, "0.00");
                DrawSlider("Pull-down floor", DamperTuning.ReboundFloorConfig, 0f, 1.5f, "0.00");

                GUILayout.Space(6f);
                GUILayout.Label("Gain 1.00 + blow-off 1.00 + ratio 1.00 = exactly the game.");
                if (DamperTuning.ReboundFloor <= 0f)
                    GUILayout.Label("Pull-down floor 0 = the game's clamp: the damper never pulls the");
                else
                    GUILayout.Label($"Pull-down floor {DamperTuning.ReboundFloor:0.00}: the damper may pull the body");
                GUILayout.Label("body down. Raise it only if rebound feels like it runs out at the top.");
            }

            GUILayout.Space(12f);
            DrawWheels();
        }

        private void DrawWheels()
        {
            GUILayout.Label("Grounded wheels (each row is one physics step, live):");
            GUILayout.Label("  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N");

            int shown = 0;
            try
            {
                foreach (var wc in _wheels)
                {
                    if (wc == null || !wc.hasHit) continue;
                    var spring = wc.spring;
                    var damper = wc.damper;
                    if (spring == null || damper == null) continue;

                    bool bump = spring.velocity <= 0f;
                    float game = damper.bumpForce * Mathf.Abs(spring.velocity);
                    if (!bump) game = -game;

                    GUILayout.Label(
                        $"  {Label(wc),-16} {spring.compressionPercent * 100f,4:F0}%  {damper.bumpForce,8:F0}  " +
                        $"{(bump ? "bump" : "reb ")} {spring.velocity,7:F2}  {damper.force,9:F0}  " +
                        $"{game,8:F0}  {spring.force,8:F0}");
                    shown++;
                    if (shown >= 12) break;
                }
            }
            catch
            {
                // never break the panel
            }

            if (shown == 0) GUILayout.Label("  (no grounded wheels)");
        }

        private static string Label(WheelController wc)
        {
            var config = wc.PartConfigurationWheel;
            if (config != null) return config.partType.ToString();
            return wc.gameObject.name;
        }

        private static void DrawSlider(string label, ConfigEntry<float>? entry, float min, float max, string format)
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
