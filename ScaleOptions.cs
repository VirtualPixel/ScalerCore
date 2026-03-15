namespace ScalerCore
{
    /// <summary>
    /// Groups per-object-type scaling settings into a single value type.
    /// Static property presets read live from ShrinkConfig so they always reflect current BepInEx values.
    /// </summary>
    public struct ScaleOptions
    {
        /// <summary>Seconds until auto-restore. 0 = never.</summary>
        public float Duration;

        /// <summary>Scale animation speed (scaled by original size magnitude).</summary>
        public float AnimationSpeed;

        /// <summary>Minimum seconds of bonk immunity after shrinking.</summary>
        public float BonkImmunity;

        /// <summary>Maximum rigidbody mass while shrunken. Clamped between 0.5 and this value.</summary>
        public float MassCap;

        /// <summary>Enemy NavMesh speed multiplier (independent of visual size).</summary>
        public float SpeedFactor;

        /// <summary>Player animation speed multiplier while shrunken.</summary>
        public float AnimSpeedMultiplier;

        /// <summary>Player footstep sound pitch multiplier while shrunken.</summary>
        public float FootstepPitchMultiplier;

        /// <summary>Damage multiplier for attacks from scaled entities.</summary>
        public float DamageMultiplier;

        /// <summary>
        /// Sensible defaults — all fields populated from ShrinkConfig.
        /// Most callers should use a specific preset (Enemy, Player, etc.) instead.
        /// </summary>
        public static ScaleOptions Default => new()
        {
            Duration              = 0f,
            AnimationSpeed        = ShrinkConfig.Speed,
            BonkImmunity          = 0f,
            MassCap               = ShrinkConfig.ShrunkMassCap,
            SpeedFactor           = 1f,
            AnimSpeedMultiplier   = 1f,
            FootstepPitchMultiplier = 1f,
            DamageMultiplier      = 1f,
        };

        /// <summary>Preset for enemies — duration, bonk immunity, speed, and damage scaling.</summary>
        public static ScaleOptions Enemy => new()
        {
            Duration              = ShrinkConfig.EnemyShrinkDuration,
            AnimationSpeed        = ShrinkConfig.Speed,
            BonkImmunity          = ShrinkConfig.EnemyBonkImmuneDuration,
            MassCap               = ShrinkConfig.ShrunkMassCap,
            SpeedFactor           = ShrinkConfig.EnemyShrinkSpeedFactor,
            AnimSpeedMultiplier   = 1f,
            FootstepPitchMultiplier = 1f,
            DamageMultiplier      = ShrinkConfig.EnemyDamageMult,
        };

        /// <summary>Preset for players — animation speed, footstep pitch, no auto-restore.</summary>
        public static ScaleOptions Player => new()
        {
            Duration              = ShrinkConfig.PlayerShrinkDuration,
            AnimationSpeed        = ShrinkConfig.Speed,
            BonkImmunity          = 0f,
            MassCap               = ShrinkConfig.ShrunkMassCap,
            SpeedFactor           = 1f,
            AnimSpeedMultiplier   = ShrinkConfig.ShrunkAnimSpeedMult,
            FootstepPitchMultiplier = ShrinkConfig.ShrunkFootstepPitchMult,
            DamageMultiplier      = 1f,
        };

        /// <summary>Preset for valuables — bonk immunity via value-drop detection, no speed scaling.</summary>
        public static ScaleOptions Valuable => new()
        {
            Duration              = ShrinkConfig.ValuableShrinkDuration,
            AnimationSpeed        = ShrinkConfig.Speed,
            BonkImmunity          = ShrinkConfig.ValuableBonkImmuneDuration,
            MassCap               = ShrinkConfig.ShrunkMassCap,
            SpeedFactor           = 1f,
            AnimSpeedMultiplier   = 1f,
            FootstepPitchMultiplier = 1f,
            DamageMultiplier      = 1f,
        };

        /// <summary>Preset for items (weapons, grenades, etc.) — long timer, no bonk restore.</summary>
        public static ScaleOptions Item => new()
        {
            Duration              = ShrinkConfig.ItemShrinkDuration,
            AnimationSpeed        = ShrinkConfig.Speed,
            BonkImmunity          = 0f,
            MassCap               = ShrinkConfig.ShrunkMassCap,
            SpeedFactor           = 1f,
            AnimSpeedMultiplier   = 1f,
            FootstepPitchMultiplier = 1f,
            DamageMultiplier      = 1f,
        };
    }
}
