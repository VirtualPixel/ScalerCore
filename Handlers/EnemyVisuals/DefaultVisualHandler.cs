using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Default visual scaling: AnimTarget.localScale + BtHead.localScale, plus grounding so
    /// the mesh stays on the floor when the collider is capped smaller. Used for all enemies
    /// without a specific override.
    /// </summary>
    internal class DefaultVisualHandler : IEnemyVisualHandler
    {
        private sealed class State
        {
            internal float FootOffset;
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            return new State { FootOffset = VisualGrounding.MeasureFootOffset(state.AnimTarget) };
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;

            if (visualState is State s)
                VisualGrounding.Apply(state, s.FootOffset, ratio);
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
