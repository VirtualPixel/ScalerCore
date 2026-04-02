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

            float currentValue = state.ValuableObject.dollarValueCurrent;
            if (ctrl._bonkImmuneTimer > 0f)
            {
                // During immunity, keep tracking value so damage taken during
                // the grace period doesn't trigger expand after immunity expires.
                state.LastKnownValue = currentValue;
            }
            else if (state.LastKnownValue >= 0f && currentValue < state.LastKnownValue)
            {
                Plugin.Log.LogInfo($"[SC] VALUE DROP {ctrl._displayName}  ${state.LastKnownValue:F2} -> ${currentValue:F2}  -> bonk expand");
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
    }
}
