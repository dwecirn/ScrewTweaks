#nullable enable

using System;
using BepInEx;
using ScrewTweaks.UI.Generated;
using UnityEngine;

namespace ScrewTweaks.UI
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.ui";
        public const string Name = "Screw Tweaks - UI";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _shown;
        private int _active;
        private Vector2 _scroll;
        private Rect _window = new Rect(24f, 24f, 440f, 420f);

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
            _window = GUILayout.Window(0x5EC1, _window, DrawWindow, $"Screw Tweaks  ({KeyBinds.Panel})");
        }

        private void DrawWindow(int id)
        {
            var sections = Panel.All;
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
