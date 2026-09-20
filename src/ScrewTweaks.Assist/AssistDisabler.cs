#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScrewTweaks.Assist
{
    public sealed class AssistDisabler
    {
        private const float DisabledSlipLimit = float.MaxValue;
        private const float DisabledSlipLimitBrake = float.MaxValue;

        private readonly List<MechanicalOutputWheel> _driveWheels = new();
        private readonly List<MechanicalOutputWheelBrake> _brakeWheels = new();
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarning;
        private readonly Action<string> _logError;

        public AssistDisabler(Action<string> logInfo, Action<string> logWarning, Action<string> logError)
        {
            _logInfo = logInfo;
            _logWarning = logWarning;
            _logError = logError;
        }

        public void ClearCache()
        {
            _driveWheels.Clear();
            _brakeWheels.Clear();
        }

        public bool ProbeCurrentCar()
        {
            try
            {
                ClearCache();

                var cars = UnityEngine.Object.FindObjectsByType<SimpleCar2>(FindObjectsSortMode.None);
                _logInfo($"SimpleCar2 count = {cars.Length}");

                if (cars.Length == 0)
                {
                    _logWarning("No SimpleCar2 found.");
                    return false;
                }

                SimpleCar2? target = null;
                foreach (var car in cars)
                {
                    if (car?.gameObject.activeInHierarchy == true)
                    {
                        target = car;
                        break;
                    }
                }

                if (target == null)
                {
                    _logWarning("No active SimpleCar2 found.");
                    return false;
                }

                _logInfo($"Target: {target.name} active={target.gameObject.activeInHierarchy}");

                // Zero out DrivingCar-level assist strengths (affects MechanicalOutputWheelBrake TCS)
                if (target.TryGetComponent<DrivingCar>(out var drivingCar))
                {
                    drivingCar.TCStrength = 0f;
                    drivingCar.ABSStrength = 0f;
                    _logInfo($"DrivingCar: TCStrength→0 ABSStrength→0 Speed={drivingCar.CurrentSpeed:0.###}");
                }
                else
                {
                    _logWarning("No DrivingCar component found.");
                }

                // Drive wheels (MechanicalOutputWheel) — TCS via slipLimit, ABS via slipLimitBrake
                var driveWheels = target.GetComponentsInChildren<MechanicalOutputWheel>(true);
                _logInfo($"MechanicalOutputWheel count: {driveWheels.Length}");

                foreach (var wheel in driveWheels)
                {
                    if (wheel != null) _driveWheels.Add(wheel);
                }

                // Brake-only wheels (MechanicalOutputWheelBrake) — ABS via slipLimitBrake only
                var brakeWheels = target.GetComponentsInChildren<MechanicalOutputWheelBrake>(true);
                _logInfo($"MechanicalOutputWheelBrake count: {brakeWheels.Length}");

                foreach (var wheel in brakeWheels)
                {
                    if (wheel != null) _brakeWheels.Add(wheel);
                }

                int totalWheels = _driveWheels.Count + _brakeWheels.Count;
                if (totalWheels == 0)
                {
                    _logWarning("No wheel components found at all.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logError($"Probe failed: {ex}");
                return false;
            }
        }

        public bool DisableAssists()
        {
            try
            {
                int totalWheels = _driveWheels.Count + _brakeWheels.Count;
                if (totalWheels == 0)
                {
                    _logWarning("No cached target. Call ProbeCurrentCar() first.");
                    return false;
                }

                foreach (var wheel in _driveWheels)
                {
                    wheel.slipLimit = DisabledSlipLimit;
                    wheel.slipLimitBrake = DisabledSlipLimitBrake;
                }

                foreach (var wheel in _brakeWheels)
                {
                    wheel.slipLimitBrake = DisabledSlipLimitBrake;
                }

                _logInfo($"TCS + ABS disabled on {_driveWheels.Count} drive wheels and {_brakeWheels.Count} brake wheels.");
                return true;
            }
            catch (Exception ex)
            {
                _logError($"DisableAssists failed: {ex}");
                return false;
            }
        }

        public bool ProbeAndDisable()
        {
            return ProbeCurrentCar() && DisableAssists();
        }
    }
}
