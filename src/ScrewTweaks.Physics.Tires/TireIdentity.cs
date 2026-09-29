#nullable enable

using System.Collections.Generic;
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
    /// Per-wheel tire identity. Captured from <c>WheelPropertiesSetter</c>, which is the one place
    /// the game knows both the wheel and the part it was made from.
    ///
    /// A plain dictionary rather than a ConditionalWeakTable: the latter is not enumerable on
    /// .NET Framework, and the diagnostic panel needs to list what has been seen.
    /// </summary>
    internal static class TireIdentities
    {
        private static readonly Dictionary<WheelController, TireIdentity> Table =
            new Dictionary<WheelController, TireIdentity>();

        private static readonly List<WheelController> Stale = new List<WheelController>();

        internal static void Set(WheelController wheel, in TireIdentity identity) => Table[wheel] = identity;

        internal static bool TryGet(WheelController wheel, out TireIdentity identity)
            => Table.TryGetValue(wheel, out identity) && identity.Captured;

        /// <summary>Fills <paramref name="into"/> with every wheel that has a captured identity.</summary>
        internal static void Collect(List<(WheelController Wheel, TireIdentity Identity)> into)
        {
            into.Clear();

            Stale.Clear();
            foreach (var entry in Table)
            {
                if (entry.Key == null) Stale.Add(entry.Key!);
            }
            foreach (var wheel in Stale)
            {
                Table.Remove(wheel);
            }

            foreach (var entry in Table)
            {
                if (entry.Value.Captured) into.Add((entry.Key, entry.Value));
            }
        }
    }
}
