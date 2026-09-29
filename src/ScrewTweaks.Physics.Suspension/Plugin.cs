#nullable enable

using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using System.Collections.Generic;
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

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID, ScrewTweaks.Panel.PluginInfo.Version)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private const float ScanInterval = 2f;

        private bool _damperOpen;
        private bool _verticalOpen;
        private WheelController[] _wheels = Array.Empty<WheelController>();
        private float _nextScan;

        private void Awake()
        {
            DamperModels.SelectedConfig = Config.Bind(
                "Model",
                "Selected",
                "Native",
                "Damper model in use. Saved when you pick one in the panel, so it is remembered across sessions.");

            TireVerticalModels.SelectedConfig = Config.Bind(
                "TireVertical",
                "Model",
                "Native",
                "Tyre vertical model. Native = the game's own wheel: rigid in the vertical, snapped to the " +
                "ground every step, no unsprung mass. Linear = the tyre becomes a spring and damper against " +
                "the ground and the wheel hang on the suspension above it. The stiffness and damping are " +
                "derived from the wheel part's mass and the suspension part's wheel rate; they are not " +
                "settings.");

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
            Localize();
            DamperProperties.Localize();

            Init("damper models", DamperModels.Init);
            Init("tyre vertical models", TireVerticalModels.Init);
            Init("suspension patches",
                () => Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID));

            PanelHost.Register("Suspension", DrawSection);
            Logger.LogInfo(
                $"[{PluginInfo.Name}] version {PluginInfo.Version} loaded " +
                $"(damper: {DamperModels.Current?.Name ?? "none"}, " +
                $"tyre vertical: {TireVerticalModels.Current?.Name ?? "none"}).");
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

            DamperProperties.FindSettings();
        }

        /// <summary>Panel strings. Model names and units are identifiers and stay as they are.</summary>
        private static void Localize()
        {
            // Keyed off the models' own Description values, so a translation can never drift away from
            // the string it is meant to translate.
            string nativeDamper = new NativeDamperModel().Description;
            string digressive = new FourWayDamper().Description;
            string nativeVertical = new NativeTireVerticalModel().Description;
            string linear = new LinearTireModel().Description;

            Loc.Add(PanelLanguage.ChineseSimplified,
                ("Suspension", "悬挂"),
                ("Damper model: {0}", "阻尼模型：{0}"),
                ("Tyre vertical model: {0}", "轮胎垂向模型：{0}"),
                (nativeDamper, "游戏自带的阻尼，未改动：力 = 系数 × |速度|，压缩与回弹用同一个系数，没有卸压。"),
                (digressive, "压缩和回弹各自有独立的低速（泄流）和高速（卸压）系数。慢速时更硬以控制车身，冲击时更软以吸收路肩。四个系数都设成 1.00 就是完全复刻原版。"),
                (nativeVertical, "游戏自带的轮子：没有垂向自由度，每帧被贴到地面上，所以轮胎什么都吸收不了，轮子本身也没有质量。"),
                (linear, "有垂向弹性的轮胎：对地面是一个线性弹簧加阻尼器，轮子以零件自身质量悬在悬挂上方。轮子从此可以跟随路面、也可以离地，尖锐载荷被轮胎吸收而不是直接传给车身。"),
                ("Damper setup (stored on the suspension part, so it travels with the car):",
                    "阻尼设置（存在悬挂零件上，因此随车走）："),
                ("  {0}   bump {1}/{2}   rebound {3}/{4}", "  {0}   压缩 {1}/{2}   回弹 {3}/{4}"),
                ("  (no car yet)", "  （暂无车辆）"),
                ("The game's Damper Force is still the base; these four are its shape.",
                    "游戏自带的 Damper Force 仍是基准，这四项是它的形状。"),
                ("Damper setup", "阻尼设置"),
                ("They are only read while a damper model is selected. Native leaves the game's own damper in charge and does not read them, but they are still saved with the car either way.",
                    "只有在选中阻尼模型时才会读取。Native 让游戏自带的阻尼全权负责、不读它们，但无论哪种模式，它们都会随车一起保存。"),
                ("Pull-down floor", "下拉下限"),
                ("Stiffness and damping are derived from the wheel part's mass and the suspension part's wheel rate. There is nothing to set.",
                    "刚度和阻尼由轮子零件的质量与悬挂零件的轮速（wheel rate）推导，没有可调项。"),
                ("Gain 1.00 + blow-off 1.00 + ratio 1.00 = exactly the game.",
                    "增益 1.00 + 卸压 1.00 + 比例 1.00 = 完全等于原版。"),
                ("Pull-down floor 0 = the game's clamp: the damper never pulls the body down.",
                    "下拉下限 0 = 游戏的钳位：阻尼永远不会把车身往下拉。"),
                ("Pull-down floor {0}: the damper may pull the body down by up to that fraction of the wheel's static load.",
                    "下拉下限 {0}：阻尼最多可以把车身往下拉到“该轮静态载荷 × {0}”。"),
                ("Raise it only if rebound feels like it runs out near full extension.",
                    "只有在感觉“回弹快伸到底时没劲”时才需要调高它。"),
                ("The game's pull-down clamp is not applied while a wheel has vertical freedom: the tyre's one-sided force is the bound.",
                    "轮子有垂向自由度时不使用游戏的钳位：真正的边界是轮胎只能推、不能拉的单侧力。"),
                ("Grounded wheels (each row is one physics step, live):", "接地轮（每行是一个物理步，实时）："),
                ("  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N",
                    "  轮子               压缩   C Ns/m   方向  v m/s     阻尼 N     原版 N    弹簧 N"),
                ("    damper {0} N (game {1})   spring {2} N   tyre {3} mm / {4} N   hop {5} Hz   sub {6}",
                    "    阻尼 {0} N（原版 {1}）   弹簧 {2} N   轮胎 {3} mm / {4} N   跳动 {5} Hz   子步 {6}"),
                ("bump", "压缩"),
                ("reb", "回弹"),
                ("  (no grounded wheels)", "  （没有接地的轮子）"));

            Loc.Add(PanelLanguage.Japanese,
                ("Suspension", "サスペンション"),
                ("Damper model: {0}", "ダンパーモデル: {0}"),
                ("Tyre vertical model: {0}", "タイヤ上下モデル: {0}"),
                (nativeDamper, "ゲーム標準のダンパーそのまま：力 = 係数 × |速度|。圧縮と伸張で同じ係数、ブローオフなし。"),
                (digressive, "圧縮と伸張それぞれに、独立した低速（ブリード）と高速（ブローオフ）の係数を持ちます。低速では硬くボディを制御し、鋭い入力では軟らかく縁石を吸収します。4 つとも 1.00 でゲームと完全一致します。"),
                (nativeVertical, "ゲーム標準のホイール：上下の自由度がなく、毎ステップ地面に貼り付けられます。タイヤは何も吸収できず、ホイール自体にも質量がありません。"),
                (linear, "上下に撓むタイヤ：地面に対して線形のばねとダンパー、その上にホイールがパーツ自身の質量で吊られます。ホイールが路面に追従し、離れることもできるようになり、鋭い荷重はシャシーではなくタイヤが吸収します。"),
                ("Damper setup (stored on the suspension part, so it travels with the car):",
                    "ダンパー設定（サスペンションパーツに保存され、車と一緒に移動します）："),
                ("  {0}   bump {1}/{2}   rebound {3}/{4}", "  {0}   圧縮 {1}/{2}   伸張 {3}/{4}"),
                ("  (no car yet)", "  （車がありません）"),
                ("The game's Damper Force is still the base; these four are its shape.",
                    "ゲームの Damper Force が引き続き基準で、この 4 つはその形を決めます。"),
                ("Damper setup", "ダンパー設定"),
                ("They are only read while a damper model is selected. Native leaves the game's own damper in charge and does not read them, but they are still saved with the car either way.",
                    "読み込まれるのはダンパーモデルを選んでいるときだけです。Native ではゲーム標準のダンパーがそのまま使われ、これらは読みません。ただしどちらの場合も車と一緒に保存されます。"),
                ("Pull-down floor", "引き下げ下限"),
                ("Stiffness and damping are derived from the wheel part's mass and the suspension part's wheel rate. There is nothing to set.",
                    "剛性と減衰はホイールパーツの質量とサスペンションパーツのホイールレートから導出されます。設定項目はありません。"),
                ("Gain 1.00 + blow-off 1.00 + ratio 1.00 = exactly the game.",
                    "ゲイン 1.00 + ブローオフ 1.00 + 比率 1.00 = ゲームと完全一致。"),
                ("Pull-down floor 0 = the game's clamp: the damper never pulls the body down.",
                    "引き下げ下限 0 = ゲームのクランプ：ダンパーがボディを下へ引くことはありません。"),
                ("Pull-down floor {0}: the damper may pull the body down by up to that fraction of the wheel's static load.",
                    "引き下げ下限 {0}：ダンパーはボディを“そのホイールの静的荷重 × {0}”まで下へ引けます。"),
                ("Raise it only if rebound feels like it runs out near full extension.",
                    "伸び切る手前で伸張が効かないと感じたときだけ上げてください。"),
                ("The game's pull-down clamp is not applied while a wheel has vertical freedom: the tyre's one-sided force is the bound.",
                    "ホイールに上下の自由度があるときは、ゲームのクランプを適用しません。境界になるのは、押すだけで引けないタイヤの片側の力です。"),
                ("Grounded wheels (each row is one physics step, live):", "接地中のホイール（各行が 1 物理ステップ、ライブ）:"),
                ("  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N",
                    "  ホイール           圧縮   C Ns/m   方向  v m/s   ダンパー N   ゲーム N   スプリング N"),
                ("    damper {0} N (game {1})   spring {2} N   tyre {3} mm / {4} N   hop {5} Hz   sub {6}",
                    "    ダンパー {0} N（ゲーム {1}）   スプリング {2} N   タイヤ {3} mm / {4} N   ホップ {5} Hz   サブ {6}"),
                ("bump", "圧縮"),
                ("reb", "伸張"),
                ("  (no grounded wheels)", "  （接地しているホイールはありません）"));
        }

        private void DrawSection()
        {
            DrawModelPicker(
                Loc.Tf("Damper model: {0}", DamperModels.Current?.Name ?? "-"),
                DamperModels.Current?.Description,
                Loc.T("The game's pull-down clamp is not applied while a wheel has vertical freedom: the tyre's one-sided force is the bound."),
                DamperModels.All, DamperModels.Current, ref _damperOpen,
                m => DamperModels.Select(m), m => m.Name);

            GUILayout.Space(10f);
            DrawDamperSetups();

            if (!DamperModels.IsNative && TireVerticalModels.IsNative)
            {
                GUILayout.Space(10f);
                DrawSlider(Loc.T("Pull-down floor"), DamperTuning.ReboundFloorConfig, 0f, 1.5f, "0.00",
                    (DamperTuning.ReboundFloor <= 0f
                        ? Loc.T("Pull-down floor 0 = the game's clamp: the damper never pulls the body down.")
                        : Loc.Tf("Pull-down floor {0}: the damper may pull the body down by up to that fraction of the wheel's static load.",
                            DamperTuning.ReboundFloor))
                    + "\n\n" + Loc.T("Raise it only if rebound feels like it runs out near full extension."));
            }

            GUILayout.Space(16f);
            DrawVertical();

            GUILayout.Space(14f);
            DrawWheels();
        }

        /// <summary>
        /// The four damper coefficients are part properties, stored in the car's own save file next to the
        /// game's Spring Force and Damper Force, so each car keeps its own setup and a shared car carries
        /// it. They are shown here read-only: the builder is where they are edited. The explanation is on
        /// hover, because it is the same on every car and only needed once.
        /// </summary>
        private void DrawDamperSetups()
        {
            PanelUi.Label(Loc.T("Damper setup"),
                Loc.T("Damper setup (stored on the suspension part, so it travels with the car):")
                + "\n\n" + Loc.T("The game's Damper Force is still the base; these four are its shape.")
                + "\n\n" + Loc.T("They are only read while a damper model is selected. Native leaves the game's own damper in charge and does not read them, but they are still saved with the car either way."));

            var seen = new HashSet<string>();
            try
            {
                foreach (var wc in _wheels)
                {
                    var suspension = wc?.PartConfigurationSuspension;
                    if (suspension == null) continue;

                    var setup = DamperProperties.For(suspension);
                    string line = Loc.Tf("  {0}   bump {1}/{2}   rebound {3}/{4}",
                        suspension.partType.ToString(),
                        setup.BumpLow.ToString("0.00"), setup.BumpHigh.ToString("0.00"),
                        setup.ReboundLow.ToString("0.00"), setup.ReboundHigh.ToString("0.00"));
                    if (seen.Add(line)) GUILayout.Label(line);
                }
            }
            catch
            {
                // never break the panel
            }

            if (seen.Count == 0) GUILayout.Label(Loc.T("  (no car yet)"));
        }

        private void DrawVertical()
        {
            DrawModelPicker(
                Loc.Tf("Tyre vertical model: {0}", TireVerticalModels.Current?.Name ?? "-"),
                TireVerticalModels.Current?.Description,
                Loc.T("Stiffness and damping are derived from the wheel part's mass and the suspension part's wheel rate. There is nothing to set."),
                TireVerticalModels.All, TireVerticalModels.Current, ref _verticalOpen,
                m => TireVerticalModels.Select(m), m => m.Name);

            if (TireVerticalModels.IsNative) return;
        }

        private void DrawWheels()
        {
            GUILayout.Label(Loc.T("Grounded wheels (each row is one physics step, live):"));
            GUILayout.Label(Loc.T("  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N"));

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
                        $"{Loc.T(bump ? "bump" : "reb"),-4} {spring.velocity,7:F2}  {damper.force,9:F0}  " +
                        $"{game,8:F0}  {spring.force,8:F0}");

                    var vertical = WheelVerticalSlot.Describe(wc);
                    GUILayout.Label(Loc.Tf(
                        "    damper {0} N (game {1})   spring {2} N   tyre {3} mm / {4} N   hop {5} Hz   sub {6}",
                        damper.force.ToString("0"),
                        game.ToString("0"),
                        spring.force.ToString("0"),
                        vertical.HasValue ? (vertical.Value.Deflection * 1000f).ToString("0.0") : "-",
                        vertical.HasValue ? vertical.Value.TireForce.ToString("0") : "-",
                        vertical.HasValue ? vertical.Value.Frequency.ToString("0.0") : "-",
                        vertical.HasValue ? vertical.Value.Substeps.ToString() : "-"));

                    shown++;
                    if (shown >= 12) break;
                }
            }
            catch
            {
                // never break the panel
            }

            if (shown == 0) GUILayout.Label(Loc.T("  (no grounded wheels)"));
        }

        private static void DrawModelPicker<T>(string heading, string? description, string? extra,
            System.Collections.Generic.IReadOnlyList<T> all,
            T? current, ref bool open, Action<T> select, Func<T, string> name)
            where T : class
        {
            string? tooltip = description != null ? Loc.T(description) : null;
            if (extra != null) tooltip = tooltip != null ? tooltip + "\n\n" + extra : extra;
            PanelUi.Label(heading, tooltip);

            GUILayout.Space(6f);
            if (GUILayout.Button($"{name(current!)} ▼", GUILayout.Width(240f)))
                open = !open;

            if (!open) return;

            foreach (var item in all)
            {
                bool isCurrent = ReferenceEquals(item, current);
                string text = isCurrent ? $"● {name(item)}" : $"     {name(item)}";
                if (!GUILayout.Button(text, GUILayout.Width(236f))) continue;
                select(item);
                open = false;
            }
        }

        private static string Label(WheelController wc)
        {
            var config = wc.PartConfigurationWheel;
            if (config != null) return config.partType.ToString();
            return wc.gameObject.name;
        }

        private static void DrawSlider(string label, ConfigEntry<float>? entry, float min, float max, string format,
            string? tooltip = null)
        {
            if (entry == null) return;
            GUILayout.BeginHorizontal();
            PanelUi.Label($"{label}: {entry.Value.ToString(format)}", tooltip);
            float value = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(value, entry.Value))
                entry.Value = value;
        }
    }
}
