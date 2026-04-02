using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Door-specific scaling. After the root transform scales, shifts the door's
    /// world position so the hinge point stays in place.
    /// </summary>
    internal class DoorHandler : IScaleHandler
    {
        internal sealed class State
        {
            internal HingeJoint? Joint;
            internal Vector3 OriginalAnchor;
            internal Vector3 HingeWorldPos; // where the hinge was before shrinking
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
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            // Snapshot where the hinge is in world space before anything moves.
            state.HingeWorldPos = ctrl.transform.TransformPoint(state.OriginalAnchor);
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state?.Joint == null) return;
            state.Joint.anchor = state.OriginalAnchor;
        }

        public void OnUpdate(ScaleController ctrl) { }

        public void OnLateUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            // Only correct during the shrink/expand animation.
            // Once it finishes, let physics own the door so it opens/closes freely.
            if (!ctrl._transitioning) return;

            Vector3 currentHingeWorld = ctrl.transform.TransformPoint(state.OriginalAnchor);
            Vector3 correction = state.HingeWorldPos - currentHingeWorld;
            if (correction.sqrMagnitude > 0.0001f)
                ctrl.transform.position += correction;
        }

        public void OnDestroy(ScaleController ctrl)
        {
            OnRestore(ctrl, false);
        }
    }
}
