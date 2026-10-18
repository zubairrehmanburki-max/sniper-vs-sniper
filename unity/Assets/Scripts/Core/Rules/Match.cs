/*
 * The match state machine (GAME_PLAN "Core loop & turn system"). A pure reducer: the same inputs
 * and seed always produce the same match, so hotseat, bot games, the server and replays share it.
 *
 * Round: setup (both place) → move(first) → shot(second) → move(second) → shot(first) → repeat.
 * On a fixed-position map there are no move phases: shot(second) → shot(first) → repeat.
 * Every shot is taken at a frozen target. First to `KillsToWin` kills takes the match.
 *
 * Turn lock (the game's rules; off for the TypeScript parity matches): you walk and change stance
 * only in your own turn, and once you fire you stay on that spot in that stance until the enemy
 * fires. Stance is always your own choice: sudden death never forces anyone up.
 */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public enum PhaseKind
    {
        Setup,
        Move,
        Shot,
        RoundOver,
        MatchOver,
    }

    public sealed class PlayerState
    {
        public Side Side;
        public string Name = "";
        public string RifleId = "";
        public BotDifficulty? Bot;
        public Vec3 Position;
        public Stance Stance;
        public int Kills;
        public int Wounds;
        /// <summary>Where this player last fired from, while they are still there (the enemy saw flash and dust).</summary>
        public Vec3? Revealed;
        /// <summary>A suppressor: firing shows no flash or dust, so the shot doesn't give the position away.</summary>
        public bool Suppressed;
        public bool ColdBore;
        /// <summary>A supersonic near miss gave this player a rough bearing to the shooter.</summary>
        public Vec3? HeardSnapFrom;
        public bool Ready;
        public int IdlePhases;
        /// <summary>Turn lock: fired, and held on that spot in that stance until the enemy fires.</summary>
        public bool Locked;

        public PlayerState Clone() => (PlayerState)MemberwiseClone();
    }

    public sealed class ShotRecord
    {
        public int Round;
        public int Pair;
        public Side Shooter;
        public Verdict Verdict;
        public HitZone? Zone;
        public ImpactKind ImpactKind;
        public Vec3 ImpactPoint;
        public double Energy;
        public double TimeOfFlight;
        public bool NearMiss;
        public MissCallResult? Call;
        public double Distance;
        public FireInput Input = null!;
        public double FireTime;
        public int ShotIndex;
    }

    public sealed class MatchState
    {
        public uint Seed;
        public string MapId = "";
        public int Round;
        /// <summary>Completed move+shot pairs this round (drives sudden death).</summary>
        public int Pair;
        public PhaseKind Phase;
        /// <summary>Who acts in a move or shot phase.</summary>
        public Side? Actor;
        /// <summary>Position at the start of the actor's move phase (movement budget is measured from here).</summary>
        public Vec3? MoveFrom;
        public double PhaseStart;
        public double PhaseEnd;
        public bool Acted;
        public Side FirstMover;
        /// <summary>Phase order within a round after setup, indexes into the cycle.</summary>
        public int Step;
        public PerSide<PlayerState> Players = null!;
        public Conditions Conditions = null!;
        public double WindScale;
        public int ShotCount;
        public List<ShotRecord> Shots = new List<ShotRecord>();
        public Side? RoundWinner;
        public Side? Winner;
        public bool Forfeit;
        public bool ForfeitOnIdle;
        /// <summary>Movement only in your own turn, held where you fired until the enemy fires; no forced stance.</summary>
        public bool TurnLock;

        /// <summary>Shallow copy with fresh player records and shot list, like the TS reducer's clone.</summary>
        public MatchState Clone()
        {
            var c = (MatchState)MemberwiseClone();
            c.Players = new PerSide<PlayerState>(Players.A.Clone(), Players.B.Clone());
            c.Shots = new List<ShotRecord>(Shots);
            return c;
        }
    }

    public enum MatchInputType
    {
        Tick,
        Place,
        Ready,
        Fire,
        NextRound,
        /// <summary>A player quits or never comes back: the other side wins.</summary>
        Forfeit,
        /// <summary>
        /// A player walks, crouches or lies down within their own roaming area. Free at any time
        /// before the match ends unless the turn lock holds them (CanMove); it doesn't count as
        /// acting and never ends a phase.
        /// </summary>
        Shift,
    }

    public sealed class MatchInput
    {
        public MatchInputType Type;
        public double Time;
        /// <summary>Place, Ready, Fire, Forfeit.</summary>
        public Side Side;
        /// <summary>Place, Shift.</summary>
        public Vec3 Position;
        public Stance Stance;
        /// <summary>Fire.</summary>
        public FireInput? Fire;

        public MatchInput Clone() => (MatchInput)MemberwiseClone();
        public MatchInput At(double time)
        {
            var c = Clone();
            c.Time = time;
            return c;
        }

        public static MatchInput Tick(double time) => new MatchInput { Type = MatchInputType.Tick, Time = time };
        public static MatchInput PlaceAt(Side side, Vec3 position, Stance stance, double time) =>
            new MatchInput { Type = MatchInputType.Place, Side = side, Position = position, Stance = stance, Time = time };
        public static MatchInput ShiftTo(Side side, Vec3 position, Stance stance, double time) =>
            new MatchInput { Type = MatchInputType.Shift, Side = side, Position = position, Stance = stance, Time = time };
        public static MatchInput ReadyUp(Side side, double time) => new MatchInput { Type = MatchInputType.Ready, Side = side, Time = time };
        public static MatchInput FireShot(Side side, FireInput input, double time) => new MatchInput { Type = MatchInputType.Fire, Side = side, Fire = input, Time = time };
        public static MatchInput NextRound(double time) => new MatchInput { Type = MatchInputType.NextRound, Time = time };
        public static MatchInput ForfeitBy(Side side, double time) => new MatchInput { Type = MatchInputType.Forfeit, Side = side, Time = time };
    }

    public sealed class StepResult
    {
        public MatchState State;
        public ShotOutcome? Outcome;
        /// <summary>Input rejected (wrong phase, wrong player, out of reach...). State is unchanged.</summary>
        public string? Error;

        public StepResult(MatchState state, ShotOutcome? outcome = null, string? error = null)
        {
            State = state;
            Outcome = outcome;
            Error = error;
        }
    }

    public sealed class PlayerSetup
    {
        public string Name = "";
        public string RifleId = "";
        public BotDifficulty? Bot;
        public bool Suppressed;

        public PlayerSetup(string name, string rifleId, BotDifficulty? bot = null)
        {
            Name = name;
            RifleId = rifleId;
            Bot = bot;
        }
    }

    public static class Match
    {
        public static Side Other(Side s) => s == Side.A ? Side.B : Side.A;

        /// <summary>
        /// Who takes the first shot of the current round: the side that doesn't move first (on a
        /// fixed-position map, where nobody moves, the same side). Rounds alternate.
        /// </summary>
        public static Side FirstShooter(MatchState state) => Other(state.FirstMover);

        /// <summary>Weather and layered wind for a round: rolled from the match seed, drifting a little each round.</summary>
        public static Conditions RollConditions(DuelMap map, uint seed, int round, GameConfig cfg)
        {
            var c = map.Climate;
            var @base = new Rng(Rng.HashSeed(seed, 0x636f6e64));
            var drift = new Rng(Rng.HashSeed(seed, 0x6472, round));
            double Between(double r, double[] range) => range[0] + (range[1] - range[0]) * r;
            double d = cfg.Rules.RoundWindDrift;
            double Jitter() => round == 1 ? 0 : (drift.Next() * 2 - 1) * d;
            double temperatureC = Between(@base.Next(), c.TemperatureC) + Jitter() * 4;
            double humidity = Math.Min(1, Math.Max(0, Between(@base.Next(), c.Humidity) + Jitter() * 0.1));
            double speed = Between(@base.Next(), c.WindSpeedMps) * (1 + Jitter());
            double dir = c.WindToward + (@base.Next() * 2 - 1) * c.WindSpread + Jitter() * 0.5;
            var zones = new List<Vec3>();
            for (int i = 0; i <= map.WindEdges.Length; i++)
            {
                double zs = speed * (0.75 + 0.5 * @base.Next()) * (1 + Jitter() * 0.5);
                double zd = dir + (@base.Next() * 2 - 1) * 0.3;
                zones.Add(new Vec3(zs * Math.Sin(zd), 0, -zs * Math.Cos(zd)));
            }
            return new Conditions
            {
                Atmosphere = new Atmosphere(c.AltitudeM, Air.StandardPressure(c.AltitudeM), temperatureC, humidity),
                Wind = new LaneWind
                {
                    Start = new Vec3(0, 0, 0),
                    Direction = new Vec3(0, 0, -1),
                    Edges = map.WindEdges,
                    Zones = zones,
                    Gust = new GustModel { Amplitude = c.GustAmplitude, Period = c.GustPeriodSeconds, Seed = Rng.HashSeed(seed, 0x67, round) },
                },
                Latitude = map.Latitude,
            };
        }

        static Conditions ScaledConditions(Conditions c, double scale)
        {
            if (scale == 1) return c;
            var wind = c.Wind.Clone();
            wind.Zones = new List<Vec3>();
            foreach (var v in c.Wind.Zones) wind.Zones.Add(new Vec3(v.X * scale, v.Y * scale, v.Z * scale));
            return new Conditions { Atmosphere = c.Atmosphere, Wind = wind, Latitude = c.Latitude };
        }

        /// <summary>Wind and atmosphere actually in force (sudden death calms the wind).</summary>
        public static Conditions ActiveConditions(MatchState state) => ScaledConditions(state.Conditions, state.WindScale);

        static Vec3 DefaultPosition(DuelMap map, Side side) => map.Spots[side][0];

        public static MatchState CreateMatch(uint seed, string mapId, PerSide<PlayerSetup> players, double time, GameConfig cfg, bool forfeitOnIdle = false, bool turnLock = false)
        {
            var map = Maps.ById(mapId);
            PlayerState Player(Side side) => new PlayerState
            {
                Side = side,
                Name = players[side].Name,
                RifleId = players[side].RifleId,
                Bot = players[side].Bot,
                Position = DefaultPosition(map, side),
                Stance = Stance.Prone,
                Kills = 0,
                Wounds = 0,
                Revealed = null,
                Suppressed = players[side].Suppressed,
                ColdBore = true,
                HeardSnapFrom = null,
                Ready = false,
                IdlePhases = 0,
            };
            return new MatchState
            {
                Seed = seed,
                MapId = mapId,
                Round = 1,
                Pair = 0,
                Phase = PhaseKind.Setup,
                Actor = null,
                MoveFrom = null,
                PhaseStart = time,
                PhaseEnd = time + cfg.Rules.SetupSeconds,
                Acted = false,
                FirstMover = Side.A,
                Step = 0,
                Players = new PerSide<PlayerState>(Player(Side.A), Player(Side.B)),
                Conditions = RollConditions(map, seed, 1, cfg),
                WindScale = 1,
                ShotCount = 0,
                Shots = new List<ShotRecord>(),
                RoundWinner = null,
                Winner = null,
                Forfeit = false,
                ForfeitOnIdle = forfeitOnIdle,
                TurnLock = turnLock,
            };
        }

        /// <summary>Sudden-death shrink factor for the current pair count (0 = full zone).</summary>
        public static double ZoneShrink(MatchState state, GameConfig cfg)
        {
            int over = state.Pair - cfg.Rules.SuddenDeathAfterPairs;
            return over < 0 ? 0 : Math.Min(1, (over + 1) * cfg.Rules.SuddenDeathZoneShrinkPerPair);
        }

        public static bool IsSuddenDeath(MatchState state, GameConfig cfg) => state.Pair >= cfg.Rules.SuddenDeathAfterPairs;

        /// <summary>Lowest stance allowed now: deep into sudden death you can no longer hide by lying flat.</summary>
        public static Stance MinStance(MatchState state, GameConfig cfg)
        {
            if (state.TurnLock) return Stance.Prone;
            int over = state.Pair - cfg.Rules.SuddenDeathAfterPairs;
            if (over >= cfg.Rules.SuddenDeathStandingAfterPairs) return Stance.Standing;
            if (over >= cfg.Rules.SuddenDeathNoProneAfterPairs) return Stance.Crouch;
            return Stance.Prone;
        }

        /// <summary>Stance order is the enum order: prone &lt; crouch &lt; standing.</summary>
        public static Stance ClampStance(MatchState state, Stance stance, GameConfig cfg)
        {
            var min = MinStance(state, cfg);
            return (int)stance < (int)min ? min : stance;
        }

        /// <summary>
        /// May this side walk or change stance now? Always without the turn lock; with it, during
        /// setup or in their own turn before they fire, and not while held where they last fired.
        /// </summary>
        public static bool CanMove(MatchState state, Side side)
        {
            if (!state.TurnLock || state.Phase == PhaseKind.Setup) return true;
            if (state.Phase != PhaseKind.Move && state.Phase != PhaseKind.Shot) return false;
            return state.Actor == side && !state.Players[side].Locked;
        }

        /// <summary>Furthest a mover may end up from where their move phase started, m.</summary>
        public static double MoveBudget(MatchState state, Side side, GameConfig cfg)
        {
            double wounded = state.Players[side].Wounds > 0 ? cfg.Player.Wound.MoveSpeedFactor : 1;
            return cfg.Player.MoveSpeedMps * cfg.Rules.MoveSeconds * wounded;
        }

        /// <summary>
        /// The phase cycle after setup: [kind, actor] per step. On a fixed-position map nobody moves, so
        /// the sides simply trade shots.
        /// </summary>
        static (PhaseKind phase, Side actor) Cycle(MatchState state, int step)
        {
            var f = state.FirstMover;
            var s = Other(f);
            if (Maps.ById(state.MapId).Fixed) return step % 2 == 0 ? (PhaseKind.Shot, s) : (PhaseKind.Shot, f);
            switch (step % 4)
            {
                case 0: return (PhaseKind.Move, f);
                case 1: return (PhaseKind.Shot, s);
                case 2: return (PhaseKind.Move, s);
                default: return (PhaseKind.Shot, f);
            }
        }

        static int CycleLength(MatchState state) => Maps.ById(state.MapId).Fixed ? 2 : 4;

        static void EnterPhase(MatchState state, int step, double time, GameConfig cfg)
        {
            var map = Maps.ById(state.MapId);
            var next = Cycle(state, step);
            state.Step = step;
            state.Phase = next.phase;
            state.Actor = next.actor;
            state.Acted = false;
            state.PhaseStart = time;
            state.PhaseEnd = time + (next.phase == PhaseKind.Move ? cfg.Rules.MoveSeconds : cfg.Rules.ShotSeconds);
            state.Players.A.Stance = ClampStance(state, state.Players.A.Stance, cfg);
            state.Players.B.Stance = ClampStance(state, state.Players.B.Stance, cfg);
            if (next.phase == PhaseKind.Move)
            {
                var p = state.Players[next.actor];
                // Sudden death: the usable zone shrinks toward the front, forcing exposure.
                var zone = Maps.UsableZone(map, next.actor, ZoneShrink(state, cfg), cfg.Rules.MinZoneDepthM);
                p.Position = Maps.ClampToZone(map, zone, p.Position);
                state.MoveFrom = p.Position;
            }
            else
            {
                state.MoveFrom = null;
            }
        }

        static void EndPhase(MatchState state, double time, GameConfig cfg)
        {
            if (state.Phase == PhaseKind.Setup)
            {
                state.Players.A.Ready = false;
                state.Players.B.Ready = false;
                EnterPhase(state, 0, time, cfg);
                return;
            }
            if (state.Phase != PhaseKind.Move && state.Phase != PhaseKind.Shot) return;
            var actor = state.Players[state.Actor!.Value];
            if (!state.Acted)
            {
                actor.IdlePhases += 1;
                if (state.ForfeitOnIdle && actor.IdlePhases >= cfg.Rules.ForfeitAfterIdlePhases)
                {
                    state.Winner = Other(actor.Side);
                    state.Forfeit = true;
                    state.Phase = PhaseKind.MatchOver;
                    state.Actor = null;
                    return;
                }
            }
            else
            {
                actor.IdlePhases = 0;
            }
            if (state.Phase == PhaseKind.Shot)
            {
                actor.HeardSnapFrom = null; // the bearing hint lasts for one of your shot phases
                state.Pair += 1;
                if (IsSuddenDeath(state, cfg))
                {
                    state.WindScale = Math.Max(0, 1 - (state.Pair - cfg.Rules.SuddenDeathAfterPairs + 1) * cfg.Rules.SuddenDeathWindDecayPerPair);
                }
            }
            EnterPhase(state, (state.Step + 1) % CycleLength(state), time, cfg);
        }

        static void EndRound(MatchState state, Side winner, double time, GameConfig cfg)
        {
            state.Players[winner].Kills += 1;
            state.RoundWinner = winner;
            state.Actor = null;
            state.PhaseStart = time;
            state.PhaseEnd = double.PositiveInfinity;
            if (state.Players[winner].Kills >= cfg.Rules.KillsToWin)
            {
                state.Winner = winner;
                state.Phase = PhaseKind.MatchOver;
            }
            else
            {
                state.Phase = PhaseKind.RoundOver;
            }
        }

        static void StartNextRound(MatchState state, double time, GameConfig cfg)
        {
            var map = Maps.ById(state.MapId);
            state.Round += 1;
            state.Pair = 0;
            state.WindScale = 1;
            state.Conditions = RollConditions(map, state.Seed, state.Round, cfg);
            state.FirstMover = Other(state.FirstMover);
            state.RoundWinner = null;
            foreach (var p in new[] { state.Players.A, state.Players.B })
            {
                p.Wounds = 0;
                p.Revealed = null;
                p.HeardSnapFrom = null;
                p.Ready = false;
                p.Locked = false;
                p.Stance = Stance.Prone;
                p.Position = DefaultPosition(map, p.Side);
            }
            state.Phase = PhaseKind.Setup;
            state.Actor = null;
            state.Acted = false;
            state.PhaseStart = time;
            state.PhaseEnd = time + cfg.Rules.SetupSeconds;
        }

        public static StepResult ApplyInput(MatchState prev, MatchInput input, GameConfig cfg)
        {
            StepResult Fail(string error) => new StepResult(prev, null, error);
            if (prev.Phase == PhaseKind.MatchOver) return Fail("Match is over");
            var state = prev.Clone();
            var map = Maps.ById(state.MapId);

            // Timers first: any input past the phase end closes the phase before it is considered.
            if (input.Time >= state.PhaseEnd && (state.Phase == PhaseKind.Setup || state.Phase == PhaseKind.Move || state.Phase == PhaseKind.Shot))
            {
                EndPhase(state, state.PhaseEnd, cfg);
                if (input.Type == MatchInputType.Tick)
                {
                    // Several phases may have elapsed (e.g. a long pause): keep closing them.
                    return state.Phase == PhaseKind.MatchOver ? new StepResult(state) : ApplyInput(state, input, cfg);
                }
                if (state.Phase == PhaseKind.MatchOver) return new StepResult(state);
            }

            switch (input.Type)
            {
                case MatchInputType.Tick:
                    return new StepResult(state);

                case MatchInputType.Forfeit:
                    state.Winner = Other(input.Side);
                    state.Forfeit = true;
                    state.Phase = PhaseKind.MatchOver;
                    state.Actor = null;
                    state.PhaseStart = input.Time;
                    state.PhaseEnd = double.PositiveInfinity;
                    return new StepResult(state);

                case MatchInputType.NextRound:
                    if (state.Phase != PhaseKind.RoundOver) return Fail("No round to start");
                    StartNextRound(state, input.Time, cfg);
                    return new StepResult(state);

                case MatchInputType.Place:
                {
                    var p = state.Players[input.Side];
                    if (state.Phase == PhaseKind.Setup)
                    {
                        p.Position = Maps.ClampToZone(map, map.Zones[input.Side], input.Position);
                        p.Stance = input.Stance;
                        return new StepResult(state);
                    }
                    if (state.Phase != PhaseKind.Move || state.Actor != input.Side) return Fail("Not your move phase");
                    if (!CanMove(state, input.Side)) return Fail("Held where you fired until the enemy fires");
                    var zone = Maps.UsableZone(map, input.Side, ZoneShrink(state, cfg), cfg.Rules.MinZoneDepthM);
                    var target = Maps.ClampToZone(map, zone, input.Position);
                    var from = state.MoveFrom!.Value;
                    double budget = MoveBudget(state, input.Side, cfg);
                    double d = Geometry.HorizontalDistance(from, target);
                    if (d > budget)
                    {
                        double f = budget / d;
                        target = Maps.ClampToZone(map, zone, new Vec3(from.X + (target.X - from.X) * f, 0, from.Z + (target.Z - from.Z) * f));
                    }
                    if (Geometry.HorizontalDistance(p.Position, target) > 0.5) p.Revealed = null; // moved away from where they last fired
                    p.Position = target;
                    p.Stance = ClampStance(state, input.Stance, cfg);
                    state.Acted = true;
                    return new StepResult(state);
                }

                case MatchInputType.Shift:
                {
                    if (!CanMove(state, input.Side))
                        return Fail(state.Players[input.Side].Locked ? "Held where you fired until the enemy fires" : "Not your turn");
                    var p = state.Players[input.Side];
                    var target = Maps.ClampToZone(map, Maps.RoamZone(map, input.Side), input.Position);
                    // Walking away from where they last fired: the enemy loses the flash and dust.
                    if (p.Revealed is Vec3 seen && Geometry.HorizontalDistance(seen, target) > 0.5) p.Revealed = null;
                    p.Position = target;
                    p.Stance = ClampStance(state, input.Stance, cfg);
                    return new StepResult(state);
                }

                case MatchInputType.Ready:
                {
                    if (state.Phase == PhaseKind.Setup)
                    {
                        state.Players[input.Side].Ready = true;
                        if (state.Players.A.Ready && state.Players.B.Ready) EndPhase(state, input.Time, cfg);
                        return new StepResult(state);
                    }
                    if ((state.Phase != PhaseKind.Move && state.Phase != PhaseKind.Shot) || state.Actor != input.Side) return Fail("Not your phase");
                    state.Acted = true;
                    EndPhase(state, input.Time, cfg);
                    return new StepResult(state);
                }

                case MatchInputType.Fire:
                {
                    if (state.Phase != PhaseKind.Shot || state.Actor != input.Side) return Fail("Not your shot phase");
                    if (state.Acted) return Fail("Already fired this phase");
                    var fire = input.Fire ?? throw new ArgumentException("Fire input without a FireInput");
                    var shooter = state.Players[input.Side];
                    var targetSide = Other(input.Side);
                    var target = state.Players[targetSide];
                    state.ShotCount += 1;
                    var outcome = Combat.ResolveShot(
                        new ShotRequest
                        {
                            Map = map,
                            Conditions = ActiveConditions(state),
                            RifleId = shooter.RifleId,
                            ShooterPosition = shooter.Position,
                            ShooterStance = shooter.Stance,
                            Target = new ShotTarget { Id = targetSide.ToString(), Side = targetSide, Position = target.Position, Stance = target.Stance, Wounds = target.Wounds },
                            Input = fire,
                            Seed = state.Seed,
                            ShotIndex = state.ShotCount,
                            FireTime = input.Time,
                            ColdBore = shooter.ColdBore,
                        },
                        cfg);
                    shooter.ColdBore = false;
                    if (state.TurnLock)
                    {
                        shooter.Locked = true;
                        target.Locked = false;
                    }
                    if (!shooter.Suppressed) shooter.Revealed = shooter.Position;
                    state.Acted = true;
                    state.Shots.Add(new ShotRecord
                    {
                        Round = state.Round,
                        Pair = state.Pair,
                        Shooter = input.Side,
                        Verdict = outcome.Verdict,
                        Zone = outcome.Zone,
                        ImpactKind = outcome.Impact.Kind,
                        ImpactPoint = outcome.Impact.Point,
                        Energy = outcome.Impact.Energy,
                        TimeOfFlight = outcome.Impact.T,
                        NearMiss = outcome.NearMiss,
                        Call = outcome.Call,
                        Distance = Geometry.HorizontalDistance(shooter.Position, target.Position),
                        Input = fire,
                        FireTime = input.Time,
                        ShotIndex = state.ShotCount,
                    });
                    if (outcome.NearMiss) target.HeardSnapFrom = shooter.Position;
                    if (outcome.Verdict == Verdict.Kill)
                    {
                        EndRound(state, input.Side, input.Time, cfg);
                    }
                    else
                    {
                        if (outcome.Verdict == Verdict.Wound) target.Wounds += 1;
                        EndPhase(state, input.Time, cfg); // the shot phase ends on fire
                    }
                    return new StepResult(state, outcome);
                }
            }
            throw new ArgumentOutOfRangeException(nameof(input), input.Type, "Unknown match input");
        }

        /// <summary>Seconds left in the current phase.</summary>
        public static double TimeLeft(MatchState state, double now) => Math.Max(0, state.PhaseEnd - now);
    }
}
