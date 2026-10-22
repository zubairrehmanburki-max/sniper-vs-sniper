using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// The open sea round the oil rigs: a platform supply vessel standing by the far rig, two more
    /// production platforms on the horizon for scale, a tall ship passing, and the buoys that mark the
    /// platforms' safety zone. Also the flare flame the rigs burn.
    /// </summary>
    public static class Offshore
    {
        public static void Build(DuelMap map, Transform parent)
        {
            var root = new GameObject("Offshore").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(map.Id.GetHashCode() ^ 0x0ff5);
            float lane = (float)map.LaneLength;
            float water = (float)(map.Water ?? 0);
            float top = 34, hs = 18;
            if (map.Look.Structures != null && map.Look.Structures.Length > 0)
            {
                top = (float)map.Look.Structures[0].TopY;
                hs = (float)map.Look.Structures[0].HalfSize;
            }

            // Neighbouring platforms of the same field, hull-down on the horizon: one beyond the enemy
            // (seen in the scope), one behind the player (seen from the far rig).
            foreach (var (x, s, yaw) in new[] { (-1250f, lane + 1300f, 25f), (1500f, -900f, -60f) })
            {
                var far = new GameObject("Far rig").transform;
                far.SetParent(root, false);
                far.localPosition = new Vector3(x, 0, s);
                far.localRotation = Quaternion.Euler(0, yaw, 0);
                Structures.Rig(far, top, hs, water, false);
            }

            // A supply vessel holding station downwind of the far rig, bow into the wind.
            SupplyVessel(new Vector3(78, water, lane + 35), -90, root);

            // A tall ship passing well clear, and another supply boat inbound far off.
            var ship = Library.Place("dutch_ship_large_01", new Vector3(-420, water, lane + 520), 75, 1, root);
            Library.Ground(ship, water - 2.2f);
            SupplyVessel(new Vector3(-900, water, -300), 20, root);

            // Safety-zone marker buoys either side of the lane, clear of the firing line.
            for (int i = 0; i < 8; i++)
            {
                float s = lane * (i + 0.5f) / 8;
                float x = (i % 2 == 0 ? -1 : 1) * (95 + (float)rng.NextDouble() * 60);
                var buoy = Library.Place(i % 2 == 0 ? "ocean_buoy" : "lateral_sea_marker", new Vector3(x, water, s), (float)rng.NextDouble() * 360, 1.3f, root);
                Library.Ground(buoy, water - 0.6f);
                buoy.AddComponent<Bob>().Phase = i * 1.7f;
            }
            // Mooring buoys off each rig's boat landing.
            foreach (float s in new[] { -30f, lane + 30 })
            {
                var buoy = Library.Place("ocean_buoy", new Vector3(s < 0 ? -48 : 48, water, s), (float)rng.NextDouble() * 360, 1.1f, root);
                Library.Ground(buoy, water - 0.6f);
                buoy.AddComponent<Bob>().Phase = s;
            }
        }

        /// <summary>
        /// An 80 m platform supply vessel: red hull with a raised forecastle and black boot-top, white
        /// accommodation and bridge forward, twin funnels, a long open cargo deck aft with containers
        /// and pipe, crash rails, and a mast with radar. Built along local +z (bow).
        /// </summary>
        /// <summary>How far forward of the vessel's origin its bridge (and the bridge roof) is centred, m.</summary>
        internal const float BridgeZ = 17.5f;

        internal static void SupplyVessel(Vector3 position, float yaw, Transform parent)
        {
            var root = new GameObject("Supply vessel").transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            var hull = new MeshBuilder();
            var boot = new MeshBuilder();
            var deck = new MeshBuilder();
            var house = new MeshBuilder();
            var glass = new MeshBuilder();
            var cargo = new MeshBuilder();

            const float Bottom = -3.5f, BootTop = 0.8f, Stern = -40, Bow = 40;
            float HalfBeam(float z) => z <= 15 ? 9 : 9 * Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow((z - 15) / (Bow - 15), 2)));
            float Sheer(float z) => 3.2f + 2.8f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5, 15, z));
            var stations = new[] { Stern, -25, -10, 0, 5, 10, 15, 20, 25, 30, 34, 37, 39, Bow };
            for (int i = 0; i < stations.Length - 1; i++)
            {
                float z0 = stations[i], z1 = stations[i + 1];
                float b0 = HalfBeam(z0), b1 = HalfBeam(z1), s0 = Sheer(z0), s1 = Sheer(z1);
                foreach (float k in new[] { -1f, 1f })
                {
                    boot.Face(new Vector3(k * b0, Bottom, z0), new Vector3(k * b1, Bottom, z1), new Vector3(k * b1, BootTop, z1), new Vector3(k * b0, BootTop, z0), new Vector3(k, 0, 0.2f));
                    hull.Face(new Vector3(k * b0, BootTop, z0), new Vector3(k * b1, BootTop, z1), new Vector3(k * b1, s1, z1), new Vector3(k * b0, s0, z0), new Vector3(k, 0, 0.2f));
                }
                deck.Face(new Vector3(-b0, s0, z0), new Vector3(b0, s0, z0), new Vector3(b1, s1, z1), new Vector3(-b1, s1, z1), Vector3.up);
            }
            hull.Face(new Vector3(-9, BootTop, Stern), new Vector3(9, BootTop, Stern), new Vector3(9, Sheer(Stern), Stern), new Vector3(-9, Sheer(Stern), Stern), Vector3.back);
            boot.Face(new Vector3(-9, Bottom, Stern), new Vector3(9, Bottom, Stern), new Vector3(9, BootTop, Stern), new Vector3(-9, BootTop, Stern), Vector3.back);
            // Crash rails and bulwarks along the cargo deck.
            foreach (float k in new[] { -1f, 1f })
            {
                hull.Box(new Vector3(k * 8.85f, 3.9f, (Stern + 6) / 2), new Vector3(0.3f, 1.4f, 6 - Stern));
                for (float z = Stern + 2; z < 5; z += 4)
                    cargo.Box(new Vector3(k * 8.2f, 5.2f, z), new Vector3(0.4f, 4.0f, 0.4f));
                cargo.Box(new Vector3(k * 8.2f, 7.1f, (Stern + 5) / 2), new Vector3(0.35f, 0.35f, 4 - Stern));
            }

            // Superstructure forward: two decks of accommodation, the bridge with wings, mast, funnels.
            house.Box(new Vector3(0, 6.0f, 15), new Vector3(16, 5.6f, 18));
            house.Box(new Vector3(0, 10.2f, 15.5f), new Vector3(14, 2.8f, 13));
            house.Box(new Vector3(0, 12.9f, 17.5f), new Vector3(17.6f, 2.6f, 7));
            house.Box(new Vector3(0, 14.3f, 17.5f), new Vector3(18, 0.25f, 7.6f));
            glass.Box(new Vector3(0, 13.0f, 21.03f), new Vector3(17.2f, 1.2f, 0.1f));
            glass.Box(new Vector3(0, 13.0f, 13.97f), new Vector3(17.2f, 1.2f, 0.1f));
            foreach (float k in new[] { -1f, 1f })
            {
                glass.Box(new Vector3(k * 8.83f, 13.0f, 17.5f), new Vector3(0.1f, 1.2f, 6.2f));
                for (float z = 8.5f; z < 22; z += 2.2f)
                {
                    glass.Box(new Vector3(k * 8.03f, 6.8f, z), new Vector3(0.1f, 0.8f, 1.0f));
                    if (z + 1 < 21.5f) glass.Box(new Vector3(k * 7.03f, 10.4f, z + 1), new Vector3(0.1f, 0.8f, 1.0f));
                }
                house.Frustum(new Vector3(k * 5, 8.8f, 7.5f), new Vector2(1.8f, 2.6f), new Vector3(k * 5, 15.5f, 7.0f), new Vector2(1.4f, 2.0f));
                cargo.Box(new Vector3(k * 5, 15.7f, 7.0f), new Vector3(1.5f, 0.4f, 2.1f));
            }
            Steelwork.Lattice(house, new Vector3(0, 14.4f, 17), new Vector3(0, 23, 17), 0.9f, 0.35f, 0.08f, 0.04f, 6, Vector3.forward);
            cargo.Box(new Vector3(0, 20.5f, 17), new Vector3(4.2f, 0.15f, 0.35f));
            // Cargo: a few containers and a stack of drill pipe on the after deck.
            var tints = new[] { new Color(0.14f, 0.28f, 0.52f), new Color(0.88f, 0.45f, 0.12f), new Color(0.22f, 0.42f, 0.26f) };
            var boxes = new MeshBuilder[tints.Length];
            for (int i = 0; i < boxes.Length; i++) boxes[i] = new MeshBuilder();
            boxes[0].Box(new Vector3(-4.5f, 3.2f + 1.3f, -28), new Vector3(2.44f, 2.59f, 6.06f));
            boxes[1].Box(new Vector3(-1.8f, 3.2f + 1.3f, -28), new Vector3(2.44f, 2.59f, 6.06f));
            boxes[2].Box(new Vector3(-4.5f, 3.2f + 3.9f, -28), new Vector3(2.44f, 2.59f, 6.06f));
            boxes[0].Box(new Vector3(4.0f, 3.2f + 1.3f, -6), new Vector3(2.44f, 2.59f, 6.06f));
            for (int i = 0; i < 12; i++)
            {
                float x = 2.0f + (i % 6) * 0.42f, y = 3.4f + (i / 6) * 0.38f;
                cargo.Tube(new Vector3(x, y, -36), new Vector3(x, y, -16), 0.2f, 0.2f, 8, caps: true);
            }

            boot.Object("Boot top", Library.Plain(new Color(0.07f, 0.07f, 0.07f), 0.3f), root);
            hull.Object("Hull", Library.Surface("rusty_painted_metal", 3, new Color(0.75f, 0.18f, 0.12f)), root);
            deck.Object("Deck", Library.Surface("metal_plate", 3, new Color(0.45f, 0.48f, 0.46f)), root);
            house.Object("Superstructure", Library.Plain(new Color(0.9f, 0.9f, 0.87f), 0.35f), root);
            glass.Object("Bridge windows", Library.Plain(new Color(0.05f, 0.08f, 0.1f), 0.92f, 0.3f), root, shadows: false);
            cargo.Object("Deck gear", Library.Surface("rusty_painted_metal", 2, new Color(0.6f, 0.6f, 0.58f)), root);
            for (int i = 0; i < boxes.Length; i++) boxes[i].Object("Containers", Library.Surface("corrugated_iron_02", 2.5f, tints[i]), root);
            var lights = new MeshBuilder();
            lights.Box(new Vector3(-8.9f, 14.0f, 18), new Vector3(0.25f, 0.25f, 0.25f));
            lights.Object("Port light", Library.Glow(new Color(0.3f, 0.02f, 0.02f), new Color(4f, 0.25f, 0.15f)), root, shadows: false);
            var stbd = new MeshBuilder();
            stbd.Box(new Vector3(8.9f, 14.0f, 18), new Vector3(0.25f, 0.25f, 0.25f));
            stbd.Object("Starboard light", Library.Glow(new Color(0.05f, 0.3f, 0.08f), new Color(0.3f, 3.5f, 0.6f)), root, shadows: false);
        }

        /// <summary>
        /// A gas flare burning at a flare tip: tongues of flame, an outer orange body and a hot yellow
        /// core, leaning downwind and flickering. `lean` is the downwind direction in the parent's frame.
        /// </summary>
        public static void Flame(Transform parent, Vector3 at, Vector3 lean, float scale)
        {
            var go = new GameObject("Flare");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * scale;
            var rng = new System.Random(at.GetHashCode());
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            lean = new Vector3(lean.x, 0, lean.z).normalized;
            var outer = new MeshBuilder();
            var core = new MeshBuilder();
            for (int i = 0; i < 6; i++)
            {
                var root = new Vector3(R(-0.5f, 0.5f), R(0, 0.4f), R(-0.5f, 0.5f));
                var dir = (Vector3.up + lean * R(0.4f, 0.8f) + new Vector3(R(-0.2f, 0.2f), 0, R(-0.2f, 0.2f))).normalized;
                outer.Tube(root, root + dir * R(6, 10), R(1.0f, 1.5f), 0.06f, 8, caps: true);
            }
            for (int i = 0; i < 3; i++)
            {
                var dir = (Vector3.up + lean * R(0.3f, 0.6f)).normalized;
                core.Tube(new Vector3(R(-0.3f, 0.3f), 0, R(-0.3f, 0.3f)), dir * R(3.5f, 5.5f), R(0.6f, 0.9f), 0.05f, 8, caps: true);
            }
            outer.Object("Flame", Library.Glow(new Color(1f, 0.45f, 0.12f), new Color(9f, 3.0f, 0.5f)), go.transform, shadows: false);
            core.Object("Flame core", Library.Glow(new Color(1f, 0.9f, 0.6f), new Color(14f, 10f, 4f)), go.transform, shadows: false);
            go.AddComponent<Flicker>().Seed = R(0, 100);
        }
    }

    /// <summary>Makes a flame breathe: noise-driven stretch and sway.</summary>
    public sealed class Flicker : MonoBehaviour
    {
        public float Seed;
        Vector3 baseScale;
        Quaternion baseRot;

        void Start()
        {
            baseScale = transform.localScale;
            baseRot = transform.localRotation;
        }

        void Update()
        {
            float t = Time.time * 2.6f;
            float a = Mathf.PerlinNoise(t, Seed) - 0.5f, b = Mathf.PerlinNoise(Seed, t * 1.3f) - 0.5f, c = Mathf.PerlinNoise(t * 0.7f, Seed + 7) - 0.5f;
            transform.localScale = Vector3.Scale(baseScale, new Vector3(1 + a * 0.2f, 1 + b * 0.45f, 1 + c * 0.2f));
            transform.localRotation = baseRot * Quaternion.Euler(a * 8, 0, c * 8);
        }
    }
}
