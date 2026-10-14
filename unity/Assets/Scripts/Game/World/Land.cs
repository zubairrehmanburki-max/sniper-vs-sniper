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

            // Layers: wet sand at the waterline, grass on gentle slopes, rock on steep ones, leaf litter in hollows.
            var layers = new[]
            {
                Layer("coast_sand_rocks_02", 8),
                Layer("sparse_grass", 10),
                Layer("rocky_terrain_02", 14),
                Layer("brown_mud_leaves_01", 9),
            };
            data.terrainLayers = layers;
            const int A = 512;
            data.alphamapResolution = A;
            var alpha = new float[A, A, layers.Length];
            float water = (float)(map.Water ?? -1000);
            for (int iz = 0; iz < A; iz++)
            {
                for (int ix = 0; ix < A; ix++)
                {
                    float nx = (ix + 0.5f) / A, nz = (iz + 0.5f) / A;
                    float y = data.GetInterpolatedHeight(nx, nz) + MinY;
                    float slope = data.GetSteepness(nx, nz);
                    float noise = Mathf.PerlinNoise(nx * 60, nz * 60);
                    float sand = Mathf.Clamp01(1 - (y - water - 1.5f) / 3f);
                    float rock = Mathf.Clamp01((slope - 24) / 12f);
                    float litter = Mathf.Clamp01((noise - 0.55f) * 4) * (1 - rock);
                    float grass = Mathf.Max(0, 1 - sand - rock - litter);
                    float sum = sand + grass + rock + litter + 1e-5f;
                    alpha[iz, ix, 0] = sand / sum;
                    alpha[iz, ix, 1] = grass / sum;
                    alpha[iz, ix, 2] = rock / sum;
                    alpha[iz, ix, 3] = litter / sum;
                }
            }
            data.SetAlphamaps(0, 0, alpha);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = new Material(Library.Template("Terrain"));
            terrain.heightmapPixelError = 4;
            terrain.basemapDistance = 600;
            // Instanced terrain needs shader variants the template material doesn't carry, so the
            // build strips them and the terrain draws nothing; the plain path uses the template's.
            terrain.drawInstanced = false;
            terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
            return terrain;
        }

        static TerrainLayer Layer(string id, float tile)
        {
            var layer = new TerrainLayer
            {
                diffuseTexture = Library.Load<Texture2D>($"TP/Textures/{id}/{id}_diff_2k"),
                normalMapTexture = Library.Load<Texture2D>($"TP/Textures/{id}/{id}_nor_gl_2k"),
                maskMapTexture = Library.Load<Texture2D>($"Generated/{id}_mask"),
                tileSize = new Vector2(tile, tile),
                normalScale = 1,
                smoothness = 0.2f,
            };
            return layer;
        }

        /// <summary>Open water: a large plane with two scrolling wave normal layers; see Waves.</summary>
        public static GameObject Water(DuelMap map, Transform parent)
        {
            var mb = new MeshBuilder { UvScale = 1 };
            float centreS = (float)map.LaneLength / 2;
            mb.Plane(new Vector3(0, (float)(map.Water ?? 0), centreS), Extent, Extent);
            var mat = new Material(Library.Template("Water"));
            mat.SetTextureScale("_BaseMap", Vector2.one / 24f);
            mat.SetTextureScale("_DetailAlbedoMap", Vector2.one / 9f);
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
