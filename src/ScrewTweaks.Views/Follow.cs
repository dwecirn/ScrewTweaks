#nullable enable

using SappUnityUtils.Scenes.Findables;
using ScriptableObjectsVariables;
using UnityEngine;

namespace ScrewTweaks.Views
{
    /// <summary>
    /// Reads the game's camera mode every frame and, while the patched mode is the one in use, keeps
    /// the camera bolted to the car. All of it happens in LateUpdate, so the game's own writer is out
    /// of the way before the pose is written and the pose is the last word on the transform.
    /// </summary>
    internal static class Follow
    {
        // Only used when a frame asks for the takeover and there was no camera to take.
        private const float RetryInterval = 0.5f;

        private static float _nextAttempt;

        /// <summary>True while this module is the one placing the camera.</summary>
        internal static bool Following { get; private set; }

        /// <summary>The mode the game is on right now, for the panel to show.</summary>
        internal static int GameMode { get; private set; }

        internal static void Tick(CameraTakeover takeover)
        {
            GameMode = SOV.CURRENT_DRIVING_CAMERA_MODE.SOV().GetValue(0);

            SimpleCar2? car = ViewSettings.Enabled ? DrivingCar() : null;
            bool wanted = car != null && GameMode == ViewSettings.Mode;

            if (wanted && !takeover.Active && Time.unscaledTime >= _nextAttempt)
            {
                _nextAttempt = Time.unscaledTime + RetryInterval;
                takeover.Engage();
            }
            else if (!wanted && takeover.Active)
            {
                takeover.Release();
            }

            Following = takeover.Active;

            if (takeover.Active) Place(takeover.Target, car);
        }

        private static void Place(Camera? camera, SimpleCar2? car)
        {
            if (camera == null || car == null) return;

            Transform? body = Body(car);
            if (body == null) return;

            float radius = Mathf.Max(0.5f, car.SizeRadius());
            ChaseContext context = new ChaseContext(
                car.CarMidWorldPosition,
                body.rotation,
                radius * ViewSettings.Distance,
                radius * ViewSettings.Height,
                radius * ViewSettings.AimHeight);

            (ViewPoses.Find(ViewSettings.Pose) ?? ViewPoses.Fallback).Place(camera.transform, context);
        }

        private static SimpleCar2? DrivingCar()
        {
            CurrentlyTargetedSimpleCar? target = FindableProvider.Find<CurrentlyTargetedSimpleCar>();
            if (target == null) return null;

            SimpleCar2? car = target.SimpleCar;
            return car != null && car.IsInstantiated ? car : null;
        }

        private static Transform? Body(SimpleCar2 car)
        {
            Rigidbody? body = car.MainParent.rigidbody;
            if (body != null) return body.transform;

            GameObject? part = car.MainParent.instPart;
            return part != null ? part.transform : null;
        }
    }
}
