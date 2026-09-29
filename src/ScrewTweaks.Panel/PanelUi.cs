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
    ///
    /// A tooltip waits <see cref="HoverDelay"/> before appearing and resets whenever the cursor moves to a
    /// different control, so sweeping across the panel does not flash boxes on the way.
    /// </summary>
    public static class PanelUi
    {
        private const float HoverDelay = 1f;

        private static string? _current;
        private static float _since;
        private static bool _hoveredThisPass;

        /// <summary>The explanation to draw, or empty while nothing is hovered or the delay is running.</summary>
        internal static string PendingTooltip
        {
            get
            {
                if (!_hoveredThisPass || string.IsNullOrEmpty(_current)) return string.Empty;
                return Time.unscaledTime - _since >= HoverDelay ? _current! : string.Empty;
            }
        }

        internal static void BeginWindow()
        {
            _hoveredThisPass = false;
        }

        private static void Hover(string? tooltip)
        {
            if (string.IsNullOrEmpty(tooltip)) return;

            _hoveredThisPass = true;
            if (_current == tooltip) return;

            _current = tooltip;
            _since = Time.unscaledTime;
        }

        /// <summary>A label with an explanation shown while the mouse rests on it.</summary>
        public static void Label(string text, string? tooltip)
        {
            if (tooltip == null)
            {
                GUILayout.Label(text);
                return;
            }

            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), GUI.skin.label);
            GUI.Label(rect, text);

            if (rect.Contains(Event.current.mousePosition)) Hover(tooltip);
        }

        /// <summary>A toggle with an explanation shown while the mouse rests on it.</summary>
        public static bool Toggle(string label, bool value, string? tooltip)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(label), GUI.skin.toggle);
            bool result = GUI.Toggle(rect, value, label);

            if (rect.Contains(Event.current.mousePosition)) Hover(tooltip);

            return result;
        }
    }
}
