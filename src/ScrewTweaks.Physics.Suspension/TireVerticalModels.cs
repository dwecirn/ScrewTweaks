#nullable enable

using System.Collections.Generic;
using BepInEx.Configuration;
using NWH.WheelController3D;

namespace ScrewTweaks.Physics.Suspension
{
    /// <summary>
    /// One wheel's tyre, at one substep, read-only.
    ///
    /// The wheel hangs from the chassis on the suspension and is pressed against the ground by
    /// whatever <see cref="ITireVerticalModel"/> returns. The game has no vertical degree of freedom
    /// - it snaps the wheel to the ground every step - so with a non-Native model this is the only
    /// thing standing between the road and the chassis.
    /// </summary>
    public readonly struct TireVerticalState
    {
        internal TireVerticalState(WheelController wheel, float deflection, float deflectionRate, float unsprungMass)
        {
            Wheel = wheel;
            Deflection = deflection;
            DeflectionRate = deflectionRate;
            UnsprungMass = unsprungMass;
        }

        /// <summary>The wheel being evaluated. Escape hatch for anything not listed here.</summary>
        public WheelController Wheel { get; }

        /// <summary>
        /// How far the tyre is pressed into the ground, in [m]. Positive while it is compressed,
        /// negative while it is off the ground - in which case the force is zero, because a tyre
        /// cannot pull.
        /// </summary>
        public float Deflection { get; }

        /// <summary>
        /// Rate of change of the deflection in [m/s], positive while the tyre is being pressed in
        /// further. This is the *relative* rate between the wheel and the ground, so a chassis moving
        /// down on a stationary wheel counts.
        /// </summary>
        public float DeflectionRate { get; }

        /// <summary>The unsprung mass this wheel is being simulated with, in [kg]. Comes from the
        /// wheel part's own mass, which the game otherwise only uses for rotational inertia.</summary>
        public float UnsprungMass { get; }

        /// <summary>The tyre radius in [m].</summary>
        public float Radius => Wheel.wheel.radius;

        /// <summary>The stiffness the host derived for this wheel, in [N/m].</summary>
        public float ReferenceStiffness => TireVerticalTuning.Stiffness(Wheel, UnsprungMass);
    }

    /// <summary>
    /// A pluggable tyre vertical force law, the third slot in this module next to IDamperModel.
    ///
    /// Contract: return the upward force the ground pushes the wheel with, in [N], never negative.
    /// Deflection and its rate are already resolved for you; direction and clamping are the host's job.
    /// </summary>
    public interface ITireVerticalModel
    {
        string Name { get; }

        string Description { get; }

        float Evaluate(in TireVerticalState state);
    }

    /// <summary>
    /// Reserved: the game's own wheel, rigid in the vertical and snapped to the ground. Selecting this
    /// leaves <c>WheelController.SuspensionUpdate</c> completely alone.
    /// </summary>
    internal sealed class NativeTireVerticalModel : ITireVerticalModel
    {
        public string Name => "Native";

        public string Description =>
            "The game's own wheel: no vertical freedom, snapped to the ground every step, so the tyre " +
            "cannot absorb anything and the wheel has no mass of its own.";

        public float Evaluate(in TireVerticalState state) => 0f;
    }

    /// <summary>
    /// The tyre that ships: a linear spring and damper against the ground, with the wheel's own mass
    /// as the unsprung mass.
    ///
    /// The stiffness is not a fixed number. It is derived from a target unsprung natural frequency
    /// (see <see cref="TireVerticalTuning"/>), which does two things at once: a heavy car with a big
    /// wheel gets a stiff tyre and a go-kart a soft one, exactly as real tyres scale, and every wheel
    /// ends up with the *same* frequency and therefore the same integration stability margin, so a
    /// 3 kg skateboard wheel cannot blow up while a 60 kg truck wheel stays calm.
    /// </summary>
    internal sealed class LinearTireModel : ITireVerticalModel
    {
        public string Name => "Linear";

        public string Description =>
            "A tyre with vertical compliance: a linear spring and damper against the ground, with the " +
            "wheel's own mass hanging on the suspension above it. The wheel can now follow the road and " +
            "leave it, and sharp loads are filtered instead of being passed straight to the chassis.";

        public float Evaluate(in TireVerticalState state)
        {
            float stiffness = TireVerticalTuning.Stiffness(state.Wheel, state.UnsprungMass);
            float damping = TireVerticalTuning.Damping(state.Wheel, state.UnsprungMass);

            float force = stiffness * state.Deflection + damping * state.DeflectionRate;
            return force > 0f ? force : 0f; // a tyre pushes, it does not pull
        }
    }
 
    /// <summary>Registry of selectable tyre vertical models. Other plugins may add models at Start().</summary>
    public static class TireVerticalModels
    {
        private static readonly List<ITireVerticalModel> Registered = new List<ITireVerticalModel>();

        /// <summary>Persists the selection, so the model does not have to be picked every session.</summary>
        internal static ConfigEntry<string>? SelectedConfig;

        internal static void Init()
        {
            Register(new NativeTireVerticalModel());
            Register(new LinearTireModel());

            Current = Find(SelectedConfig?.Value) ?? Registered[0];
        }

        private static ITireVerticalModel? Find(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var model in Registered)
            {
                if (model.Name == name) return model;
            }
            return null;
        }

        public static void Register(ITireVerticalModel model)
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

        public static IReadOnlyList<ITireVerticalModel> All => Registered;

        public static ITireVerticalModel? Current { get; private set; }

        /// <summary>True when the selected model leaves the game's own vertical solver alone.</summary>
        public static bool IsNative => Current is NativeTireVerticalModel or null;

        public static void Select(ITireVerticalModel model)
        {
            Current = model;
            if (SelectedConfig != null && SelectedConfig.Value != model.Name)
                SelectedConfig.Value = model.Name;
        }
    }
}
