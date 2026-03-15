using UnityEngine;

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
        /// Scale an object. Gets existing ScaleController or returns if none attached.
        /// Factor parameter reserved for future use — currently uses ShrinkConfig.Factor.
        /// </summary>
        public static void Apply(GameObject target, float factor = 0)
        {
            var ctrl = target.GetComponent<ScaleController>();
            if (ctrl == null) return;
            ctrl.DispatchShrink();
        }

        /// <summary>
        /// Restore with animation (timer expiry, gun toggle).
        /// </summary>
        public static void Restore(GameObject target)
        {
            var ctrl = target.GetComponent<ScaleController>();
            if (ctrl == null) return;
            ctrl.DispatchExpand();
        }

        /// <summary>
        /// Restore immediately without animation (bonk/damage).
        /// </summary>
        public static void RestoreImmediate(GameObject target)
        {
            var ctrl = target.GetComponent<ScaleController>();
            if (ctrl == null) return;
            ctrl.DispatchExpandNow();
        }

        /// <summary>
        /// Check if an object is currently scaled.
        /// </summary>
        public static bool IsScaled(GameObject target)
        {
            var ctrl = target.GetComponent<ScaleController>();
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
