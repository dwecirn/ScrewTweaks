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

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID, ScrewTweaks.Panel.PluginInfo.Version)]
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
            Localize();

            AutoShiftFeature.Init(msg => Logger.LogInfo(msg));
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            PanelHost.Register("Auto Shift", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private static void Localize()
        {
            Loc.Add(PanelLanguage.ChineseSimplified,
                ("Auto Shift", "自动换挡"),
                (" Faster automatic shifting", " 更快的自动换挡"),
                ("RPM threshold held for", "转速阈值维持时间"),
                ("Minimum time between shifts", "两次换挡的最小间隔"),
                ("Torque cut while engaging", "啮合时的扭矩中断"),
                ("{0} s   (game: {1} s)", "{0} 秒   （原版 {1} 秒）"),
                ("The box is saved to the config file and applies immediately.",
                    "勾选会保存到配置文件，并立即生效。"),
                ("Unticking it restores the game's own shift timing.",
                    "取消勾选会恢复游戏自己的换挡时序。"));

            Loc.Add(PanelLanguage.Japanese,
                ("Auto Shift", "オートシフト"),
                (" Faster automatic shifting", " オートシフトを速くする"),
                ("RPM threshold held for", "RPM しきい値の保持時間"),
                ("Minimum time between shifts", "シフト間の最小時間"),
                ("Torque cut while engaging", "接続時のトルクカット"),
                ("{0} s   (game: {1} s)", "{0} 秒   （ゲーム: {1} 秒）"),
                ("The box is saved to the config file and applies immediately.",
                    "チェックは設定ファイルに保存され、すぐに反映されます。"),
                ("Unticking it restores the game's own shift timing.",
                    "チェックを外すとゲーム本来のシフトタイミングに戻ります。"));
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
            bool wanted = GUILayout.Toggle(enabled, Loc.T(" Faster automatic shifting"));
            if (wanted != enabled)
                _enabled.Value = wanted;

            GUILayout.Space(4f);
            var game = AutoShiftFeature.GameValues;
            TimingRow(Loc.T("RPM threshold held for"), AutoShiftFeature.FastOverThreshFor, game?.OverThreshFor);
            TimingRow(Loc.T("Minimum time between shifts"), AutoShiftFeature.FastShiftMinCooldownTime, game?.MinCooldown);
            TimingRow(Loc.T("Torque cut while engaging"), AutoShiftFeature.FastShiftDeadTime, game?.DeadTime);

            GUILayout.Space(10f);
            GUILayout.Label(Loc.T("The box is saved to the config file and applies immediately."));
            GUILayout.Label(Loc.T("Unticking it restores the game's own shift timing."));
        }

        /// <summary>
        /// One line of the comparison. The game's value only shows once a shifter has been seen, which
        /// is also when the feature learns it.
        /// </summary>
        private static void TimingRow(string label, float fast, float? game)
        {
            string value = game.HasValue
                ? Loc.Tf("{0} s   (game: {1} s)", fast.ToString("0.00"), game.Value.ToString("0.00"))
                : $"{fast:0.00} s";
            GUILayout.Label($"{label}   {value}");
        }
    }
}
