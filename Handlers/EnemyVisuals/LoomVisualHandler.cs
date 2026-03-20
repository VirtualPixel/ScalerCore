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

            return loomState;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;

            // Scale the mesh
            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            // Re-scale wrist positions. The game set these to full-scale values
            // in Update; we override them here in LateUpdate.
            if (visualState is LoomState loom)
            {
                if (loom.RightWrist != null)
                    loom.RightWrist.localPosition = loom.RightWrist.localPosition * ratio;
                if (loom.LeftWrist != null)
                    loom.LeftWrist.localPosition = loom.LeftWrist.localPosition * ratio;
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
