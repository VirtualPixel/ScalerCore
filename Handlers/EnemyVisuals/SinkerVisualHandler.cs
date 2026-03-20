using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for enemies that sink into the ground when shrunk.
    /// Applies Y compensation to AnimTarget.localPosition based on the gap
    /// between Rigidbody and AnimTarget local Y positions.
    ///
    /// The problem: when the RB's colliders shrink, physics settles it lower.
    /// But the game maintains AnimTarget.localPosition.y at its original value,
    /// so the mesh drops below floor level.
    ///
    /// The fix: adjust AnimTarget.localPosition.y so the gap between RB and
    /// AnimTarget scales proportionally with the shrink ratio.
    /// </summary>
    internal class SinkerVisualHandler : IEnemyVisualHandler
    {
        internal sealed class SinkerState
        {
            internal float OriginalGap; // rb.localPos.y - animTarget.localPos.y at full scale
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            if (state.AnimTarget == null) return null;
            return new SinkerState
            {
                OriginalGap = state.RbOriginalLocalPos.y - state.AnimOriginalLocalPos.y
            };
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;

            // Scale the mesh
            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            // BtHead (shouldn't be present on sinkers, but safe to include)
            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;

            // Y compensation
            if (visualState is SinkerState sinker)
            {
                float expectedGap = sinker.OriginalGap * ratio;
                float actualGap = ctrl._t.localPosition.y - state.AnimTarget.localPosition.y;
                float correction = expectedGap - actualGap;

                if (Mathf.Abs(correction) > 0.001f)
                {
                    var pos = state.AnimTarget.localPosition;
                    pos.y -= correction;
                    state.AnimTarget.localPosition = pos;
                }
            }
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale = state.AnimOriginalScale;
                state.AnimTarget.localPosition = state.AnimOriginalLocalPos;
            }
            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale;
        }
    }
}
