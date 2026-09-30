#nullable enable

using UnityEngine;

namespace ScrewTweaks.Views
{
    /// <summary>
    /// Everything a pose is allowed to look at, expressed in the car's own frame. A pose that uses
    /// only these numbers is rigid by construction: it cannot lag, because it remembers nothing.
    /// </summary>
    public readonly struct ChaseContext
    {
        public ChaseContext(
            Vector3 origin, Quaternion rotation, float distance, float height, float aimHeight)
        {
            Origin = origin;
            Rotation = rotation;
            Distance = distance;
            Height = height;
            AimHeight = aimHeight;
        }

        /// <summary>Mid point of the car body, world space.</summary>
        public Vector3 Origin { get; }

        /// <summary>The car body's rotation, roll included.</summary>
        public Quaternion Rotation { get; }

        /// <summary>Metres behind the origin, along the car's own backward axis.</summary>
        public float Distance { get; }

        /// <summary>Metres above the origin, along the car's own up axis.</summary>
        public float Height { get; }

        /// <summary>Metres above the origin that the camera aims at.</summary>
        public float AimHeight { get; }
    }

    /// <summary>
    /// Where the camera goes. Implementations register themselves in <see cref="ViewPoses"/> and are
    /// selected by name; that name is a value in the config, so it is never translated.
    /// </summary>
    public interface IViewPose
    {
        string Name { get; }

        void Place(Transform camera, ChaseContext context);
    }
}
