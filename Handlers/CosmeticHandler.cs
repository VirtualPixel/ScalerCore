namespace ScalerCore.Handlers
{
    /// <summary>
    /// Cosmetic-box scaling logic (v0.4 CosmeticWorldObject, physics-grabbable boxes
    /// with health and an extraction zone, neither ValuableObject nor ItemAttributes).
    /// Tracks health drop on the sibling NotValuableObject and triggers expand on damage,
    /// mirroring ValuableHandler's dollar-value tracking pattern.
    /// </summary>
    internal class CosmeticHandler : IScaleHandler
    {
        internal sealed class State
        {
            internal NotValuableObject NotValuable = null!;
            internal int LastKnownHealth = -1;
        }

        public void Setup(ScaleController ctrl)
        {
            var nvo = ctrl.GetComponent<NotValuableObject>();
            if (nvo == null) return;
            ctrl.HandlerState = new State { NotValuable = nvo };
        }

        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            state.LastKnownHealth = state.NotValuable.healthCurrent;
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            state.LastKnownHealth = -1;
        }

        public void OnUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (ctrl._options.SuppressValueDropExpand) return;

            int currentHealth = state.NotValuable.healthCurrent;
            if (ctrl._bonkImmuneTimer > 0f)
            {
                state.LastKnownHealth = currentHealth;
            }
            else if (state.LastKnownHealth >= 0 && currentHealth < state.LastKnownHealth)
            {
                Plugin.Log.LogDebug($"[SC] HEALTH DROP {ctrl._displayName}  {state.LastKnownHealth} -> {currentHealth}  -> bonk expand");
                state.LastKnownHealth = -1;
                ctrl.DispatchExpandNow();
            }
            else
            {
                state.LastKnownHealth = currentHealth;
            }
        }

        public void OnLateUpdate(ScaleController ctrl) { }
        public void OnDestroy(ScaleController ctrl) { }
    }
}
