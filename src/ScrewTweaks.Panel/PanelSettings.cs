#nullable enable

using BepInEx.Configuration;

namespace ScrewTweaks.Panel
{
    public enum PanelSide
    {
        Right,
        Left
    }

    /// <summary>Panel settings, persisted in dev.dwecirn.screwtweaks.panel.cfg.</summary>
    internal static class PanelSettings
    {
        internal static ConfigEntry<PanelSide>? SideConfig;
        internal static ConfigEntry<PanelLanguage>? LanguageConfig;

        internal static PanelSide Side => SideConfig?.Value ?? PanelSide.Right;

        /// <summary>Which screen edge the panel is anchored to, and centred on vertically.</summary>
        internal static bool OnRight => Side == PanelSide.Right;

        internal static PanelLanguage Language => LanguageConfig?.Value ?? PanelLanguage.English;

        internal static void SetSide(PanelSide side)
        {
            if (SideConfig != null) SideConfig.Value = side;
        }

        internal static void SetLanguage(PanelLanguage language)
        {
            if (LanguageConfig != null) LanguageConfig.Value = language;
        }
    }
}
