using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Cart-specific scaling logic. When a full-size cart (not the pocket cart) is shrunken,
    /// PocketHelper injects an ItemEquippable so the player can stash it. Cart-specific
    /// setup like the PhysGrabCart reference lives here; the actual equippable injection
    /// is shared across all item types via PocketHelper.
    /// </summary>
    internal class CartHandler : IScaleHandler
    {
        internal sealed class State
        {
            internal PhysGrabCart Cart = null!;
            internal bool AddedEquippable;
        }

        public void Setup(ScaleController ctrl)
        {
            var cart = ctrl.GetComponent<PhysGrabCart>();
            if (cart == null) return;
            ctrl.HandlerState = new State { Cart = cart };
        }

        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            state.AddedEquippable = PocketHelper.InjectEquippable(ctrl);
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (state.AddedEquippable)
            {
                if (PocketHelper.RemoveEquippable(ctrl))
                    state.AddedEquippable = false;
            }
        }

        public void OnUpdate(ScaleController ctrl) { }
        public void OnLateUpdate(ScaleController ctrl) { }
        public void OnDestroy(ScaleController ctrl) { }
    }
}
