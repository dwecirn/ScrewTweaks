#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Panel
{
    /// <summary>Which side of the window the tab column is drawn on.</summary>
    public enum TabSide
    {
        Left,
        Right
    }

    /// <summary>Panel settings, persisted in dev.dwecirn.screwtweaks.panel.cfg.</summary>
    internal static class PanelSettings
    {
        internal static ConfigEntry<TabSide>? TabSideConfig;
        internal static ConfigEntry<PanelLanguage>? LanguageConfig;

        internal static TabSide TabSide => TabSideConfig?.Value ?? TabSide.Left;

        internal static bool TabsOnRight => TabSide == TabSide.Right;

        internal static PanelLanguage Language => LanguageConfig?.Value ?? PanelLanguage.English;

        internal static void SetTabSide(TabSide side)
        {
            if (TabSideConfig != null) TabSideConfig.Value = side;
        }

        internal static void SetLanguage(PanelLanguage language)
        {
            if (LanguageConfig != null) LanguageConfig.Value = language;
        }
    }
}
