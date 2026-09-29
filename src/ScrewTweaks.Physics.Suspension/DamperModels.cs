#nullable enable

using System.Collections.Generic;
using BepInEx.Configuration;
using NWH.WheelController3D;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// One suspension, one physics step, read-only. A model computes a force and nothing else; the
    /// host owns the wheel, the sign convention and the ground geometry.
    /// </summary>
    public readonly struct DamperState
    {
        internal DamperState(WheelController wheel, bool compressing, float velocity, float gameCoefficient,
            DamperSetup setup)
        {
            Wheel = wheel;
            Compressing = compressing;
            Velocity = velocity;
            GameCoefficient = gameCoefficient;
            Setup = setup;
        }

        /// <summary>The suspension being evaluated. Escape hatch for anything not listed here.</summary>
        public WheelController Wheel { get; }

        /// <summary>True while the suspension is compressed further (bump), false while it extends (rebound).</summary>
        public bool Compressing { get; }

        /// <summary>Absolute suspension velocity in [m/s], never negative.</summary>
        public float Velocity { get; }

        /// <summary>
        /// The damping coefficient the game computed for this wheel, in [N*s/m]. It comes from the
        /// car's mass, the number of wheels and the suspension part's `damperforce` property:
        ///     mass * 1.3333 * (4 / wheels) * (damperforce / 100)
        /// The game writes the same number to the bump and the rebound coefficient. Using it as the
        /// base keeps a model scaled the way the rest of the game is.
        /// </summary>
        public float GameCoefficient { get; }

        /// <summary>Spring travel of this suspension in [m] (`spring.maxLength`).</summary>
        public float Travel => Wheel.spring.maxLength;

        /// <summary>How far into its travel the suspension is: 0 = fully extended, 1 = fully compressed.</summary>
        public float CompressionPercent => Wheel.spring.compressionPercent;

        /// <summary>Force the spring is exerting right now, in [N]. Always positive.</summary>
        public float SpringForce => Wheel.spring.force;

        /// <summary>What the game's own damper would produce at this velocity: `GameCoefficient * Velocity`.</summary>
        public float GameForce => GameCoefficient * Velocity;

        /// <summary>
        /// This wheel's four-way setup, read from the suspension part it is fitted to - so it travels with
        /// the car rather than with the mod. The game's own `damperforce` is still the base coefficient the
        /// four are multiples of.
        /// </summary>
        public DamperSetup Setup { get; }

        /// <summary>
        /// Velocity [m/s] at which each half's shim stack opens. A shaft property, so it is the same for
        /// every car and is not stored per part.
        /// </summary>
        public float KneeVelocity => DamperTuning.KneeVelocity;
    }

    /// <summary>
    /// A pluggable damper force law, the suspension counterpart of ITireModel.
    ///
    /// The host calls <see cref="Evaluate"/> once per grounded wheel per physics step and replaces the
    /// game's damper term with the result *before* it is applied to the rigidbody - so the tyre load,
    /// the chassis force and the bump sound all see the same number. The game's own law is
    /// `coefficient * |velocity|`, with one coefficient shared by bump and rebound.
    ///
    /// Contract: return the magnitude of the resistive force in [N], never negative. The direction is
    /// not the model's business - the host applies the sign (bump pushes the body up, rebound pulls it
    /// down) and handles the contact normal.
    /// </summary>
    public interface IDamperModel
    {
        string Name { get; }

        string Description { get; }

        float Evaluate(in DamperState state);
    }

    /// <summary>Reserved: never touches <c>WheelController.SuspensionUpdate</c>.</summary>
    internal sealed class NativeDamperModel : IDamperModel
    {
        public string Name => "Native";

        public string Description =>
            "The game's own damper, untouched: force = coefficient * |velocity|, with the same coefficient " +
            "in bump and rebound, and no blow-off.";

        public float Evaluate(in DamperState state)
        {
            return state.GameForce;
        }
    }

    /// <summary>Registry of selectable damper models. Other plugins may add models at Start().</summary>
    public static class DamperModels
    {
        private static readonly List<IDamperModel> Registered = new List<IDamperModel>();

        /// <summary>Persists the selection, so the model does not have to be picked every session.</summary>
        internal static ConfigEntry<string>? SelectedConfig;

        internal static void Init()
        {
            Register(new NativeDamperModel());
            Register(new FourWayDamper());

            // An unknown name (hand-edited config, or a model whose plugin is missing) falls back to
            // Native: a damper that is silently wrong is worse than the game's own.
            Current = Find(SelectedConfig?.Value) ?? Registered[0];
        }

        private static IDamperModel? Find(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var model in Registered)
            {
                if (model.Name == name) return model;
            }
            return null;
        }

        public static void Register(IDamperModel model)
        {
            if (model == null) return;
            foreach (var existing in Registered)
            {
                if (existing.Name != model.Name) continue;
                if (Current == existing) Current = model;
                Registered[Registered.IndexOf(existing)] = model;
                return;
            }
            Registered.Add(model);
        }

        public static IReadOnlyList<IDamperModel> All => Registered;

        public static IDamperModel? Current { get; private set; }

        /// <summary>True when the selected model leaves the game's own damper alone.</summary>
        public static bool IsNative => Current is NativeDamperModel or null;

        public static void Select(IDamperModel model)
        {
            Current = model;
            if (SelectedConfig != null && SelectedConfig.Value != model.Name)
                SelectedConfig.Value = model.Name;
        }
    }
}
