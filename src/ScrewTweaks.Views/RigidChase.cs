#nullable enable

using UnityEngine;

namespace ScrewTweaks.Views
{
    /// <summary>
    /// Bolted to the car. The camera takes the car's position and the car's rotation every frame with
    /// no interpolation anywhere, and aims at a fixed point in the car's own frame, so the horizon
    /// tilts with the car exactly as it does in the seat view. This is the seat view, moved out to
    /// third person.
    /// </summary>
    public sealed class RigidChase : IViewPose
    {
        /// <summary>The config value that selects this pose. Not translated.</summary>
        public const string Name = "Rigid";

        public static readonly RigidChase Shared = new RigidChase();

        string IViewPose.Name => Name;

        void IViewPose.Place(Transform camera, ChaseContext context)
        {
            Vector3 local = new Vector3(0f, context.Height, -context.Distance);
            Vector3 aim = new Vector3(0f, context.AimHeight, 0f);

            camera.position = context.Origin + context.Rotation * local;

            Vector3 direction = context.Rotation * (aim - local);
            if (direction.sqrMagnitude < 1e-6f)
            {
                camera.rotation = context.Rotation;
                return;
            }

            // The car's own up, so the camera rolls with it instead of holding the horizon level.
            camera.rotation = Quaternion.LookRotation(direction, context.Rotation * Vector3.up);
        }
    }
}
