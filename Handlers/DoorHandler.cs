using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Door-specific scaling. Compensates localPosition so the hinge point
    /// stays in place when the door shrinks (otherwise it drifts because
    /// the door's local origin isn't at the hinge).
    /// </summary>
    internal class DoorHandler : IScaleHandler
    {
        internal sealed class State
        {
            internal Vector3 HingeLocalOffset; // hingePoint.localPosition at setup time
            internal Vector3 OriginalLocalPos; // door's original localPosition
        }

        public void Setup(ScaleController ctrl)
        {
            var hinge = ctrl.GetComponent<PhysGrabHinge>();
            if (hinge == null) return;

            var state = new State
            {
                OriginalLocalPos = ctrl.transform.localPosition,
            };

            // HingeJoint anchor was set to hingePoint.localPosition in PhysGrabHinge.Awake.
            var joint = ctrl.GetComponent<HingeJoint>();
            if (joint != null)
                state.HingeLocalOffset = joint.anchor;

            ctrl.HandlerState = state;
        }

        public void OnScale(ScaleController ctrl) { }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            ctrl.transform.localPosition = state.OriginalLocalPos;
        }

        public void OnUpdate(ScaleController ctrl) { }

        public void OnLateUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (!ctrl.IsScaled && !ctrl._transitioning) return;

            // The hinge offset in local space doesn't change, but when the
            // door scales down, that offset maps to a smaller world distance.
            // Compensate by shifting localPosition so the hinge stays put.
            float ratio = ctrl._t.localScale.x / ctrl.OriginalScale.x;
            Vector3 drift = state.HingeLocalOffset * (1f - ratio);
            ctrl.transform.localPosition = state.OriginalLocalPos + ctrl.transform.parent.InverseTransformVector(ctrl.transform.TransformVector(drift));
        }

        public void OnDestroy(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            ctrl.transform.localPosition = state.OriginalLocalPos;
        }
    }
}
