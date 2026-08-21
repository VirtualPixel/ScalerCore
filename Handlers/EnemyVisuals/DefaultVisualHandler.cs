namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Default visual scaling: AnimTarget.localScale + BtHead.localScale, plus grounding so
    /// the mesh stays on the floor when the collider is capped smaller. Used for all enemies
    /// without a specific override.
    /// </summary>
    internal class DefaultVisualHandler : IEnemyVisualHandler
    {
        public object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep) => new VisualGrounding();

        public void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio)
        {
            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale * ratio;

            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;

            (visualState as VisualGrounding)?.Apply(state, ratio);
        }

        public void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState)
        {
            // Hand the height back rather than snapping to the pose captured at spawn: for
            // every enemy whose mesh transform the game drives per frame, that snapshot is
            // stale the moment the enemy walks anywhere.
            (visualState as VisualGrounding)?.Restore(state.AnimTarget);

            if (state.AnimTarget != null)
                state.AnimTarget.localScale = state.AnimOriginalScale;
            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale;
        }
    }
}
