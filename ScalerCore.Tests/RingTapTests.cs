using ScalerCore.Utilities;
using Xunit;

namespace ScalerCore.Tests
{
    // The voice shifter reads its taps on the audio thread, per sample, per channel. An index
    // one past the end there is not a glitch, it is an exception on a thread nobody is catching
    // on. The wrap is not obvious either: a delay a few millionths of a frame off a write head
    // at zero gives -0.0000307, and adding the window back rounds that up to the window itself
    // in float32, because the gap between representable floats at 3072 is about 0.00024.
    public class RingTapTests
    {
        const int Window = 3072;

        [Fact]
        public void TinyDelayAtTheWriteHeadStaysInsideTheBuffer()
        {
            RingTap.Locate(0, 1e-8f * Window, Window, out int older, out int newer, out float blend);

            Assert.InRange(older, 0, Window - 1);
            Assert.InRange(newer, 0, Window - 1);
            Assert.InRange(blend, 0f, 1f);
        }

        [Fact]
        public void EveryWriteHeadAndPhaseStaysInsideTheBuffer()
        {
            // Both taps, swept across the whole phase cycle at every write position the ring
            // takes, which is the space the shifter actually walks.
            for (int write = 0; write < Window; write++)
            {
                for (int step = 0; step <= 512; step++)
                {
                    float phase = step / 512f;
                    if (phase >= 1f) phase = 0.9999999f;

                    float second = phase + 0.5f;
                    if (second >= 1f) second -= 1f;

                    foreach (float delay in new[] { phase * Window, second * Window })
                    {
                        RingTap.Locate(write, delay, Window, out int older, out int newer, out float blend);
                        Assert.InRange(older, 0, Window - 1);
                        Assert.InRange(newer, 0, Window - 1);
                        Assert.InRange(blend, 0f, 1f);
                    }
                }
            }
        }

        [Fact]
        public void ZeroDelayReadsTheSampleJustWritten()
        {
            RingTap.Locate(1200, 0f, Window, out int older, out int newer, out float blend);

            Assert.Equal(1200, older);
            Assert.Equal(1201, newer);
            Assert.Equal(0f, blend);
        }

        [Fact]
        public void HalfAFrameBackReadsBetweenTheTwoNeighbours()
        {
            RingTap.Locate(1200, 0.5f, Window, out int older, out int newer, out float blend);

            Assert.Equal(1199, older);
            Assert.Equal(1200, newer);
            Assert.Equal(0.5f, blend, 5);
        }

        [Fact]
        public void ReadingPastTheHeadWrapsToTheOtherEnd()
        {
            RingTap.Locate(0, 1.5f, Window, out int older, out int newer, out float blend);

            Assert.Equal(Window - 2, older);
            Assert.Equal(Window - 1, newer);
            Assert.Equal(0.5f, blend, 5);
        }
    }
}
