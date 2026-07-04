using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for enemies whose mesh pivot sits above their feet, so scaling drops
    /// the mesh through the floor.
    ///
    /// Shrinking and growing need different compensation. Shrinking shrinks the colliders
    /// too, so physics settles the body lower and we track the rb-to-mesh gap. Growing
    /// scales the mesh around a pivot the game keeps pinned at its vanilla local position,
    /// which pushes the feet down by the pivot-to-feet distance times the growth, so we
    /// lift the mesh by exactly that. The lift reads only the pinned pivot, not the body,
    /// so it holds with or without the grow caps engaged.
    /// </summary>
    internal class SinkerVisualHandler : IEnemyVisualHandler
    {
        // Floating enemies (e.g. the floater) hover instead of standing, so they should not
        // be grounded when grown, just scaled. They still want shrink compensation.
        private readonly bool _groundOnGrow;

        public SinkerVisualHandler(bool groundOnGrow = true) => _groundOnGrow = groundOnGrow;

        internal sealed class SinkerState
        {
            internal float OriginalGap;  // rb.localPos.y - animTarget.localPos.y, for shrinking
            internal float FootOffset;   // pivot-to-floor distance, for growing
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            if (state.AnimTarget == null) return null;
            return new SinkerState
            {
                OriginalGap = state.RbOriginalLocalPos.y - state.AnimOriginalLocalPos.y,
                FootOffset = VisualGrounding.MeasureFootOffset(state.AnimTarget),
            };
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;

            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;

            if (visualState is not SinkerState sinker) return;

            if (ratio >= 1f)
            {
                // Growing: lift the mesh to keep its feet on the floor (skip for floaters).
                if (_groundOnGrow)
                    VisualGrounding.Apply(state, sinker.FootOffset, ratio);
            }
            else
            {
                // Shrinking: track the rb-to-mesh gap as physics settles the smaller body.
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
