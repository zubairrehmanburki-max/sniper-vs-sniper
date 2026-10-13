/*
 * The PvE sniper. One brain, three noise profiles: each turn it asks the real solver for the true
 * firing solution and perturbs its INPUTS (range, wind read, fire timing). The bullet flies honestly.
 */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;
using Environment = SniperDuel.Ballistics.Environment;

namespace SniperDuel.Rules
{
    public sealed class BotAction
    {
        /// <summary>Seconds after the phase starts.</summary>
        public double At;
        public MatchInput Input;

        public BotAction(double at, MatchInput input)
        {
            At = at;
            Input = input;
        }
    }

    public static class Bot
    {
        static Rng RngFor(MatchState state, Side side, int salt) =>
            new Rng(Rng.HashSeed(state.Seed, 0x626f74, state.Round, state.Pair, state.Step, side == Side.A ? 1 : 2, salt));

        /// <summary>Where the bot goes in a setup or move phase.</summary>
        public static List<BotAction> Move(MatchState state, Side side, GameConfig cfg)
        {
            var me = state.Players[side];
            var profile = cfg.Bot[me.Bot ?? BotDifficulty.Medium];
            var map = Maps.ById(state.MapId);
            var rng = RngFor(state, side, 1);
            var spots = map.Spots[side];
            var pool = profile.Positions > 0 ? spots.GetRange(0, Math.Min(profile.Positions, spots.Count)) : spots;
            Vec3 target;
            if (pool.Count == 1)
            {
                target = pool[0]; // a fixed-position map: nowhere else to go
            }
            else if (profile.Positions > 0)
            {
                // Predictable: walks a fixed rotation.
                target = pool[(state.Round + state.Pair) % pool.Count];
            }
            else
            {
                int current = pool.FindIndex(p => Geometry.HorizontalDistance(p, me.Position) < 1);
                bool mustMove = profile.RelocateAfterFiring && me.Revealed != null;
                // Punish-aware: avoid staying where it was revealed, and otherwise relocate now and then.
                if (current >= 0 && !mustMove && rng.Next() < 0.4) target = pool[current];
                else
                {
                    var choices = new List<Vec3>();
                    for (int i = 0; i < pool.Count; i++)
                        if (i != current) choices.Add(pool[i]);
                    target = choices[(int)Math.Floor(rng.Next() * choices.Count)];
                }
            }
            // Lowest stance from which it can see out over its cover toward the enemy zone.
            var ez = map.Zones[Match.Other(side)];
            double lx = (ez.XMin + ez.XMax) / 2;
            double lz = -(ez.SMin + ez.SMax) / 2;
            var look = new Vec3(lx, map.Ground(lx, lz) + 0.6, lz);
            Stance? sees = null;
            foreach (var st in new[] { Stance.Prone, Stance.Crouch, Stance.Standing })
            {
                if (Visibility.LineOfSight(Combat.EyeOf(target, st, cfg), look, map.Ground, map.Cover))
                {
                    sees = st;
                    break;
                }
            }
            var stance = Match.ClampStance(state, sees ?? Stance.Prone, cfg);
            double at = state.Phase == PhaseKind.Setup ? 1 + rng.Next() * 3 : 3 + rng.Next() * 8;
            return new List<BotAction>
            {
                new BotAction(at, MatchInput.PlaceAt(side, target, stance, 0)),
                new BotAction(at + 1 + rng.Next() * 4, MatchInput.ReadyUp(side, 0)),
            };
        }

        /// <summary>Does the bot see its target this phase? Line of sight plus a stance-based spotting roll.</summary>
        public static bool Spots(MatchState state, Side side, GameConfig cfg)
        {
            var me = state.Players[side];
            var enemy = state.Players[Match.Other(side)];
            var map = Maps.ById(state.MapId);
            var eye = Combat.EyeOf(me.Position, me.Stance, cfg);
            var chest = Combat.ChestOf(Combat.SilhouetteFor("t", enemy.Position, enemy.Stance, Combat.FacingYaw(enemy.Side), cfg));
            var head = Combat.EyeOf(enemy.Position, enemy.Stance, cfg);
            if (!Visibility.LineOfSight(eye, chest, map.Ground, map.Cover) && !Visibility.LineOfSight(eye, head, map.Ground, map.Cover)) return false;
            if (enemy.Revealed != null) return true; // it saw the flash and dust
            var profile = cfg.Bot[me.Bot ?? BotDifficulty.Medium];
            return RngFor(state, side, 2).Next() < profile.SpotChance[enemy.Stance];
        }

        /// <summary>The bot's shot phase: aim, solve with noisy inputs, fire on its sway timing (or hold fire).</summary>
        public static List<BotAction> Shot(MatchState state, Side side, GameConfig cfg)
        {
            var me = state.Players[side];
            var difficulty = me.Bot ?? BotDifficulty.Medium;
            var profile = cfg.Bot[difficulty];
            var rng = RngFor(state, side, 3);
            double tLo = cfg.Bot.ThinkSeconds[0], tHi = cfg.Bot.ThinkSeconds[1];
            double think = Math.Min(cfg.Rules.ShotSeconds - 1, tLo + (tHi - tLo) * rng.Next());
            if (!Spots(state, side, cfg)) return new List<BotAction> { new BotAction(4 + rng.Next() * 6, MatchInput.ReadyUp(side, 0)) };

            var enemy = state.Players[Match.Other(side)];
            var eye = Combat.EyeOf(me.Position, me.Stance, cfg);
            var chest = Combat.ChestOf(Combat.SilhouetteFor("t", enemy.Position, enemy.Stance, Combat.FacingYaw(enemy.Side), cfg));
            double dx = chest.X - eye.X;
            double dz = chest.Z - eye.Z;
            double horiz = JsMath.Hypot(dx, dz);
            double yaw = Math.Atan2(dx, -dz);
            double pitch = Math.Atan2(chest.Y - eye.Y, horiz);
            double trueRange = JsMath.Hypot(horiz, chest.Y - eye.Y);
            double rangeEstimate = trueRange * (1 + (rng.Next() * 2 - 1) * profile.RangeErrorFrac);

            // The bot's wind read: what it believes, not what is.
            var conditions = Match.ActiveConditions(state);
            double fireAt = state.PhaseStart + think;
            WindField wind;
            if (profile.WindMode == WindMode.Layered)
            {
                // Full layered read, but its gust timing is slightly off.
                var layered = World.ShotWindField(conditions.Wind, eye, yaw);
                var g = conditions.Wind.Gust;
                double mistimed = g != null ? WindField.GustFactor(g, fireAt + (rng.Next() * 2 - 1) * 3) : 1;
                wind = new WindField();
                foreach (var z in layered.Zones) wind.Zones.Add(new WindZone(z.UntilRange, new Vec3(z.Vector.X * mistimed, 0, z.Vector.Z * mistimed)));
            }
            else
            {
                var station = World.VectorToShot(World.LaneWindAt(conditions.Wind, me.Position), yaw);
                double scale = profile.WindMode == WindMode.HalfStation ? 0.5 : 1;
                double speed = JsMath.Hypot(station.X, station.Z) * scale;
                double from = (Math.Atan2(station.Z, station.X) * 180) / Math.PI;
                wind = WindField.Uniform(speed, from);
            }

            var rifle = cfg.RifleById(me.RifleId);
            var shot = RifleSetup.RifleShot(rifle, cfg);
            shot.LosElevation = pitch;
            double elevation;
            double windage;
            try
            {
                var sol = Solutions.SolveFiringSolution(shot, new Environment { Atmosphere = conditions.Atmosphere, Wind = wind }, rangeEstimate, new SolveOptions
                {
                    Effects = new Effects { SpinDrift = cfg.Ballistics.Effects.SpinDrift },
                });
                elevation = sol.Elevation * 1000;
                windage = sol.Windage * 1000;
            }
            catch (InvalidOperationException)
            {
                return new List<BotAction> { new BotAction(think, MatchInput.ReadyUp(side, 0)) };
            }
            double click = cfg.Scope.ClickMrad;
            // Fire timing on the sway curve: a scaled sample of its own sway at the moment it fires.
            var breath = Sway.HoldBreath(Sway.InitialBreath(), fireAt - 2, cfg);
            var sway = Sway.Offset(
                new SwayInput { Stance = me.Stance, Bipod = true, Breath = breath, Seed = Rng.HashSeed(state.Seed, 0x737779), Penalty = me.Wounds > 0 ? cfg.Player.Wound.SwayFactor : 1 },
                fireAt,
                cfg);
            var input = new FireInput
            {
                Yaw = yaw + (sway.Right * profile.SwayFireFactor) / 1000,
                Pitch = pitch + (sway.Up * profile.SwayFireFactor) / 1000,
                DialElevationMrad = JsMath.Round(elevation / click) * click,
                DialWindageMrad = JsMath.Round(windage / click) * click,
            };
            return new List<BotAction> { new BotAction(think, MatchInput.FireShot(side, input, 0)) };
        }

        /// <summary>All bot actions for the current phase, with absolute times filled in.</summary>
        public static List<MatchInput> Plan(MatchState state, Side side, GameConfig cfg)
        {
            var actions = new List<BotAction>();
            if (state.Phase == PhaseKind.Setup) actions = Move(state, side, cfg);
            else if (state.Phase == PhaseKind.Move && state.Actor == side) actions = Move(state, side, cfg);
            else if (state.Phase == PhaseKind.Shot && state.Actor == side) actions = Shot(state, side, cfg);
            var plan = new List<MatchInput>();
            foreach (var a in actions) plan.Add(a.Input.At(state.PhaseStart + a.At));
            return plan;
        }
    }
}
