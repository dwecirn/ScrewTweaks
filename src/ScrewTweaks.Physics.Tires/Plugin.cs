#nullable enable

using System.Reflection;
using BepInEx;
using HarmonyLib;
using ScrewTweaks.UI;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    public static class PluginInfo
    {
        public const string GUID = "dev.dwecirn.screwtweaks.physics.tires";
        public const string Name = "Screw Tweaks - Tire Physics";
        public const string Version = "0.1.0";
    }

    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        private bool _open;

        private void Start()
        {
            TireModels.Init();
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), PluginInfo.GUID);
            Panel.Register("Tires", DrawSection);
            Logger.LogInfo($"[{PluginInfo.Name}] version {PluginInfo.Version} loaded.");
        }

        private void DrawSection()
        {
            var current = TireModels.Current;
            GUILayout.Label($"Tyre model: {(current != null ? current.Name : "none")}");
            if (current != null)
            {
                GUILayout.Label(current.Description);
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

            GUILayout.Space(10f);
            GUILayout.Label("The slot replaces WheelController.FrictionUpdate;");
            GUILayout.Label("Native keeps the game's original friction.");
        }
    }
}
