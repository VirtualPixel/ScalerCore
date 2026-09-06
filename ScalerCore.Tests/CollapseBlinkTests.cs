using System;
using System.Collections.Generic;
using ScalerCore.Utilities;
using Xunit;

namespace ScalerCore.Tests
{
    // The collapse warning lights are meant to blink somewhere between every 4 seconds and
    // every 1.5 seconds. Anything faster is a strobe on every light in the level for a minute
    // and a half, and the siren restarts on every rising edge, so a blink that flips once a
    // frame is both a photosensitivity hazard and a wall of noise. These walk the whole
    // collapse at 60 fps the way the coroutine does and check the flips are spaced the way
    // the period says they should be.
    public class CollapseBlinkTests
    {
        const double Duration = 90.0;
        const double Frame = 1.0 / 60.0;

        // The clock argument stays so the walk mirrors the coroutine, which has a shared clock
        // reading in hand every frame; the blink must not depend on it.
        static List<double> Transitions(double clockAtStart)
        {
            var flips = new List<double>();
            bool? last = null;
            for (double t = 0; t <= Duration; t += Frame)
            {
                bool on = CollapseBlink.IsOn(t, Duration);
                if (last.HasValue && on != last.Value) flips.Add(t);
                last = on;
            }
            return flips;
        }

        // A real Photon room clock: server time is in the millions of seconds.
        [Theory]
        [InlineData(3_000_000.25)]
        // Time.time after half an hour in the game, the singleplayer clock.
        [InlineData(1800.0)]
        [InlineData(0.0)]
        public void NoFlipLandsCloserThanTheShortestWindow(double clockAtStart)
        {
            var flips = Transitions(clockAtStart);
            // The shortest window the period allows is the 0.4 on-fraction of 1.5 s.
            double shortest = CollapseBlink.PeriodEnd * CollapseBlink.OnFraction;
            for (int i = 1; i < flips.Count; i++)
                Assert.True(flips[i] - flips[i - 1] >= shortest - Frame,
                    $"flip at {flips[i]:F3}s came {flips[i] - flips[i - 1]:F3}s after the previous one");
        }

        [Theory]
        [InlineData(3_000_000.25)]
        [InlineData(1800.0)]
        public void FlipCountMatchesTheCycleCount(double clockAtStart)
        {
            // Integrating 1/period over the collapse gives the number of cycles the sliding
            // period allows, two flips each.
            double cycles = 0;
            for (double t = 0; t < Duration; t += Frame) cycles += Frame / CollapseBlink.Period(t, Duration);
            int flips = Transitions(clockAtStart).Count;
            Assert.InRange(flips, (int)(2 * cycles) - 2, (int)(2 * cycles) + 2);
        }

        [Fact]
        public void TheOnWindowIsTheOnFractionOfThePeriodAtTheStart()
        {
            // First cycle: 4 s period, so on for 1.6 s then off for 2.4 s.
            var flips = Transitions(0.0);
            Assert.True(flips.Count >= 2);
            Assert.InRange(flips[0], 1.6 - 0.05, 1.6 + 0.05);
            Assert.InRange(flips[1] - flips[0], 2.4 - 0.1, 2.4 + 0.1);
        }
    }
}
