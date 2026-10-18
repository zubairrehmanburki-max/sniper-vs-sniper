using System;

namespace SniperDuel.Rules
{
    /// <summary>
    /// JavaScript Math semantics the rules depend on, so the C# port reaches the same numbers as the
    /// TypeScript referee: Math.round rounds half up (not to even) and Math.hypot follows V8's
    /// max-scaled Kahan sum.
    /// </summary>
    public static class JsMath
    {
        /// <summary>JS Math.round: nearest integer, halves toward +∞.</summary>
        public static double Round(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            double f = Math.Floor(x);
            return x - f >= 0.5 ? f + 1 : f;
        }

        /// <summary>JS Math.hypot(a, b), bit-identical to V8.</summary>
        public static double Hypot(double a, double b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            if (double.IsInfinity(a) || double.IsInfinity(b)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            double max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, compensation = 0;
            Kahan(a / max, ref sum, ref compensation);
            Kahan(b / max, ref sum, ref compensation);
            return Math.Sqrt(sum) * max;
        }

        /// <summary>JS Math.hypot(a, b, c), bit-identical to V8.</summary>
        public static double Hypot(double a, double b, double c)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            c = Math.Abs(c);
            if (double.IsInfinity(a) || double.IsInfinity(b) || double.IsInfinity(c)) return double.PositiveInfinity;
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) return double.NaN;
            double max = Math.Max(a, Math.Max(b, c));
            if (max == 0) return 0;
            double sum = 0, compensation = 0;
            Kahan(a / max, ref sum, ref compensation);
            Kahan(b / max, ref sum, ref compensation);
            Kahan(c / max, ref sum, ref compensation);
            return Math.Sqrt(sum) * max;
        }

        static void Kahan(double n, ref double sum, ref double compensation)
        {
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
        }
    }
}
