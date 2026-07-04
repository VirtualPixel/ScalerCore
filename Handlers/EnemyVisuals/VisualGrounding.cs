using UnityEngine;

namespace ScalerCore.Handlers.EnemyVisuals
{
    /// <summary>
    /// Shared grounding: when the collider is held smaller than the mesh, scaling the mesh
    /// from a pivot that sits above its feet drops the feet through the floor. Measure how
    /// far the pivot sits above the floor, then move the mesh by exactly the amount scaling
    /// would shift it so the silhouette keeps a proportional stance on the ground.
    /// </summary>
    internal static class VisualGrounding
    {
        internal static float MeasureFootOffset(Transform? animTarget)
        {
            if (animTarget == null) return 0f;

            // Pivot-to-FLOOR by raycast, on the same mask the game re-seats enemies with.
            // Scaling then shrinks the whole silhouette-to-floor distance, so an enemy whose
            // hem hovers by design (the Robe glides above the ground) keeps a proportional
            // gap instead of its full-size one, and any slack in the skinned bounds drops
            // out of the measurement. Enemies set up standing on their spawn point, so the
            // floor is right below.
            if (Physics.Raycast(animTarget.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit floor, 10f,
                    LayerMask.GetMask("Default", "NavmeshOnly", "PlayerOnlyCollision")))
                return Mathf.Max(0f, animTarget.position.y - floor.point.y);

            // No floor under the spawn point: fall back to pivot-to-lowest-rendered-point.
            // Character mesh only. Shadows, ground decals and particle effects are separate
            // renderers that often sit below the feet and would inflate the offset.
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
