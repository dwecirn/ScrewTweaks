#nullable enable

using System;
using System.Collections.Generic;
using BepInEx;
using ScrewTweaks.Panel.Generated;
using UnityEngine;

namespace ScrewTweaks.Panel
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.panel";
        public const string Name = "Screw Tweaks - Panel";
        public const string Version = PluginVersion.Value;
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private const int WindowId = 0x5EC1;

        private const float SidebarWidth = 116f;
        private const float ContentWidth = 410f;
        private const float WindowWidth = SidebarWidth + ContentWidth + 44f;
        private const float Margin = 24f;

        // Height as a fraction of the screen, clamped. Unity IMGUI windows can also be resized by
        // dragging the lower-right corner; this only sets the starting size.
        private const float HeightFraction = 0.30f;
        private const float MinHeight = 200f;
        private const float MaxHeight = 380f;

        private bool _shown;

        // Two separate selectors rather than one index: the section list is appended to while plugins
        // start, and Settings is not a section at all.
        private int _active;
        private bool _settingsActive;

        private bool _placed;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;
        private PanelSide _placedSide = PanelSide.Right;

        private Vector2 _tabScroll;
        private Vector2 _contentScroll;

        private Rect _window = new Rect(0f, 0f, WindowWidth, 320f);

        private void Awake()
        {
            PanelSettings.SideConfig = Config.Bind(
                "Panel",
                "Side",
                PanelSide.Right,
                "Which screen edge the panel is anchored to, vertically centred. " +
                "Also switchable from the panel's Settings tab.");

            PanelSettings.LanguageConfig = Config.Bind(
                "Panel",
                "Language",
                PanelLanguage.English,
                "Panel language: English, ChineseSimplified or Japanese. " +
                "Also switchable from the panel's Settings tab.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyBinds.Panel))
                _shown = !_shown;

            // Also picks up a hand-edited config file.
            Loc.SetLanguage(PanelSettings.Language);

            if (_shown)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            if (!_shown) return;

            Place();
            _window = GUILayout.Window(WindowId, _window, DrawWindow, $"Screw Tweaks  ({KeyBinds.Panel})");
        }

        /// <summary>
        /// Anchored to the chosen edge and centred vertically. Re-done on the first frame, on a
        /// resolution change and when the side setting changes; otherwise the window stays where it was
        /// dragged to.
        /// </summary>
        private void Place()
        {
            bool resized = Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight;
            bool movedSide = PanelSettings.Side != _placedSide;
            if (_placed && !resized && !movedSide) return;

            if (!_placed || resized)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                _window.width = WindowWidth;
                _window.height = Mathf.Clamp(Screen.height * HeightFraction, MinHeight, MaxHeight);
            }

            _placed = true;
            _placedSide = PanelSettings.Side;

            _window.x = PanelSettings.OnRight
                ? Mathf.Max(Margin, Screen.width - _window.width - Margin)
                : Margin;
            _window.y = Mathf.Max(Margin, (Screen.height - _window.height) * 0.5f);
        }

        private void DrawWindow(int id)
        {
            var sections = PanelHost.All;
            if (sections.Count == 0) _settingsActive = true;
            if (_active < 0 || _active >= sections.Count) _active = 0;

            GUILayout.BeginHorizontal();
            DrawSidebar(sections);
            DrawContent(sections);
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private void DrawSidebar(IReadOnlyList<PanelHost.Section> sections)
        {
            GUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
            _tabScroll = GUILayout.BeginScrollView(_tabScroll, false, true, GUILayout.Width(SidebarWidth));

            float width = SidebarWidth - 20f;
            for (int i = 0; i < sections.Count; i++)
            {
                bool active = !_settingsActive && _active == i;
                if (GUILayout.Button(TabLabel(Loc.T(sections[i].Title), active), GUILayout.Width(width)))
                {
                    if (!active) _contentScroll = Vector2.zero;
                    _settingsActive = false;
                    _active = i;
                }
            }

            if (GUILayout.Button(TabLabel(Loc.T("Settings"), _settingsActive), GUILayout.Width(width)))
            {
                if (!_settingsActive) _contentScroll = Vector2.zero;
                _settingsActive = true;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawContent(IReadOnlyList<PanelHost.Section> sections)
        {
            GUILayout.BeginVertical(GUILayout.Width(ContentWidth));
            _contentScroll = GUILayout.BeginScrollView(_contentScroll);
            try
            {
                if (_settingsActive || sections.Count == 0)
                    DrawSettings();
                else
                    sections[_active].Draw?.Invoke();
            }
            catch (Exception e)
            {
                GUILayout.Label(Loc.Tf("Section error: {0}", e.Message));
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawSettings()
        {
            GUILayout.Label(Loc.T("Panel settings"));
            GUILayout.Space(10f);

            GUILayout.Label(Loc.T("Panel side"));
            GUILayout.BeginHorizontal();
            if (Choice(Loc.T("Right"), PanelSettings.OnRight)) PanelSettings.SetSide(PanelSide.Right);
            if (Choice(Loc.T("Left"), !PanelSettings.OnRight)) PanelSettings.SetSide(PanelSide.Left);
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label(Loc.T("Language"));
            GUILayout.BeginHorizontal();
            foreach (var language in Loc.All)
            {
                bool active = Loc.Language == language;
                if (!Choice(Loc.DisplayName(language), active)) continue;

                PanelSettings.SetLanguage(language);
                Loc.SetLanguage(language); // no one-frame lag on the labels
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(16f);
            GUILayout.Label($"{PluginInfo.Name}  {PluginInfo.Version}");
            GUILayout.Label(Loc.Tf("Settings are saved to {0}.", $"{PluginInfo.GUID}.cfg"));
            GUILayout.Label(Loc.T("Drag the title bar to move the window, the lower-right corner to resize it."));
        }

        /// <summary>A tab caption. The active one is marked rather than re-coloured.</summary>
        private static string TabLabel(string text, bool active)
        {
            return active ? $"> {text}" : $"  {text}";
        }

        private static bool Choice(string label, bool selected)
        {
            string text = selected ? $"● {label}" : $"  {label}";
            return GUILayout.Button(text, GUILayout.Width(96f));
        }
    }
}
