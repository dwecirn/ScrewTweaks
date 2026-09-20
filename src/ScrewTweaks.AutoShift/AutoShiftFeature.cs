#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScrewTweaks.AutoShift
{
    public static class AutoShiftFeature
    {
        private const float FastOverThreshFor = 0.2f;
        private const float FastShiftMinCooldownTime = 0.3f;
        private const float FastShiftDeadTime = 0.05f;

        private static FieldInfo? _overThreshForField;
        private static FieldInfo? _shiftMinCooldownField;
        private static FieldInfo? _shiftDeadTimeField;
        private static Action<string> _logInfo = _ => { };
        private static bool _hasApplied;

        public static void Init(Action<string> logInfo)
        {
            _logInfo = logInfo;
        }

        public static void Apply()
        {
            EnsureFields();

            var shifters = UnityEngine.Object.FindObjectsByType<MechanicalOutputShifter>(FindObjectsSortMode.None);
            int count = 0;
            foreach (var shifter in shifters)
            {
                if (shifter == null) continue;
                _overThreshForField!.SetValue(shifter, FastOverThreshFor);
                _shiftMinCooldownField!.SetValue(shifter, FastShiftMinCooldownTime);
                _shiftDeadTimeField!.SetValue(shifter, FastShiftDeadTime);
                count++;
            }

            _hasApplied = true;
            _logInfo($"[AutoShift] Applied — overThreshFor={FastOverThreshFor}s, " +
                     $"shiftMinCooldownTime={FastShiftMinCooldownTime}s, " +
                     $"shiftDeadTime={FastShiftDeadTime}s " +
                     $"on {count} MechanicalOutputShifter(s)");
        }

        private static void EnsureFields()
        {
            _overThreshForField ??= AccessTools.Field(typeof(MechanicalOutputShifter), "overThreshFor");
            _shiftMinCooldownField ??= AccessTools.Field(typeof(MechanicalOutputShifter), "shiftMinCooldownTime");
            _shiftDeadTimeField ??= AccessTools.Field(typeof(MechanicalOutputShifter), "shiftDeadTime");
        }

        [HarmonyPatch(typeof(MechanicalOutputShifter), "Start")]
        [HarmonyPostfix]
        internal static void OnStart(MechanicalOutputShifter __instance)
        {
            if (!_hasApplied || __instance == null) return;
            EnsureFields();
            _overThreshForField!.SetValue(__instance, FastOverThreshFor);
            _shiftMinCooldownField!.SetValue(__instance, FastShiftMinCooldownTime);
            _shiftDeadTimeField!.SetValue(__instance, FastShiftDeadTime);
        }
    }
}
