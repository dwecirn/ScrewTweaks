#nullable enable

using System.Collections.Generic;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// A pluggable tyre force model. The host fills in the contact speeds
    /// (<c>forwardFriction.speed</c> / <c>sideFriction.speed</c>) before calling <see cref="Apply"/>,
    /// and afterwards publishes RPM / wheel-hit slip to the drivetrain.
    ///
    /// When <see cref="Apply"/> returns true the model has written <c>forwardFriction.force</c>,
    /// <c>sideFriction.force</c>, both <c>slip</c> values and integrated <c>wheel.angularVelocity</c>;
    /// the game's built-in <c>WheelController.FrictionUpdate</c> is then skipped for this step.
    /// </summary>
    public interface ITireModel
    {
        string Name { get; }
        string Description { get; }
        bool Apply(WheelController wheel, float dt);
    }

    /// <summary>The game's original NWH friction — never overrides anything.</summary>
    internal sealed class NativeTireModel : ITireModel
    {
        public string Name => "Native";
        public string Description => "Game's built-in NWH WheelController3D friction (unchanged).";
        public bool Apply(WheelController wheel, float dt) => false;
    }

    /// <summary>Debug model: zero tyre force. Used to prove the slot plumbing.</summary>
    internal sealed class ZeroGripTireModel : ITireModel
    {
        public string Name => "No Grip (debug)";
        public string Description => "Zero tyre force. The car will not accelerate or steer.";
        public bool Apply(WheelController wheel, float dt)
        {
            wheel.forwardFriction.force = 0f;
            wheel.sideFriction.force = 0f;
            wheel.forwardFriction.slip = 0f;
            wheel.sideFriction.slip = 0f;
            return true;
        }
    }

    /// <summary>Registry of selectable tyre models. Other plugins may add models at Start().</summary>
    public static class TireModels
    {
        private static readonly List<ITireModel> Registered = new List<ITireModel>();

        internal static void Init()
        {
            Register(new NativeTireModel());
            Register(new PacejkaTireModel());
            Register(new ZeroGripTireModel());
            Current = Registered[0];
        }

        public static void Register(ITireModel model)
        {
            if (model == null) return;
            foreach (var existing in Registered)
            {
                if (existing.Name == model.Name)
                {
                    if (Current == existing) Current = model;
                    Registered[Registered.IndexOf(existing)] = model;
                    return;
                }
            }
            Registered.Add(model);
        }

        public static IReadOnlyList<ITireModel> All => Registered;

        public static ITireModel? Current { get; private set; }

        public static void Select(ITireModel model) => Current = model;
    }
}
