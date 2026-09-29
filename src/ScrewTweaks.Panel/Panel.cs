#nullable enable

using System;
using System.Collections.Generic;

namespace ScrewTweaks.Panel
{
    /// <summary>
    /// Shared in-game panel host (F7). Feature plugins register a titled section in their
    /// Start() and draw it with GUILayout; the host renders the tab bar and the active section.
    ///
    /// Named PanelHost rather than Panel because a type cannot share its name with the last segment
    /// of its own namespace: `ScrewTweaks.Panel.Panel` would make `Panel.Register(...)` resolve
    /// `Panel` to the namespace instead of the type.
    /// </summary>
    public static class PanelHost
    {
        internal sealed class Section
        {
            public string Title = string.Empty;
            public Action? Draw;
        }

        private static readonly List<Section> Sections = new List<Section>();

        /// <summary>Register (or replace) a panel section by title. Call from plugin Start().</summary>
        public static void Register(string title, Action draw)
        {
            foreach (var section in Sections)
            {
                if (section.Title != title) continue;
                section.Draw = draw;
                return;
            }
            Sections.Add(new Section { Title = title, Draw = draw });
        }

        internal static IReadOnlyList<Section> All => Sections;
    }
}
