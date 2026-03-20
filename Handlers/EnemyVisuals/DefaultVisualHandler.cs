using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Default visual scaling: AnimTarget.localScale + BtHead.localScale.
    /// Used for all enemies without a specific override.
    /// </summary>
    internal class DefaultVisualHandler : IEnemyVisualHandler
    {
        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            // Default handler needs no extra state — AnimTarget and BtHead are on base State.
            return null;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;
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
