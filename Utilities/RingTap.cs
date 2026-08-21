namespace ScalerCore.Utilities
{
    /// <summary>
    /// Index math for the voice shifter's circular read taps.
    ///
    /// Split out and kept free of Unity types because it runs on the audio thread, per sample,
    /// per channel, and the wrap case is not obvious: a delay a few millionths of a frame off a
    /// write head at zero gives -0.0000307, and adding the window back rounds that straight up
    /// to the window itself in float32. Reading at that index is one past the end of the buffer.
    /// </summary>
    internal static class RingTap
    {
        /// <summary>
        /// Locate the pair of samples `delay` frames behind `write` and how far between them the
        /// read sits. Both indices are always inside [0, window).
        /// </summary>
        internal static void Locate(int write, float delay, int window, out int older, out int newer, out float blend)
        {
            float pos = write - delay;
            if (pos < 0f) pos += window;

            older = (int)pos;
            if (older >= window) { older = 0; pos = 0f; }
            else if (older < 0) { older = 0; pos = 0f; }

            blend = pos - older;
            newer = older + 1 == window ? 0 : older + 1;
        }
    }
}
