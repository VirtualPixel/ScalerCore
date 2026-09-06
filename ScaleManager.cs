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
        /// Whether scaling may hit dead Semibot heads. Policy belongs to the
        /// calling mod (bind a config there and assign this), not to ScalerCore:
        /// the library carries no user-facing settings of its own. Default off;
        /// a pea-sized head is easy to lose and reviving from one is its own problem.
        /// </summary>
        public static bool AllowDeadHeads { get; set; }

        /// <summary>
        /// Scale resting items, valuables, carts and vehicles by respawning them through the
        /// game's own network instantiate with the scale in the instantiation data, which the
        /// game applies on every client whether or not it runs ScalerCore. Players without the
        /// mod see the object at its real size with the right colliders instead of a full-size
        /// body shaking against the host's small one. Players and enemies stay on the RPC path.
        /// A respawn replaces the GameObject: re-resolve with <see cref="GetController"/> after
        /// an Apply if you keep a reference. On by default; a consuming mod can turn it off, and
        /// a scalercore_nativesync_off file in BepInEx/config turns it off for a session.
        /// </summary>
        public static bool NativeSync { get; set; } = true;

        /// <summary>
        /// With <see cref="NativeSync"/>, an object in a hand, an inventory slot or a seat is
        /// scaled on the spot over the RPC and respawned the moment it is free. Off keeps such
        /// objects on the RPC path for good.
        /// </summary>
        public static bool NativeSyncWhileHeld { get; set; } = true;

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
        /// Replace the active session's options without re-dispatching scale.
        /// Pattern: read CurrentOptions, mutate fields, pass back. Useful when a config slider
        /// for RestoreSpeed/SuppressImpactFlash/SuppressCameraShake moves while a session is live
        /// and the change should apply on the upcoming restore.
        /// Fields consumed once at dispatch (Factor, MassCap, BonkImmuneDuration) don't reapply
        /// retroactively, they take effect on the next Apply.
        /// Returns false on missing controller, no active session, or RejectExternalApply lock.
        /// </summary>
        public static bool UpdateOptions(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null || !ctrl.IsScaled) return false;
            if (IsLockedFromExternal(ctrl)) return false;
            ctrl._options = options;
            return true;
        }

        /// <summary><see cref="UpdateOptions"/> without the lock check.</summary>
        public static bool ForceUpdateOptions(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null || !ctrl.IsScaled) return false;
            ctrl._options = options;
            return true;
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
