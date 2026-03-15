namespace ScalerCore
{
    /// <summary>
    /// Thin wrapper around a float scale multiplier.
    /// Implicit conversions let callers pass a ScaleFactor anywhere a float is expected and vice versa.
    /// Named properties read live from ShrinkConfig so they always reflect current BepInEx values.
    /// </summary>
    public readonly struct ScaleFactor
    {
        public readonly float Value;

        public ScaleFactor(float value) => Value = value;

        public static implicit operator float(ScaleFactor f) => f.Value;
        public static implicit operator ScaleFactor(float v) => new(v);

        /// <summary>Current shrink multiplier (e.g. 0.4 = 40% of original size).</summary>
        public static ScaleFactor Shrink => ShrinkConfig.Factor;

        /// <summary>Growth multiplier — reserved for future grow-ray.</summary>
        public static ScaleFactor Grow => 2.0f;

        /// <summary>Identity / no scaling.</summary>
        public static ScaleFactor Normal => 1.0f;

        public override string ToString() => Value.ToString("F2");
    }
}
