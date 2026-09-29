#nullable enable

using System.Runtime.CompilerServices;
using NWH.WheelController3D;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>Identity of the tire a wheel was built from, captured at spawn.</summary>
    internal struct TireIdentity
    {
        internal PartType Type;
        internal float Grip;
        internal float PartRadius;
        internal float PartWidth;
        internal bool Captured;
    }

    /// <summary>
    /// Per-wheel tire identity. Captured from <c>WheelPropertiesSetter.SetWheelProperties</c>,
    /// which is the one place the game knows both the wheel and the part it was made from.
    /// </summary>
    internal static class TireIdentities
    {
        private sealed class Holder
        {
            internal TireIdentity Identity;
        }

        private static readonly ConditionalWeakTable<WheelController, Holder> Table =
            new ConditionalWeakTable<WheelController, Holder>();

        internal static void Set(WheelController wheel, in TireIdentity identity)
            => Table.GetOrCreateValue(wheel).Identity = identity;

        internal static bool TryGet(WheelController wheel, out TireIdentity identity)
        {
            if (Table.TryGetValue(wheel, out var holder))
            {
                identity = holder.Identity;
                return identity.Captured;
            }

            identity = default;
            return false;
        }
    }
}
