using System;

namespace ScalerCore.Utilities
{
    // Warning-light blink for the map collapse: the period slides from 4 s down to 1.5 s
    // across the collapse. Pure arithmetic, no Unity types, so the tests can walk it.
    //
    // The phase has to come from the collapse's own elapsed time, not from a clock reading
    // taken modulo the period. With the period changing every frame, clock % period jumps
    // by floor(clock / period) times the change in period, so on a clock in the millions
    // (Photon's server time) or the thousands (Time.time half an hour in) the lights flipped
    // at frame rate and the siren restarted on every false edge.
    internal static class CollapseBlink
    {
        public const double PeriodStart = 4.0;
        public const double PeriodEnd = 1.5;
        public const double OnFraction = 0.4;

        public static double Period(double t, double duration)
        {
            double p = duration > 0 ? Math.Max(0.0, Math.Min(1.0, t / duration)) : 1.0;
            return PeriodStart + (PeriodEnd - PeriodStart) * p;
        }

        // Cycles completed at collapse time t: the integral of 1 / period(t), which for a
        // period linear in t is ln(period(t) / period(0)) / slope. Same t on every client
        // gives the same phase, which is the whole sync story.
        public static double Phase(double t, double duration)
        {
            if (duration <= 0) return 0;
            t = Math.Max(0.0, Math.Min(duration, t));
            double slope = (PeriodEnd - PeriodStart) / duration;
            if (Math.Abs(slope) < 1e-9) return t / PeriodStart;
            return Math.Log((PeriodStart + slope * t) / PeriodStart) / slope;
        }

        // t: seconds since the collapse's shrink phase began.
        public static bool IsOn(double t, double duration)
        {
            double phase = Phase(t, duration);
            return (phase - Math.Floor(phase)) < OnFraction;
        }
    }
}
