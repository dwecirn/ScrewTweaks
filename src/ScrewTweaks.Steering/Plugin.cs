#nullable enable

using System.Reflection;
using BepInEx;
using HarmonyLib;
using ScrewTweaks.Panel;
using ScrewTweaks.Steering.Generated;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.steering";
        public const string Name = "Screw Tweaks - Steering";
        public const string Version = PluginVersion.Value;
    }

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID, ScrewTweaks.Panel.PluginInfo.Version)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            SteeringSettings.LimitRelaxConfig = Config.Bind(
                "Steering",
                "LimitRelax",
                0.0f,
                "How much of the game's speed-sensitive steering limit to blend away, 0 to 1. " +
                "0 = the game's own limit. Also on the game's controls page, next to Instant Steering.");

            SteeringSettings.InstantConfig = Config.Bind(
                "Steering",
                "InstantSteering",
                false,
                "Apply binary (keyboard / d-pad) steering input in one frame instead of ramping it, the " +
                "way an analog stick already behaves. Also on the game's controls page.");
        }

        private void Start()
        {
            Localize();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            PanelHost.Register("Steering", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private static void Localize()
        {
            Loc.Add(PanelLanguage.ChineseSimplified,
                ("Steering", "转向"),
                (" Instant steering", " 瞬间转向"),
                ("Binary (keyboard / d-pad) steering is applied in one frame instead of being ramped.",
                    "键盘 / 十字键这类二值输入立即到位，不再被逐渐推过去。"),
                ("Steering limit relax: {0}", "转向限制松弛：{0}"),
                ("Blends the game's speed-sensitive steering limit toward none. 0 = vanilla.",
                    "把游戏随速度收紧的转向限制朝“无限制”方向混合。0 = 原版。"),
                ("Both are the same settings as the two rows on the game's controls page; either place changes them,",
                    "这两项和游戏操控设置页里的那两行是同一份设置；两边都能改，"),
                ("and both are saved in the mod's config rather than in the game's settings.",
                    "而且它们保存在 mod 的配置里，不在游戏设置里。"));

            Loc.Add(PanelLanguage.Japanese,
                ("Steering", "ステアリング"),
                (" Instant steering", " インスタントステアリング"),
                ("Binary (keyboard / d-pad) steering is applied in one frame instead of being ramped.",
                    "キーボード／十字キーのような二値入力でも、徐々に動かされず 1 フレームで目標角度に届きます。"),
                ("Steering limit relax: {0}", "ステアリング制限の緩和: {0}"),
                ("Blends the game's speed-sensitive steering limit toward none. 0 = vanilla.",
                    "速度に応じて厳しくなるステアリング制限を「制限なし」側へ混ぜます。0 = バニラ。"),
                ("Both are the same settings as the two rows on the game's controls page; either place changes them,",
                    "どちらもゲームの操作設定ページにある 2 行と同じ設定です。どこで変えても共通で、"),
                ("and both are saved in the mod's config rather than in the game's settings.",
                    "mod の設定ファイルに保存され、ゲームの設定ではありません。"));
        }

        private void Update()
        {
            // Picks up a change made in the game's own controls page as well as in the panel.
            if (SteeringSettings.ConsumeInstantChange())
                SteeringSpeedApplier.ApplyToAll();
        }

        private void DrawSection()
        {
            bool instant = SteeringSettings.InstantSteering;
            bool wanted = PanelUi.Toggle(Loc.T(" Instant steering"), instant,
                Loc.T("Binary (keyboard / d-pad) steering is applied in one frame instead of being ramped."));
            if (wanted != instant)
            {
                SteeringSettings.SetInstant(wanted);
                SteeringSpeedApplier.ApplyToAll();
            }

            GUILayout.Space(12f);
            string shared = Loc.T("Both are the same settings as the two rows on the game's controls page; either place changes them,")
                            + " " + Loc.T("and both are saved in the mod's config rather than in the game's settings.");

            GUILayout.BeginHorizontal();
            PanelUi.Label(Loc.Tf("Steering limit relax: {0}", SteeringSettings.LimitRelax.ToString("0.00")),
                Loc.T("Blends the game's speed-sensitive steering limit toward none. 0 = vanilla.") + "\n\n" + shared);
            float relax = GUILayout.HorizontalSlider(SteeringSettings.LimitRelax, 0f, 1f, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(relax, SteeringSettings.LimitRelax))
                SteeringSettings.SetLimitRelax(relax);
        }
    }
}
