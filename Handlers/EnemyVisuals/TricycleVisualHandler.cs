using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for the Tricycle (Bella) enemy.
    /// The rider mesh (AnimTarget) and the trike body (EnemyTricycleVisuals GO)
    /// are separate visual trees, both need to be scaled.
    /// </summary>
    internal class TricycleVisualHandler : IEnemyVisualHandler
    {
        internal sealed class TricycleState
        {
            internal Transform? TrikeMesh;
            internal Vector3 TrikeMeshOriginalScale;
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            var visuals = ep.GetComponentInChildren<EnemyTricycleVisuals>();
            if (visuals == null) return null;

            return new TricycleState
            {
                TrikeMesh = visuals.transform,
                TrikeMeshOriginalScale = visuals.transform.localScale
            };
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (visualState is TricycleState trike && trike.TrikeMesh != null)
                trike.TrikeMesh.localScale = trike.TrikeMeshOriginalScale * ratio;
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale = state.AnimOriginalScale;
                state.AnimTarget.localPosition = state.AnimOriginalLocalPos;
            }

            if (visualState is TricycleState trike && trike.TrikeMesh != null)
                trike.TrikeMesh.localScale = trike.TrikeMeshOriginalScale;
        }
    }
}
