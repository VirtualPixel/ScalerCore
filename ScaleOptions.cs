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

        /// <summary>
        /// How much scaled things sound their size beyond pitch, 0 to 1. Drives the
        /// volume lift, the reverb lean for the echo, the sound falloff scaling
        /// (giants carry, tiny things go quiet at range), and the same treatment on
        /// player voice chat. 0 = pitch only, 1 = the full effect.
        /// </summary>
        public float AudioPresence;

        /// <summary>
        /// Grow-only, enemies only: the body stops growing PHYSICALLY (colliders,
        /// nav agent radius) at this factor while the visuals keep climbing to
        /// <see cref="Factor"/>, so a giant still fits through the doorways the
        /// navmesh was baked for. Reach and attacks track the visual size.
        /// 0 disables the cap. Ignored for shrinking and for non-enemies.
        /// </summary>
        public float EnemyPhysicalFactorCap;

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
        /// Defaults for a growth mod: twice the size, heavier, a touch faster,
        /// slower deliberate animation, low footsteps. Voice and entity sounds
        /// deepen automatically from Factor; these are the knobs that don't.
        /// Override fields as needed, same as Default.
        /// </summary>
        public static ScaleOptions Growth => new()
        {
            Factor                    = 2.0f,
            Duration                  = 0f,
            Speed                     = 2.0f,
            RestoreSpeed              = 0f,
            BonkImmuneDuration        = 5.0f,
            MassCap                   = 500f,
            SpeedFactor               = 1.25f,
            AnimSpeedMultiplier       = 0.75f,
            FootstepPitchMultiplier   = 0.55f,
            AudioPresence             = 1f,
            EnemyPhysicalFactorCap    = 1.4f,
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

        /// <summary>
        /// Sensible defaults for a shrink ray. Override fields as needed.
        /// For growth mods, start from <see cref="Growth"/> instead.
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
            AudioPresence             = 1f,
            EnemyPhysicalFactorCap    = 0f,
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
