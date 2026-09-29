#nullable enable

using System;
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
        private const float WindowWidth = 440f;
        private const float Margin = 24f;

        // Height as a fraction of the screen, clamped. Unity IMGUI windows can also be resized by
        // dragging the lower-right corner; this only sets the starting size.
        private const float HeightFraction = 0.30f;
        private const float MinHeight = 180f;
        private const float MaxHeight = 340f;

        private bool _shown;
        private int _active;
        private int _lastScreenWidth = -1;
        private Vector2 _scroll;
        private Rect _window = new Rect(0f, Margin, WindowWidth, 320f);

        private void Update()
        {
            if (Input.GetKeyDown(KeyBinds.Panel))
                _shown = !_shown;

            if (_shown)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void OnGUI()
        {
            if (!_shown) return;

            // Anchored to the right edge. Only re-anchored when the resolution changes (or on the
            // first frame), so a resize cannot strand the window off screen while dragging still
            // sticks.
            if (Screen.width != _lastScreenWidth)
            {
                _lastScreenWidth = Screen.width;
                _window.width = WindowWidth;
                _window.height = Mathf.Clamp(Screen.height * HeightFraction, MinHeight, MaxHeight);
                _window.x = Mathf.Max(Margin, Screen.width - _window.width - Margin);
            }

            _window = GUILayout.Window(0x5EC1, _window, DrawWindow, $"Screw Tweaks  ({KeyBinds.Panel})");
        }

        private void DrawWindow(int id)
        {
            var sections = PanelHost.All;
            if (sections.Count == 0)
            {
                GUILayout.Label("No sections registered.");
                GUI.DragWindow();
                return;
            }

            if (_active < 0 || _active >= sections.Count)
                _active = 0;

            GUILayout.BeginHorizontal();
            for (int i = 0; i < sections.Count; i++)
            {
                string title = i == _active ? $"[ {sections[i].Title} ]" : sections[i].Title;
                if (GUILayout.Button(title))
                    _active = i;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            _scroll = GUILayout.BeginScrollView(_scroll);
            try
            {
                sections[_active].Draw?.Invoke();
            }
            catch (Exception e)
            {
                GUILayout.Label("section error: " + e.Message);
            }
            GUILayout.EndScrollView();

            GUI.DragWindow();
        }
    }
}
