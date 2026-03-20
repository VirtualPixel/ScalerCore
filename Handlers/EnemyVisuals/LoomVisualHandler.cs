using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for Loom (Shadow enemy).
    /// Fixes arm detachment by re-scaling wrist pivot positions in LateUpdate
    /// after the game's EnemyShadow.Update sets them to full-scale values.
    /// </summary>
    internal class LoomVisualHandler : IEnemyVisualHandler
    {
        // Reflection cache for EnemyShadow fields
        static readonly FieldInfo? _rightWristField = AccessTools.Field(typeof(EnemyShadow), "rightWristPivot");
        static readonly FieldInfo? _leftWristField  = AccessTools.Field(typeof(EnemyShadow), "leftWristPivot");
        static readonly FieldInfo? _origRightPosField = AccessTools.Field(typeof(EnemyShadow), "originalRightWristPosition");
        static readonly FieldInfo? _origLeftPosField  = AccessTools.Field(typeof(EnemyShadow), "originalLeftWristPosition");

        internal sealed class LoomState
        {
            internal EnemyShadow? Shadow;
            internal Transform? RightWrist;
            internal Transform? LeftWrist;
            internal Vector3 OrigRightWristPos;
            internal Vector3 OrigLeftWristPos;
        }

        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep)
        {
            var shadow = ep.GetComponentInChildren<EnemyShadow>();
            if (shadow == null)
            {
                Plugin.Log.LogWarning("[SC]   Loom: EnemyShadow component not found, falling back to default");
                return null;
            }

            var loomState = new LoomState { Shadow = shadow };

            if (_rightWristField != null)
                loomState.RightWrist = (Transform?)_rightWristField.GetValue(shadow);
            if (_leftWristField != null)
                loomState.LeftWrist = (Transform?)_leftWristField.GetValue(shadow);
            if (_origRightPosField != null)
                loomState.OrigRightWristPos = (Vector3)_origRightPosField.GetValue(shadow);
            if (_origLeftPosField != null)
                loomState.OrigLeftWristPos = (Vector3)_origLeftPosField.GetValue(shadow);

            Plugin.Log.LogInfo($"[SC]   Loom: wrists found R={loomState.RightWrist != null} L={loomState.LeftWrist != null}");

            // Diagnostic: dump hierarchy from AnimTarget to understand arm bone structure
            if (state.AnimTarget != null)
            {
                Plugin.Log.LogInfo($"[SC]   Loom: AnimTarget='{state.AnimTarget.name}' localPos={state.AnimTarget.localPosition} parent='{state.AnimTarget.parent?.name}'");
                DumpChildren(state.AnimTarget, 2);
            }
            if (loomState.RightWrist != null)
            {
                var chain = "";
                var t = loomState.RightWrist;
                while (t != null && t != ep.transform)
                {
                    chain = $"{t.name}(lp={t.localPosition:F2},ls={t.localScale:F2})" + (chain.Length > 0 ? " → " + chain : "");
                    t = t.parent;
                }
                Plugin.Log.LogInfo($"[SC]   Loom: R wrist chain: {chain}");
            }

            return loomState;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;

            // Scale the mesh
            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            // NOTE: Wrist localPositions are in their parent bone's space.
            // Since the parent hierarchy is already scaled by AnimTarget.localScale,
            // the positions are automatically proportional — no additional scaling needed.
            // Previous attempt to scale wrist positions caused double-scaling (elbow-to-hand
            // stretched wrong). The arm detachment issue needs a different approach.
        }

        static void DumpChildren(Transform t, int maxDepth, int depth = 0)
        {
            if (depth >= maxDepth) return;
            var indent = new string(' ', (depth + 1) * 2);
            foreach (Transform child in t)
            {
                int renderers = child.GetComponentsInChildren<Renderer>().Length;
                Plugin.Log.LogInfo($"[SC]   Loom: {indent}{child.name}  localPos={child.localPosition:F2}  localScale={child.localScale:F2}  renderers={renderers}");
                DumpChildren(child, maxDepth, depth + 1);
            }
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale = state.AnimOriginalScale;
                state.AnimTarget.localPosition = state.AnimOriginalLocalPos;
            }
            // Wrist positions are set by game code every frame, no explicit restore needed.
        }
    }
}
