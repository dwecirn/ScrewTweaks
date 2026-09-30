#nullable enable

using System;
using System.Collections.Generic;

namespace ScrewTweaks.Views
{
    /// <summary>
    /// The pose slots. The bundled poses are only the ones that happen to ship; anything registered
    /// here is selectable by name from the config.
    /// </summary>
    public static class ViewPoses
    {
        private static readonly List<IViewPose> Registered = new List<IViewPose>();

        public static void Register(IViewPose pose)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));

            Registered.RemoveAll(p => string.Equals(p.Name, pose.Name, StringComparison.Ordinal));
            Registered.Add(pose);
        }

        public static IReadOnlyList<IViewPose> All => Registered;

        /// <summary>The pose the config asked for, or null if nothing registered that name.</summary>
        public static IViewPose? Find(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < Registered.Count; i++)
            {
                if (string.Equals(Registered[i].Name, name, StringComparison.Ordinal))
                    return Registered[i];
            }

            return null;
        }

        /// <summary>What to use when the configured name is not a pose anyone registered.</summary>
        public static IViewPose Fallback => Registered.Count > 0 ? Registered[0] : RigidChase.Shared;
    }
}
