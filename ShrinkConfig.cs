using BepInEx.Configuration;

namespace ScalerCore
{
    static class ShrinkConfig
    {
        static ConfigEntry<float> _factor                   = null!;
        static ConfigEntry<float> _speed                    = null!;
        static ConfigEntry<float> _enemyDamageMult          = null!;
        static ConfigEntry<float> _enemyShrinkDuration      = null!;
        static ConfigEntry<float> _valuableShrinkDuration   = null!;
        static ConfigEntry<float> _itemShrinkDuration       = null!;
        static ConfigEntry<float> _enemyBonkImmuneDuration  = null!;
        static ConfigEntry<float> _valuableBonkImmuneDuration = null!;
        static ConfigEntry<float> _shrunkMassCap = null!;
        static ConfigEntry<float> _enemyShrinkSpeedFactor = null!;
        static ConfigEntry<float> _playerShrinkDuration    = null!;
        static ConfigEntry<float> _shrunkAnimSpeedMult     = null!;
        static ConfigEntry<float> _shrunkFootstepPitchMult = null!;

        public static float Factor                    => _factor.Value;
        public static float Speed                     => _speed.Value;
        public static float EnemyDamageMult           => _enemyDamageMult.Value;
        public static float EnemyShrinkDuration       => _enemyShrinkDuration.Value;
        public static float ValuableShrinkDuration    => _valuableShrinkDuration.Value;
        public static float ItemShrinkDuration        => _itemShrinkDuration.Value;
        public static float PlayerShrinkDuration      => _playerShrinkDuration.Value;
        public static float EnemyBonkImmuneDuration   => _enemyBonkImmuneDuration.Value;
        public static float ValuableBonkImmuneDuration => _valuableBonkImmuneDuration.Value;
        public static float ShrunkMassCap             => _shrunkMassCap.Value;
        public static float EnemyShrinkSpeedFactor    => _enemyShrinkSpeedFactor.Value;
        public static float ShrunkAnimSpeedMult       => _shrunkAnimSpeedMult.Value;
        public static float ShrunkFootstepPitchMult   => _shrunkFootstepPitchMult.Value;

        public static void Init(ConfigFile cfg)
        {
            _factor = cfg.Bind("Shrink", "ShrinkFactor", 0.4f,
                "Scale multiplier applied when shrunk (0.4 = 40% of original).");
            _speed  = cfg.Bind("Shrink", "ShrinkSpeed",  2.0f,
                "Animation speed. Scaled by the object's original size so large items don't animate slower.");
            _enemyDamageMult = cfg.Bind("Shrink", "EnemyDamageMultiplier", 0.1f,
                "Damage multiplier for shrunken enemies (0.1 = 10% of normal damage).");
            _enemyShrinkDuration = cfg.Bind("Shrink", "EnemyShrinkDuration", 120f,
                "Seconds until a shrunken enemy auto-restores. 0 = never.");
            _valuableShrinkDuration = cfg.Bind("Shrink", "ValuableShrinkDuration", 0f,
                "Seconds until a shrunken valuable auto-restores. 0 = never (permanent until sold/end of round).");
            _itemShrinkDuration = cfg.Bind("Shrink", "ItemShrinkDuration", 300f,
                "Seconds until a shrunken item (weapon/grenade/etc) auto-restores. 0 = never.");
            _enemyBonkImmuneDuration = cfg.Bind("Shrink", "EnemyBonkImmuneDuration", 5f,
                "Seconds after shrinking that an enemy cannot be bonk-restored by taking damage.");
            _valuableBonkImmuneDuration = cfg.Bind("Shrink", "ValuableBonkImmuneDuration", 5f,
                "Seconds after shrinking that a valuable cannot be bonk-restored by impact.");
            _enemyShrinkSpeedFactor = cfg.Bind("Shrink", "EnemyShrinkSpeedFactor", 0.65f,
                "Speed multiplier for shrunken enemies, independent of visual size. " +
                "1.0 = full speed, Factor = same as size scale. Default 0.65 keeps enemies " +
                "noticeably slower but not sluggish (vs Factor=0.4 which felt too slow).");
            _playerShrinkDuration = cfg.Bind("Shrink", "PlayerShrinkDuration", 0f,
                "Seconds until a shrunken player auto-restores. 0 = never (must unshrink via damage or F10).");
            _shrunkMassCap = cfg.Bind("Shrink", "ShrunkMassCap", 5.0f,
                "Maximum rb.mass while shrunken. Mass is calculated as originalMass * Factor, " +
                "clamped between 0.5 (grab spring oscillation floor) and this cap. " +
                "OverrideMinGrabStrength handles grabbability regardless of mass.");
            _shrunkAnimSpeedMult = cfg.Bind("Shrink", "ShrunkAnimSpeedMult", 1.5f,
                "Animation speed multiplier for shrunken players. Makes footsteps faster. " +
                "1.0 = normal speed, 1.5 = 50% faster.");
            _shrunkFootstepPitchMult = cfg.Bind("Shrink", "ShrunkFootstepPitchMult", 1.5f,
                "Pitch multiplier for shrunken player footstep sounds. " +
                "1.0 = normal pitch, 1.5 = 50% higher pitch.");
        }
    }
}
