#nullable enable

using BepInEx;
using ScrewTweaks.Panel;
using ScrewTweaks.Views.Generated;
using UnityEngine;

namespace ScrewTweaks.Views
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.views";
        public const string Name = "Screw Tweaks - Views";
        public const string Version = PluginVersion.Value;
    }

    [BepInDependency(ScrewTweaks.Panel.PluginInfo.GUID, ScrewTweaks.Panel.PluginInfo.Version)]
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private readonly CameraTakeover _takeover = new CameraTakeover();

        private void Awake()
        {
            ViewSettings.EnabledConfig = Config.Bind(
                "Follow",
                "Enabled",
                false,
                "Bolt the camera to the car for one of the game's camera modes: no smoothing, no lag, " +
                "and the horizon tilts with the car. Also the checkbox in the panel's Views section.");

            ViewSettings.ModeConfig = Config.Bind(
                "Follow",
                "Mode",
                10,
                "Which of the game's camera modes to patch, as the game numbers them. Switch to that " +
                "view in game, then press 'use current' in the panel.");

            ViewSettings.PoseConfig = Config.Bind(
                "Follow",
                "Pose",
                RigidChase.Name,
                "The pose algorithm, by name. Anything registered through ViewPoses.Register can be " +
                "named here.");

            ViewSettings.DistanceConfig = Config.Bind(
                "Follow",
                "Distance",
                3.0f,
                "How far behind the car the camera sits, as a multiple of the car's own size radius.");

            ViewSettings.HeightConfig = Config.Bind(
                "Follow",
                "Height",
                1.4f,
                "How far above the car the camera sits, as a multiple of the car's own size radius.");

            ViewSettings.AimHeightConfig = Config.Bind(
                "Follow",
                "AimHeight",
                0.3f,
                "The height in the car's own frame that the camera aims at, as a multiple of the " +
                "car's own size radius.");
        }

        private void Start()
        {
            Localize();

            ViewPoses.Register(RigidChase.Shared);
            PanelHost.Register("Views", DrawSection);

            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void LateUpdate()
        {
            Follow.Tick(_takeover);
        }

        private static void Localize()
        {
            Loc.Add(PanelLanguage.ChineseSimplified,
                ("Views", "视角"),
                (" Rigid chase", " 刚性跟随"),
                ("Third person, bolted to the car: no smoothing, no lag, and the horizon tilts with the car the way it does in the seat view. Only the mode below is touched.",
                    "第三人称，直接拴在车身上：没有平滑、没有滞后，地平线随车倾斜，和驾驶舱视角一样。只改下面那个视角。"),
                ("Patched mode: {0}   ·   game is on {1}", "patch 的视角：{0}   ·   游戏当前：{1}"),
                ("Which of the game's camera modes this patches. Switch to the view you want in game, then press use current.",
                    "要 patch 的是游戏哪个视角。先在游戏里切到你要的那个，再点“取用当前”。"),
                ("Use current", "取用当前"),
                ("Following.", "跟随中。"),
                ("Waiting for the patched mode.", "正在等待被 patch 的视角。"),
                ("Pose: {0}", "姿态：{0}"),
                ("The algorithm that places the camera. Registered through ViewPoses.Register.",
                    "决定相机位置与姿态的算法。通过 ViewPoses.Register 注册。"));

            Loc.Add(PanelLanguage.Japanese,
                ("Views", "ビュー"),
                (" Rigid chase", " リジッドチェイス"),
                ("Third person, bolted to the car: no smoothing, no lag, and the horizon tilts with the car the way it does in the seat view. Only the mode below is touched.",
                    "三人称。車体に直接固定され、平滑化も遅れもなく、シート視点と同じく地平線が車と一緒に傾きます。下のモードだけが対象です。"),
                ("Patched mode: {0}   ·   game is on {1}", "対象モード: {0}   ·   ゲームの現在: {1}"),
                ("Which of the game's camera modes this patches. Switch to the view you want in game, then press use current.",
                    "ゲームのどの視点を対象にするか。先にゲーム内で目的の視点に切り替え、「現在を使う」を押します。"),
                ("Use current", "現在を使う"),
                ("Following.", "追従中。"),
                ("Waiting for the patched mode.", "対象モードを待っています。"),
                ("Pose: {0}", "ポーズ: {0}"),
                ("The algorithm that places the camera. Registered through ViewPoses.Register.",
                    "カメラの位置と向きを決めるアルゴリズム。ViewPoses.Register で登録します。"));
        }

        private void DrawSection()
        {
            bool on = ViewSettings.Enabled;
            bool wanted = PanelUi.Toggle(Loc.T(" Rigid chase"), on, Loc.T(
                "Third person, bolted to the car: no smoothing, no lag, and the horizon tilts with the car the way it does in the seat view. Only the mode below is touched."));

            if (wanted != on) ViewSettings.Enabled = wanted;

            GUILayout.Space(12f);

            GUILayout.BeginHorizontal();
            PanelUi.Label(
                Loc.Tf("Patched mode: {0}   ·   game is on {1}", ViewSettings.Mode, Follow.GameMode),
                Loc.T("Which of the game's camera modes this patches. Switch to the view you want in game, then press use current."));
            if (GUILayout.Button(Loc.T("Use current"), GUILayout.Width(96f)))
                ViewSettings.Mode = Follow.GameMode;
            GUILayout.EndHorizontal();

            PanelUi.Label(
                Follow.Following ? Loc.T("Following.") : Loc.T("Waiting for the patched mode."),
                Loc.T("The pose is written after the game's own camera code, and the mode below is the only one it touches."));

            PanelUi.Label(
                Loc.Tf("Pose: {0}", ViewSettings.Pose),
                Loc.T("The algorithm that places the camera. Registered through ViewPoses.Register."));
        }
    }
}
