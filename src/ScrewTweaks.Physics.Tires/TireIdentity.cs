#nullable enable

using System.Collections.Generic;
using NWH.WheelController3D;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>Identity of the tire a wheel was built from.</summary>
    internal struct TireIdentity
    {
        internal PartType Type;
        internal float Grip;
        internal float PartRadius;
        internal float PartWidth;
        internal bool Captured;
    }

    /// <summary>
    /// Per-wheel tire identity, resolved lazily from <c>WheelController.PartConfigurationWheel</c>.
    ///
    /// No Harmony patch is needed for this: the controller already carries the part it was built
    /// from, which is also what the game itself uses (WheelSurfaceFrictionManager reads the same
    /// property to pick the tire's asphalt / sand FrictionPreset). Resolved once per wheel and
    /// cached, because Part.MakePart is a lookup rather than something to do per physics step.
    /// </summary>
    internal static class TireIdentities
    {
        private static readonly Dictionary<WheelController, TireIdentity> Table =
            new Dictionary<WheelController, TireIdentity>();

        private static readonly List<WheelController> Stale = new List<WheelController>();

        internal static bool TryGet(WheelController wheel, out TireIdentity identity)
        {
            if (Table.TryGetValue(wheel, out identity)) return identity.Captured;

            identity = Resolve(wheel);
            Table[wheel] = identity;
            return identity.Captured;
        }

        private static TireIdentity Resolve(WheelController wheel)
        {
            try
            {
                var configuration = wheel.PartConfigurationWheel;
                if (configuration == null || configuration.partType == PartType.NONE)
                    return default;

                var part = Part.MakePart(configuration.partType);
                if (part == null) return default;

                return new TireIdentity
                {
                    Type = configuration.partType,
                    Grip = part.Grip,
                    PartRadius = part.WheelRadius,
                    PartWidth = part.WheelWidth,
                    Captured = true,
                };
            }
            catch
            {
                return default;
            }
        }

        /// <summary>Fills <paramref name="into"/> with every wheel that has a resolved identity.</summary>
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
