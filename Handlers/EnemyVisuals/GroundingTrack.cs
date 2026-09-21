namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Decides which height a scaled enemy mesh gets nudged from.
    ///
    /// The nudge itself is trivial (pivotToFeet * (ratio - 1)); getting it wrong is entirely
    /// about the baseline. A mesh nobody else touches keeps the height it had when the enemy
    /// was first scaled, and rewriting the same value every frame has to be a no-op. A mesh
    /// the game repositions per frame has a new resting height every frame, and using a
    /// stored one pins it where it was when it spawned. Both cases fall out of one rule: if
    /// the height is no longer the value we left there, somebody else wrote it, and theirs
    /// is the one to work from.
    ///
    /// Deliberately free of Unity types so it can be checked without a scene.
    /// </summary>
    internal struct GroundingTrack
    {
        float _restY;
        float _lastWrittenY;
        bool  _wrote;

        /// <summary>
        /// Distance from the mesh pivot down to the ground, at full size, from what the pivot
        /// and the ground read at a known ratio.
        ///
        /// The Chef, the Mentalist, Trudge and Elsa each carry a transform at their soles and
        /// steer off it: the agent is warped to it, the navmesh is sampled at it, and the Chef
        /// writes its own position from it. Take the ground from that transform when the enemy
        /// has one. The lowest rendered point is whatever hangs furthest down on the model,
        /// and grounding to it leaves the soles off the floor by the difference times the
        /// growth, which the enemy then navigates from.
        /// </summary>
        internal static float PivotToFeet(float pivotY, float lowestRenderedY, float markerY, bool hasMarker, float ratio)
        {
            if (ratio <= 0f) return 0f;
            return (pivotY - (hasMarker ? markerY : lowestRenderedY)) / ratio;
        }

        /// <summary>The height at which the mesh should sit this frame.</summary>
        internal float Next(float currentY, float pivotToFeet, float ratio)
        {
            // Transform writes round-trip exactly, so an exact comparison is the question
            // being asked: is this still our value, or did something else move the mesh?
            if (!_wrote || currentY != _lastWrittenY)
                _restY = currentY;

            _lastWrittenY = _restY + pivotToFeet * (ratio - 1f);
            _wrote = true;
            return _lastWrittenY;
        }

        /// <summary>
        /// The height to hand back on restore. False when our nudge is already gone, in which
        /// case whatever is there now belongs to the game and must be left alone.
        /// </summary>
        internal bool TryRelease(float currentY, out float restY)
        {
            restY = _restY;
            bool ours = _wrote && currentY == _lastWrittenY;
            _wrote = false;
            return ours;
        }

        /// <summary>Drop the tracked height so the next scale reads the pose fresh.</summary>
        internal void Forget() => _wrote = false;
    }
}
