using SniperDuel.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The ground: a Unity Terrain sampled from the map's terrain function (the same function the hit
    /// test uses), painted from height and slope with photo-scanned ground textures, and the water.
    /// </summary>
    public static class Land
    {
        public const float Extent = 4096;
        const int Res = 1025;
        const float MinY = -20, MaxY = 220;

        public static Terrain Build(DuelMap map, Transform parent)
        {
            var ground = map.Terrain ?? map.Ground;
            float centreS = (float)map.LaneLength / 2;
            var origin = new Vector3(-Extent / 2, MinY, centreS - Extent / 2);

            var data = new TerrainData { heightmapResolution = Res };
            data.size = new Vector3(Extent, MaxY - MinY, Extent);
            var h = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
            {
                float s = origin.z + iz * Extent / (Res - 1);
                for (int ix = 0; ix < Res; ix++)
                {
                    float x = origin.x + ix * Extent / (Res - 1);
                    double y = ground(x, -s);
                    h[iz, ix] = Mathf.Clamp01(((float)y - MinY) / (MaxY - MinY));
                }
            }
            data.SetHeights(0, 0, h);

            // Each map's own ground: four photo-scanned layers mixed by height, slope and noise (SoilFor).
            var soil = SoilFor(map);
            var layers = new TerrainLayer[soil.Layers.Length];
            for (int k = 0; k < layers.Length; k++) layers[k] = Layer(soil.Layers[k]);
            data.terrainLayers = layers;
            int A = soil.Resolution;
            data.alphamapResolution = A;
            var alpha = new float[A, A, layers.Length];
            var w = new float[layers.Length];
            float water = (float)(map.Water ?? -1000);
            for (int iz = 0; iz < A; iz++)
            {
                for (int ix = 0; ix < A; ix++)
                {
                    float nx = (ix + 0.5f) / A, nz = (iz + 0.5f) / A;
                    float y = data.GetInterpolatedHeight(nx, nz) + MinY;
                    float slope = data.GetSteepness(nx, nz);
                    float n1 = Mathf.PerlinNoise(nx * 60, nz * 60);
                    float n2 = Mathf.PerlinNoise(nx * 140 + 17.3f, nz * 140 + 5.1f);
                    soil.Mix(origin.x + nx * Extent, origin.z + nz * Extent, y, slope, n1, n2, water, w);
                    float sum = 1e-5f;
                    for (int k = 0; k < w.Length; k++) sum += w[k] = Mathf.Max(0, w[k]);
                    for (int k = 0; k < w.Length; k++) alpha[iz, ix, k] = w[k] / sum;
                }
            }
            data.SetAlphamaps(0, 0, alpha);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = new Material(Library.Template("Terrain"));
            terrain.heightmapPixelError = soil.PixelError;
            terrain.basemapDistance = 600;
            // Instanced terrain needs shader variants the template material doesn't carry, so the
            // build strips them and the terrain draws nothing; the plain path uses the template's.
            terrain.drawInstanced = false;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
            return terrain;
        }

        static TerrainLayer Layer((string id, string fallback, float tile, Color tint) l)
        {
            // A texture the asset fetch couldn't get falls back to one it always has, tinted to suit.
            string id = Library.Load<Texture2D>($"TP/Textures/{l.id}/{l.id}_diff_2k") != null ? l.id : l.fallback;
            var tint = id == l.id ? l.tint : l.tint * FallbackTint(l.id);
            var layer = new TerrainLayer
            {
                diffuseTexture = Library.Load<Texture2D>($"TP/Textures/{id}/{id}_diff_2k"),
                normalMapTexture = Library.Load<Texture2D>($"TP/Textures/{id}/{id}_nor_gl_2k"),
                maskMapTexture = Library.Load<Texture2D>($"Generated/{id}_mask"),
                tileSize = new Vector2(l.tile, l.tile),
                normalScale = 1,
                smoothness = 0.2f,
                diffuseRemapMin = Vector4.zero,
                diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1),
            };
            return layer;
        }

        /// <summary>Snow drawn with the sand texture until the snow scan is downloaded.</summary>
        static Color FallbackTint(string missing) => missing.StartsWith("snow") ? new Color(1.45f, 1.5f, 1.65f) : Color.white;

        /// <summary>Fills one weight per layer at a ground point: world x, lane distance s, height, slope (deg), two noises (0–1), water level.</summary>
        delegate void Mix(float x, float s, float y, float slope, float n1, float n2, float water, float[] w);

        /// <summary>A map's ground look: four texture layers (Poly Haven id, fallback id, tile metres, tint) and how they mix.</summary>
        sealed class Soil
        {
            public (string id, string fallback, float tile, Color tint)[] Layers = null!;
            public Mix Mix = null!;
            /// <summary>Splat texels across the 4 km terrain: 1024 paints 4 m detail (tracks, firing pads).</summary>
            public int Resolution = 512;
            /// <summary>Terrain LOD tolerance in pixels; tight where buildings must hide the terrain's steps.</summary>
            public float PixelError = 4;
        }

        static readonly Color White = Color.white;

        static Soil SoilFor(DuelMap map) => map.Id switch
        {
            // Gravel desert floor, wind-blown sand in the wash and drifts, stony red hardpan on the benches
            // and along the track, warm rock on every steep face.
            "dry-valley" => new Soil
            {
                Resolution = 1024,
                Layers = new[]
                {
                    ("gravelly_sand", "coast_sand_01", 7f, new Color(1.02f, 0.93f, 0.8f)),
                    ("coast_sand_01", "coast_sand_01", 9f, new Color(1.08f, 0.94f, 0.76f)),
                    ("red_laterite_soil_stones", "brown_mud_leaves_01", 8f, new Color(1f, 0.88f, 0.76f)),
                    ("rock_boulder_dry", "red_laterite_soil_stones", 16f, new Color(1.05f, 0.9f, 0.76f)),
                },
                Mix = (x, s, y, slope, n1, n2, water, w) =>
                {
                    float rock = Mathf.Clamp01((slope - 20) / 10);
                    float q = (s - 430 - 25 * Mathf.Sin(x * 0.02f)) / 38;
                    float wash = Mathf.Exp(-q * q);
                    float track = Mathf.Clamp01(1 - (Mathf.Abs(x - DryValley.TrackX(s)) - 2.5f) / 3f);
                    float drift = Mathf.Clamp01((n1 - 0.58f) * 4) + wash * 1.3f;
                    float pan = Mathf.Clamp01((n2 - 0.5f) * 3) * (1 - wash) + track * 4;
                    w[0] = Mathf.Max(0.15f, 1 - drift - pan) * (1 - rock);
                    w[1] = drift * (1 - rock) * (1 - track);
                    w[2] = pan * (1 - rock);
                    w[3] = rock * 1.5f;
                },
            },
            // Meadow on the shoulder, scree on the slopes and the gully floor, cliff rock on steep faces,
            // snow above a ragged snow line except where it is too steep to lie.
            "alpine-ridge" => new Soil
            {
                Resolution = 1024,
                Layers = new[]
                {
                    ("sparse_grass", "sparse_grass", 10f, new Color(0.86f, 0.9f, 0.74f)),
                    ("rocky_terrain_02", "rocky_terrain_02", 12f, new Color(0.9f, 0.9f, 0.92f)),
                    ("cliff_side", "aerial_rocks_02", 20f, new Color(0.85f, 0.86f, 0.9f)),
                    ("snow_02", "coast_sand_01", 9f, new Color(0.95f, 0.96f, 0.99f)),
                },
                Mix = (x, s, y, slope, n1, n2, water, w) =>
                {
                    float snowLine = AlpineRidge.SnowLine + (n1 - 0.5f) * 44;
                    float cliff = Mathf.Clamp01((slope - 30) / 9);
                    // The snowfield's edge melts out over ~40 m: patches and tongues (a finer noise) lie
                    // on wet scree below the line, bare islands show through above it, rather than one
                    // hard white edge on the meadow.
                    float patch = Mathf.PerlinNoise(x * 0.06f + 11.3f, s * 0.06f + 4.7f) * 0.6f + n2 * 0.4f;
                    float cover = (y - snowLine) / 40 + 0.5f;
                    float snow = Mathf.SmoothStep(0, 1, Mathf.Clamp01((cover + (patch - 0.5f) * 0.9f - 0.35f) / 0.3f))
                        * (1 - Mathf.Clamp01((slope - 30) / 12));
                    float fringe = Mathf.Clamp01(1 - Mathf.Abs(cover - 0.4f) * 2.2f) * (1 - snow);
                    float scree = Mathf.Clamp01((slope - 17) / 10) * (1 - cliff) + Mathf.Clamp01((n2 - 0.6f) * 3) * 0.6f
                        + Mathf.Clamp01((-40 - y) / 8) + fringe * 0.9f;
                    w[0] = Mathf.Max(0, 1 - snow - cliff - scree);
                    w[1] = scree * (1 - snow);
                    w[2] = cliff;
                    w[3] = snow;
                },
            },
            // A mown range: gravel firing pads at both lines and a gravel service road, bare earth on
            // banks and in worn patches, rock only on the steepest hillsides.
            "training-lane" => new Soil
            {
                Resolution = 1024,
                Layers = new[]
                {
                    ("sparse_grass", "sparse_grass", 8f, new Color(0.95f, 1.04f, 0.86f)),
                    ("gravelly_sand", "coast_sand_rocks_02", 5f, new Color(0.92f, 0.9f, 0.86f)),
                    ("brown_mud_leaves_01", "brown_mud_leaves_01", 9f, White),
                    ("rocky_terrain_02", "rocky_terrain_02", 14f, White),
                },
                Mix = (x, s, y, slope, n1, n2, water, w) =>
                {
                    float pads = Band(x, -58, 58, 2) * (Band(s, TrainingRange.LineA - 22, TrainingRange.LineA + 16, 2) + Band(s, TrainingRange.LineB - 16, TrainingRange.LineB + 22, 2));
                    float road = Mathf.Clamp01(1 - (Mathf.Abs(x - TrainingRange.RoadX) - 3) / 1.5f);
                    float rock = Mathf.Clamp01((slope - 30) / 8);
                    float earth = Mathf.Clamp01((slope - 14) / 8) * (1 - rock) + Mathf.Clamp01((n2 - 0.72f) * 4) * 0.7f;
                    w[0] = Mathf.Max(0, 1 - earth - rock);
                    w[1] = Mathf.Max(pads, road) * 4;
                    w[2] = earth;
                    w[3] = rock;
                },
            },
            // Asphalt streets and yards, paved concrete lots, silt on the channel bed, bare dirt plots.
            "harbor-rooftops" => new Soil
            {
                // The roofs are steps in the terrain; coarse LODs would push them out through the facades.
                PixelError = 1,
                Layers = new[]
                {
                    ("asphalt_02", "asphalt_02", 8f, White),
                    ("concrete_wall_008", "concrete_wall_008", 6f, new Color(0.78f, 0.78f, 0.76f)),
                    ("coast_sand_rocks_02", "coast_sand_rocks_02", 10f, new Color(0.7f, 0.68f, 0.62f)),
                    ("brown_mud_leaves_01", "brown_mud_leaves_01", 9f, White),
                },
                Mix = (x, s, y, slope, n1, n2, water, w) =>
                {
                    float bed = Mathf.Clamp01((-0.4f - y) / 1f);
                    float lots = n1 > 0.52f ? 1 : 0;
                    float dirt = Mathf.Clamp01((n2 - 0.74f) * 5);
                    w[0] = (1 - lots) * (1 - bed);
                    w[1] = lots * (1 - bed) * (1 - dirt);
                    w[2] = bed * 3;
                    w[3] = dirt * (1 - bed);
                },
            },
            // An estuary in a city: rip-rap and wet silt at the waterline, mown grass up the banks and
            // over the land, a little bare earth where it's worn, rock only on the steepest ground.
            "twin-bridges" => new Soil
            {
                Resolution = 1024,
                PixelError = 2,
                Layers = new[]
                {
                    ("coast_sand_rocks_02", "coast_sand_rocks_02", 6f, new Color(0.72f, 0.7f, 0.66f)),
                    // Leafy_grass is tan with fallen leaves; tinted, it reads as a mown lawn from the bridges.
                    ("leafy_grass", "sparse_grass", 7f, new Color(0.31f, 0.48f, 0.21f)),
                    ("sparse_grass", "sparse_grass", 9f, new Color(0.58f, 0.68f, 0.42f)),
                    ("rocky_terrain_02", "rocky_terrain_02", 12f, White),
                },
                Mix = (x, s, y, slope, n1, n2, water, w) =>
                {
                    float shore = Mathf.Clamp01(1 - (y - water - 2.2f) / 1.5f);
                    float rock = Mathf.Clamp01((slope - 32) / 8f) * (1 - shore);
                    // Patches of coarser, drier grass so the lawns aren't one flat colour.
                    float worn = Mathf.Clamp01((n1 - 0.45f) * 2.5f) * 0.6f * (1 - shore) * (1 - rock);
                    w[0] = shore * 2;
                    w[1] = Mathf.Max(0, 1 - shore - rock - worn);
                    w[2] = worn;
                    w[3] = rock;
                },
            },
            // Elsewhere: wet sand at the waterline, grass on gentle slopes, rock on steep ones, leaf litter in hollows.
            _ => new Soil
            {
                Layers = new[]
                {
                    ("coast_sand_rocks_02", "coast_sand_rocks_02", 8f, White),
                    ("sparse_grass", "sparse_grass", 10f, White),
                    ("rocky_terrain_02", "rocky_terrain_02", 14f, White),
                    ("brown_mud_leaves_01", "brown_mud_leaves_01", 9f, White),
                },
                Mix = (x, s, y, slope, n1, n2, water, w) =>
                {
                    float sand = Mathf.Clamp01(1 - (y - water - 1.5f) / 3f);
                    float rock = Mathf.Clamp01((slope - 24) / 12f);
                    float litter = Mathf.Clamp01((n1 - 0.55f) * 4) * (1 - rock);
                    w[0] = sand;
                    w[1] = Mathf.Max(0, 1 - sand - rock - litter);
                    w[2] = rock;
                    w[3] = litter;
                },
            },
        };

        /// <summary>1 inside [lo, hi], fading to 0 over `soft` metres outside it.</summary>
        static float Band(float v, float lo, float hi, float soft) => Mathf.Clamp01((v - lo) / soft + 1) * Mathf.Clamp01((hi - v) / soft + 1);

        /// <summary>Open water: a large plane with two scrolling wave normal layers; see Waves.</summary>
        public static GameObject Water(DuelMap map, Transform parent)
        {
            var mb = new MeshBuilder { UvScale = 1 };
            float centreS = (float)map.LaneLength / 2;
            mb.Plane(new Vector3(0, (float)(map.Water ?? 0), centreS), Extent, Extent);
            var mat = new Material(Library.Template("Water"));
            mat.SetTextureScale("_BaseMap", Vector2.one / 24f);
            mat.SetTextureScale("_DetailAlbedoMap", Vector2.one / 9f);
            if (map.Id == "twin-bridges")
            {
                // A wide, slow estuary: long low swell with fine chop on it, silty green-grey.
                mat.SetTextureScale("_BaseMap", Vector2.one / 46f);
                mat.SetTextureScale("_DetailAlbedoMap", Vector2.one / 13f);
                mat.SetFloat("_BumpScale", 0.75f);
                mat.SetFloat("_DetailNormalMapScale", 0.45f);
                mat.SetColor("_BaseColor", new Color(0.07f, 0.11f, 0.1f));
            }
            var go = mb.Object("Water", mat, parent, shadows: false);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
            go.AddComponent<Waves>().Material = mat;
            return go;
        }
    }

    /// <summary>Scrolls the water's two normal layers in different directions so the surface moves.</summary>
    public sealed class Waves : MonoBehaviour
    {
        public Material Material = null!;
        public Vector2 Flow = new Vector2(0.006f, 0.011f);

        void Update()
        {
            float t = Time.time;
            Material.SetTextureOffset("_BaseMap", Flow * t);
            Material.SetTextureOffset("_DetailAlbedoMap", new Vector2(-Flow.y, Flow.x) * 1.7f * t);
        }
    }
}
