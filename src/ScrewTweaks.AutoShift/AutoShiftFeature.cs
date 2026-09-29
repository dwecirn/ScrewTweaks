#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScrewTweaks.AutoShift
{
    /// <summary>
    /// Makes automatic shifting react faster.
    ///
    /// <see cref="MechanicalOutputShifter"/> decides when to shift from three private, hard-coded
    /// fields:
    ///   overThreshFor         how long the RPM threshold must be held before shifting  (game: 1.00 s)
    ///   shiftMinCooldownTime  minimum time between two shifts                        (game: 0.75 s)
    ///   shiftDeadTime         torque cut while the gear is engaged                    (game: 0.15 s)
    ///
    /// Nothing else in the game sets or exposes them, so this is a genuine toggle and not a
    /// rebalance of game data. The game's own values are captured from the first shifter we touch
    /// and written back when the toggle goes off, so turning it off really restores the game.
    /// </summary>
    [HarmonyPatch]
    public static class AutoShiftFeature
    {
        internal const float FastOverThreshFor = 0.2f;
        internal const float FastShiftMinCooldownTime = 0.3f;
        internal const float FastShiftDeadTime = 0.05f;

        private static FieldInfo? _overThreshForField;
        private static FieldInfo? _shiftMinCooldownField;
        private static FieldInfo? _shiftDeadTimeField;
        private static Action<string> _logInfo = _ => { };

        private static float _originalOverThreshFor;
        private static float _originalShiftMinCooldownTime;
        private static float _originalShiftDeadTime;
        private static bool _hasOriginals;

        /// <summary>Whether the fast tuning is currently applied.</summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// The game's own shift timing, or null until a shifter has been seen. The panel shows these
        /// next to the fast values so the comparison is never hard-coded.
        /// </summary>
        public static (float OverThreshFor, float MinCooldown, float DeadTime)? GameValues
            => _hasOriginals
                ? (_originalOverThreshFor, _originalShiftMinCooldownTime, _originalShiftDeadTime)
                : null;

        public static void Init(Action<string> logInfo)
        {
            _logInfo = logInfo;
        }

        /// <summary>Apply the fast tuning to every shifter, or restore the game's own values.</summary>
        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            EnsureFields();

            var shifters = UnityEngine.Object.FindObjectsByType<MechanicalOutputShifter>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var shifter in shifters)
            {
                if (shifter == null) continue;
                Apply(shifter);
                count++;
            }

            _logInfo(enabled
                ? $"[AutoShift] On — overThreshFor={FastOverThreshFor}s, " +
                  $"shiftMinCooldownTime={FastShiftMinCooldownTime}s, " +
                  $"shiftDeadTime={FastShiftDeadTime}s on {count} MechanicalOutputShifter(s)"
                : $"[AutoShift] Off — the game's own shift timing restored on {count} MechanicalOutputShifter(s)");
        }

        private static void Apply(MechanicalOutputShifter shifter)
        {
            // Captured before the first write, so it is the value the class initialises itself with.
            // These fields are private and not serialised, which means every instance starts from the
            // same three numbers and one snapshot is enough for the whole session.
            if (!_hasOriginals)
            {
                _originalOverThreshFor = (float)_overThreshForField!.GetValue(shifter);
                _originalShiftMinCooldownTime = (float)_shiftMinCooldownField!.GetValue(shifter);
                _originalShiftDeadTime = (float)_shiftDeadTimeField!.GetValue(shifter);
                _hasOriginals = true;
                _logInfo($"[AutoShift] Captured the game's values: overThreshFor={_originalOverThreshFor}s, " +
                         $"shiftMinCooldownTime={_originalShiftMinCooldownTime}s, " +
                         $"shiftDeadTime={_originalShiftDeadTime}s");
            }

            _overThreshForField!.SetValue(shifter, Enabled ? FastOverThreshFor : _originalOverThreshFor);
            _shiftMinCooldownField!.SetValue(shifter, Enabled ? FastShiftMinCooldownTime : _originalShiftMinCooldownTime);
            _shiftDeadTimeField!.SetValue(shifter, Enabled ? FastShiftDeadTime : _originalShiftDeadTime);
        }

        private static void EnsureFields()
        {
            _overThreshForField ??= AccessTools.Field(typeof(MechanicalOutputShifter), "overThreshFor");
            _shiftMinCooldownField ??= AccessTools.Field(typeof(MechanicalOutputShifter), "shiftMinCooldownTime");
            _shiftDeadTimeField ??= AccessTools.Field(typeof(MechanicalOutputShifter), "shiftDeadTime");
        }

        /// <summary>
        /// A shifter that spawns while the toggle is on gets the fast tuning too. Nothing to do when
        /// it is off: a fresh instance already holds the game's own values.
        /// </summary>
        [HarmonyPatch(typeof(MechanicalOutputShifter), "Start")]
        [HarmonyPostfix]
        internal static void OnStart(MechanicalOutputShifter __instance)
        {
            if (!Enabled || __instance == null) return;
            EnsureFields();
            Apply(__instance);
        }
    }
}
