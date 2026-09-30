#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace ScrewTweaks.Views
{
    /// <summary>
    /// Holds the camera that renders the driving view: finds it, switches off whatever else writes
    /// its transform, and puts those switches back the way it found them when it lets go. The game
    /// disables CameraRigidbodyFollow the same way for its own trailer camera.
    /// </summary>
    internal sealed class CameraTakeover
    {
        private readonly List<Behaviour> _silenced = new List<Behaviour>();

        internal Camera? Target { get; private set; }

        internal bool Active { get; private set; }

        internal void Engage()
        {
            if (Active) return;

            CameraRigidbodyFollow? follow = Object.FindFirstObjectByType<CameraRigidbodyFollow>();

            Camera? camera = follow != null ? follow.GetComponent<Camera>() : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;

            Target = camera;
            Active = true;

            Silence(camera.gameObject);
            if (follow != null) Silence(follow.gameObject);
        }

        internal void Release()
        {
            if (!Active) return;

            for (int i = 0; i < _silenced.Count; i++)
            {
                if (_silenced[i] != null) _silenced[i].enabled = true;
            }

            _silenced.Clear();
            Target = null;
            Active = false;
        }

        /// <summary>
        /// Switches off the components that write this camera's pose. A disabled component does not
        /// get its LateUpdate, so this is what keeps the game's own writer out of the frame.
        /// </summary>
        private void Silence(GameObject host)
        {
            MonoBehaviour[] behaviours = host.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.enabled) continue;
                if (!WritesThePose(behaviour)) continue;

                behaviour.enabled = false;
                _silenced.Add(behaviour);
            }
        }

        private static bool WritesThePose(MonoBehaviour behaviour)
        {
            return behaviour is CameraRigidbodyFollow || behaviour is DrivingCamera;
        }
    }
}
