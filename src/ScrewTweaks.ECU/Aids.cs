#nullable enable

using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ScrewTweaks.ECU
{
    /// <summary>Everything an aid algorithm gets to see for one wheel on one physics step.</summary>
    public struct AidContext
    {
        /// <summary>The wheel this step belongs to. Null for the brake-only wheel type.</summary>
        public MechanicalOutputWheel? Wheel;

        /// <summary>
        /// The NWH wheel itself: load, radius, angular velocity, motor/brake torque, the friction
        /// preset in use, the latest slip values. Null when the car is not on the NWH backend.
        /// </summary>
        public NWH.WheelController3D.WheelController? Controller;

        /// <summary>Kinematic longitudinal slip. Positive while spinning up, negative while locking.</summary>
        public float ForwardSlip;

        /// <summary>Lateral contact speed.</summary>
        public float SidewaySlip;

        public float WheelRpm;
        public float CarSpeed;

        /// <summary>The brake torque the game asked for, before this aid touched it.</summary>
        public float DesiredBrake;
    }

    /// <summary>
    /// Brake-side electronic aid slot (ABS). Implement this and register it with
    /// <see cref="EcuAids.Register(IBrakeAid)"/> to add your own algorithm; it then shows up in the
    /// F7 panel next to the built-in ones.
    /// </summary>
    public interface IBrakeAid
    {
        /// <summary>Shown in the panel. Must be unique; this is what gets saved to the config.</summary>
        string Name { get; }

        string Description { get; }

        /// <summary>Return the brake torque to actually apply. Called every physics step while braking.</summary>
        float Apply(in AidContext ctx, float desiredBrake);
    }

    /// <summary>
    /// Drive-side electronic aid slot (traction control). Same contract as <see cref="IBrakeAid"/>,
    /// but the value being shaped is the motor torque.
    /// </summary>
    public interface IDriveAid
    {
        string Name { get; }
        string Description { get; }
        float Apply(in AidContext ctx, float desiredTorque);
    }

    /// <summary>
    /// Proportional ABS. Below the target slip it applies the full brake; above it the brake is
    /// scaled down by the slip error but never below <see cref="Aids.AbsFloor"/>, so deceleration
    /// (and therefore front weight transfer) is preserved while the wheel is kept from locking.
    /// </summary>
    internal sealed class ProgressiveAbs : IBrakeAid
    {
        public string Name => "Progressive";

        public string Description => "Cuts the brake in proportion to how far slip exceeds the target, with a floor.";

        public float Apply(in AidContext ctx, float desiredBrake)
        {
            if (desiredBrake <= 0f) return desiredBrake;

            float target = Aids.AbsTarget;
            float slip = Mathf.Abs(ctx.ForwardSlip);
            if (slip <= target) return desiredBrake;

            float factor = 1f - (slip - target) * Aids.AbsGain;
            factor = Mathf.Clamp(factor, Aids.AbsFloor, 1f);
            return desiredBrake * factor;
        }
    }

    /// <summary>
    /// Proportional traction control. Below the target slip the requested torque is passed through;
    /// above it the torque is scaled down by the slip error, so the wheel is held near the peak of
    /// the longitudinal curve instead of spinning. This matters much more under a real combined-slip
    /// model than under the native one: a spinning wheel loses lateral grip in proportion to its
    /// longitudinal slip, which is what makes the car snap into oversteer on power.
    /// </summary>
    internal sealed class ProgressiveTraction : IDriveAid
    {
        public string Name => "Progressive";

        public string Description => "Cuts the drive torque in proportion to how far slip exceeds the target.";

        public float Apply(in AidContext ctx, float desiredTorque)
        {
            if (desiredTorque == 0f) return desiredTorque;

            float target = Aids.TractionTarget;
            float slip = Mathf.Abs(ctx.ForwardSlip);
            if (slip <= target) return desiredTorque;

            float factor = 1f - (slip - target) * Aids.TractionGain;
            return desiredTorque * Mathf.Clamp01(factor);
        }
    }

    /// <summary>
    /// Public entry point for the aid slots. Call these from another plugin's Start(), and the
    /// registered algorithm becomes selectable in the F7 panel (persisted by name in the config).
    /// </summary>
    public static class EcuAids
    {
        public static void Register(IBrakeAid aid) => Aids.RegisterBrakeAid(aid);

        public static void Register(IDriveAid aid) => Aids.RegisterDriveAid(aid);

        public static IReadOnlyList<IBrakeAid> BrakeAids => Aids.BrakeAids;

        public static IReadOnlyList<IDriveAid> DriveAids => Aids.DriveAids;
    }

    /// <summary>
    /// Central registry: which algorithm is plugged into each channel.
    ///
    /// The selection is stored as a name. Two names are reserved and mean "no custom algorithm":
    /// <c>Native</c> leaves the game's own aid alone, <c>Off</c> disables it without replacing it.
    /// Everything else is looked up in the registry.
    /// </summary>
    internal static class Aids
    {
        internal const string NativeName = "Native";
        internal const string OffName = "Off";

        internal static ConfigEntry<string>? AbsConfig;
        internal static ConfigEntry<string>? TractionConfig;

        internal static ConfigEntry<float>? AbsTargetConfig;
        internal static ConfigEntry<float>? AbsGainConfig;
        internal static ConfigEntry<float>? AbsFloorConfig;

        internal static ConfigEntry<float>? TractionTargetConfig;
        internal static ConfigEntry<float>? TractionGainConfig;

        private static readonly List<IBrakeAid> Brake = new List<IBrakeAid>();
        private static readonly List<IDriveAid> Drive = new List<IDriveAid>();

        internal static IReadOnlyList<IBrakeAid> BrakeAids => Brake;
        internal static IReadOnlyList<IDriveAid> DriveAids => Drive;

        /// <summary>Register the built-in algorithms. Called once at plugin start.</summary>
        internal static void Init()
        {
            RegisterBrakeAid(new ProgressiveAbs());
            RegisterDriveAid(new ProgressiveTraction());
        }

        internal static void RegisterBrakeAid(IBrakeAid aid)
        {
            if (aid == null) return;
            foreach (var existing in Brake)
            {
                if (existing.Name != aid.Name) continue;
                Brake[Brake.IndexOf(existing)] = aid;
                return;
            }
            Brake.Add(aid);
        }

        internal static void RegisterDriveAid(IDriveAid aid)
        {
            if (aid == null) return;
            foreach (var existing in Drive)
            {
                if (existing.Name != aid.Name) continue;
                Drive[Drive.IndexOf(existing)] = aid;
                return;
            }
            Drive.Add(aid);
        }

        internal static string BrakeSelection => AbsConfig?.Value ?? NativeName;
        internal static string DriveSelection => TractionConfig?.Value ?? NativeName;

        /// <summary>The algorithm to run, or null for Native / Off.</summary>
        internal static IBrakeAid? BrakeAid => Find(Brake, BrakeSelection);
        internal static IDriveAid? DriveAid => Find(Drive, DriveSelection);

        /// <summary>
        /// True when the game's own aid must be neutralised. Deliberately false for a name that is
        /// not registered (for example because the plugin providing it was removed): falling back to
        /// Native is safe, whereas neutralising with nothing to replace it would leave the car with
        /// no ABS at all.
        /// </summary>
        internal static bool BrakeManaged => BrakeSelection == OffName || BrakeAid != null;

        internal static bool DriveManaged => DriveSelection == OffName || DriveAid != null;

        private static IBrakeAid? Find(List<IBrakeAid> aids, string name)
        {
            if (name == NativeName || name == OffName) return null;
            foreach (var aid in aids)
            {
                if (aid.Name == name) return aid;
            }
            return null;
        }

        private static IDriveAid? Find(List<IDriveAid> aids, string name)
        {
            if (name == NativeName || name == OffName) return null;
            foreach (var aid in aids)
            {
                if (aid.Name == name) return aid;
            }
            return null;
        }

        /// <summary>
        /// Selection names to offer in the panel: the two reserved modes followed by every
        /// registered algorithm. Read live, so a plugin registering later still shows up.
        /// </summary>
        internal static List<string> BrakeNames()
        {
            var names = new List<string> { NativeName, OffName };
            foreach (var aid in Brake) names.Add(aid.Name);
            return names;
        }

        internal static List<string> DriveNames()
        {
            var names = new List<string> { NativeName, OffName };
            foreach (var aid in Drive) names.Add(aid.Name);
            return names;
        }

        internal static float AbsTarget => AbsTargetConfig?.Value ?? 0.12f;
        internal static float AbsGain => AbsGainConfig?.Value ?? 6f;
        internal static float AbsFloor => AbsFloorConfig?.Value ?? 0.10f;

        internal static float TractionTarget => TractionTargetConfig?.Value ?? 0.12f;
        internal static float TractionGain => TractionGainConfig?.Value ?? 3f;

        /// <summary>Re-apply the current channel selection to every wheel currently in the scene.</summary>
        internal static void ReapplyAll()
        {
            foreach (var wheel in Object.FindObjectsByType<MechanicalOutputWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AidApplier.Apply(wheel);

            foreach (var wheel in Object.FindObjectsByType<MechanicalOutputWheelBrake>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AidApplier.Apply(wheel);
        }
    }
}
