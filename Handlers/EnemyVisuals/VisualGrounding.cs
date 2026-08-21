using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Keeps a scaled enemy mesh standing on the floor.
    ///
    /// Scaling a mesh around a pivot that sits above its feet moves the feet by
    /// pivotToFeet * (ratio - 1): up when shrinking, down when growing. Cancelling that is a
    /// small vertical nudge, and the only interesting part is what the nudge is measured FROM.
    /// Plenty of enemies have their mesh transform rewritten every frame by something else:
    /// Unity's NavMeshAgent owns the position of whichever transform it sits on, Enemy.Update
    /// walks the whole enemy toward the host's networked position on every remote client, and
    /// EnemyBowtieAnim / EnemyValuableThrowerAnim copy followTarget.position straight onto
    /// their own transform. Nudging from a height captured at spawn pins those meshes at that
    /// spawn height, so they float (or sink) as soon as the enemy reaches a floor at a
    /// different level. GroundingTrack keeps that decision honest frame to frame.
    /// </summary>
    internal sealed class VisualGrounding
    {
        // The body can still be inactive or half-built on the frame the enemy is first scaled,
        // so the measurement retries until a character renderer shows up and then stops asking
        // rather than walking the hierarchy for the rest of the session.
        const int MeasureAttempts = 60;

        float _pivotToFeet;
        bool  _measured;
        int   _attempts;
        GroundingTrack _track;

        /// <summary>
        /// Distance from the mesh pivot down to its lowest rendered point, at full size.
        /// Taken from the live renderers the first time the enemy is actually scaled rather
        /// than at spawn: an enemy that enables part of its body later (an attached head, a
        /// second rig) would otherwise be measured while none of it renders, and get no
        /// grounding at all.
        /// </summary>
        void Measure(Transform animTarget, float ratio)
        {
            if (_measured || ratio <= 0f) return;
            if (++_attempts > MeasureAttempts) { _measured = true; return; }

            // Character mesh only. Shadows, ground decals and particle effects are separate
            // renderers that often sit below the feet and would inflate the distance, which
            // lifts the enemy off the ground instead of planting it.
            Renderer[] renderers = animTarget.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0)
                renderers = animTarget.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return;

            float lowest = float.MaxValue;
            foreach (var r in renderers)
                lowest = Mathf.Min(lowest, r.bounds.min.y);

            // The bounds sit `ratio` times further from the pivot than they do at full size,
            // so dividing it back out gives the same number whatever the mesh happens to be
            // scaled to when this finally runs.
            _pivotToFeet = (animTarget.position.y - lowest) / ratio;
            _measured = true;
        }

        /// <summary>Nudge the mesh so its feet land where they would at full size.</summary>
        internal void Apply(EnemyHandler.State state, float ratio)
        {
            var animTarget = state.AnimTarget;
            if (animTarget == null) return;

            Measure(animTarget, ratio);
            if (_pivotToFeet == 0f) return;

            var pos = animTarget.localPosition;
            pos.y = _track.Next(pos.y, _pivotToFeet, ratio);
            animTarget.localPosition = pos;
        }

        /// <summary>Put the mesh back at the height the game last had it, if our nudge is still there.</summary>
        internal void Restore(Transform? animTarget)
        {
            if (animTarget == null) { Forget(); return; }
            var pos = animTarget.localPosition;
            if (_track.TryRelease(pos.y, out float restY))
            {
                pos.y = restY;
                animTarget.localPosition = pos;
            }
        }

        /// <summary>Drop the tracked height so the next scale reads the pose fresh.</summary>
        internal void Forget() => _track.Forget();
    }
}
