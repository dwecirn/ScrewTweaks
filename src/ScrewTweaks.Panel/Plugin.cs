#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using SappInput;
using SappUnityUtils.CursorManagement;
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
    public class Plugin : BaseUnityPlugin, ICursorHider, IInputBlocker
    {
        private const int WindowId = 0x5EC1;

        private const string SuitePrefix = "dev.dwecirn.screwtweaks";
        private const string SuiteName = "Screw Tweaks - ";

        // The tab column sizes itself to the widest caption; only these bounds are fixed.
        private const float MinSidebarWidth = 96f;
        private const float MaxSidebarWidth = 190f;

        // Room the content column needs before the tab column is allowed to keep growing.
        private const float MinContentWidth = 220f;
        private const float PreferredContentWidth = 410f;

        // Unity's window style padding plus the outer margin.
        private const float WindowPadding = 44f;
        private const float Margin = 24f;

        private const float MinWindowWidth = MinSidebarWidth + MinContentWidth + WindowPadding;

        private const float ScrollbarWidth = 8f;
        private const float GripSize = 16f;

        // Starting size. Height is a fraction of the screen; the window is resizable afterwards.
        private const float HeightFraction = 0.30f;
        private const float MinHeight = 180f;
        private const float MaxInitialHeight = 400f;

        private bool _shown;

        // True while the panel is registered with the game as a cursor hider and an input blocker.
        private bool _claiming;

        // Two separate selectors rather than one index: the section list is appended to while plugins
        // start, and Settings is not a section at all.
        private int _active;
        private bool _settingsActive = true;

        private bool _placed;
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;

        private float _sidebarWidth = MinSidebarWidth;

        private bool _resizing;
        private bool _gripOnRight = true;

        private Vector2 _tabScroll;
        private Vector2 _contentScroll;

        private Rect _window = new Rect(0f, 0f, PreferredContentWidth, 320f);

        private static GUIStyle? _thinScrollbar;

        private void Awake()
        {
            PanelSettings.TabSideConfig = Config.Bind(
                "Panel",
                "TabSide",
                TabSide.Left,
                "Which side of the window the tab column sits on. Left or Right. " +
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

            if (_shown != _claiming)
                Claim(_shown);
        }

        /// <summary>
        /// Joins the game's own cursor and input arbitration while the panel is up. Setting
        /// <c>Cursor.lockState</c> directly does not hold: the game's CursorManager rewrites it every
        /// frame from its list of registered hiders, and the driving camera is one of them.
        /// </summary>
        private void Claim(bool claimed)
        {
            _claiming = claimed;

            if (claimed)
            {
                CursorManager.AddCursorHider(this);
                InputAccess.AddInputBlocker(this);
            }
            else
            {
                CursorManager.RemoveCursorHider(this);
                InputAccess.RemoveInputBlocker(this);
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

            // Never let the tab column squeeze the content below what it needs, however narrow the
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
        /// Opened against the right edge of the screen, vertically centred. Only re-done on the first
        /// frame and on a resolution change, so a size and position the player dragged out survive.
        /// </summary>
        private void Place()
        {
            bool resized = Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight;
            if (_placed && !resized) return;

            if (!_placed || resized)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                _window.width = _sidebarWidth + PreferredContentWidth + WindowPadding;
                _window.height = Mathf.Clamp(Screen.height * HeightFraction, MinHeight, MaxInitialHeight);
            }

            _placed = true;

            _window.x = Mathf.Max(Margin, Screen.width - _window.width - Margin);
            _window.y = Mathf.Max(Margin, (Screen.height - _window.height) * 0.5f);
        }

        private void DrawWindow(int id)
        {
            var sections = PanelHost.All;
            if (sections.Count == 0) _settingsActive = true;
            if (_active < 0 || _active >= sections.Count) _active = 0;

            PanelUi.BeginWindow();
            DrawBackdrop();

            // The grip sits in the corner that is free to follow the mouse, so which one it is depends
            // on whether the window is currently up against the right edge of the screen.
            if (!_resizing) _gripOnRight = _window.xMax < Screen.width - Margin - 2f;

            GUILayout.BeginHorizontal();
            if (PanelSettings.TabsOnRight)
            {
                DrawContent(sections);
                DrawSidebar(sections);
            }
            else
            {
                DrawSidebar(sections);
                DrawContent(sections);
            }
            GUILayout.EndHorizontal();

            DrawTooltip();
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

            // Settings first: it is what you want on opening the panel, and the section list follows it.
            // No width on the buttons: they take the column's width, so the content can never be wider
            // than the view and no horizontal scrollbar is ever needed.
            if (GUILayout.Button(TabLabel(Loc.T("Settings"), _settingsActive)))
            {
                if (!_settingsActive) _contentScroll = Vector2.zero;
                _settingsActive = true;
            }

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
            PanelUi.Label(Loc.T("Panel settings"),
                Loc.T("Drag the title bar to move the window, the dotted grip in a corner to resize it."));
            GUILayout.Space(10f);

            GUILayout.Label(Loc.T("Tab side"));
            GUILayout.BeginHorizontal();
            if (Choice(Loc.T("Left"), !PanelSettings.TabsOnRight)) PanelSettings.SetTabSide(TabSide.Left);
            if (Choice(Loc.T("Right"), PanelSettings.TabsOnRight)) PanelSettings.SetTabSide(TabSide.Right);
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
            DrawModules();

            GUILayout.Space(10f);
            GUILayout.Label(Loc.Tf("Settings are saved to {0}.", $"{PluginInfo.GUID}.cfg"));
        }

        /// <summary>
        /// What is actually loaded, with versions, taken from BepInEx rather than from a list kept here:
        /// that way it reports what is running instead of what should be, and it picks up modules this
        /// panel has never heard of as long as they are part of the suite.
        /// </summary>
        private static void DrawModules()
        {
            PanelUi.Label(Loc.T("Modules"),
                Loc.T("Read from BepInEx, so this is what is loaded rather than what should be."));

            try
            {
                foreach (var pair in BepInEx.Bootstrap.Chainloader.PluginInfos.OrderBy(p => p.Value?.Metadata?.Name))
                {
                    if (!pair.Key.StartsWith(SuitePrefix, StringComparison.Ordinal)) continue;

                    var metadata = pair.Value?.Metadata;
                    string name = metadata?.Name ?? pair.Key;
                    if (name.StartsWith(SuiteName, StringComparison.Ordinal))
                        name = name.Substring(SuiteName.Length);

                    GUILayout.Label($"  {name}   {metadata?.Version}");
                }
            }
            catch
            {
                // never break the panel
            }
        }

        /// <summary>
        /// A background of our own. The skin's window is translucent, which is fine over a menu and not over
        /// a moving scene: the text is the thing that has to stay readable. Drawn first, so everything else
        /// lands on top, and a little oversized so the style's own padding is covered too.
        /// </summary>
        private void DrawBackdrop()
        {
            if (Event.current.type != EventType.Repaint) return;

            Color previous = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.94f);
            GUI.DrawTexture(new Rect(-16f, -16f, _window.width + 32f, _window.height + 32f),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        /// <summary>
        /// Draws whatever the section under the mouse asked to explain. Kept inside the window so it is
        /// clipped with it, and flipped above the cursor when there is no room below.
        /// </summary>
        private void DrawTooltip()
        {
            if (Event.current.type != EventType.Repaint) return;

            string text = PanelUi.PendingTooltip;
            if (string.IsNullOrEmpty(text)) return;

            var style = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                fontSize = 11,
                padding = new RectOffset(8, 8, 6, 6)
            };

            float width = Mathf.Min(360f, Mathf.Max(120f, _window.width - 20f));
            float height = style.CalcHeight(new GUIContent(text), width);

            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Clamp(mouse.x + 18f, 4f, Mathf.Max(4f, _window.width - width - 4f));
            float y = mouse.y + 22f;
            if (y + height > _window.height - 4f) y = Mathf.Max(4f, mouse.y - height - 8f);

            GUI.Box(new Rect(x, y, width, height), text, style);
        }

        /// <summary>
        /// A small dot grip, drawn in the free corner of the window. Unity's IMGUI windows are not
        /// resizable on their own, so this is the whole mechanism: the drag becomes a size change, and
        /// the mouse events are consumed before <c>GUI.DragWindow</c> can move the window instead.
        ///
        /// Which corner is free depends on where the window is. Pinned against the right edge of the
        /// screen, growing to the right is impossible, so the window has to grow leftwards - and then
        /// the grip belongs in the bottom-left corner, because that is the corner that will follow the
        /// mouse. Anywhere else the grip is in the bottom-right, as usual.
        /// </summary>
        private void DrawResizeGrip()
        {
            var grip = new Rect(
                _gripOnRight ? _window.width - GripSize : 0f,
                _window.height - GripSize,
                GripSize, GripSize);

            if (Event.current.type == EventType.Repaint)
            {
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.35f);

                float column = _gripOnRight ? grip.xMax - 5f : grip.xMin + 2f;
                float step = _gripOnRight ? -4f : 4f;
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col <= row; col++)
                    {
                        GUI.DrawTexture(
                            new Rect(column + row * step, grip.yMax - 5f - col * 4f, 3f, 3f),
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
            float screenLimit = Mathf.Max(MinWindowWidth, Screen.width - 2f * Margin);

            // The edge being dragged moves; the opposite one stays. On the right the window is also
            // capped by the room left to the screen edge, so it cannot be dragged off screen.
            float maxWidth = _gripOnRight
                ? Mathf.Max(MinWindowWidth, Mathf.Min(screenLimit, Screen.width - Margin - _window.x))
                : screenLimit;

            float desired = _gripOnRight ? _window.width + dx : _window.width - dx;
            float width = Mathf.Clamp(desired, MinWindowWidth, maxWidth);

            float maxHeight = Mathf.Max(MinHeight, Screen.height - 2f * Margin);
            float height = Mathf.Clamp(_window.height + dy, MinHeight, maxHeight);

            if (!_gripOnRight) _window.x += _window.width - width;

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

        // The panel wants the cursor free, so it never hides or confines it. Its priority sits above
        // the driving hider (0) and below the escape menu (101), so a game menu still takes over.
        bool ICursorHider.ShouldBeConsidered() => _shown;
        bool ICursorHider.IsHidingAndLockingCursor() => false;
        bool ICursorHider.IsConfiningCursor() => false;
        bool ICursorHider.IsOnlyHidingCursor() => false;
        int ICursorHider.ProvidePriority() => 100;

        // The panel is a menu: while it is up, clicks and keys belong to it and not to the game.
        bool IInputBlocker.IsBlockingInput() => _shown;
    }
}
