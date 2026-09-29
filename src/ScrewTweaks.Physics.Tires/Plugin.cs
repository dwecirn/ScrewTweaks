#nullable enable

using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using NWH.WheelController3D;
using ScrewTweaks.Physics.Tires.Generated;
using ScrewTweaks.Panel;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.physics.tires";
        public const string Name = "Screw Tweaks - Tire Physics";
        public const string Version = PluginVersion.Value;
    }

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID, ScrewTweaks.Panel.PluginInfo.Version)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _open;
        private static readonly HashSet<PartType> SeenTypes = new HashSet<PartType>();
        private static readonly List<(WheelController Wheel, TireIdentity Identity)> SeenWheels =
            new List<(WheelController Wheel, TireIdentity Identity)>();

        private void Awake()
        {
            TireModels.SelectedConfig = Config.Bind(
                "Model",
                "Selected",
                "Native",
                "Tyre model in use. Saved when you pick one in the panel, so it is remembered across sessions.");

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

            TireTuning.CombinedSlipConfig = Config.Bind(
                "Pacejka",
                "CombinedSlip",
                1.0f,
                "0 = longitudinal and lateral forces are independent, 1 = full ADAMS friction ellipse (locked/spinning wheels lose side grip).");

            TireTuning.RelaxationLengthConfig = Config.Bind(
                "Pacejka",
                "RelaxationLength",
                0.30f,
                "Distance [m] the tyre needs to build up slip, at a 0.30 m wheel. Scaled per tire by its radius, so big soft tires get a longer one and small rigid ones a shorter one. 0 disables it.");

            TireTuning.GeometryShapeCouplingConfig = Config.Bind(
                "Pacejka",
                "GeometryShapeCoupling",
                0.0f,
                "How strongly the contact patch (radius x width) moves the slip-curve peak. Off by default: the game already ships per-tire asphalt/sand curves, so this is an extra on top. 1 = full brush-model geometry.");

            TireTuning.CamberDynamicsConfig = Config.Bind(
                "Pacejka",
                "CamberDynamics",
                1.0f,
                "How much camber softens the tyre beyond camber thrust (peak moves later, lateral peak drops). 1 = typical MF coefficients, 0 = thrust only. Only does anything if you actually run camber.");
        }

        private void Start()
        {
            // Each step is isolated: a failure in one must not cost us the panel registration
            // (that is how the Tires tab silently disappeared), and it must be logged.
            Localize();

            Init("tire models", TireModels.Init);
            Init("friction slot patch", () => Harmony.CreateAndPatchAll(typeof(TireSlot), PluginInfo.GUID));

            PanelHost.Register("Tires", DrawSection);
            TireRecorder.Log = Logger;
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded (model: {TireModels.Current?.Name ?? "none"}).");
        }

        /// <summary>Panel strings. Model names, slips and units are identifiers and stay as they are.</summary>
        private static void Localize()
        {
            // Keyed off the models' own Description values, so a translation can never drift away from
            // the string it is meant to translate.
            string g = new NativeTireModel().Description;
            string z = new ZeroGripTireModel().Description;
            string p = new PacejkaTireModel().Description;

            Loc.Add(PanelLanguage.ChineseSimplified,
                ("Tires", "轮胎"),
                ("Tyre model: {0}", "轮胎模型：{0}"),
                (g, "游戏自带的 NWH WheelController3D 摩擦力（未改动）。"),
                (z, "轮胎力为零。车既不会加速也不会转向。"),
                (p, "游戏 BCDE + 载荷敏感度 + 松弛长度 + ADAMS 摩擦椭圆 + 外倾推力。"),
                ("Grip scale", "抓地力倍率"),
                ("Camber thrust / deg", "外倾推力 / 度"),
                ("Camber dynamics", "外倾动态"),
                ("Combined slip", "组合滑移"),
                ("Relaxation length", "松弛长度"),
                ("Geometry -> peak", "几何 → 峰值"),
                ("Tires seen (identity captured, independent of the live slots):",
                    "已识别的轮胎（与实时槽位无关）："),
                ("  (none yet - enter a car)", "  （暂无——先进入一辆车）"),
                ("Live wheels ({0}: record 30s to CSV):", "实时轮子（{0}：录制 30 秒到 CSV）："),
                ("  kappa | alpha deg | Fx/FxMax | Fy/FyMax | Fz | vx | omega",
                    "  κ | α（度） | Fx/FxMax | Fy/FyMax | Fz | vx | ω"),
                ("The slot replaces WheelController.FrictionUpdate;", "该槽位替换 WheelController.FrictionUpdate；"),
                ("Native keeps the game's original friction.", "Native 保留游戏原本的摩擦力。"));

            Loc.Add(PanelLanguage.Japanese,
                ("Tires", "タイヤ"),
                ("Tyre model: {0}", "タイヤモデル: {0}"),
                (g, "ゲーム内蔵の NWH WheelController3D 摩擦（変更なし）。"),
                (z, "タイヤ力はゼロ。車は加速も旋回もしません。"),
                (p, "ゲームの BCDE + 荷重感度 + 緩和長 + ADAMS 摩擦楕円 + キャンバスラスト。"),
                ("Grip scale", "グリップ倍率"),
                ("Camber thrust / deg", "キャンバスラスト / 度"),
                ("Camber dynamics", "キャンバ動特性"),
                ("Combined slip", "複合スリップ"),
                ("Relaxation length", "緩和長"),
                ("Geometry -> peak", "形状 → ピーク"),
                ("Tires seen (identity captured, independent of the live slots):",
                    "確認済みのタイヤ（ライブスロットとは独立）:"),
                ("  (none yet - enter a car)", "  （まだありません - 車に乗ってください）"),
                ("Live wheels ({0}: record 30s to CSV):", "ライブホイール（{0}: 30 秒を CSV に記録）:"),
                ("  kappa | alpha deg | Fx/FxMax | Fy/FyMax | Fz | vx | omega",
                    "  κ | α（度） | Fx/FxMax | Fy/FyMax | Fz | vx | ω"),
                ("The slot replaces WheelController.FrictionUpdate;", "このスロットは WheelController.FrictionUpdate を置き換えます。"),
                ("Native keeps the game's original friction.", "Native はゲーム本来の摩擦をそのまま使います。"));
        }

        private void Init(string what, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                Logger.LogError($"init failed ({what}): {e}");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyBinds.Telemetry))
                TireRecorder.Toggle();

            TireRecorder.Update();
        }

        private void DrawSection()
        {
            var current = TireModels.Current;
            GUILayout.Label(Loc.Tf("Tyre model: {0}", current?.Name ?? "-"));
            if (current != null)
            {
                GUILayout.Label(Loc.T(current.Description));
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
                DrawSlider(Loc.T("Grip scale"), TireTuning.GripScaleConfig, 0.5f, 2f, "0.00");
                DrawSlider(Loc.T("Camber thrust / deg"), TireTuning.CamberThrustConfig, -0.08f, 0.08f, "0.000");
                DrawSlider(Loc.T("Camber dynamics"), TireTuning.CamberDynamicsConfig, 0f, 2f, "0.00");
                DrawSlider(Loc.T("Combined slip"), TireTuning.CombinedSlipConfig, 0f, 1f, "0.00");
                DrawSlider(Loc.T("Relaxation length"), TireTuning.RelaxationLengthConfig, 0f, 1.5f, "0.00");
                DrawSlider(Loc.T("Geometry -> peak"), TireTuning.GeometryShapeCouplingConfig, 0f, 2f, "0.00");
            }

            GUILayout.Space(12f);
            GUILayout.Label(Loc.T("Tires seen (identity captured, independent of the live slots):"));
            try
            {
                SeenTypes.Clear();
                TireIdentities.Collect(SeenWheels);
                foreach (var entry in SeenWheels)
                {
                    var id = entry.Identity;
                    if (!SeenTypes.Add(id.Type)) continue;
                    GUILayout.Label($"  {id.Type}  grip={id.Grip,5:F2}  partR={id.PartRadius,6:F3}  partW={id.PartWidth,5:F3}");
                }

                if (SeenTypes.Count == 0)
                    GUILayout.Label(Loc.T("  (none yet - enter a car)"));
            }
            catch
            {
                // never break the panel
            }

            GUILayout.Space(12f);
            GUILayout.Label(Loc.Tf("Live wheels ({0}: record 30s to CSV):", KeyBinds.Telemetry));
            GUILayout.Label(Loc.T("  kappa | alpha deg | Fx/FxMax | Fy/FyMax | Fz | vx | omega"));
            int count = TireTelemetry.Count;
            for (int i = 0; i < count; i++)
            {
                if (!TireTelemetry.TryGet(i, out var wc, out var s) || wc == null)
                    continue;

                string id = s.HasIdentity ? $"{s.TireType} g={s.TireGrip,5:F2}" : "(no id)";
                GUILayout.Label(
                    $"{id}  BCDE=({s.BcdeB,5:F1},{s.BcdeC,5:F2},{s.BcdeD,5:F2},{s.BcdeE,5:F2})  " +
                    $"k={s.Kappa,6:F3}/{s.KappaRaw,6:F3} a={s.AlphaDeg,6:F1}/{s.AlphaRawDeg,6:F1} " +
                    $"y={SafeRatio(s.Fy, s.FyMax),4:F2} Fz={s.Fz,6:F0} R={s.Radius,5:F2} sig={s.Sigma,4:F2} " +
                    $"camber={s.CamberDeg,5:F1} camFx={s.CamberThrustForce,6:F0}");
            }

            GUILayout.Space(10f);
            GUILayout.Label(Loc.T("The slot replaces WheelController.FrictionUpdate;"));
            GUILayout.Label(Loc.T("Native keeps the game's original friction."));
        }

        private static float SafeRatio(float value, float max)
        {
            return max > 1e-4f ? Mathf.Abs(value) / max : 0f;
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
