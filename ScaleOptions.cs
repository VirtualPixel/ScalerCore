using System;

namespace ScalerCore
{
    [Flags]
    public enum ScaleTargets
    {
        None      = 0,
        Players   = 1 << 0,
        Enemies   = 1 << 1,
        Items     = 1 << 2,
        Valuables = 1 << 3,
        All       = Players | Enemies | Items | Valuables
    }

    /// <summary>
    /// Per-call scaling options. Every ScaleManager.Apply() call passes one of these.
    /// The ScaleController stores it for the duration of the scale session.
    /// Use ScaleOptions.Default and override fields as needed.
    /// </summary>
    public struct ScaleOptions
    {
        /// <summary>Scale multiplier. Values below 1 shrink, above 1 enlarge. (0.4 = 40%, 2.0 = 200%)</summary>
        public float Factor;

        /// <summary>Seconds until auto-restore. 0 = permanent until toggled or bonked.</summary>
        public float Duration;

        /// <summary>Scale animation speed (scaled by original size magnitude).</summary>
        public float Speed;

        /// <summary>Animation speed for the expand direction. 0 falls back to <see cref="Speed"/>.</summary>
        public float RestoreSpeed;

        /// <summary>Minimum seconds of bonk immunity after scaling.</summary>
        public float BonkImmuneDuration;

        /// <summary>Maximum rigidbody mass while scaled. Set higher for growth mods.</summary>
        public float MassCap;

        /// <summary>Enemy NavMesh speed multiplier while scaled.</summary>
        public float SpeedFactor;

        /// <summary>Player animation speed multiplier while scaled.</summary>
        public float AnimSpeedMultiplier;

        /// <summary>Player footstep sound pitch multiplier while scaled.</summary>
        public float FootstepPitchMultiplier;

        /// <summary>Which object types this scaling applies to.</summary>
        public ScaleTargets AllowedTargets;

        /// <summary>
        /// If true, the scaled state is the default.
        /// Bonk/damage temporarily restores; timer re-scales.
        /// </summary>
        public bool InvertedMode;

        /// <summary>
        /// If true, valuables won't restore when they take damage while scaled.
        /// Useful for cart mods where items bump into each other constantly.
        /// </summary>
        public bool SuppressValueDropExpand;

        /// <summary>
        /// If true, rigidbody mass stays at its original value while scaled.
        /// Useful for cart mods where items should weigh the same regardless of visual size.
        /// </summary>
        public bool PreserveMass;

        /// <summary>If true, skips the impact flash on shrink/expand.</summary>
        public bool SuppressImpactFlash;

        /// <summary>If true, skips the camera shake on expand. Pair with <see cref="SuppressImpactFlash"/>.</summary>
        public bool SuppressCameraShake;

        /// <summary>If true, no audio pitch shift on the controller's sounds or the player's voice chat.</summary>
        public bool SuppressVoicePitch;

        /// <summary>If true, damage does not restore the controller. Covers every bonk path.</summary>
        public bool IgnoreBonkExpand;

        /// <summary>
        /// If true, external <see cref="ScaleManager.Apply"/> / <see cref="ScaleManager.Restore"/> /
        /// <see cref="ScaleManager.RestoreImmediate"/> no-op on this controller. The owning mod uses
        /// <see cref="ScaleManager.ForceApply"/> / <see cref="ScaleManager.ForceRestore"/> to bypass.
        /// </summary>
        public bool RejectExternalApply;

        /// <summary>
        /// Sensible defaults for a shrink ray. Override fields as needed.
        /// For growth mods, set Factor > 1 and increase MassCap.
        /// </summary>
        public static ScaleOptions Default => new()
        {
            Factor                    = 0.4f,
            Duration                  = 0f,
            Speed                     = 2.0f,
            RestoreSpeed              = 0f,
            BonkImmuneDuration        = 5.0f,
            MassCap                   = 50f,
            SpeedFactor               = 0.75f,
            AnimSpeedMultiplier       = 1.5f,
            FootstepPitchMultiplier   = 1.5f,
            AllowedTargets            = ScaleTargets.All,
            InvertedMode              = false,
            SuppressValueDropExpand   = false,
            PreserveMass              = false,
            SuppressImpactFlash       = false,
            SuppressCameraShake       = false,
            SuppressVoicePitch        = false,
            IgnoreBonkExpand          = false,
            RejectExternalApply       = false,
        };
    }
}
