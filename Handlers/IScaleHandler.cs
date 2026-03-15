namespace ScalerCore.Handlers
{
    public interface IScaleHandler
    {
        void Setup(ScaleController ctrl);
        void OnScale(ScaleController ctrl);
        void OnRestore(ScaleController ctrl, bool isBonk);
        void OnUpdate(ScaleController ctrl);
        void OnLateUpdate(ScaleController ctrl);
        void OnDestroy(ScaleController ctrl);
    }
}
