namespace ScalerCore
{
    /// <summary>
    /// Default values for scaling behavior. These are not user-configurable —
    /// ScalerCore is a library. Consuming mods (like ShrinkerGun) can expose
    /// whichever settings make sense for their use case.
    /// </summary>
    public static class ShrinkConfig
    {
        public static float Factor                    = 0.4f;
        public static float MinimumStrength           = 0.6f;
        public static float MaximumStrength           = 2.0f;
        public static float Speed                     = 2.0f;
        public static float EnemyDamageMult           = 0.1f;
        public static float EnemyShrinkDuration       = 120f;
        public static float ValuableShrinkDuration    = 0f;
        public static float ItemShrinkDuration        = 0f;
        public static float PlayerShrinkDuration      = 0f;
        public static float EnemyBonkImmuneDuration   = 5f;
        public static float ValuableBonkImmuneDuration = 5f;
        public static float ShrunkMassCap             = 5.0f;
        public static float EnemyShrinkSpeedFactor    = 0.65f;
        public static float ShrunkAnimSpeedMult       = 1.5f;
        public static float ShrunkFootstepPitchMult   = 1.5f;
    }
}
