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
            internal bool DiagLogged;
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

            if (visualState is not State s) return;
            VisualGrounding.Apply(state, s.FootOffset, ratio);

            // One-shot grounding snapshot on the first scaled frame: live pivot/body/floor
            // geometry for enemies that still float or sink after the offset correction
            // (the Robe). Says whether the mesh pivot really stays pinned while the body
            // settles, and what surface a floor ray under the pivot actually hits.
            if (!s.DiagLogged && ratio < 1f && state.AnimTarget != null)
            {
                s.DiagLogged = true;
                bool hit = Physics.Raycast(state.AnimTarget.position + Vector3.up * 0.1f, Vector3.down,
                    out RaycastHit floor, 10f, LayerMask.GetMask("Default", "NavmeshOnly", "PlayerOnlyCollision"));
                Plugin.Log.LogDebug($"[SC] GROUND-DIAG {ctrl._displayName}" +
                    $"  ratio={ratio:F2}  footOffset={s.FootOffset:F3}" +
                    $"  pivotY={state.AnimTarget.position.y:F3}  rbY={ctrl._t.position.y:F3}" +
                    $"  animLocalY={state.AnimTarget.localPosition.y:F3} (orig {state.AnimOriginalLocalPos.y:F3})" +
                    $"  floor={(hit ? $"{floor.point.y:F3} on '{floor.collider.name}'" : "no hit")}");
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
