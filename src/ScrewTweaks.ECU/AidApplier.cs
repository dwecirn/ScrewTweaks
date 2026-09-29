#nullable enable

using System.Runtime.CompilerServices;
using UnityEngine;

namespace ScrewTweaks.ECU
{
    /// <summary>
    /// Applies the channel selection by neutralising the game's built-in aids on a per-wheel basis.
    ///
    /// The game's TCS/ABS are driven by public tuning fields on the wheels, so we do not need a
    /// transpiler: to take a channel off native we push the relevant limits to float.MaxValue,
    /// which makes the built-in "if (slip >= limit)" checks never fire. We remember the prefab
    /// values so selecting Native restores them exactly.
    /// </summary>
    internal static class AidApplier
    {
        private sealed class State
        {
            internal float SlipLimit;
            internal float SlipLimitBrake;
            internal float SidewaySpeedSupress;
            internal bool Captured;
        }

        private static readonly ConditionalWeakTable<Component, State> States = new();

        private static State GetState(Component component) => States.GetOrCreateValue(component);

        internal static void Apply(MechanicalOutputWheel wheel)
        {
            var state = GetState(wheel);
            if (!state.Captured)
            {
                state.SlipLimit = wheel.slipLimit;
                state.SlipLimitBrake = wheel.slipLimitBrake;
                state.SidewaySpeedSupress = wheel.sidewaySpeedSupress;
                state.Captured = true;
            }

            // ABS: neutralise the binary brake release unless we are on Native.
            wheel.slipLimitBrake = Aids.BrakeManaged ? float.MaxValue : state.SlipLimitBrake;

            // TCS: neutralise the slip cut and the lateral torque suppression.
            wheel.slipLimit = Aids.DriveManaged ? float.MaxValue : state.SlipLimit;
            wheel.sidewaySpeedSupress = Aids.DriveManaged ? float.MaxValue : state.SidewaySpeedSupress;
        }

        internal static void Apply(MechanicalOutputWheelBrake wheel)
        {
            var state = GetState(wheel);
            if (!state.Captured)
            {
                state.SlipLimitBrake = wheel.slipLimitBrake;
                state.Captured = true;
            }

            wheel.slipLimitBrake = Aids.BrakeManaged ? float.MaxValue : state.SlipLimitBrake;
        }
    }
}
