using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Valuable-specific scaling logic (objects with ValuableObject).
    /// Tracks dollar value and triggers expand when value drops (replaces BreakMedium/Heavy patches).
    /// State is created ONCE in Setup and NEVER cleared or recreated.
    /// </summary>
    internal class ValuableHandler : IScaleHandler
    {

        internal sealed class State
        {
            internal ValuableObject ValuableObject = null!;
            internal float LastKnownValue = -1f;

            // Diagnostic: throttle for the per-frame mass-drift watchdog (see OnDiagnoseMass).
            internal float LastMassDiagTime = -999f;
            internal float LastMassReported = float.NaN;
        }

        public void Setup(ScaleController ctrl)
        {
            var vo = ctrl.GetComponent<ValuableObject>();
            if (vo == null) return;

            var state = new State();
            state.ValuableObject = vo;
            ctrl.HandlerState = state;
        }

        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            // Snapshot current value so we can detect drops.
            state.LastKnownValue = state.ValuableObject.dollarValueCurrent;
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            state.LastKnownValue = -1f;
        }

        /// <summary>
        /// Per-frame value tracking for shrunken valuables.
        /// Expands when value drops (catches ALL sources of value loss: floor impacts, enemy hits, etc.)
        /// and ignores impacts that don't actually reduce value (e.g. indestructible drone).
        /// Called from Update() on host when IsScaled.
        /// </summary>
        public void OnUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (ctrl._options.SuppressValueDropExpand) return;

            float currentValue = state.ValuableObject.dollarValueCurrent;
            if (ctrl._bonkImmuneTimer > 0f)
            {
                // During immunity, keep tracking value so damage taken during
                // the grace period doesn't trigger expand after immunity expires.
                state.LastKnownValue = currentValue;
            }
            else if (state.LastKnownValue >= 0f && currentValue < state.LastKnownValue)
            {
                Plugin.Log.LogDebug($"[SC] VALUE DROP {ctrl._displayName}  ${state.LastKnownValue:F2} -> ${currentValue:F2}  -> bonk expand");
                state.LastKnownValue = -1f;
                ctrl.DispatchExpandNow();
            }
            else
                state.LastKnownValue = currentValue;
        }

        public void OnLateUpdate(ScaleController ctrl)
        {
            // No valuable-specific LateUpdate logic.
        }

        public void OnDestroy(ScaleController ctrl)
        {
            // No valuable-specific destroy logic.
        }

        /// <summary>
        /// Diagnostic: runs every frame on both host and client (called from ScaleController.Update
        /// outside the host gate). Logs a single line every 0.5s with: current rb.mass vs the
        /// post-shrink target we expect, plus PhysGrabObject's massOriginal/timerAlterMass so we
        /// can tell if the game's OverrideMass/ResetMass machinery is intercepting our value.
        /// Also fires an immediate INFO line on the first drift after a steady period.
        /// </summary>
        public static void OnDiagnoseMass(ScaleController ctrl, bool isHost)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null || ctrl._rb == null) return;

            float now = Time.unscaledTime;
            bool throttle = now - state.LastMassDiagTime < 0.5f;

            float f       = ctrl._options.Factor;
            float wantRaw = ctrl._originalMass * f;
            float expected = ctrl._options.PreserveMass
                ? ctrl._originalMass
                : Mathf.Clamp(wantRaw, 0.5f, ctrl._options.MassCap);
            float actual = ctrl._rb.mass;
            bool drift   = Mathf.Abs(actual - expected) > 0.001f;

            // Immediate log on first divergence even if throttled, so we catch the
            // exact moment a fight starts.
            bool noteworthy = drift && !float.IsNaN(state.LastMassReported) &&
                              Mathf.Abs(actual - state.LastMassReported) > 0.001f;

            if (throttle && !noteworthy) return;
            state.LastMassDiagTime = now;
            state.LastMassReported = actual;

            string side  = isHost ? "HOST" : "CLIENT";
            string tag   = drift ? "MASS_DRIFT" : "MASS_OK  ";
            bool inCart  = IsInAnyCart(ctrl._physGrabObject);
            var pgo      = ctrl._physGrabObject;

            Plugin.Log.LogInfo(
                $"[SC-DIAG][{side}] {tag} {ctrl._displayName}  " +
                $"rbMass={actual:F3}  want={expected:F3}  wantRaw={wantRaw:F3}  " +
                $"origMass={ctrl._originalMass:F3}  factor={f:F2}  " +
                (pgo != null
                    ? $"pgo.massOrig={pgo.massOriginal:F3}  pgo.alterMass={pgo.alterMassValue:F3}  pgo.timerAlter={pgo.timerAlterMass:F2}  "
                    : "pgo=null  ") +
                $"inCart={inCart}");
        }

        static bool IsInAnyCart(PhysGrabObject? pgo)
        {
            if (pgo == null) return false;
            // Diagnostic-only; FindObjectsOfType is fine here, called at most every 0.5s
            // per shrunken valuable and not at all when no valuable is scaled.
            foreach (var inCart in Object.FindObjectsOfType<PhysGrabInCart>())
            {
                if (inCart == null || inCart.inCartObjects == null) continue;
                foreach (var co in inCart.inCartObjects)
                    if (co != null && co.physGrabObject == pgo) return true;
            }
            return false;
        }
    }
}
