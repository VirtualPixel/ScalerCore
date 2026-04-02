using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Door-specific scaling logic.
    /// Detects whether a door's hinge point is far enough from the GO origin that
    /// scaling would pull the door off its hinge. If so, cleanly breaks the hinge
    /// via the game's own HingeBreakImpulse before shrinking.
    /// Doors with the hinge near the origin (drawers, safes, etc.) scale normally.
    /// </summary>
    internal class DoorHandler : IScaleHandler
    {
        // If the hinge anchor is farther than this from the GO origin,
        // the door can't scale to hinge and we break it off instead.
        const float AnchorDistanceThreshold = 1.0f;

        internal sealed class State
        {
            internal PhysGrabHinge Hinge = null!;
            internal bool ShouldBreakOnShrink;
        }

        public void Setup(ScaleController ctrl)
        {
            var hinge = ctrl.GetComponent<PhysGrabHinge>();
            if (hinge == null) return;

            var state = new State { Hinge = hinge };

            // Check how far the hinge anchor is from the GO's local origin.
            // Doors where the pivot is at the edge (swing doors) have a large offset
            // and will separate from the frame when localScale shrinks toward the origin.
            if (hinge.hingePoint != null)
            {
                float anchorDist = hinge.hingePoint.localPosition.magnitude;
                bool hasWallTags = hinge.wallTagObjects != null && hinge.wallTagObjects.Length > 0;

                // Only break doors that are both far from pivot AND affect wall occlusion.
                // Room doors (single swing doors) have wallTagObjects for sound blocking.
                // Lockers, cabinets, etc. have large anchor offsets but no wallTagObjects.
                state.ShouldBreakOnShrink = anchorDist > AnchorDistanceThreshold && hasWallTags;

                Plugin.Log.LogInfo($"[SC]   Door: hingeAnchorDist={anchorDist:F3}" +
                    $"  wallTags={hinge.wallTagObjects?.Length ?? 0}" +
                    $"  willBreak={state.ShouldBreakOnShrink}" +
                    $"  broken={hinge.broken}  dead={hinge.dead}");
            }

            ctrl.HandlerState = state;
        }

        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (state.ShouldBreakOnShrink && !state.Hinge.broken && !state.Hinge.dead)
            {
                Plugin.Log.LogInfo($"[SC]   Door: breaking hinge on {ctrl._displayName} (anchor too far from origin)");

                // HingeBreakImpulse sets broken=true, plays break sound/FX, changes layers.
                // But it does NOT destroy the HingeJoint — that only happens when Unity's
                // OnJointBreak fires from physics forces. We must destroy it ourselves.
                state.Hinge.HingeBreakImpulse();

                if (state.Hinge.joint != null)
                {
                    Object.Destroy(state.Hinge.joint);
                    state.Hinge.joint = null;
                }
            }
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            // Hinge break is permanent — no restore needed.
        }

        public void OnUpdate(ScaleController ctrl) { }
        public void OnLateUpdate(ScaleController ctrl) { }
        public void OnDestroy(ScaleController ctrl) { }
    }
}
