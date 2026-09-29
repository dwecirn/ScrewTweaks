#nullable enable

using System.Runtime.CompilerServices;
using NWH.WheelController3D;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Per-wheel transient tyre state. The relaxed (delayed) slip is what the model feeds to the
    /// curves: a real tyre needs a certain distance to build up slip, so the force lags the
    /// instantaneous kinematics instead of appearing instantly.
    /// </summary>
    internal sealed class TireState
    {
        internal float KappaRelaxed;
        internal float AlphaRelaxed;
    }

    /// <summary>State table keyed by wheel; entries disappear with the wheel.</summary>
    internal static class TireStates
    {
        private static readonly ConditionalWeakTable<WheelController, TireState> Table =
            new ConditionalWeakTable<WheelController, TireState>();

        internal static TireState Get(WheelController wheel) => Table.GetOrCreateValue(wheel);
    }
}
