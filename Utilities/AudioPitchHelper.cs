using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ScalerCore.Utilities
{
    /// <summary>
    /// Manages sound state for a single ScaleController instance.
    /// Gathers every Sound field under a hierarchy via reflection, applies the
    /// size treatment (pitch both directions; volume/reverb/falloff presence for
    /// grown entities) and restores originals at expand time.
    /// </summary>
    internal class AudioPitchHelper
    {
        // Big things are loud and they fill the room. Volume pushes up a touch
        // (AudioSource clamps at 1, so this mostly lifts quieter sounds),
        // ReverbMix leans into whatever reverb zone the room has for the echo,
        // and falloff scales with the factor so a giant carries further while a
        // tiny one gets sneaky-quiet at range. All of it rides one knob,
        // ScaleOptions.AudioPresence: these are the strengths at presence 1.
        // Reverb is a light touch: past ~1.0 ReverbMix amplifies the wet path
        // and everything big turns echoey-but-muffled, the diffuse tail buries
        // the dry transient that actually reads as "big"
        const float GrowVolumeBoost = 0.25f;
        const float GrowReverbBoost = 0.15f;

        static float VolumeMult(float presence) => 1f + GrowVolumeBoost * Mathf.Clamp01(presence);
        static float ReverbMult(float presence) => 1f + GrowReverbBoost * Mathf.Clamp01(presence);
        static float FalloffMult(float factor, float presence) => Mathf.Lerp(1f, factor, Mathf.Clamp01(presence));

        // Per-instance state, populated at shrink, cleared at expand.
        Sound[]?  _pitchedSounds;
        float[]?  _soundOriginalPitch;
        float[]?  _soundOriginalLoopPitch;
        float[]?  _soundOriginalVolume;
        float[]?  _soundOriginalLoopVolume;
        float[]?  _soundOriginalReverb;
        float[]?  _soundOriginalFalloff;

        /// <summary>
        /// Collect every Sound instance referenced by any MonoBehaviour under root.
        /// Sound is a plain serializable class (not a Component), so GetComponentsInChildren
        /// won't find it, we walk fields via reflection instead.
        /// </summary>
        // The full field walk per component is the expensive part and the result
        // only depends on the TYPE, so it's cached: after the first encounter of
        // each component type the scan is a dictionary hit and (usually) an
        // empty-array skip. Keeps the apply-frame cost flat instead of paying
        // reflection across the whole hierarchy on every scale.
        static readonly Dictionary<System.Type, FieldInfo[]> _soundFieldCache = new();

        internal static Sound[] GatherSounds(Component root)
        {
            var seen = new HashSet<Sound>();
            var found = new List<Sound>();
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true))
            {
                if (mb == null) continue;
                var type = mb.GetType();
                if (!_soundFieldCache.TryGetValue(type, out var fields))
                {
                    fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                            | BindingFlags.Instance)
                                 .Where(f => f.FieldType == typeof(Sound))
                                 .ToArray();
                    _soundFieldCache[type] = fields;
                }
                foreach (var f in fields)
                {
                    if (f.GetValue(mb) is Sound s && seen.Add(s))
                        found.Add(s);
                }
            }
            return found.ToArray();
        }

        static float PitchMult(float factor) =>
            // Chipmunk when small, deep when big. The grow side is shallower
            // and floors at 0.5: deep reads as "big" well before half pitch,
            // and below that the highs are gone and everything turns to mud
            factor >= 1f
                ? Mathf.Max(0.5f, 1f - (factor - 1f) * 0.35f)
                : Mathf.Clamp(1f + (1f - factor) * 0.5f, 1f, 2f);

        // Pitch and presence for a transient effect object (a spawned explosion).
        // No capture, no restore: the instance and its sounds die with the effect.
        internal static void PitchOneShot(Component root, float factor, float presence)
        {
            float mult = PitchMult(factor);
            bool grown = factor > 1f;
            foreach (var s in GatherSounds(root))
            {
                s.Pitch *= mult;
                s.LoopPitch *= mult;
                s.FalloffMultiplier *= FalloffMult(factor, presence);
                if (grown)
                {
                    s.Volume = Mathf.Min(1f, s.Volume * VolumeMult(presence));
                    s.ReverbMix = Mathf.Min(1f, s.ReverbMix * ReverbMult(presence));
                }
            }
        }

        internal void ApplyPitch(Component searchRoot, float factor, float presence)
        {
            // Rescale safety: a second apply while a capture is live (grow then
            // re-shot as shrink) would record the already-treated values as the
            // originals, and the old treatment would survive every restore
            if (_pitchedSounds != null) RestorePitch();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            float mult = PitchMult(factor);
            bool grown = factor > 1f;
            _pitchedSounds           = GatherSounds(searchRoot);
            _soundOriginalPitch      = _pitchedSounds.Select(s => s.Pitch).ToArray();
            _soundOriginalLoopPitch  = _pitchedSounds.Select(s => s.LoopPitch).ToArray();
            _soundOriginalVolume     = _pitchedSounds.Select(s => s.Volume).ToArray();
            _soundOriginalLoopVolume = _pitchedSounds.Select(s => s.LoopVolume).ToArray();
            _soundOriginalReverb     = _pitchedSounds.Select(s => s.ReverbMix).ToArray();
            _soundOriginalFalloff    = _pitchedSounds.Select(s => s.FalloffMultiplier).ToArray();

            for (int i = 0; i < _pitchedSounds.Length; i++)
            {
                var s = _pitchedSounds[i];
                s.Pitch = _soundOriginalPitch[i] * mult;

                // LoopPitch is captured once when a loop starts; update it so that
                // PlayLoop's "Source.pitch = LoopPitch * multiplier" stays pitched.
                float lp = _soundOriginalLoopPitch[i];
                s.LoopPitch = lp * mult;

                // A giant carries further, a tiny one fades sooner.
                s.FalloffMultiplier = _soundOriginalFalloff[i] * FalloffMult(factor, presence);

                if (grown)
                {
                    s.Volume = Mathf.Min(1f, _soundOriginalVolume[i] * VolumeMult(presence));
                    s.LoopVolume = Mathf.Min(1f, _soundOriginalLoopVolume[i] * VolumeMult(presence));
                    s.ReverbMix = Mathf.Min(1f, _soundOriginalReverb[i] * ReverbMult(presence));
                }

                // Immediately apply to any currently-playing loop source so it doesn't
                // wait until the next loop toggle to take effect.
                if (s.Source != null && s.Source.isPlaying)
                    s.Source.pitch *= mult;
            }

            Plugin.Log.LogDebug($"[SC]   sound treatment x{mult:F2} pitch{(grown ? ", grown presence" : "")} on {_pitchedSounds.Length} Sound objects under {searchRoot.gameObject.name}");
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 5)
                Plugin.Log.LogWarning($"[SC] slow sound treatment: {sw.ElapsedMilliseconds}ms for {_pitchedSounds.Length} sounds under {searchRoot.gameObject.name}");
        }

        /// <summary>
        /// Restore all Sound objects to their original values.
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
                s.Volume = _soundOriginalVolume![i];
                s.LoopVolume = _soundOriginalLoopVolume![i];
                s.ReverbMix = _soundOriginalReverb![i];
                s.FalloffMultiplier = _soundOriginalFalloff![i];
                if (s.Source != null && s.Source.isPlaying)
                    s.Source.pitch = _soundOriginalLoopPitch![i];
            }
            _pitchedSounds           = null;
            _soundOriginalPitch      = null;
            _soundOriginalLoopPitch  = null;
            _soundOriginalVolume     = null;
            _soundOriginalLoopVolume = null;
            _soundOriginalReverb     = null;
            _soundOriginalFalloff    = null;
        }
    }
}
