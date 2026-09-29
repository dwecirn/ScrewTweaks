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

        // The sidebar sizes itself to the widest tab caption; only these bounds are fixed.
        private const float MinSidebarWidth = 96f;
        private const float MaxSidebarWidth = 190f;

        // Room the content column needs before the sidebar is allowed to keep growing.
        private const float MinContentWidth = 220f;
        private const float PreferredContentWidth = 410f;

        // Window chrome: Unity's window style padding plus the outer margin.
        private const float WindowPadding = 44f;
        private const float Margin = 24f;

        private const float ScrollbarWidth = 8f;
        private const float GripSize = 16f;

        // Starting size. Height is a fraction of the screen; the window is resizable from its
        // lower-right corner afterwards.
        private const float HeightFraction = 0.30f;
        private const float MinHeight = 180f;
        private const float MaxInitialHeight = 400f;

        private bool _shown;

        // Two separate selectors rather than one index: the section list is appended to while plugins
        // start, and Settings is not a section at all.
        private int _active;
        private bool _settingsActive;

        private bool _placed;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;
        private PanelSide _placedSide = PanelSide.Right;

        private float _sidebarWidth = MinSidebarWidth;
        private bool _resizing;

        private Vector2 _tabScroll;
        private Vector2 _contentScroll;

        private Rect _window = new Rect(0f, 0f, PreferredContentWidth, 320f);

        private static GUIStyle? _thinScrollbar;

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

            // Measured every frame, so switching language re-splits the window instead of clipping.
            MeasureSidebar();
            Place();

            _window = GUILayout.Window(WindowId, _window, DrawWindow, $"Screw Tweaks  ({KeyBinds.Panel})");
        }

        /// <summary>
        /// Width of the tab column: the widest caption plus room for the scrollbar, clamped. The tab
        /// buttons themselves are left to fill whatever this ends up being, which is what keeps a
        /// horizontal scrollbar from ever appearing.
        /// </summary>
        private void MeasureSidebar()
        {
            float widest = CaptionWidth(Loc.T("Settings"));
            var sections = PanelHost.All;
            for (int i = 0; i < sections.Count; i++)
            {
                float width = CaptionWidth(Loc.T(sections[i].Title));
                if (width > widest) widest = width;
            }

            float wanted = Mathf.Clamp(widest + ScrollbarWidth + 22f, MinSidebarWidth, MaxSidebarWidth);

            // Never let the sidebar squeeze the content column below what it needs, however narrow the
            // window has been dragged.
            float available = _window.width - MinContentWidth - WindowPadding;
            _sidebarWidth = Mathf.Max(MinSidebarWidth, Mathf.Min(wanted, available));
        }

        private static float CaptionWidth(string text)
        {
            // The active caption carries a marker, so it is the one that has to fit.
            return GUI.skin.button.CalcSize(new GUIContent("> " + text)).x;
        }

        /// <summary>
        /// Anchored to the chosen edge and centred vertically. Re-done on the first frame, on a
        /// resolution change and when the side setting changes; a size or position the player dragged
        /// out is left alone otherwise.
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
                _window.width = _sidebarWidth + PreferredContentWidth + WindowPadding;
                _window.height = Mathf.Clamp(Screen.height * HeightFraction, MinHeight, MaxInitialHeight);
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

            DrawResizeGrip();

            // Runs last, and the grip consumes its own mouse events first, so dragging the corner
            // resizes instead of moving the window.
            GUI.DragWindow();
        }

        private void DrawSidebar(IReadOnlyList<PanelHost.Section> sections)
        {
            GUILayout.BeginVertical(GUILayout.Width(_sidebarWidth));
            _tabScroll = GUILayout.BeginScrollView(_tabScroll, false, false,
                GUIStyle.none, ThinScrollbar(), GUIStyle.none);

            // No width on the buttons: they take the column's width, so the content can never be wider
            // than the view and no horizontal scrollbar is ever needed.
            for (int i = 0; i < sections.Count; i++)
            {
                bool active = !_settingsActive && _active == i;
                if (GUILayout.Button(TabLabel(Loc.T(sections[i].Title), active)))
                {
                    if (!active) _contentScroll = Vector2.zero;
                    _settingsActive = false;
                    _active = i;
                }
            }

            if (GUILayout.Button(TabLabel(Loc.T("Settings"), _settingsActive)))
            {
                if (!_settingsActive) _contentScroll = Vector2.zero;
                _settingsActive = true;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawContent(IReadOnlyList<PanelHost.Section> sections)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _contentScroll = GUILayout.BeginScrollView(_contentScroll, false, true,
                GUIStyle.none, ThinScrollbar(), GUIStyle.none);
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

        /// <summary>
        /// A small dot grip in the corner. Unity's IMGUI windows are not resizable on their own, so
        /// this is the whole mechanism: the drag is turned into a size change and the mouse events are
        /// consumed before <c>GUI.DragWindow</c> gets a chance to move the window instead.
        /// </summary>
        private void DrawResizeGrip()
        {
            var grip = new Rect(_window.width - GripSize, _window.height - GripSize, GripSize, GripSize);

            if (Event.current.type == EventType.Repaint)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col <= row; col++)
                    {
                        GUI.DrawTexture(
                            new Rect(grip.xMax - 5f - row * 4f, grip.yMax - 5f - col * 4f, 3f, 3f),
                            Texture2D.whiteTexture);
                    }
                }
                GUI.color = previous;
            }

            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event current = Event.current;

            switch (current.GetTypeForControl(control))
            {
                case EventType.MouseDown:
                    if (current.button != 0 || !grip.Contains(current.mousePosition)) break;
                    _resizing = true;
                    GUIUtility.hotControl = control;
                    current.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_resizing) break;
                    ResizeBy(current.delta.x, current.delta.y);
                    current.Use();
                    break;

                case EventType.MouseUp:
                    if (!_resizing) break;
                    _resizing = false;
                    if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
                    current.Use();
                    break;
            }
        }

        private void ResizeBy(float dx, float dy)
        {
            float maxWidth = Mathf.Max(MinContentWidth, Screen.width - 2f * Margin);
            float minWidth = Mathf.Min(maxWidth,
                Mathf.Max(MinSidebarWidth + MinContentWidth + WindowPadding, _sidebarWidth + MinContentWidth + WindowPadding));
            float width = Mathf.Clamp(_window.width + dx, minWidth, maxWidth);

            float maxHeight = Mathf.Max(MinHeight, Screen.height - 2f * Margin);
            float height = Mathf.Clamp(_window.height + dy, MinHeight, maxHeight);

            // Growing on the right edge pushes the window left, so the anchored edge stays put.
            if (PanelSettings.OnRight) _window.x += _window.width - width;

            _window.width = width;
            _window.height = height;
            _window.y = Mathf.Clamp(_window.y, Margin, Mathf.Max(Margin, Screen.height - height - Margin));
        }

        /// <summary>
        /// Unity's scrollbars are about twice as wide as they need to be and carry arrow buttons. A
        /// copy with a fixed width and no margin keeps the skin's look without eating the layout.
        /// </summary>
        private static GUIStyle ThinScrollbar()
        {
            if (_thinScrollbar == null)
            {
                _thinScrollbar = new GUIStyle(GUI.skin.verticalScrollbar)
                {
                    fixedWidth = ScrollbarWidth,
                    margin = new RectOffset(2, 0, 0, 0)
                };
            }
            return _thinScrollbar;
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
