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
    /// The ScaleController stores it for the duration of the shrink session.
    /// Use ScaleOptions.Default and override fields as needed.
    /// </summary>
    public struct ScaleOptions
    {
        /// <summary>Scale multiplier (0.4 = 40% of original).</summary>
        public float Factor;

        /// <summary>Seconds until auto-restore. 0 = permanent until toggled/bonked.</summary>
        public float Duration;

        /// <summary>Scale animation speed (scaled by original size magnitude).</summary>
        public float Speed;

        /// <summary>Minimum seconds of bonk immunity after scaling.</summary>
        public float BonkImmuneDuration;

        /// <summary>Maximum rigidbody mass while scaled. Clamped between 0.5 and this value.</summary>
        public float MassCap;

        /// <summary>Enemy NavMesh speed multiplier while scaled.</summary>
        public float SpeedFactor;

        /// <summary>Damage multiplier for attacks from scaled entities.</summary>
        public float DamageMultiplier;

        /// <summary>Player animation speed multiplier while scaled.</summary>
        public float AnimSpeedMultiplier;

        /// <summary>Player footstep sound pitch multiplier while scaled.</summary>
        public float FootstepPitchMultiplier;

        /// <summary>Which object types this scaling applies to.</summary>
        public ScaleTargets AllowedTargets;

        /// <summary>
        /// If true, shrunken is the default state.
        /// Bonk/damage temporarily grows back; timer re-shrinks.
        /// </summary>
        public bool InvertedMode;

        /// <summary>
        /// If true, valuables won't expand when they take damage while scaled.
        /// Useful for cart mods where items bump into each other constantly.
        /// Default: false (value-drop detection triggers bonk expand).
        /// </summary>
        public bool SuppressValueDropExpand;

        /// <summary>
        /// If true, rigidbody mass stays at its original value while scaled.
        /// Useful for cart mods where items should weigh the same regardless of visual size.
        /// Default: false (mass scales with factor, clamped by MassCap).
        /// </summary>
        public bool PreserveMass;

        /// <summary>
        /// Default options matching current ShrinkerGun behavior.
        /// Use this as a starting point and override fields as needed.
        /// </summary>
        public static ScaleOptions Default => new()
        {
            Factor                = 0.4f,
            Duration              = 0f,
            Speed                 = 2.0f,
            BonkImmuneDuration    = 5.0f,
            MassCap               = 5.0f,
            SpeedFactor           = 0.65f,
            DamageMultiplier      = 0.1f,
            AnimSpeedMultiplier   = 1.5f,
            FootstepPitchMultiplier = 1.5f,
            AllowedTargets            = ScaleTargets.All,
            InvertedMode              = false,
            SuppressValueDropExpand   = false,
            PreserveMass              = false,
        };
    }
}
