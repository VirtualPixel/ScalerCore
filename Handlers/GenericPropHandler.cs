namespace ScalerCore.Handlers
{
    /// <summary>
    /// Plain grabbable props with no marker component the other handlers key on
    /// (dead Semibot heads, the shop radio). ScaleController's core pass covers
    /// everything they need; deliberately NO pocket injection, a shrunken head
    /// in a pocket would fight the revive logic.
    /// </summary>
    internal class GenericPropHandler : IScaleHandler
    {
        public void Setup(ScaleController ctrl) { }
        public void OnScale(ScaleController ctrl) { }
        public void OnRestore(ScaleController ctrl, bool isBonk) { }
        public void OnUpdate(ScaleController ctrl) { }
        public void OnLateUpdate(ScaleController ctrl) { }
        public void OnDestroy(ScaleController ctrl) { }
    }
}
