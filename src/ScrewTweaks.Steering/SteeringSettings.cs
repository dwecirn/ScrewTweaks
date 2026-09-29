#nullable enable

using SappUnityUtils.IO.SimpleSaveables;
using UnityEngine;

namespace ScrewTweaks.Steering
{
    /// <summary>
    /// The two steering settings, both stored in the game's own settings save rather than in the mod's
    /// config, because both of them are things the *game's* controls page edits: they show up there next
    /// to each other, and the panel shows them too. One storage, two editors, so they cannot drift apart.
    ///
    /// (They used to be a per-suspension-part property for Instant Steering and a Saveables value for the
    /// limit; the per-part one is gone so the two sit together and apply to the whole game.)
    /// </summary>
    internal static class SteeringSettings
    {
        /// <summary>How much of the game's speed-sensitive steering limit to blend away, 0..1.</summary>
        internal const string LimitRelaxKey = "screwtweaks_steerlimitrelax";

        /// <summary>Whether binary steering input is applied instantly instead of being ramped.</summary>
        internal const string InstantKey = "screwtweaks_instantsteering";

        private static float _cachedInstant = -1f;

        internal static float LimitRelax
        {
            get
            {
                try { return Mathf.Clamp01(Saveables.GetValueFloat(LimitRelaxKey, 0f)); }
                catch { return 0f; }
            }
            set
            {
                try { Saveables.SetValue(LimitRelaxKey, Mathf.Clamp01(value)); }
                catch { /* ignore */ }
            }
        }

        internal static bool InstantSteering
        {
            get
            {
                try { return Saveables.GetValueFloat(InstantKey, 0f) > 0.5f; }
                catch { return false; }
            }
            set
            {
                try { Saveables.SetValue(InstantKey, value ? 1f : 0f); }
                catch { /* ignore */ }
            }
        }

        /// <summary>
        /// True when Instant Steering changed since the last check, whoever changed it - the panel, the
        /// game's controls page, or a hand-edited save. The caller reapplies it to the running cars.
        /// </summary>
        internal static bool ConsumeInstantChange()
        {
            float now = InstantSteering ? 1f : 0f;
            if (Mathf.Approximately(now, _cachedInstant)) return false;

            bool first = _cachedInstant < 0f;
            _cachedInstant = now;
            return !first;
        }
    }
}
