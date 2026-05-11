using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Vehicle-specific scaling logic for v0.4 ItemVehicle. Vehicles deparent their visible
    /// mesh from the root transform when a player sits in them, `ItemVehicle.DeparentMesh`
    /// calls `meshTransform.SetParent(null, true)` and drives `meshTransform.position` /
    /// `rotation` directly each frame. Once deparented, the mesh no longer inherits the root
    /// transform's scale, so a plain root-scale write shrinks the colliders but leaves the
    /// visible mesh at full size, and worse, when the game later reparents (player exits)
    /// it does `SetParent(originalParent, worldPositionStays: true)`, which inflates the
    /// child's localScale to compensate for the shrunken parent. On expand, the inflation
    /// multiplies through and the mesh ends up at 1/factor times original size.
    ///
    /// We piggyback on ItemHandler's pocketing path (vehicles are ItemAttributes-bearing
    /// and benefit from the same pocketing inject), then drive meshTransform.localScale
    /// each frame to keep its world scale matching the root's intended scale, regardless of
    /// the current parent state. ItemVehicle never writes meshTransform.localScale itself,
    /// so our enforcement isn't fighting another writer.
    /// </summary>
    internal class VehicleHandler : IScaleHandler
    {
        internal sealed class State
        {
            internal ItemVehicle Vehicle = null!;
            internal Transform? Mesh;
            internal Vector3 OriginalMeshLocalScale = Vector3.one;
            internal bool AddedEquippable;
        }

        public void Setup(ScaleController ctrl)
        {
            var v = ctrl.GetComponent<ItemVehicle>();
            if (v == null) return;
            var state = new State { Vehicle = v, Mesh = v.meshTransform };
            if (state.Mesh != null)
                state.OriginalMeshLocalScale = state.Mesh.localScale;
            ctrl.HandlerState = state;
        }

        public void OnScale(ScaleController ctrl)
        {
            // Same pocketing path ItemHandler uses, vehicles are ItemAttributes-bearing.
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
                PocketHelper.RemoveEquippable(ctrl);
                state.AddedEquippable = false;
            }

            // Snap mesh back to its original localScale. If the game reparents during expand
            // animation, ItemVehicle's reparent uses worldPositionStays=true, but by the time
            // OnLateUpdate runs again we're back at root scale 1.0, so localScale = original
            // gives the correct world scale.
            if (state.Mesh != null)
                state.Mesh.localScale = state.OriginalMeshLocalScale;
        }

        public void OnUpdate(ScaleController ctrl) { }

        public void OnLateUpdate(ScaleController ctrl)
        {
            // Enforce the right mesh localScale based on current parent state. Cheap check
            // one Transform reference comparison plus a Vector3 set if it changed.
            var state = (State?)ctrl.HandlerState;
            if (state == null || state.Mesh == null) return;
            if (!ctrl.IsScaled) return;

            float factor = ctrl._options.Factor;
            // When parented: parent's scale already provides the factor, mesh stays at original.
            // When deparented (meshTransform.parent == null): mesh must carry the factor itself.
            Vector3 desired = state.Mesh.parent == null
                ? state.OriginalMeshLocalScale * factor
                : state.OriginalMeshLocalScale;
            if (state.Mesh.localScale != desired)
                state.Mesh.localScale = desired;
        }

        public void OnDestroy(ScaleController ctrl) { }
    }
}
