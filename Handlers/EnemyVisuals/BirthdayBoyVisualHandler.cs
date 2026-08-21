using System.Collections.Generic;
using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for Birthday Boy.
    /// Scales the enemy mesh (AnimTarget) and all placed balloons in sync.
    /// Balloons are separate world-space GOs tracked via EnemyBirthdayBoy.balloons;
    /// they don't inherit the enemy's transform, so we scale them individually.
    /// </summary>
    internal class BirthdayBoyVisualHandler : IEnemyVisualHandler
    {
        internal sealed class BirthdayBoyState
        {
            internal EnemyBirthdayBoy? BirthdayBoy;
            internal readonly VisualGrounding Grounding = new();
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            var bb = ep.GetComponentInChildren<EnemyBirthdayBoy>();
            if (bb == null)
                Plugin.Log.LogWarning("[SC]   BirthdayBoy: EnemyBirthdayBoy component not found, balloons stay full size");
            else
                Plugin.Log.LogDebug($"[SC]   BirthdayBoy: cached, maxBalloons={bb.maxBalloons}");

            return new BirthdayBoyState { BirthdayBoy = bb };
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            // Scale the enemy mesh like DefaultVisualHandler.
            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;

            if (visualState is not BirthdayBoyState bb) return;

            // Keep the mesh feet on the floor.
            bb.Grounding.Apply(state, ratio);

            // Scale all active balloons to match.
            if (bb.BirthdayBoy == null) return;
            foreach (KeyValuePair<Vector3, GameObject> kvp in bb.BirthdayBoy.balloons)
            {
                if (kvp.Value == null) continue;
                kvp.Value.transform.localScale = Vector3.one * ratio;
            }
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            (visualState as BirthdayBoyState)?.Grounding.Restore(state.AnimTarget);

            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale;
            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale;

            // Restore all balloon scales.
            if (visualState is not BirthdayBoyState bb || bb.BirthdayBoy == null) return;
            foreach (KeyValuePair<Vector3, GameObject> kvp in bb.BirthdayBoy.balloons)
            {
                if (kvp.Value == null) continue;
                kvp.Value.transform.localScale = Vector3.one;
            }
        }
    }
}
