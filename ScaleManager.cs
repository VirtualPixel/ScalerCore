using UnityEngine;
using ScalerCore.Handlers;

namespace ScalerCore
{
    /// <summary>
    /// Public API facade for ScalerCore.
    /// All external callers (guns, patches, other mods) go through here.
    /// Delegates to ScaleController — no new logic.
    /// </summary>
    public static class ScaleManager
    {
        /// <summary>
        /// Scale an object using the provided options.
        /// Gets existing ScaleController or returns if none attached.
        /// Skips the target if its handler type is not included in <see cref="ScaleOptions.AllowedTargets"/>.
        /// </summary>
        public static void Apply(GameObject target) => Apply(target, ScaleOptions.Default);

        /// <summary>
        /// Scale an object using the provided options.
        /// </summary>
        public static void Apply(GameObject target, ScaleOptions options)
        {
            var ctrl = target.GetComponent<ScaleController>()
                    ?? target.GetComponent<PlayerShrinkLink>()?.Controller;
            if (ctrl == null) return;

            // Check AllowedTargets against handler type
            if (ctrl.Handler is PlayerHandler   && (options.AllowedTargets & ScaleTargets.Players)   == 0) return;
            if (ctrl.Handler is EnemyHandler    && (options.AllowedTargets & ScaleTargets.Enemies)   == 0) return;
            if (ctrl.Handler is ItemHandler     && (options.AllowedTargets & ScaleTargets.Items)     == 0) return;
            if (ctrl.Handler is ValuableHandler && (options.AllowedTargets & ScaleTargets.Valuables) == 0) return;

            ctrl.DispatchShrink(options);
        }

        /// <summary>
        /// Restore with animation (timer expiry, gun toggle).
        /// </summary>
        public static void Restore(GameObject target)
        {
            var ctrl = target.GetComponent<ScaleController>()
                    ?? target.GetComponent<PlayerShrinkLink>()?.Controller;
            if (ctrl == null) return;
            ctrl.DispatchExpand();
        }

        /// <summary>
        /// Restore immediately without animation (bonk/damage).
        /// </summary>
        public static void RestoreImmediate(GameObject target)
        {
            var ctrl = target.GetComponent<ScaleController>()
                    ?? target.GetComponent<PlayerShrinkLink>()?.Controller;
            if (ctrl == null) return;
            ctrl.DispatchExpandNow();
        }

        /// <summary>
        /// Check if an object is currently scaled.
        /// </summary>
        public static bool IsScaled(GameObject target)
        {
            var ctrl = target.GetComponent<ScaleController>()
                    ?? target.GetComponent<PlayerShrinkLink>()?.Controller;
            return ctrl != null && ctrl.IsScaled;
        }

        /// <summary>
        /// Cleanup all scaled objects on level change.
        /// </summary>
        public static void CleanupAll()
        {
            ScaleController.CleanupAll();
        }
    }
}
