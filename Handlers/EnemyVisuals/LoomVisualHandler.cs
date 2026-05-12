using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Visual handler for Loom (Shadow enemy). Scales the mesh, the IK
    /// chain bind lengths used by BotSystemSpringPoseAnimator, and the
    /// hand-reach distances so the arms track the shrunken body.
    /// Uses reflection for EnemyShadow fields, direct publicizer access
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
        static readonly FieldInfo? _limbChainsField   = AccessTools.Field(typeof(BotSystemSpringPoseAnimator), "limbChains");

        internal sealed class LoomState
        {
            internal EnemyShadow? Shadow;
            internal BotSystemSpringPoseAnimator? Spring;
            internal Transform? RightWrist;
            internal Transform? LeftWrist;
            internal Vector3 OrigRightWristPos;
            internal Vector3 OrigLeftWristPos;
            internal float OrigHandMoveDist;
            internal float OrigSlapDist;
            // Per-chain copy of the bind-pose bone lengths captured at Setup time.
            // The IK solver reads lenBind directly when laying out joints, scaling
            // it down with the body brings the arms into the body's envelope.
            internal Dictionary<BotSystemSpringPoseAnimator.LimbChain, float[]>? OrigLenBind;
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

            loomState.Spring = shadow.springPoseAnimator;
            CaptureBindLengths(loomState);

            Plugin.Log.LogDebug($"[SC]   Loom: wrists R={loomState.RightWrist != null} L={loomState.LeftWrist != null}" +
                $"  handMoveDist={loomState.OrigHandMoveDist:F1}  slapDist={loomState.OrigSlapDist:F1}" +
                $"  chains={loomState.OrigLenBind?.Count ?? 0}");

            return loomState;
        }

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget == null) return;
            if (visualState is not LoomState loom) return;

            state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (loom.Shadow != null)
            {
                if (_handMoveDistField != null)
                    _handMoveDistField.SetValue(loom.Shadow, loom.OrigHandMoveDist * ratio);
                if (_slapDistField != null)
                    _slapDistField.SetValue(loom.Shadow, loom.OrigSlapDist * ratio);
            }

            ApplyBindLengths(loom, ratio);
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale = state.AnimOriginalScale;
                state.AnimTarget.localPosition = state.AnimOriginalLocalPos;
            }

            if (visualState is not LoomState loom) return;

            if (loom.Shadow != null)
            {
                if (_handMoveDistField != null)
                    _handMoveDistField.SetValue(loom.Shadow, loom.OrigHandMoveDist);
                if (_slapDistField != null)
                    _slapDistField.SetValue(loom.Shadow, loom.OrigSlapDist);
            }

            ApplyBindLengths(loom, 1f);
        }

        static void CaptureBindLengths(LoomState loom)
        {
            if (loom.Spring == null || _limbChainsField == null) return;
            var chains = _limbChainsField.GetValue(loom.Spring) as System.Collections.IDictionary;
            if (chains == null) return;

            loom.OrigLenBind = new Dictionary<BotSystemSpringPoseAnimator.LimbChain, float[]>();
            foreach (System.Collections.DictionaryEntry entry in chains)
            {
                if (entry.Value is BotSystemSpringPoseAnimator.LimbChain chain && chain.lenBind != null)
                    loom.OrigLenBind[chain] = (float[])chain.lenBind.Clone();
            }
        }

        static void ApplyBindLengths(LoomState loom, float ratio)
        {
            if (loom.OrigLenBind == null) return;
            foreach (var kv in loom.OrigLenBind)
            {
                var chain = kv.Key;
                var orig  = kv.Value;
                if (chain.lenBind == null || chain.lenBind.Length != orig.Length) continue;
                for (int i = 0; i < orig.Length; i++)
                    chain.lenBind[i] = orig[i] * ratio;
            }
        }
    }
}
