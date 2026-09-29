#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScrewTweaks.Panel
{
    /// <summary>
    /// The mouse is the one input the game reads straight from <see cref="Input"/> instead of through
    /// its action layer: the buttons, the cursor position, the scroll wheel and the "Mouse X" /
    /// "Mouse Y" axes. Blocking the action layer therefore stops the keyboard and the controller and
    /// lets the mouse through, which is the wrong way round. These prefixes are the single place that
    /// sees every one of those reads, so the panel can take the mouse and leave the keys alone.
    /// </summary>
    internal static class MouseInput
    {
        /// <summary>Set while the panel is up and the game should not see the mouse.</summary>
        internal static bool Claimed;

        // Off screen rather than zero, so the world raycasts behind the panel miss instead of picking
        // up whatever sits in the bottom left corner of the screen.
        private static readonly Vector3 Nowhere = new Vector3(-10000f, -10000f, 0f);

        // The game's own menu is a grid of buttons, so it gets the mouse back while it is open.
        private static bool Intercepted => Claimed && !EscapeMenu.IsOpen;

        [HarmonyPatch]
        private static class Buttons
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Input), nameof(Input.GetMouseButton), new[] { typeof(int) });
                yield return AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown), new[] { typeof(int) });
                yield return AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp), new[] { typeof(int) });
            }

            // False skips the original, and a skipped method returns default(bool), which is what a
            // mouse button reads out as when it is not pressed.
            private static bool Prefix() => !Intercepted;
        }

        [HarmonyPatch]
        private static class Axes
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
                yield return AccessTools.Method(typeof(Input), nameof(Input.GetAxisRaw), new[] { typeof(string) });
            }

            // Only the "Mouse X" / "Mouse Y" / "Mouse ScrollWheel" axes; every other axis, and every
            // key, is the game's.
            private static bool Prefix(string axisName, ref float __result)
            {
                if (!Intercepted || !axisName.StartsWith("Mouse", StringComparison.Ordinal)) return true;
                __result = 0f;
                return false;
            }
        }

        [HarmonyPatch(typeof(Input), nameof(Input.mousePosition), MethodType.Getter)]
        private static class Position
        {
            private static bool Prefix(ref Vector3 __result)
            {
                if (!Intercepted) return true;
                __result = Nowhere;
                return false;
            }
        }

        [HarmonyPatch(typeof(Input), nameof(Input.mouseScrollDelta), MethodType.Getter)]
        private static class Scroll
        {
            private static bool Prefix(ref Vector2 __result)
            {
                if (!Intercepted) return true;
                __result = Vector2.zero;
                return false;
            }
        }
    }
}
