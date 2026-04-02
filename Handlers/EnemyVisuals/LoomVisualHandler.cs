using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for Loom (Shadow enemy).
    /// Scales the mesh and adjusts attack distances so the Loom behaves
    /// proportionally when shrunken. Arm reach is handled by a Harmony patch
    /// on UpdateHandPositionTo (see EnemyPatches) since the game's IK runs
    /// during Update before our LateUpdate.
    /// Uses reflection for EnemyShadow fields — direct publicizer access
    /// causes NREs in the game's hand logic after shrink/unshrink cycles.
    /// </summary>
    internal class LoomVisualHandler : IEnemyVisualHandler
    {
        static readonly FieldInfo? _rightWristField = AccessTools.Field(typeof(EnemyShadow), "rightWristPivot");
        static readonly FieldInfo? _leftWristField  = AccessTools.Field(typeof(EnemyShadow), "leftWristPivot");
        static readonly FieldInfo? _origRightPosField = AccessTools.Field(typeof(EnemyShadow), "originalRightWristPosition");
        static readonly FieldInfo? _origLeftPosField  = AccessTools.Field(typeof(EnemyShadow), "originalLeftWristPosition");
        static readonly FieldInfo? _handMoveDistField = AccessTools.Field(typeof(EnemyShadow), "handMoveDistance");
        static readonly FieldInfo? _slapDistField     = AccessTools.Field(typeof(EnemyShadow), "slapDistance");

        internal sealed class LoomState
        {
            internal EnemyShadow? Shadow;
            internal Transform? RightWrist;
            internal Transform? LeftWrist;
            internal Vector3 OrigRightWristPos;
            internal Vector3 OrigLeftWristPos;
            internal float OrigHandMoveDist;
            internal float OrigSlapDist;
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
            if (_handMoveDistField != null)
                loomState.OrigHandMoveDist = (float)_handMoveDistField.GetValue(shadow);
            if (_slapDistField != null)
                loomState.OrigSlapDist = (float)_slapDistField.GetValue(shadow);

            Plugin.Log.LogInfo($"[SC]   Loom: wrists R={loomState.RightWrist != null} L={loomState.LeftWrist != null}" +
                $"  handMoveDist={loomState.OrigHandMoveDist:F1}  slapDist={loomState.OrigSlapDist:F1}");

            return loomState;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;
            if (visualState is not LoomState loom) return;

            // Scale the mesh
            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            // Arm IK targets are scaled by SpringPoseStretchPatch in EnemyPatches.
            // Scale attack distances so the Loom doesn't reach from full distance
            if (loom.Shadow != null)
            {
                if (_handMoveDistField != null)
                    _handMoveDistField.SetValue(loom.Shadow, loom.OrigHandMoveDist * ratio);
                if (_slapDistField != null)
                    _slapDistField.SetValue(loom.Shadow, loom.OrigSlapDist * ratio);
            }
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale = state.AnimOriginalScale;
                state.AnimTarget.localPosition = state.AnimOriginalLocalPos;
            }

            if (visualState is LoomState loom && loom.Shadow != null)
            {
                if (_handMoveDistField != null)
                    _handMoveDistField.SetValue(loom.Shadow, loom.OrigHandMoveDist);
                if (_slapDistField != null)
                    _slapDistField.SetValue(loom.Shadow, loom.OrigSlapDist);
            }
        }
    }
}
