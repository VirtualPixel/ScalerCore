using UnityEngine;
using ScalerCore.Handlers;

namespace ScalerCore
{
    /// <summary>
    /// Public API facade for ScalerCore.
    /// All external callers (guns, patches, other mods) go through here.
    /// Delegates to ScaleController, no new logic.
    /// </summary>
    public static class ScaleManager
    {
        /// <summary>
        /// Scale an object using the provided options.
        /// Gets existing ScaleController or returns if none attached.
        /// Skips the target if its handler type is not included in <see cref="ScaleOptions.AllowedTargets"/>,
        /// or if the controller's current session was locked via <see cref="ScaleOptions.RejectExternalApply"/>.
        /// </summary>
        public static void Apply(GameObject target) => Apply(target, ScaleOptions.Default);

        /// <summary>
        /// Scale an object using the provided options.
        /// </summary>
        public static void Apply(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            if (IsLockedFromExternal(ctrl)) return;
            if (!IsTargetAllowed(ctrl, options.AllowedTargets)) return;
            ctrl.DispatchShrink(options);
        }

        /// <summary>
        /// Scale an object only if it isn't already scaled.
        /// Unlike Apply(), this won't toggle or rescale, it's a no-op if the object is already scaled.
        /// Ideal for cart mods and other continuous triggers that fire every frame.
        /// </summary>
        public static bool ApplyIfNotScaled(GameObject target) => ApplyIfNotScaled(target, ScaleOptions.Default);

        /// <summary>
        /// Scale an object only if it isn't already scaled.
        /// Returns true if the object was scaled, false if it was already scaled, has no controller,
        /// is locked by another mod, or is filtered out by AllowedTargets.
        /// </summary>
        public static bool ApplyIfNotScaled(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null || ctrl.IsScaled) return false;
            if (IsLockedFromExternal(ctrl)) return false;
            if (!IsTargetAllowed(ctrl, options.AllowedTargets)) return false;
            ctrl.DispatchShrink(options);
            return true;
        }

        /// <summary>
        /// <see cref="Apply(GameObject, ScaleOptions)"/> without the lock check.
        /// Still respects <see cref="ScaleOptions.AllowedTargets"/>.
        /// </summary>
        public static void ForceApply(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            if (!IsTargetAllowed(ctrl, options.AllowedTargets)) return;
            ctrl.DispatchShrink(options);
        }

        /// <summary>
        /// Get the ScaleController for a game object, resolving through PlayerShrinkLink if needed.
        /// Returns null if no controller is attached.
        /// </summary>
        public static ScaleController? GetController(GameObject target)
        {
            return target.GetComponent<ScaleController>()
                ?? target.GetComponent<PlayerShrinkLink>()?.Controller;
        }

        /// <summary>
        /// Restore with animation (timer expiry, gun toggle).
        /// No-op if the session was locked via <see cref="ScaleOptions.RejectExternalApply"/>.
        /// </summary>
        public static void Restore(GameObject target)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            if (IsLockedFromExternal(ctrl)) return;
            ctrl.DispatchExpand();
        }

        /// <summary>
        /// Restore immediately without animation (bonk/damage).
        /// No-op if the session was locked via <see cref="ScaleOptions.RejectExternalApply"/>.
        /// </summary>
        public static void RestoreImmediate(GameObject target)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            if (IsLockedFromExternal(ctrl)) return;
            ctrl.DispatchExpandNow();
        }

        /// <summary><see cref="Restore"/> without the lock check.</summary>
        public static void ForceRestore(GameObject target)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            ctrl.DispatchExpand();
        }

        /// <summary>
        /// Check if an object is currently scaled.
        /// </summary>
        public static bool IsScaled(GameObject target)
        {
            var ctrl = GetController(target);
            return ctrl != null && ctrl.IsScaled;
        }

        /// <summary>
        /// Cleanup all scaled objects on level change.
        /// </summary>
        public static void CleanupAll()
        {
            ScaleController.CleanupAll();
        }

        // Handlers without a dedicated flag bit (e.g. CosmeticHandler) fall through to Valuables.
        static bool IsTargetAllowed(ScaleController ctrl, ScaleTargets allowed)
        {
            return ctrl.Handler switch
            {
                PlayerHandler                  => (allowed & ScaleTargets.Players)   != 0,
                EnemyHandler                   => (allowed & ScaleTargets.Enemies)   != 0,
                ItemHandler or VehicleHandler  => (allowed & ScaleTargets.Items)     != 0,
                _                              => (allowed & ScaleTargets.Valuables) != 0,
            };
        }

        static bool IsLockedFromExternal(ScaleController ctrl) =>
            ctrl.IsScaled && ctrl._options.RejectExternalApply;
    }
}
