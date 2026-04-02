using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Door-specific scaling. Adjusts the HingeJoint anchor after scaling
    /// so the door still pivots at the right point.
    /// </summary>
    internal class DoorHandler : IScaleHandler
    {
        internal sealed class State
        {
            internal HingeJoint? Joint;
            internal Vector3 OriginalAnchor;
        }

        public void Setup(ScaleController ctrl)
        {
            var state = new State();
            state.Joint = ctrl.GetComponent<HingeJoint>();
            if (state.Joint != null)
                state.OriginalAnchor = state.Joint.anchor;
            ctrl.HandlerState = state;
        }

        public void OnScale(ScaleController ctrl)
        {
            // Rescale the anchor to compensate — when localScale shrinks,
            // the anchor (in local space) needs to grow so it maps to the
            // same world-space position.
            var state = (State?)ctrl.HandlerState;
            if (state?.Joint == null) return;
            float ratio = ctrl._options.Factor;
            if (ratio > 0f)
                state.Joint.anchor = state.OriginalAnchor / ratio;
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state?.Joint == null) return;
            state.Joint.anchor = state.OriginalAnchor;
        }

        public void OnUpdate(ScaleController ctrl) { }
        public void OnLateUpdate(ScaleController ctrl) { }
        public void OnDestroy(ScaleController ctrl)
        {
            OnRestore(ctrl, false);
        }
    }
}
