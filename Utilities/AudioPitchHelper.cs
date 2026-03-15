using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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
        // Sound.LoopPitch is internal; access via reflection.
        static readonly FieldInfo _loopPitchField =
            AccessTools.Field(typeof(Sound), "LoopPitch");

        // Per-instance state — populated at shrink, cleared at expand.
        Sound[]?  _pitchedSounds;
        float[]?  _soundOriginalPitch;
        float[]?  _soundOriginalLoopPitch;

        /// <summary>
        /// Collect every Sound instance referenced by any MonoBehaviour under root.
        /// Sound is a plain serializable class (not a Component), so GetComponentsInChildren
        /// won't find it — we walk fields via reflection instead.
        /// </summary>
        static Sound[] GatherSounds(Component root)
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
        internal void ApplyPitch(Component searchRoot)
        {
            float mult = 1f + (1f - ShrinkConfig.Factor) * 0.5f;
            _pitchedSounds          = GatherSounds(searchRoot);
            _soundOriginalPitch     = _pitchedSounds.Select(s => s.Pitch).ToArray();
            _soundOriginalLoopPitch = _pitchedSounds
                .Select(s => (float)_loopPitchField.GetValue(s)).ToArray();

            for (int i = 0; i < _pitchedSounds.Length; i++)
            {
                var s = _pitchedSounds[i];
                s.Pitch = _soundOriginalPitch[i] * mult;

                // LoopPitch is captured once when a loop starts; update it so that
                // PlayLoop's "Source.pitch = LoopPitch * multiplier" stays pitched.
                float lp = _soundOriginalLoopPitch[i];
                _loopPitchField.SetValue(s, lp * mult);

                // Immediately apply to any currently-playing loop source so it doesn't
                // wait until the next loop toggle to take effect.
                if (s.Source != null && s.Source.isPlaying)
                    s.Source.pitch *= mult;
            }

            Plugin.Log.LogInfo($"[SC]   sound pitch x{mult:F2} on {_pitchedSounds.Length} Sound objects under {searchRoot.gameObject.name}");
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
                _loopPitchField.SetValue(s, _soundOriginalLoopPitch![i]);
                if (s.Source != null && s.Source.isPlaying)
                    s.Source.pitch = _soundOriginalLoopPitch![i];
            }
            _pitchedSounds          = null;
            _soundOriginalPitch     = null;
            _soundOriginalLoopPitch = null;
        }
    }
}
