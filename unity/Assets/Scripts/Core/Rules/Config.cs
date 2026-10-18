using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using SniperDuel.Ballistics;
using Environment = SniperDuel.Ballistics.Environment;

namespace SniperDuel.Rules
{
    public enum Stance
    {
        Prone,
        Crouch,
        Standing,
    }

    public enum BotDifficulty
    {
        Easy,
        Medium,
        Hard,
    }

    public enum WindMode
    {
        [EnumMember(Value = "half-station")] HalfStation,
        [EnumMember(Value = "station")] Station,
        [EnumMember(Value = "layered")] Layered,
    }

    /// <summary>One value per stance (TS Record&lt;Stance, T&gt;).</summary>
    public sealed class StanceMap<T>
    {
        public T Prone = default!;
        public T Crouch = default!;
        public T Standing = default!;

        public T this[Stance s]
        {
            get => s == Stance.Prone ? Prone : s == Stance.Crouch ? Crouch : Standing;
        }
    }

    public sealed class ColdBoreShift
    {
        public double Elevation;
        public double Windage;
    }

    public sealed class RifleConfig
    {
        public string Id = "";
        public string Name = "";
        public string Class = "";
        public string Cartridge = "";
        public DragModel DragModel;
        public double Bc;
        public double MassKg;
        public double DiameterM;
        public double LengthM;
        public double MuzzleVelocityMps;
        public double TwistM;
        public double[] SweetSpotM = new double[2];
        public ColdBoreShift ColdBoreShiftMrad = new ColdBoreShift();
    }

    public sealed class BallisticsConfig
    {
        public sealed class TransonicSettings
        {
            public double MachThreshold;
            public double SigmaRadPerSqrtS;
        }

        public sealed class EffectsSettings
        {
            public bool SpinDrift;
            public bool AerodynamicJump;
            public bool Coriolis;
            public bool TransonicDispersion;

            public Effects ToEffects() => new Effects { SpinDrift = SpinDrift, AerodynamicJump = AerodynamicJump, Coriolis = Coriolis, TransonicDispersion = TransonicDispersion };
        }

        public double DtSeconds;
        public Integrator Integrator;
        public double SightHeightM;
        public double ZeroRangeM;
        public double MuzzleVelocitySdMps;
        public TransonicSettings Transonic = new TransonicSettings();
        public EffectsSettings Effects = new EffectsSettings();
    }

    public sealed class ScopeConfig
    {
        public double MinZoom;
        public double MaxZoom;
        public double FovAt1xDeg;
        public double ClickMrad;
        public double MaxElevationMrad;
        public double MaxWindageMrad;
        public double EyeFovDeg;
    }

    public sealed class SwayConfig
    {
        public sealed class HoldBreathSettings
        {
            public double SteadySeconds;
            public double SteadyFactor;
            public double RecoverSeconds;
            public double StrainGrowthPerSecond;
            public double MaxStrainFactor;
        }

        public StanceMap<double> AmplitudeMrad = new StanceMap<double>();
        public double BipodFactor;
        public double PeriodSeconds;
        public HoldBreathSettings HoldBreath = new HoldBreathSettings();
        public StanceMap<double> EyeHeightM = new StanceMap<double>();
    }

    public sealed class TargetConfig
    {
        public double WidthM;
        public double HeightM;
        public double HeadM;
    }

    public sealed class AtmosphereSpec
    {
        public double AltitudeM;
        public double TemperatureC;
        public double Humidity;
        public double? PressureHPa;
    }

    public sealed class PracticeRangeConfig
    {
        public sealed class WindSettings
        {
            public double[] BaseSpeedMps = new double[2];
            public double DirectionJitterDeg;
            public double GustAmplitude;
            public double GustPeriodSeconds;
            public double ZoneSpeedJitter;
        }

        public double[] TargetsM = Array.Empty<double>();
        public double LaneWidthM;
        public double[] WindZonesM = Array.Empty<double>();
        public WindSettings Wind = new WindSettings();
        public AtmosphereSpec Atmosphere = new AtmosphereSpec();
        public double Latitude;
        public double SpeedOfSoundForAudioMps;
        /// <summary>The firing point is a raised bench so prone shooters see over the grass.</summary>
        public double FiringPointHeightM;
    }

    public sealed class SoftCoverConfig
    {
        public double MinEnergyJ;
        public double EnergyKeep;
        public int MaxLayers;
    }

    public sealed class RulesConfig
    {
        public double SetupSeconds;
        public double MoveSeconds;
        public double ShotSeconds;
        public int KillsToWin;
        public int SuddenDeathAfterPairs;
        public double SuddenDeathWindDecayPerPair;
        public double SuddenDeathZoneShrinkPerPair;
        public double MinZoneDepthM;
        public double ChestKillEnergyJ;
        public int WoundsToDown;
        public double NearMissRadiusM;
        public int ForfeitAfterIdlePhases;
        /// <summary>Sudden death pairs after which nobody may lie prone, and after which everyone stands.</summary>
        public int SuddenDeathNoProneAfterPairs;
        public int SuddenDeathStandingAfterPairs;
        /// <summary>Online: pause between a round's last shot and the next setup, s.</summary>
        public double RoundBreakSeconds;
        /// <summary>Online: how long a dropped player's seat is held before they forfeit, s.</summary>
        public double ReconnectGraceSeconds;
        public double RoundWindDrift;
    }

    public sealed class SilhouetteSize
    {
        public double HeightM;
        public double WidthM;
        public double HeadM;
    }

    public sealed class PlayerConfig
    {
        public sealed class WoundSettings
        {
            public double SwayFactor;
            public double MoveSpeedFactor;
        }

        public StanceMap<SilhouetteSize> Silhouette = new StanceMap<SilhouetteSize>();
        public double MoveSpeedMps;
        public WoundSettings Wound = new WoundSettings();
    }

    public sealed class BotProfile
    {
        public double RangeErrorFrac;
        /// <summary>half-station: holds half the wind at its own position; station: uses its station wind for the whole lane; layered: reads every zone.</summary>
        public WindMode WindMode;
        /// <summary>1 = fires anywhere on the sway curve, 0 = fires exactly at the sway null.</summary>
        public double SwayFireFactor;
        public StanceMap<double> SpotChance = new StanceMap<double>();
        /// <summary>Number of fixed spots it rotates through (0 = uses the map's full position set).</summary>
        public int Positions;
        public bool RelocateAfterFiring;
    }

    public sealed class BotConfig
    {
        public double[] ThinkSeconds = new double[2];
        public BotProfile Easy = new BotProfile();
        public BotProfile Medium = new BotProfile();
        public BotProfile Hard = new BotProfile();

        public BotProfile this[BotDifficulty d] => d == BotDifficulty.Easy ? Easy : d == BotDifficulty.Medium ? Medium : Hard;
    }

    /// <summary>
    /// Every gameplay-visible number (config/game.json, mirrored at Assets/Resources/Config/game.json).
    /// Field names match the JSON keys case-insensitively.
    /// </summary>
    public sealed class GameConfig
    {
        public BallisticsConfig Ballistics = new BallisticsConfig();
        public List<RifleConfig> Rifles = new List<RifleConfig>();
        public ScopeConfig Scope = new ScopeConfig();
        public SwayConfig Sway = new SwayConfig();
        public TargetConfig Target = new TargetConfig();
        public PracticeRangeConfig PracticeRange = new PracticeRangeConfig();
        public SoftCoverConfig SoftCover = new SoftCoverConfig();
        public RulesConfig Rules = new RulesConfig();
        public PlayerConfig Player = new PlayerConfig();
        public BotConfig Bot = new BotConfig();

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        /// <summary>Parses the contents of config/game.json.</summary>
        public static GameConfig Parse(string json) =>
            JsonConvert.DeserializeObject<GameConfig>(json, Settings) ?? throw new FormatException("Empty game config");

        public RifleConfig RifleById(string id)
        {
            foreach (var r in Rifles)
                if (r.Id == id) return r;
            throw new ArgumentException($"Unknown rifle {id}");
        }
    }

    public static class RifleSetup
    {
        public static Projectile ProjectileOf(RifleConfig rifle) => new Projectile
        {
            DragModel = rifle.DragModel,
            Bc = rifle.Bc,
            MassKg = rifle.MassKg,
            DiameterM = rifle.DiameterM,
            LengthM = rifle.LengthM,
        };

        /// <summary>Atmosphere for a map: station pressure follows altitude unless given.</summary>
        public static Atmosphere AtmosphereFor(double altitudeM, double temperatureC, double humidity, double? pressureHPa = null) =>
            new Atmosphere(altitudeM, pressureHPa ?? Air.StandardPressure(altitudeM), temperatureC, humidity);

        public static Atmosphere AtmosphereFor(AtmosphereSpec a) => AtmosphereFor(a.AltitudeM, a.TemperatureC, a.Humidity, a.PressureHPa);

        static readonly Dictionary<string, double> ZeroCache = new Dictionary<string, double>();

        /// <summary>
        /// A rifle's shot setup, zeroed at the config zero range. Players zero in standard conditions on a
        /// home range, so field weather shows up as changed dope, exactly like the real thing.
        /// Returns a fresh Shot each call; callers may modify it.
        /// </summary>
        public static Shot RifleShot(RifleConfig rifle, GameConfig cfg)
        {
            var shot = new Shot
            {
                Projectile = ProjectileOf(rifle),
                MuzzleVelocity = rifle.MuzzleVelocityMps,
                MuzzleVelocitySd = cfg.Ballistics.MuzzleVelocitySdMps,
                SightHeight = cfg.Ballistics.SightHeightM,
                Twist = rifle.TwistM,
            };
            string key = FormattableString.Invariant($"{rifle.Id}:{rifle.Bc:R}:{rifle.MuzzleVelocityMps:R}:{cfg.Ballistics.ZeroRangeM:R}");
            double zero;
            bool found;
            lock (ZeroCache) found = ZeroCache.TryGetValue(key, out zero);
            if (!found)
            {
                zero = Solutions.SolveZero(shot, new Environment { Atmosphere = AtmosphereFor(0, 15, 0.5) }, cfg.Ballistics.ZeroRangeM);
                lock (ZeroCache) ZeroCache[key] = zero;
            }
            shot.ZeroElevation = zero;
            return shot;
        }
    }
}
