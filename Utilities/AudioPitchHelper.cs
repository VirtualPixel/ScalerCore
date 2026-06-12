using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ScalerCore.Utilities
{
    /// <summary>
    /// Manages sound pitch state for a single ScaleController instance.
    /// Gathers every Sound field under a hierarchy via reflection, applies a pitch
    /// multiplier at shrink time, and restores originals at expand time.
    /// </summary>
    internal class AudioPitchHelper
    {

        // Per-instance state, populated at shrink, cleared at expand.
        Sound[]?  _pitchedSounds;
        float[]?  _soundOriginalPitch;
        float[]?  _soundOriginalLoopPitch;

        /// <summary>
        /// Collect every Sound instance referenced by any MonoBehaviour under root.
        /// Sound is a plain serializable class (not a Component), so GetComponentsInChildren
        /// won't find it, we walk fields via reflection instead.
        /// </summary>
        internal static Sound[] GatherSounds(Component root)
        {
            var found = new List<Sound>();
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (mb == null) continue;
                foreach (var f in mb.GetType()
                                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                               | BindingFlags.Instance)
                                    .Where(f => f.FieldType == typeof(Sound)))
                {
                    if (f.GetValue(mb) is Sound s && !found.Contains(s))
                        found.Add(s);
                }
            }
            return found.ToArray();
        }

        /// <summary>
        /// Apply pitch multiplier to all Sound objects under searchRoot.
        /// Called once at shrink time. Multiplier: 1 + (1 - factor) * 0.5.
        /// </summary>
        // Pitch a transient effect object (a spawned explosion) once. No capture,
        // no restore: the instance and its sounds die with the effect.
        internal static void PitchOneShot(Component root, float factor)
        {
            float mult = Mathf.Clamp(1f + (1f - factor) * 0.5f, 0.35f, 2f);
            foreach (var s in GatherSounds(root))
            {
                s.Pitch *= mult;
                s.LoopPitch *= mult;
            }
        }

        internal void ApplyPitch(Component searchRoot, float factor)
        {
            // Linear around 1: chipmunk when small, deep when big. Clamped so
            // extreme growth factors bottom out at a usable rumble instead of
            // running the formula negative (factor 3+ would invert the audio).
            float mult = Mathf.Clamp(1f + (1f - factor) * 0.5f, 0.35f, 2f);
            _pitchedSounds          = GatherSounds(searchRoot);
            _soundOriginalPitch     = _pitchedSounds.Select(s => s.Pitch).ToArray();
            _soundOriginalLoopPitch = _pitchedSounds
                .Select(s => s.LoopPitch).ToArray();

            for (int i = 0; i < _pitchedSounds.Length; i++)
            {
                var s = _pitchedSounds[i];
                s.Pitch = _soundOriginalPitch[i] * mult;

                // LoopPitch is captured once when a loop starts; update it so that
                // PlayLoop's "Source.pitch = LoopPitch * multiplier" stays pitched.
                float lp = _soundOriginalLoopPitch[i];
                s.LoopPitch = lp * mult;

                // Immediately apply to any currently-playing loop source so it doesn't
                // wait until the next loop toggle to take effect.
                if (s.Source != null && s.Source.isPlaying)
                    s.Source.pitch *= mult;
            }

            Plugin.Log.LogDebug($"[SC]   sound pitch x{mult:F2} on {_pitchedSounds.Length} Sound objects under {searchRoot.gameObject.name}");
        }

        /// <summary>
        /// Restore all Sound objects to their original pitch values.
        /// Called at expand time and during cleanup.
        /// </summary>
        internal void RestorePitch()
        {
            if (_pitchedSounds == null) return;
            for (int i = 0; i < _pitchedSounds.Length; i++)
            {
                var s = _pitchedSounds[i];
                if (s == null) continue;
                s.Pitch = _soundOriginalPitch![i];
                s.LoopPitch = _soundOriginalLoopPitch![i];
                if (s.Source != null && s.Source.isPlaying)
                    s.Source.pitch = _soundOriginalLoopPitch![i];
            }
            _pitchedSounds          = null;
            _soundOriginalPitch     = null;
            _soundOriginalLoopPitch = null;
        }
    }
}
