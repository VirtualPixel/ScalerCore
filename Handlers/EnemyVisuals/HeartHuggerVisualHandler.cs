using System.Collections.Generic;
using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for HeartHugger (Heart Hugger).
    /// The RB and Visuals are siblings under the Enable container.
    /// ScaleController handles the RB; this handler scales the Visuals GO
    /// and its localPosition so the mesh stays aligned with the shrunken physics body.
    /// External COLLIDER Segment GOs are also tracked and scaled.
    /// </summary>
    internal class HeartHuggerVisualHandler : IEnemyVisualHandler
    {
        internal sealed class HuggerState
        {
            internal Transform? VisualsTransform;
            internal Vector3 VisualsOriginalScale;
            internal Vector3 VisualsOriginalLocalPos;
            internal List<Transform>? SegmentColliders;
            internal List<Vector3>? SegmentOriginalScales;
            internal List<Vector3>? SegmentOriginalLocalPositions;
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            var huggerState = new HuggerState();

            // Find the Visuals sibling of the Rigidbody within Enable.
            // It's a direct child of AnimTarget (Enable) that is NOT the RB
            // and NOT the Controller, and has renderers in its children.
            if (state.AnimTarget != null)
            {
                foreach (Transform child in state.AnimTarget)
                {
                    if (child.GetComponent<EnemyRigidbody>() != null) continue;
                    if (child.GetComponentInChildren<Renderer>() == null) continue;

                    huggerState.VisualsTransform = child;
                    huggerState.VisualsOriginalScale = child.localScale;
                    huggerState.VisualsOriginalLocalPos = child.localPosition;
                    Plugin.Log.LogDebug($"[SC]   HeartHugger: visual root={child.name}  localPos={child.localPosition}");
                    break;
                }
            }

            // Find COLLIDER Segment GOs outside AnimTarget
            huggerState.SegmentColliders = new List<Transform>();
            huggerState.SegmentOriginalScales = new List<Vector3>();
            huggerState.SegmentOriginalLocalPositions = new List<Vector3>();

            foreach (var col in ep.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (col.gameObject.layer != 10) continue;
                if (!col.gameObject.name.Contains("Segment")) continue;
                if (state.AnimTarget != null && col.transform.IsChildOf(state.AnimTarget)) continue;

                huggerState.SegmentColliders.Add(col.transform);
                huggerState.SegmentOriginalScales.Add(col.transform.localScale);
                huggerState.SegmentOriginalLocalPositions.Add(col.transform.localPosition);
            }

            if (huggerState.SegmentColliders.Count > 0)
                Plugin.Log.LogDebug($"[SC]   HeartHugger: found {huggerState.SegmentColliders.Count} external segment colliders");

            return huggerState;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (visualState is not HuggerState hugger) return;

            // Scale the Visuals GO directly — NOT Enable (which contains the RB).
            // ScaleController handles the RB; we handle the visual mesh.
            // Use world-space positioning so the Visuals follow the RB's position
            // AND rotation — when tipped/knocked over, the offset rotates with the body.
            if (hugger.VisualsTransform != null)
            {
                hugger.VisualsTransform.localScale = hugger.VisualsOriginalScale * ratio;
                Vector3 offset = (hugger.VisualsOriginalLocalPos - state.RbOriginalLocalPos) * ratio;
                hugger.VisualsTransform.position = ctrl._t.position + ctrl._t.rotation * offset;
            }

            // Scale external segment colliders
            if (hugger.SegmentColliders != null)
            {
                for (int i = 0; i < hugger.SegmentColliders.Count; i++)
                {
                    if (hugger.SegmentColliders[i] == null) continue;
                    hugger.SegmentColliders[i].localScale = hugger.SegmentOriginalScales![i] * ratio;
                    hugger.SegmentColliders[i].localPosition = hugger.SegmentOriginalLocalPositions![i] * ratio;
                }
            }
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            if (visualState is not HuggerState hugger) return;

            if (hugger.VisualsTransform != null)
            {
                hugger.VisualsTransform.localScale = hugger.VisualsOriginalScale;
                hugger.VisualsTransform.localPosition = hugger.VisualsOriginalLocalPos;
            }

            if (hugger.SegmentColliders != null)
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
