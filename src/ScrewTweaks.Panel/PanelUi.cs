#nullable enable

using UnityEngine;

namespace ScrewTweaks.Panel
{
    /// <summary>
    /// Small things every panel section needs, so that explanations can live on hover instead of taking up
    /// room in the panel itself.
    ///
    /// The hover is decided here rather than left to `GUI.tooltip`: Unity only sets that for controls whose
    /// style has tooltips, and it is the host that has to draw the box either way. Doing both here means a
    /// section only has to say what the explanation is.
    /// </summary>
    public static class PanelUi
    {
        internal static string PendingTooltip { get; private set; } = string.Empty;

        internal static void BeginWindow()
        {
            PendingTooltip = string.Empty;
        }

        /// <summary>A label with an explanation shown while the mouse is over it.</summary>
        public static void Label(string text, string? tooltip)
        {
            if (tooltip == null)
            {
                GUILayout.Label(text);
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), GUI.skin.label);
            GUI.Label(rect, text);

            if (rect.Contains(Event.current.mousePosition))
                PendingTooltip = tooltip;
        }

        /// <summary>A toggle with an explanation shown while the mouse is over it.</summary>
        public static bool Toggle(string label, bool value, string? tooltip)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(label), GUI.skin.toggle);
            bool result = GUI.Toggle(rect, value, label);

            if (tooltip != null && rect.Contains(Event.current.mousePosition))
                PendingTooltip = tooltip;

            return result;
        }
    }
}
