/*
 * Aim wander. The crosshair drifts on a slow figure-8 whose size depends on stance, bipod, breath
 * and wounds. Firing is a timing read on this curve. Pure functions of time, so the bot and replays
 * can evaluate the same curve.
 */
using System;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public sealed class BreathState
    {
        public bool Holding;
        /// <summary>Match time the current hold (or recovery) started, s.</summary>
        public double Since;
        /// <summary>Amplitude factor at the moment the last hold was released (≥ 1 means strained).</summary>
        public double StrainAtRelease;

        public BreathState(bool holding, double since, double strainAtRelease)
        {
            Holding = holding;
            Since = since;
            StrainAtRelease = strainAtRelease;
        }
    }

    public sealed class SwayInput
    {
        public Stance Stance;
        public bool Bipod;
        public BreathState Breath = Sway.InitialBreath();
        /// <summary>Extra multiplier, e.g. 1.4 for a wounded player.</summary>
        public double Penalty = 1;
        public uint Seed;
    }

    public struct SwayOffset
    {
        public double Right;
        public double Up;
    }

    public static class Sway
    {
        public static BreathState InitialBreath() => new BreathState(false, -1e9, 1);

        /// <summary>Amplitude multiplier from breathing at time `now`.</summary>
        public static double BreathFactor(BreathState state, double now, GameConfig cfg)
        {
            var hb = cfg.Sway.HoldBreath;
            double dt = Math.Max(0, now - state.Since);
            if (state.Holding)
            {
                const double settle = 0.6;
                if (dt < settle) return 1 + (hb.SteadyFactor - 1) * (dt / settle);
                if (dt < hb.SteadySeconds) return hb.SteadyFactor;
                return Math.Min(hb.MaxStrainFactor, hb.SteadyFactor + hb.StrainGrowthPerSecond * (dt - hb.SteadySeconds));
            }
            double excess = Math.Max(0, state.StrainAtRelease - 1);
            return 1 + excess * Math.Exp(-dt / hb.RecoverSeconds);
        }

        public static BreathState HoldBreath(BreathState state, double now, GameConfig cfg)
        {
            if (state.Holding) return state;
            // Can't get a clean hold until you've recovered from a strained one.
            return new BreathState(true, now - Math.Max(0, BreathFactor(state, now, cfg) - 1) * 0.3, 1);
        }

        public static BreathState ReleaseBreath(BreathState state, double now, GameConfig cfg)
        {
            if (!state.Holding) return state;
            double f = BreathFactor(state, now, cfg);
            return new BreathState(false, now, Math.Max(1, f));
        }

        /// <summary>Aim offset at time `now`, in mrad (right, up).</summary>
        public static SwayOffset Offset(SwayInput input, double now, GameConfig cfg)
        {
            var s = cfg.Sway;
            double amp =
                s.AmplitudeMrad[input.Stance] *
                (input.Bipod && input.Stance == Stance.Prone ? s.BipodFactor : 1) *
                BreathFactor(input.Breath, now, cfg) *
                input.Penalty;
            double w = (2 * Math.PI) / s.PeriodSeconds;
            // Figure-8 plus a little seeded drift so it never feels mechanical.
            double right = amp * (Math.Sin(w * now) + 0.35 * Rng.ValueNoise1D(input.Seed, now * 0.4));
            double up = amp * (0.5 * Math.Sin(2 * w * now) + 0.3 * Rng.ValueNoise1D(unchecked(input.Seed + 1), now * 0.5));
            return new SwayOffset { Right = right, Up = up };
        }
    }
}
