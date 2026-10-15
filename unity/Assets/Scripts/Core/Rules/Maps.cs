/*
 * Duel maps. A map is two opposing zones with no-man's-land between. Lane coordinate s runs from
 * player A's rear (s = 0, world z = 0) toward player B (world z = −s). A looks north (yaw 0), B south.
 */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public enum Side
    {
        A,
        B,
    }

    /// <summary>One value per side (TS Record&lt;Side, T&gt;).</summary>
    public sealed class PerSide<T>
    {
        public T A;
        public T B;

        public PerSide(T a, T b)
        {
            A = a;
            B = b;
        }

        public T this[Side s]
        {
            get => s == Side.A ? A : B;
            set
            {
                if (s == Side.A) A = value;
                else B = value;
            }
        }
    }

    public sealed class Zone
    {
        /// <summary>Lateral extent, world x.</summary>
        public double XMin;
        public double XMax;
        /// <summary>Lane-distance extent (s = −z).</summary>
        public double SMin;
        public double SMax;
        /// <summary>Lane distance of the edge facing the enemy.</summary>
        public double Front;

        public Zone(double xMin, double xMax, double sMin, double sMax, double front)
        {
            XMin = xMin;
            XMax = xMax;
            SMin = sMin;
            SMax = sMax;
            Front = front;
        }

        public Zone Clone() => (Zone)MemberwiseClone();
    }

    public enum StructureKind
    {
        Bridge,
        Tower,
    }

    public enum TowerStyle
    {
        Concrete,
        Rig,
    }

    /// <summary>A man-made platform players stand on: the hit model treats its top as ground.</summary>
    public sealed class Structure
    {
        public StructureKind Kind;
        // Bridge
        public double S;
        public double DeckY;
        public double HalfSpan;
        public double HalfWidth;
        public double[] PylonsX = Array.Empty<double>();
        // Tower
        public double X;
        public double TopY;
        public double HalfSize;
        public TowerStyle Style;

        public static Structure Bridge(double s, double deckY, double halfSpan, double halfWidth, double[] pylonsX) =>
            new Structure { Kind = StructureKind.Bridge, S = s, DeckY = deckY, HalfSpan = halfSpan, HalfWidth = halfWidth, PylonsX = pylonsX };

        public static Structure Tower(double x, double s, double topY, double halfSize, TowerStyle style) =>
            new Structure { Kind = StructureKind.Tower, X = x, S = s, TopY = topY, HalfSize = halfSize, Style = style };
    }

    public enum SkyKind
    {
        Sunrise,
        Clear,
        Sunset,
    }

    public enum Vegetation
    {
        Pine,
        Shrub,
        None,
    }

    /// <summary>Colours for drawing a map; anything left out uses the client's default green range.</summary>
    public sealed class Palette
    {
        /// <summary>Which photographic sky lights the map, and where its sun sits (degrees clockwise from the enemy's direction).</summary>
        public SkyKind? Sky;
        public double? SunAzimuthDeg;
        /// <summary>Ground hue/saturation/lightness in the lane and around it (0–1 each).</summary>
        public double[]? Lane;
        public double[]? Outside;
        /// <summary>Ground below sea level (a harbor channel) in this colour.</summary>
        public double[]? Water;
        public string? Grass;
        public string? Hills;
        public string? Rocks;
        /// <summary>Trees or bushes on the land around the lane.</summary>
        public Vegetation? Vegetation;
        /// <summary>Snow line height for the far mountains, m (none if absent).</summary>
        public double? SnowAbove;
    }

    public sealed class Climate
    {
        public double AltitudeM;
        public double[] TemperatureC = new double[2];
        public double[] Humidity = new double[2];
        public double[] WindSpeedMps = new double[2];
        /// <summary>Mean direction the wind blows toward, rad from north toward east, and its spread.</summary>
        public double WindToward;
        public double WindSpread;
        public double GustAmplitude;
        public double GustPeriodSeconds;
    }

    /// <summary>How the map is drawn: wind flags (world x, lane distance s) and an optional palette.</summary>
    public sealed class MapLook
    {
        public double[][] Flags = Array.Empty<double[]>();
        public double LaneHalfWidth;
        public Palette? Palette;
        public Structure[]? Structures;
    }

    public sealed class DuelMap
    {
        public string Id = "";
        public string Name = "";
        /// <summary>One-line pitch for the map picker.</summary>
        public string? Blurb;
        public double LaneLength;
        /// <summary>
        /// Fixed-position duel: each side has one spot, nothing to hide behind and no move phases, so the
        /// whole game is the shot. Zones collapse to that spot.
        /// </summary>
        public bool Fixed;
        /// <summary>Water surface height, if the map has open water (shots falling in it splash).</summary>
        public double? Water;
        /// <summary>Terrain as drawn, when the hit-test ground also includes platform tops; defaults to Ground.</summary>
        public Ground? Terrain;
        public PerSide<Zone> Zones = null!;
        /// <summary>
        /// Where each player may walk, crouch and lie during the duel, if not their whole zone: on a
        /// fixed-position map, the open part of their own deck or platform round the firing spot.
        /// </summary>
        public PerSide<Zone>? Roam;
        public Ground Ground = Hits.FlatGround;
        public List<Box> Cover = new List<Box>();
        /// <summary>Named firing positions per side (bots use these; humans may stand anywhere in the zone).</summary>
        public PerSide<List<Vec3>> Spots = new PerSide<List<Vec3>>(new List<Vec3>(), new List<Vec3>());
        /// <summary>Lane distances where the wind zones change (shooter / mid / target thirds).</summary>
        public double[] WindEdges = Array.Empty<double>();
        public Climate Climate = new Climate();
        public double Latitude;
        public MapLook Look = new MapLook();
    }

    public static class Maps
    {
        static double Smoothstep(double e0, double e1, double x)
        {
            double t = Math.Min(1, Math.Max(0, (x - e0) / (e1 - e0)));
            return t * t * (3 - 2 * t);
        }

        static double[] V(params double[] v) => v;

        /// <summary>
        /// Training Lane: 650 m, A on a 3 m rise, B on a 5 m rise, a shallow valley with rolling dead ground
        /// between. Placeholder geometry for the Phase 2 hotseat; Dry Valley replaces it in Phase 4.
        /// </summary>
        static double TrainingLaneGround(double x, double z)
        {
            double s = -z;
            double a = 3 * (1 - Smoothstep(85, 170, s));
            double b = 5 * Smoothstep(470, 565, s);
            double valley = Smoothstep(150, 220, s) * (1 - Smoothstep(440, 500, s));
            double ripples = valley * (0.9 * Math.Sin(x * 0.045 + 1.3) * Math.Sin(s * 0.028) + 0.4 * Math.Sin(s * 0.09 + x * 0.02));
            double outside = Smoothstep(70, 220, Math.Abs(x));
            double hills = outside * (10 + 8 * Math.Sin(x * 0.01 + s * 0.006) + 6 * Math.Cos(s * 0.012));
            return a + b + ripples + hills;
        }

        static Box Rock(string id, double x, double s, double w, double h, double d, Ground ground)
        {
            double y = ground(x, -s);
            return new Box { Id = id, Min = new Vec3(x - w / 2, y - 0.2, -s - d / 2), Max = new Vec3(x + w / 2, y + h, -s + d / 2), Material = CoverMaterial.Hard };
        }

        static DuelMap MakeTrainingLane()
        {
            Ground ground = TrainingLaneGround;
            var cover = new List<Box>();
            var spots = new PerSide<List<Vec3>>(new List<Vec3>(), new List<Vec3>());
            // Five positions per side, staggered so no single spot sees everything.
            double[][] layoutA =
            {
                V(-38, 70, 1.3),
                V(-16, 78, 0.9),
                V(4, 64, 1.4),
                V(24, 76, 1.0),
                V(42, 66, 1.2),
            };
            for (int i = 0; i < layoutA.Length; i++)
            {
                double x = layoutA[i][0], s = layoutA[i][1], h = layoutA[i][2];
                cover.Add(Rock($"A-rock-{i}", x, s, 2.2, h, 1.4, ground));
                spots.A.Add(new Vec3(x + (i % 2 == 0 ? 1.6 : -1.6), ground(x, -(s - 1.6)), -(s - 1.6)));
            }
            double[][] layoutB =
            {
                V(-42, 582, 1.2),
                V(-20, 574, 1.4),
                V(0, 588, 1.0),
                V(18, 572, 1.3),
                V(40, 584, 0.9),
            };
            for (int i = 0; i < layoutB.Length; i++)
            {
                double x = layoutB[i][0], s = layoutB[i][1], h = layoutB[i][2];
                cover.Add(Rock($"B-rock-{i}", x, s, 2.2, h, 1.4, ground));
                spots.B.Add(new Vec3(x + (i % 2 == 0 ? -1.6 : 1.6), ground(x, -(s + 1.6)), -(s + 1.6)));
            }
            return new DuelMap
            {
                Id = "training-lane",
                Name = "Training Lane",
                LaneLength = 650,
                Zones = new PerSide<Zone>(new Zone(-55, 55, 0, 80, 80), new Zone(-55, 55, 570, 650, 570)),
                Ground = ground,
                Cover = cover,
                Spots = spots,
                WindEdges = V(217, 433),
                Climate = new Climate
                {
                    AltitudeM = 400,
                    TemperatureC = V(8, 30),
                    Humidity = V(0.25, 0.75),
                    WindSpeedMps = V(1.5, 7),
                    WindToward = Math.PI / 2,
                    WindSpread = 0.6,
                    GustAmplitude = 0.3,
                    GustPeriodSeconds = 10,
                },
                Latitude = 38,
                Look = new MapLook
                {
                    Flags = new[] { V(-62, 120), V(64, 325), V(-66, 530) },
                    LaneHalfWidth = 70,
                },
            };
        }

        /// <summary>
        /// Dry Valley (GAME_PLAN launch map): 690–870 m across a dry wash at 1,200 m, hot and thin-aired,
        /// with a strong, steady crosswind. A sits on a low bench, B on a higher one with a wash between.
        /// </summary>
        static double DryValleyGround(double x, double z)
        {
            double s = -z;
            double benchA = 6 * (1 - Smoothstep(95, 180, s));
            double benchB = 11 * Smoothstep(650, 770, s);
            // The wash: a shallow channel snaking across mid-lane, with gravel bars either side.
            double q = (s - 430 - 25 * Math.Sin(x * 0.02)) / 38;
            double wash = -3.2 * Math.Exp(-(q * q));
            double bars = 0.8 * Math.Sin(s * 0.035 + x * 0.015) * Smoothstep(180, 260, s) * (1 - Smoothstep(600, 660, s));
            double outside = Smoothstep(80, 240, Math.Abs(x));
            double ridges = outside * (16 + 12 * Math.Sin(x * 0.008 + s * 0.004) + 7 * Math.Cos(s * 0.01));
            return benchA + benchB + wash + bars + ridges;
        }

        static DuelMap MakeDryValley()
        {
            Ground ground = DryValleyGround;
            var cover = new List<Box>();
            var spots = new PerSide<List<Vec3>>(new List<Vec3>(), new List<Vec3>());
            // Six boulders a side: two pairs close together (a short dash between them) and two loners.
            double[][] layoutA =
            {
                V(-46, 76, 1.4, 2.6),
                V(-40, 64, 1.0, 2.0),
                V(-12, 84, 1.2, 2.4),
                V(14, 70, 1.6, 3.0),
                V(20, 58, 1.0, 1.8),
                V(44, 80, 1.3, 2.2),
            };
            for (int i = 0; i < layoutA.Length; i++)
            {
                double x = layoutA[i][0], s = layoutA[i][1], h = layoutA[i][2], w = layoutA[i][3];
                cover.Add(Rock($"A-rock-{i}", x, s, w, h, 1.6, ground));
                spots.A.Add(new Vec3(x + (i % 2 == 0 ? 1.8 : -1.8), ground(x, -(s - 1.8)), -(s - 1.8)));
            }
            double[][] layoutB =
            {
                V(-44, 792, 1.3, 2.4),
                V(-18, 784, 1.5, 2.8),
                V(-12, 800, 1.0, 1.8),
                V(10, 790, 1.2, 2.2),
                V(36, 786, 1.6, 3.0),
                V(42, 802, 1.0, 2.0),
            };
            for (int i = 0; i < layoutB.Length; i++)
            {
                double x = layoutB[i][0], s = layoutB[i][1], h = layoutB[i][2], w = layoutB[i][3];
                cover.Add(Rock($"B-rock-{i}", x, s, w, h, 1.6, ground));
                spots.B.Add(new Vec3(x + (i % 2 == 0 ? -1.8 : 1.8), ground(x, -(s + 1.8)), -(s + 1.8)));
            }
            return new DuelMap
            {
                Id = "dry-valley",
                Name = "Dry Valley",
                LaneLength = 870,
                Zones = new PerSide<Zone>(new Zone(-60, 60, 0, 90, 90), new Zone(-60, 60, 780, 870, 780)),
                Ground = ground,
                Cover = cover,
                Spots = spots,
                WindEdges = V(290, 580),
                Climate = new Climate
                {
                    AltitudeM = 1200,
                    TemperatureC = V(26, 42),
                    Humidity = V(0.08, 0.3),
                    WindSpeedMps = V(5, 10),
                    WindToward = Math.PI / 2,
                    WindSpread = 0.25,
                    GustAmplitude = 0.12,
                    GustPeriodSeconds = 14,
                },
                Latitude = 34,
                Look = new MapLook
                {
                    Flags = new[] { V(-70, 160), V(72, 440), V(-74, 700) },
                    LaneHalfWidth = 75,
                    Palette = new Palette
                    {
                        Sky = SkyKind.Clear,
                        SunAzimuthDeg = 105,
                        Lane = V(0.09, 0.32, 0.56),
                        Outside = V(0.08, 0.28, 0.5),
                        Grass = "#b9a66b",
                        Hills = "#a88d6c",
                        Rocks = "#9c8165",
                    },
                },
            };
        }

        /// <summary>A box standing on the ground at (x, s): w wide (x), d deep (along the lane), h tall.</summary>
        static Box Block(string id, double x, double s, double w, double d, double h, Ground ground, CoverMaterial material, CoverLook? look, double? baseY = null)
        {
            double y = baseY ?? ground(x, -s);
            return new Box { Id = id, Min = new Vec3(x - w / 2, y - 0.05, -s - d / 2), Max = new Vec3(x + w / 2, y + h, -s + d / 2), Material = material, Look = look };
        }

        /*
         * Harbor Rooftops (GAME_PLAN launch map): two flat roofs facing each other across quays and a
         * channel, 330–440 m. A's roof is 12 m up, B's 16 m. Parapets hide a prone or crouching shooter;
         * air-conditioning units are sheet metal, so they hide you but a .338 or .50 goes straight through.
         * Light wind that swirls between the buildings.
         */
        const double RoofA = 12;
        const double RoofB = 16;

        static double HarborGround(double x, double z)
        {
            double s = -z;
            double inX = 1 - Smoothstep(46, 48, Math.Abs(x));
            double roofA = RoofA * inX * (1 - Smoothstep(56, 58, s)) * Smoothstep(-14, -12, s);
            double roofB = RoofB * inX * Smoothstep(380, 382, s) * (1 - Smoothstep(452, 454, s));
            double channel = -3 * Smoothstep(150, 158, s) * (1 - Smoothstep(282, 290, s));
            return Math.Max(roofA, roofB) + channel;
        }

        static DuelMap MakeHarborRooftops()
        {
            Ground ground = HarborGround;
            var cover = new List<Box>();
            var spots = new PerSide<List<Vec3>>(new List<Vec3>(), new List<Vec3>());
            double Roof(Side side) => side == Side.A ? RoofA : RoofB;
            // Front parapets with gaps (s is the parapet line, dir points toward the enemy).
            void Parapet(Side side, double s)
            {
                double[][] segs = { V(-46, -30), V(-26, -6), V(-2, 18), V(22, 46) };
                for (int i = 0; i < segs.Length; i++)
                {
                    double x0 = segs[i][0], x1 = segs[i][1];
                    cover.Add(Block($"{side}-parapet-{i}", (x0 + x1) / 2, s, x1 - x0, 0.35, 1.05, ground, CoverMaterial.Hard, CoverLook.Concrete, Roof(side)));
                }
            }
            Parapet(Side.A, 56.6);
            Parapet(Side.B, 381.4);
            // Roof furniture: stair housings (concrete) and AC units (sheet metal).
            var furniture = new (Side side, string kind, double x, double s, double w, double d, double h, CoverMaterial material, CoverLook look)[]
            {
                (Side.A, "stairs", -30, 30, 3.2, 3.2, 2.8, CoverMaterial.Hard, CoverLook.Concrete),
                (Side.A, "ac", -8, 46, 2.2, 1.4, 1.5, CoverMaterial.Soft, CoverLook.Metal),
                (Side.A, "ac", 12, 38, 2.2, 1.4, 1.5, CoverMaterial.Soft, CoverLook.Metal),
                (Side.A, "stairs", 30, 22, 3.2, 3.2, 2.8, CoverMaterial.Hard, CoverLook.Concrete),
                (Side.A, "ac", 36, 48, 2.2, 1.4, 1.5, CoverMaterial.Soft, CoverLook.Metal),
                (Side.B, "stairs", 28, 408, 3.2, 3.2, 2.8, CoverMaterial.Hard, CoverLook.Concrete),
                (Side.B, "ac", 6, 392, 2.2, 1.4, 1.5, CoverMaterial.Soft, CoverLook.Metal),
                (Side.B, "ac", -14, 400, 2.2, 1.4, 1.5, CoverMaterial.Soft, CoverLook.Metal),
                (Side.B, "stairs", -32, 418, 3.2, 3.2, 2.8, CoverMaterial.Hard, CoverLook.Concrete),
                (Side.B, "ac", -36, 392, 2.2, 1.4, 1.5, CoverMaterial.Soft, CoverLook.Metal),
            };
            for (int i = 0; i < furniture.Length; i++)
            {
                var f = furniture[i];
                cover.Add(Block($"{f.side}-{f.kind}-{i}", f.x, f.s, f.w, f.d, f.h, ground, f.material, f.look, Roof(f.side)));
                // Firing spots: beside the AC units (peek round the metal), not behind the stairs (blind).
                if (f.kind == "ac") spots[f.side].Add(new Vec3(f.x + f.w / 2 + 0.9, Roof(f.side), -f.s));
            }
            // And the gaps in the parapets, the best views and the most exposed.
            foreach (double x in new double[] { -28, -4, 20 })
            {
                spots.A.Add(new Vec3(x, RoofA, -55));
                spots.B.Add(new Vec3(x, RoofB, -383));
            }
            // Buildings on the quays break up the sight lines between the roofs.
            cover.Add(Block("warehouse", -24, 100, 30, 26, 10, ground, CoverMaterial.Hard, CoverLook.Concrete));
            cover.Add(Block("crane-tower", 32, 128, 10, 10, 26, ground, CoverMaterial.Hard, CoverLook.Concrete));
            cover.Add(Block("office", -30, 312, 22, 18, 25, ground, CoverMaterial.Hard, CoverLook.Concrete));
            cover.Add(Block("shed", 22, 330, 18, 12, 7, ground, CoverMaterial.Hard, CoverLook.Concrete));
            return new DuelMap
            {
                Id = "harbor-rooftops",
                Name = "Harbor Rooftops",
                LaneLength = 450,
                Zones = new PerSide<Zone>(new Zone(-42, 42, 4, 54, 54), new Zone(-42, 42, 384, 434, 384)),
                Ground = ground,
                Cover = cover,
                Spots = spots,
                WindEdges = V(150, 300),
                Climate = new Climate
                {
                    AltitudeM = 5,
                    TemperatureC = V(12, 26),
                    Humidity = V(0.65, 0.95),
                    WindSpeedMps = V(1, 4.5),
                    WindToward = Math.PI / 2,
                    WindSpread = 1.6,
                    GustAmplitude = 0.5,
                    GustPeriodSeconds = 6,
                },
                Latitude = 44,
                Look = new MapLook
                {
                    Flags = new[] { V(-52, 60), V(55, 230), V(-52, 375) },
                    LaneHalfWidth = 60,
                    Palette = new Palette
                    {
                        Sky = SkyKind.Clear,
                        SunAzimuthDeg = 140,
                        Lane = V(0.08, 0.05, 0.5),
                        Outside = V(0.58, 0.25, 0.42),
                        Water = V(0.56, 0.35, 0.32),
                        Grass = "#8f9b6b",
                        Hills = "#7f8a93",
                        Rocks = "#8d8a85",
                    },
                },
            };
        }

        /// <summary>
        /// Alpine Ridge (GAME_PLAN launch map): 900–1,100 m from a shoulder at 2,400 m, across a gully, up
        /// to positions 110–150 m higher on the far slope. Cold, thin air shoots flat; the climb makes every shot an incline shot;
        /// gusty wind. Log piles are wood: cover a .50 shoots through.
        /// </summary>
        static double AlpineGround(double x, double z)
        {
            double s = -z;
            double shoulder = 6 * (1 - Smoothstep(60, 160, s));
            double gully = -55 * Smoothstep(150, 420, s) * (1 - Smoothstep(560, 860, s));
            // A steady climb from the gully floor to the summit behind B, so B's zone faces A down the slope
            // instead of hiding behind a crest.
            double climb = Math.Min(1, Math.Max(0, (s - 560) / 560));
            double ridge = 150 * Math.Pow(climb, 1.15);
            double bumps = 1.2 * Math.Sin(x * 0.05 + s * 0.02) * Math.Cos(s * 0.013);
            double flanks = Smoothstep(90, 260, Math.Abs(x)) * (40 + 30 * Math.Sin(x * 0.006 + s * 0.003));
            return shoulder + gully + ridge + bumps + flanks;
        }

        static DuelMap MakeAlpineRidge()
        {
            Ground ground = AlpineGround;
            var cover = new List<Box>();
            var spots = new PerSide<List<Vec3>>(new List<Vec3>(), new List<Vec3>());
            var layoutA = new (double x, double s, double h, double w, CoverMaterial material)[]
            {
                (-44, 82, 1.6, 2.8, CoverMaterial.Hard),
                (-20, 66, 1.1, 2.2, CoverMaterial.Hard),
                (0, 88, 1.0, 3.6, CoverMaterial.Soft),
                (22, 72, 1.5, 2.6, CoverMaterial.Hard),
                (46, 86, 1.2, 2.2, CoverMaterial.Hard),
            };
            for (int i = 0; i < layoutA.Length; i++)
            {
                var (x, s, h, w, material) = layoutA[i];
                cover.Add(Block($"A-cover-{i}", x, s, w, 1.6, h, ground, material, material == CoverMaterial.Soft ? CoverLook.Wood : CoverLook.Rock));
                spots.A.Add(new Vec3(x + (i % 2 == 0 ? 1.9 : -1.9), ground(x, -(s - 1.9)), -(s - 1.9)));
            }
            var layoutB = new (double x, double s, double h, double w, CoverMaterial material)[]
            {
                (-42, 1012, 1.4, 2.6, CoverMaterial.Hard),
                (-16, 1026, 1.2, 2.4, CoverMaterial.Hard),
                (4, 1008, 1.0, 3.6, CoverMaterial.Soft),
                (24, 1020, 1.6, 2.8, CoverMaterial.Hard),
                (44, 1010, 1.1, 2.0, CoverMaterial.Hard),
            };
            for (int i = 0; i < layoutB.Length; i++)
            {
                var (x, s, h, w, material) = layoutB[i];
                cover.Add(Block($"B-cover-{i}", x, s, w, 1.6, h, ground, material, material == CoverMaterial.Soft ? CoverLook.Wood : CoverLook.Rock));
                spots.B.Add(new Vec3(x + (i % 2 == 0 ? -1.9 : 1.9), ground(x, -(s + 1.9)), -(s + 1.9)));
            }
            return new DuelMap
            {
                Id = "alpine-ridge",
                Name = "Alpine Ridge",
                LaneLength = 1100,
                Zones = new PerSide<Zone>(new Zone(-60, 60, 0, 95, 95), new Zone(-60, 60, 1000, 1095, 1000)),
                Ground = ground,
                Cover = cover,
                Spots = spots,
                WindEdges = V(370, 730),
                Climate = new Climate
                {
                    AltitudeM = 2400,
                    TemperatureC = V(-10, 8),
                    Humidity = V(0.3, 0.7),
                    WindSpeedMps = V(2, 9),
                    WindToward = Math.PI / 2,
                    WindSpread = 0.7,
                    GustAmplitude = 0.45,
                    GustPeriodSeconds = 8,
                },
                Latitude = 46,
                Look = new MapLook
                {
                    Flags = new[] { V(-72, 200), V(74, 560), V(-74, 930) },
                    LaneHalfWidth = 75,
                    Palette = new Palette
                    {
                        Sky = SkyKind.Clear,
                        SunAzimuthDeg = 80,
                        Lane = V(0.3, 0.18, 0.5),
                        Outside = V(0.6, 0.08, 0.78),
                        Grass = "#8fa06a",
                        Hills = "#dfe6ec",
                        Rocks = "#7d7f82",
                    },
                },
            };
        }

        // ---- fixed-position maps -------------------------------------------------------------------
        // Both players stand in the open on a platform with nothing but water between them. No moving, no
        // hiding: the duel is the wind call, the range and the trigger.

        /// <summary>Hit-test ground: the terrain, with every platform top as a hard plateau.</summary>
        static Ground WithPlatforms(Ground terrain, Structure[] structures) => (x, z) =>
        {
            double s = -z;
            double y = terrain(x, z);
            foreach (var st in structures)
            {
                if (st.Kind == StructureKind.Bridge)
                {
                    if (Math.Abs(x) <= st.HalfSpan && Math.Abs(s - st.S) <= st.HalfWidth) y = Math.Max(y, st.DeckY);
                }
                else if (Math.Abs(x - st.X) <= st.HalfSize && Math.Abs(s - st.S) <= st.HalfSize) y = Math.Max(y, st.TopY);
            }
            return y;
        };

        /// <summary>
        /// The walkable top of each side's structure (A's is the first, B's the second): `halfX` either
        /// way across the lane and `halfS` along it from the structure's centre line.
        /// </summary>
        static PerSide<Zone> RoamZones(Structure[] structures, double halfX, double halfS)
        {
            Zone On(Structure st, bool facesUp) =>
                new Zone(st.X - halfX, st.X + halfX, st.S - halfS, st.S + halfS, facesUp ? st.S + halfS : st.S - halfS);
            return new PerSide<Zone>(On(structures[0], true), On(structures[1], false));
        }

        static PerSide<Zone> FixedZones(Vec3 a, Vec3 b) =>
            new PerSide<Zone>(new Zone(a.X, a.X, -a.Z, -a.Z, -a.Z), new Zone(b.X, b.X, -b.Z, -b.Z, -b.Z));

        /// <summary>
        /// Twin Bridges: two road bridges 640 m apart across a wide, slow estuary. Each sniper stands at
        /// mid-span, 24 m over the water, with the whole river between them. Sea level, a steady wind
        /// along the valley: a crosswind with nothing to break it.
        /// </summary>
        static DuelMap MakeTwinBridges()
        {
            const double Deck = 24;
            Ground terrain = (x, z) =>
            {
                double s = -z;
                double ax = Math.Abs(x);
                // River bed between the banks, then the banks climb to the road level and on into hills.
                double bed = -8 * (1 - Smoothstep(180, 230, ax));
                double bank = Deck * Smoothstep(200, 290, ax);
                double hills = Smoothstep(300, 700, ax) * (30 + 25 * Math.Sin(ax * 0.004 + s * 0.003) + 12 * Math.Cos(s * 0.007 + x * 0.002));
                double roll = 2 * Math.Sin(s * 0.01) * Smoothstep(240, 400, ax);
                return bed + bank + hills + roll;
            };
            var structures = new[]
            {
                Structure.Bridge(0, Deck, 210, 7, V(-90, 90)),
                Structure.Bridge(640, Deck, 210, 7, V(-90, 90)),
            };
            var a = new Vec3(0, Deck, -2);
            var b = new Vec3(0, Deck, -638);
            return new DuelMap
            {
                Id = "twin-bridges",
                Name = "Twin Bridges",
                Blurb = "Mid-span to mid-span across an estuary, 640 m",
                LaneLength = 640,
                Fixed = true,
                Water = 0,
                Terrain = terrain,
                Zones = FixedZones(a, b),
                // The road between the guardrails, out to the barriers either side of the spot.
                Roam = RoamZones(structures, 5, 4.6),
                Ground = WithPlatforms(terrain, structures),
                Cover = new List<Box>(),
                Spots = new PerSide<List<Vec3>>(new List<Vec3> { a }, new List<Vec3> { b }),
                WindEdges = V(215, 425),
                Climate = new Climate
                {
                    AltitudeM = 4,
                    TemperatureC = V(9, 24),
                    Humidity = V(0.55, 0.9),
                    WindSpeedMps = V(3, 8),
                    WindToward = Math.PI / 2,
                    WindSpread = 0.35,
                    GustAmplitude = 0.2,
                    GustPeriodSeconds = 11,
                },
                Latitude = 48,
                Look = new MapLook
                {
                    Flags = new[] { V(-26, 110), V(26, 320), V(-26, 530) },
                    LaneHalfWidth = 180,
                    Structures = structures,
                    Palette = new Palette
                    {
                        Sky = SkyKind.Clear,
                        SunAzimuthDeg = 125,
                        Lane = V(0.3, 0.3, 0.42),
                        Outside = V(0.27, 0.32, 0.38),
                        Water = V(0.55, 0.4, 0.3),
                        Grass = "#7f9a4f",
                        Hills = "#6f8a66",
                        Rocks = "#8a8580",
                        Vegetation = Vegetation.Pine,
                    },
                },
            };
        }

        /// <summary>
        /// Watchtowers: two concrete watchtowers standing in a flooded marsh, 450 m apart, open platforms
        /// 22 m up. Reeds and dead pines all round, the air damp and almost still, then a gust.
        /// </summary>
        static DuelMap MakeWatchtowers()
        {
            const double Top = 22;
            Ground terrain = (x, z) =>
            {
                double s = -z;
                double dx = x;
                double ds = s - 225;
                double r = JsMath.Hypot(dx * 0.75, ds);
                // Marsh floor under the water in the middle, reed islands, then wooded shores beyond 360 m.
                const double floor = -3;
                double shore = 5 * Smoothstep(340, 460, r) + Smoothstep(420, 800, r) * (22 + 14 * Math.Sin(x * 0.006 + s * 0.004) + 8 * Math.Cos(s * 0.009));
                double islets = 0.9 * Math.Max(0, Math.Sin(x * 0.045) * Math.Sin(s * 0.052) - 0.55) * 8 * (1 - Smoothstep(300, 340, r));
                return floor + shore + islets;
            };
            var structures = new[]
            {
                Structure.Tower(0, 0, Top, 3, TowerStyle.Concrete),
                Structure.Tower(0, 450, Top, 3, TowerStyle.Concrete),
            };
            var a = new Vec3(0, Top, -1.2);
            var b = new Vec3(0, Top, -448.8);
            return new DuelMap
            {
                Id = "watchtowers",
                Name = "Watchtowers",
                Blurb = "Tower to tower over a drowned marsh, 450 m",
                LaneLength = 450,
                Fixed = true,
                Water = 0,
                Terrain = terrain,
                Zones = FixedZones(a, b),
                Roam = RoamZones(structures, 2.3, 2.3),
                Ground = WithPlatforms(terrain, structures),
                Cover = new List<Box>(),
                Spots = new PerSide<List<Vec3>>(new List<Vec3> { a }, new List<Vec3> { b }),
                WindEdges = V(150, 300),
                Climate = new Climate
                {
                    AltitudeM = 180,
                    TemperatureC = V(4, 18),
                    Humidity = V(0.7, 0.98),
                    WindSpeedMps = V(0.5, 5),
                    WindToward = Math.PI / 2,
                    WindSpread = 1.2,
                    GustAmplitude = 0.55,
                    GustPeriodSeconds = 7,
                },
                Latitude = 52,
                Look = new MapLook
                {
                    Flags = new[] { V(-30, 90), V(34, 225), V(-30, 360) },
                    LaneHalfWidth = 160,
                    Structures = structures,
                    Palette = new Palette
                    {
                        Sky = SkyKind.Clear,
                        SunAzimuthDeg = 95,
                        Lane = V(0.22, 0.28, 0.34),
                        Outside = V(0.2, 0.3, 0.3),
                        Water = V(0.46, 0.25, 0.22),
                        Grass = "#9a9b55",
                        Hills = "#55684f",
                        Rocks = "#7d7c74",
                        Vegetation = Vegetation.Pine,
                    },
                },
            };
        }

        /// <summary>
        /// Oil Rigs: two production platforms 900 m apart in open sea, helidecks 34 m above the swell.
        /// Nothing between them but wind: strong, steady and cold. The .338 and .50 map.
        /// </summary>
        static DuelMap MakeOilRigs()
        {
            const double Top = 34;
            Ground terrain = (x, z) => -40;
            var structures = new[]
            {
                Structure.Tower(0, 0, Top, 18, TowerStyle.Rig),
                Structure.Tower(0, 900, Top, 18, TowerStyle.Rig),
            };
            var a = new Vec3(0, Top, -14);
            var b = new Vec3(0, Top, -886);
            return new DuelMap
            {
                Id = "oil-rigs",
                Name = "Oil Rigs",
                Blurb = "Helideck to helideck in the open sea, 870 m",
                LaneLength = 900,
                Fixed = true,
                Water = 0,
                Terrain = terrain,
                Zones = FixedZones(a, b),
                Roam = RoamZones(structures, 15, 15),
                Ground = WithPlatforms(terrain, structures),
                Cover = new List<Box>(),
                Spots = new PerSide<List<Vec3>>(new List<Vec3> { a }, new List<Vec3> { b }),
                WindEdges = V(300, 600),
                Climate = new Climate
                {
                    AltitudeM = 0,
                    TemperatureC = V(2, 14),
                    Humidity = V(0.7, 0.95),
                    WindSpeedMps = V(6, 13),
                    WindToward = Math.PI / 2,
                    WindSpread = 0.3,
                    GustAmplitude = 0.18,
                    GustPeriodSeconds = 16,
                },
                Latitude = 58,
                Look = new MapLook
                {
                    Flags = new[] { V(-24, 150), V(24, 450), V(-24, 750) },
                    LaneHalfWidth = 200,
                    Structures = structures,
                    Palette = new Palette
                    {
                        Sky = SkyKind.Sunset,
                        SunAzimuthDeg = 95,
                        Lane = V(0.58, 0.3, 0.22),
                        Outside = V(0.58, 0.3, 0.22),
                        Water = V(0.58, 0.32, 0.2),
                        Grass = "#7f8f70",
                        Hills = "#6c7a84",
                        Rocks = "#777",
                        Vegetation = Vegetation.None,
                    },
                },
            };
        }

        static readonly Lazy<Dictionary<string, DuelMap>> Registry = new Lazy<Dictionary<string, DuelMap>>(() =>
        {
            var list = new[]
            {
                MakeTwinBridges(),
                MakeWatchtowers(),
                MakeOilRigs(),
                MakeDryValley(),
                MakeHarborRooftops(),
                MakeAlpineRidge(),
                MakeTrainingLane(),
            };
            var d = new Dictionary<string, DuelMap>();
            foreach (var m in list) d[m.Id] = m;
            return d;
        });

        /// <summary>Map ids in picker order (same order as the TS MAPS record).</summary>
        public static readonly string[] Ids = { "twin-bridges", "watchtowers", "oil-rigs", "dry-valley", "harbor-rooftops", "alpine-ridge", "training-lane" };

        public static IReadOnlyDictionary<string, DuelMap> All => Registry.Value;

        public const string DefaultMap = "twin-bridges";

        public static DuelMap ById(string id)
        {
            if (!Registry.Value.TryGetValue(id, out var m)) throw new ArgumentException($"Unknown map {id}");
            return m;
        }

        public static double LaneDistance(Vec3 p) => -p.Z;

        /// <summary>The usable part of a side's zone: during sudden death it shrinks toward the front edge.</summary>
        public static Zone UsableZone(DuelMap map, Side side, double shrink, double minDepth)
        {
            var z = map.Zones[side];
            double depth = Math.Max(minDepth, (z.SMax - z.SMin) * (1 - shrink));
            var c = z.Clone();
            if (side == Side.A)
            {
                c.SMin = z.Front - depth;
                c.SMax = z.Front;
            }
            else
            {
                c.SMin = z.Front;
                c.SMax = z.Front + depth;
            }
            return c;
        }

        /// <summary>Where a side may move freely during the duel: its roaming area, else its zone.</summary>
        public static Zone RoamZone(DuelMap map, Side side) => map.Roam?[side] ?? map.Zones[side];

        /// <summary>Clamp a world position into a zone and onto the ground, outside any cover box.</summary>
        public static Vec3 ClampToZone(DuelMap map, Zone zone, Vec3 p)
        {
            double x = Math.Min(zone.XMax, Math.Max(zone.XMin, p.X));
            double s = Math.Min(zone.SMax, Math.Max(zone.SMin, -p.Z));
            foreach (var box in map.Cover)
            {
                bool inside = x > box.Min.X && x < box.Max.X && -s > box.Min.Z && -s < box.Max.Z;
                if (inside)
                {
                    // Step out the short way, toward the player's own rear.
                    double toLeft = x - box.Min.X;
                    double toRight = box.Max.X - x;
                    double dz = Math.Min(-s - box.Min.Z, box.Max.Z + s);
                    if (dz <= Math.Min(toLeft, toRight))
                    {
                        double rear = zone.Front == zone.SMax ? box.Max.Z + 0.3 : box.Min.Z - 0.3; // A's rear is +z
                        s = -rear;
                    }
                    else
                    {
                        x = toLeft < toRight ? box.Min.X - 0.3 : box.Max.X + 0.3;
                    }
                }
            }
            s = Math.Min(zone.SMax, Math.Max(zone.SMin, s));
            return new Vec3(x, map.Ground(x, -s), -s);
        }
    }
}
