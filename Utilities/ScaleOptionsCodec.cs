namespace ScalerCore.Utilities
{
    // The wire shape of a session's options: the same two arrays the shrink RPC has carried
    // since 1.0, so an old host can still drive a new client and the other way round. Also what
    // rides along in a natively scaled respawn's instantiation data.
    internal static class ScaleOptionsCodec
    {
        public static float[] PackFloats(in ScaleOptions o) => new[]
        {
            o.Factor, o.Speed, o.MassCap,
            o.SpeedFactor, o.AnimSpeedMultiplier,
            o.FootstepPitchMultiplier, o.BonkImmuneDuration,
            o.RestoreSpeed, o.AudioPresence,
            o.EnemyPhysicalFactorCap,
            o.EnemyWidthFactorCap, o.EnemyNavRadiusFactorCap,
            o.EnemyHeightFactorCap
        };

        public static bool[] PackBools(in ScaleOptions o) => new[]
        {
            o.PreserveMass, o.InvertedMode, o.SuppressImpactFlash,
            o.SuppressVoicePitch, o.IgnoreBonkExpand, o.RejectExternalApply,
            o.SuppressCameraShake
        };

        // Slots past the first seven were added over time and are length-guarded, so a shorter
        // array from an older sender still reads. Fields the wire never carried keep the value
        // already in `into` (AllowedTargets, Duration, SuppressValueDropExpand).
        public static ScaleOptions Unpack(float[] opts, bool[] flags, ScaleOptions into)
        {
            ScaleOptions o = into;
            o.Factor                   = opts.Length > 0 ? opts[0] : o.Factor;
            o.Speed                    = opts.Length > 1 ? opts[1] : o.Speed;
            o.MassCap                  = opts.Length > 2 ? opts[2] : o.MassCap;
            o.SpeedFactor              = opts.Length > 3 ? opts[3] : o.SpeedFactor;
            o.AnimSpeedMultiplier      = opts.Length > 4 ? opts[4] : o.AnimSpeedMultiplier;
            o.FootstepPitchMultiplier  = opts.Length > 5 ? opts[5] : o.FootstepPitchMultiplier;
            o.BonkImmuneDuration       = opts.Length > 6 ? opts[6] : o.BonkImmuneDuration;
            o.RestoreSpeed             = opts.Length > 7 ? opts[7] : 0f;
            o.AudioPresence            = opts.Length > 8 ? opts[8] : 1f;
            o.EnemyPhysicalFactorCap   = opts.Length > 9 ? opts[9] : 0f;
            o.EnemyWidthFactorCap      = opts.Length > 10 ? opts[10] : 0f;
            o.EnemyNavRadiusFactorCap  = opts.Length > 11 ? opts[11] : 0f;
            o.EnemyHeightFactorCap     = opts.Length > 12 ? opts[12] : 0f;
            o.PreserveMass             = flags.Length > 0 && flags[0];
            o.InvertedMode             = flags.Length > 1 && flags[1];
            o.SuppressImpactFlash      = flags.Length > 2 && flags[2];
            o.SuppressVoicePitch       = flags.Length > 3 && flags[3];
            o.IgnoreBonkExpand         = flags.Length > 4 && flags[4];
            o.RejectExternalApply      = flags.Length > 5 && flags[5];
            o.SuppressCameraShake      = flags.Length > 6 && flags[6];
            return o;
        }
    }
}
