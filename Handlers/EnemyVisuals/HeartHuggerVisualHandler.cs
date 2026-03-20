using System.Collections.Generic;
using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for HeartHugger (Heart Hugger).
    /// Fixes: visual floating above grab area by scaling AnimTarget.localPosition.y,
    /// and scales segment collider GOs that sit outside the main RB hierarchy.
    /// </summary>
    internal class HeartHuggerVisualHandler : IEnemyVisualHandler
    {
        internal sealed class HuggerState
        {
            internal List<Transform>? SegmentColliders;
            internal List<Vector3>? SegmentOriginalScales;
            internal List<Vector3>? SegmentOriginalLocalPositions;
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            var huggerState = new HuggerState();

            // Find segment collider GOs on layer 10 that are children of EnemyParent
            // but NOT children of the Rigidbody or AnimTarget.
            // These are "COLLIDER Segment 1/2/3" in the hierarchy.
            huggerState.SegmentColliders = new List<Transform>();
            huggerState.SegmentOriginalScales = new List<Vector3>();
            huggerState.SegmentOriginalLocalPositions = new List<Vector3>();

            foreach (var col in ep.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                // Layer 10 colliders that contain "Segment" in name
                if (col.gameObject.layer != 10) continue;
                if (!col.gameObject.name.Contains("Segment")) continue;

                // Skip if it's under the Rigidbody (those scale with ctrl._t already)
                if (col.transform.IsChildOf(ctrl._t)) continue;

                huggerState.SegmentColliders.Add(col.transform);
                huggerState.SegmentOriginalScales.Add(col.transform.localScale);
                huggerState.SegmentOriginalLocalPositions.Add(col.transform.localPosition);
            }

            if (huggerState.SegmentColliders.Count > 0)
                Plugin.Log.LogInfo($"[SC]   HeartHugger: found {huggerState.SegmentColliders.Count} segment colliders to scale");

            // Diagnostic: dump AnimTarget details and parent chain
            if (state.AnimTarget != null)
            {
                Plugin.Log.LogInfo($"[SC]   HeartHugger: AnimTarget='{state.AnimTarget.name}' localPos={state.AnimTarget.localPosition} localScale={state.AnimTarget.localScale} parent='{state.AnimTarget.parent?.name}'");
                // Show immediate children to understand inner structure
                foreach (Transform child in state.AnimTarget)
                    Plugin.Log.LogInfo($"[SC]   HeartHugger:   child='{child.name}' localPos={child.localPosition} localScale={child.localScale} renderers={child.GetComponentsInChildren<Renderer>().Length}");
            }
            // Dump RB children for collider positions
            Plugin.Log.LogInfo($"[SC]   HeartHugger: RB localPos={ctrl._t.localPosition} localScale={ctrl._t.localScale}");
            foreach (Transform child in ctrl._t)
                Plugin.Log.LogInfo($"[SC]   HeartHugger:   RB child='{child.name}' localPos={child.localPosition}");

            return huggerState;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;

            // Scale mesh
            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            // Scale AnimTarget Y position so visual tracks scaled collider position.
            var pos = state.AnimOriginalLocalPos;
            pos.y *= ratio;
            state.AnimTarget.localPosition = pos;

            // Scale segment colliders
            if (visualState is HuggerState hugger && hugger.SegmentColliders != null)
            {
                for (int i = 0; i < hugger.SegmentColliders.Count; i++)
                {
                    if (hugger.SegmentColliders[i] == null) continue;
                    hugger.SegmentColliders[i].localScale = hugger.SegmentOriginalScales![i] * ratio;
                    var segPos = hugger.SegmentOriginalLocalPositions![i];
                    segPos.y *= ratio;
                    hugger.SegmentColliders[i].localPosition = segPos;
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

            if (visualState is HuggerState hugger && hugger.SegmentColliders != null)
            {
                for (int i = 0; i < hugger.SegmentColliders.Count; i++)
                {
                    if (hugger.SegmentColliders[i] == null) continue;
                    hugger.SegmentColliders[i].localScale = hugger.SegmentOriginalScales![i];
                    hugger.SegmentColliders[i].localPosition = hugger.SegmentOriginalLocalPositions![i];
                }
            }
        }
    }
}
