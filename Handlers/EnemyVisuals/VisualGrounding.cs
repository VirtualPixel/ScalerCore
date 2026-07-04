using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Shared grounding: when the collider is held smaller than the mesh, scaling the mesh
    /// from a pivot that sits above its feet drops the feet through the floor. Measure how
    /// far the pivot is above the lowest rendered point, then lift the mesh by exactly the
    /// amount scaling would sink it so the feet stay put.
    /// </summary>
    internal static class VisualGrounding
    {
        internal static float MeasureFootOffset(Transform? animTarget)
        {
            if (animTarget == null) return 0f;
            // Measure from the character mesh only. Shadows, ground decals and particle
            // effects are separate renderers that often sit below the feet and would
            // inflate the offset, lifting the enemy off the ground.
            Renderer[] renderers = animTarget.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0)
                renderers = animTarget.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return 0f;
            float lowest = float.MaxValue;
            foreach (var r in renderers)
                lowest = Mathf.Min(lowest, r.bounds.min.y);
            return animTarget.position.y - lowest;
        }

        internal static void Apply(EnemyHandler.State state, float footOffset, float ratio)
        {
            if (state.AnimTarget == null || footOffset == 0f) return;
            var pos = state.AnimTarget.localPosition;
            // Scaling the mesh around a pivot that sits above the feet moves the feet by
            // footOffset*(ratio-1): down when growing, up (floating) when shrinking. This
            // cancels that in both directions for position-pinned enemies whose body doesn't
            // settle. Physics-settling enemies don't use this on shrink; they track the rb gap.
            pos.y = state.AnimOriginalLocalPos.y + footOffset * (ratio - 1f);
            state.AnimTarget.localPosition = pos;
        }
    }
}
