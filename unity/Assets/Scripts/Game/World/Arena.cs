using System.Collections;
using System.Collections.Generic;
using SniperDuel.Ballistics;
using SniperDuel.Rules;
using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// One map built in 3D: sky, terrain, water, structures, dressing, wind flags, the firing-spot
    /// kits and the enemy. Unity coordinates are rules coordinates with z flipped (Library.ToUnity),
    /// so player A sits near the origin looking along +z.
    /// </summary>
    public sealed class Arena
    {
        public DuelMap Map = null!;
        public Transform Root = null!;
        public Atmos.Sky Sky = null!;
        public readonly List<Flag> Flags = new List<Flag>();
        public Figure? Enemy;
        public Figure? Player;
        /// <summary>The player's outfit colour and rifle finish (the in-duel kit; menus show the defaults).</summary>
        public Color PlayerTint = new Color(0.56f, 0.55f, 0.47f);
        public Color? PlayerFinish;
        /// <summary>The models each side is drawn with: soldier and rifle (null: the defaults).</summary>
        public string? PlayerSoldier, PlayerRifle, EnemySoldier = "soldier_boonie", EnemyRifle;
        /// <summary>Props on the firing spots (ammo box, crate, barriers): the player walks round them.</summary>
        public readonly List<Bounds> Blocks = new List<Bounds>();

        public static Arena Build(DuelMap map)
        {
            Arena? built = null;
            var steps = Building(map, a => built = a, trim: false);
            while (steps.MoveNext()) { }
            return built!;
        }

        /// <summary>
        /// Builds a map over several frames: with `trim`, the imported originals are let go after
        /// the heavy steps, so a big map's models never all sit in memory at full size at once.
        /// </summary>
        public static IEnumerator Building(DuelMap map, System.Action<Arena> done, bool trim = true)
        {
            var arena = new Arena { Map = map };
            void Step(string what) => Trail.Mark($"map {map.Id}: {what}");
            Step("sound");
            // This map's rifle report (with its echoes) and ambience, synthesised in the background.
            Sound.SetMap(map.Id);
            var root = new GameObject("Arena " + map.Id).transform;
            arena.Root = root;
            Step("sky");
            arena.Sky = Atmos.Build(map, root);
            Step("land");
            Land.Build(map, root);
            if (map.Water.HasValue) Land.Water(map, root);
            Step("structures");
            Structures.Build(map, root);
            Step("dressing");
            Dressing.Build(map, root);
            // Desert, harbor, alpine and range maps get their own terrain dressing.
            if (trim)
            {
                yield return Trim();
                Mem.Log($"map {map.Id} before biome");
            }
            Step("biome");
            Biomes.Build(map, root, arena.Flags);
            // The estuary maps get a city on the banks so the far horizon isn't empty water.
            if (map.Id == "twin-bridges")
            {
                Skyline.Build(map, root, 260);
                Estuary.Build(map, root);
                // Wooded hills where the valley closes, 3 km out: through the scope they stand in
                // the haze behind the enemy's bridge instead of a bare horizon.
                var terrain = map.Terrain ?? map.Ground;
                Biomes.Horizon(root, (float)map.LaneLength / 2, new Biomes.Hills
                {
                    // Low enough that the scope at 12x shows the ridge line with sky above it.
                    MinHeight = 35,
                    MaxHeight = 90,
                    Rock = Library.Surface("sparse_grass", 60, new Color(0.62f, 0.7f, 0.58f)),
                    Seed = 23,
                    Ground = (x, s) => (float)terrain(x, -s),
                });
            }
            // The towers stand in a drowned marsh; the rigs in open sea with a field round them.
            if (map.Id == "watchtowers") Marsh.Build(map, root);
            if (map.Id == "oil-rigs") Offshore.Build(map, root);
            // The downloaded set pieces: viaduct, towers, rigs, containers, cover, cliffs, trees, rocks.
            if (trim)
            {
                yield return Trim();
                Mem.Log($"map {map.Id} before scenery");
            }
            Step("scenery");
            Scenery.Build(map, root);
            Step("ambience");
            Ambience.Build(map, root);
            Step("flags");

            var ground = map.Terrain ?? map.Ground;
            float water = (float)(map.Water ?? -1000);
            foreach (var f in map.Look.Flags)
            {
                float x = (float)f[0], s = (float)f[1];
                float y = Mathf.Max((float)ground(x, -s), water);
                bool afloat = y <= water + 0.01f;
                float bed = (float)ground(x, -s);
                var basePos = new Vector3(x, y, s);
                float pole = 7;
                if (afloat && water - bed < 4)
                {
                    // Shallow marsh: the flag pole is driven into the mud and stands out of the water.
                    basePos.y = bed - 0.3f;
                    pole = water - bed + 5.3f;
                }
                else if (afloat)
                {
                    // Over deep water the flag rides a marker buoy.
                    var buoy = Library.Place("lateral_sea_marker", basePos, 0, 1.4f, root);
                    Library.Ground(buoy, water - 0.6f);
                    basePos.y = Library.BoundsOf(buoy).max.y - 0.2f;
                    pole = 5;
                }
                arena.Flags.Add(Flag.Create(basePos, pole, new Color(0.85f, 0.25f, 0.12f), root));
            }

            Step("spot kits");
            for (int i = 0; i < 2; i++)
            {
                var side = i == 0 ? Side.A : Side.B;
                foreach (var spot in Maps.FiringSpots(map, side))
                    arena.Blocks.AddRange(Dressing.Kit(Library.ToUnity(spot), side == Side.A ? 0 : 180, root, !OnSmallTower(map, spot)));
            }
            Step("shadows");
            ShadowsNearSpots(map, root);
            // Water and steel reflect the finished scene; the probe renders once per map.
            Step("reflection probe");
            arena.Sky.Probe.RenderProbe();
            Step("built");
            Debug.Log($"[Arena] {map.Id}: {Wilds.Triangles(root.gameObject):N0} triangles; heaviest:{Wilds.Heaviest(root.gameObject)}");
            done(arena);
        }

        /// <summary>
        /// Tears a map down and frees everything only it used: the meshes, textures and materials
        /// built at runtime and the models loaded for it outlive their objects until Unity unloads
        /// unused assets. Yield the
        /// result before building the next map, so two maps never sit in memory together.
        /// </summary>
        public static AsyncOperation Unload(Arena arena)
        {
            if (arena.Root != null) Object.DestroyImmediate(arena.Root.gameObject);
            arena.Enemy = null;
            arena.Player = null;
            // The sky photo and the caches of models, textures and materials hold the old map's
            // assets too; whatever the next map needs is loaded again.
            RenderSettings.skybox = null;
            Library.Forget();
            Impostor.Forget();
            Figure.Forget();
            Scenery.Forget();
            Pick.Forget();
            System.GC.Collect();
            return Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// After a map is built: lets go of the models and pictures it was built from. The scene keeps
        /// its own converted copies, so the imported originals (uncompressed glTF textures among
        /// them) can be unloaded. Yield the result.
        /// </summary>
        public static AsyncOperation Trim()
        {
            Library.Forget();
            Figure.Forget();
            Scenery.Forget();
            Debug.Log($"[Memory] {Library.LeanCount} model textures made lean in {Library.LeanMs:0} ms");
            System.GC.Collect();
            return Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// Small things (under a metre across) cast shadows only near the firing spots, where the
        /// eye is close enough to see them; out in the map their shadows are a few pixels that
        /// still cost a draw into every shadow cascade.
        /// </summary>
        static void ShadowsNearSpots(DuelMap map, Transform root)
        {
            var spots = new List<Vector3>();
            foreach (var p in map.Spots[Side.A]) spots.Add(Library.ToUnity(p));
            foreach (var p in map.Spots[Side.B]) spots.Add(Library.ToUnity(p));
            int off = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                var b = r.bounds;
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) >= 1) continue;
                bool near = false;
                foreach (var sp in spots)
                    if ((b.center - sp).sqrMagnitude < 30 * 30) { near = true; break; }
                if (near) continue;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                off++;
            }
            Debug.Log($"[Arena] {map.Id}: shadows off for {off} small far renderers");
        }

        /// <summary>True when a spot is on a tower roof too small for the kit's road barriers.</summary>
        static bool OnSmallTower(DuelMap map, Vec3 spot)
        {
            if (map.Look.Structures == null) return false;
            foreach (var st in map.Look.Structures)
                if (st.Kind == StructureKind.Tower && st.HalfSize < 6 && System.Math.Abs(spot.X - st.X) <= st.HalfSize && System.Math.Abs(-spot.Z - st.S) <= st.HalfSize)
                    return true;
            return false;
        }

        static bool trimDue;
        static AsyncOperation? trimming;

        /// <summary>
        /// Asks for a <see cref="Trim"/> once the frame is done: soldiers, rifles and the
        /// first-person arms are made mid-match, and the models they load (full-size textures and
        /// all) would otherwise stay cached until the next map.
        /// </summary>
        public static void TrimSoon() => trimDue = true;

        /// <summary>Runs the trim asked for, unless one is still going.</summary>
        public static void TrimIfDue()
        {
            if (!trimDue || trimming != null && !trimming.isDone) return;
            trimDue = false;
            trimming = Trim();
        }

        /// <summary>The enemy posed in each stance, made once per soldier and rifle (EnemyKit).</summary>
        readonly Dictionary<Stance, Figure> enemyPoses = new Dictionary<Stance, Figure>();
        string? enemyKit;

        /// <summary>
        /// Places the enemy figure at a rules position in a stance, facing the other side. Each
        /// stance's figure is made once and kept: a bot moving or changing stance only moves and
        /// swaps them, rather than loading, converting and unloading the soldier every time.
        /// </summary>
        public Figure PlaceEnemy(Vec3 position, Stance stance, Side enemySide)
        {
            UseEnemyKit();
            if (!enemyPoses.TryGetValue(stance, out var fig) || fig == null)
            {
                fig = enemyPoses[stance] = Figure.Create(stance, 1.8f, Root, EnemyTint, false, EnemySoldier, EnemyRifle);
                TrimSoon();
            }
            foreach (var other in enemyPoses.Values)
                if (other != null && other != fig) other.gameObject.SetActive(false);
            fig.gameObject.SetActive(true);
            fig.transform.position = Library.ToUnity(position);
            fig.transform.rotation = Quaternion.Euler(0, enemySide == Side.A ? 0 : 180, 0);
            Enemy = fig;
            return fig;
        }

        static readonly Color EnemyTint = new Color(0.62f, 0.64f, 0.52f);

        /// <summary>
        /// Makes the enemy in every stance now, in one go (the match is starting), so no stance is
        /// made mid-match: the models load and convert once and are let go once.
        /// </summary>
        public void PrepareEnemy()
        {
            UseEnemyKit();
            foreach (var stance in new[] { Stance.Standing, Stance.Crouch, Stance.Prone })
            {
                if (enemyPoses.TryGetValue(stance, out var f) && f != null) continue;
                f = enemyPoses[stance] = Figure.Create(stance, 1.8f, Root, EnemyTint, false, EnemySoldier, EnemyRifle);
                if (f != Enemy) f.gameObject.SetActive(false);
            }
            TrimSoon();
        }

        /// <summary>Takes the enemy away (solo practice) and frees its figures.</summary>
        public void ClearEnemy()
        {
            foreach (var f in enemyPoses.Values) if (f != null) Object.Destroy(f.gameObject);
            enemyPoses.Clear();
            enemyKit = null;
            Enemy = null;
        }

        /// <summary>A different soldier or rifle for the enemy: the figures made for the old one go.</summary>
        void UseEnemyKit()
        {
            string kit = EnemySoldier + "|" + EnemyRifle;
            if (kit == enemyKit) return;
            ClearEnemy();
            enemyKit = kit;
        }

        /// <summary>
        /// Places (or re-poses) the player's own figure, facing down-lane. A live one (the duel's)
        /// stays skinned so it can walk and change stance; the caller then moves it.
        /// </summary>
        public Figure PlacePlayer(Vec3 position, Stance stance, Side side, bool live = false)
        {
            if (Player != null) Object.Destroy(Player.gameObject);
            Player = Figure.Create(stance, 1.8f, Root, PlayerTint, live, PlayerSoldier, PlayerRifle);
            if (PlayerFinish is Color finish) Figure.PaintRifle(Player.gameObject, finish);
            Player.name = "Player";
            Player.transform.position = Library.ToUnity(position);
            Player.transform.rotation = Quaternion.Euler(0, side == Side.A ? 0 : 180, 0);
            TrimSoon();
            return Player;
        }

        /// <summary>Feeds the lane wind to the flags.</summary>
        public void SetWind(LaneWind wind)
        {
            foreach (var f in Flags)
            {
                var w = SniperDuel.Rules.World.LaneWindAt(wind, Library.ToRules(f.transform.position));
                f.Wind = new Vector3((float)w.X, 0, (float)-w.Z);
            }
            var mid = SniperDuel.Rules.World.LaneWindAt(wind, new Vec3(0, 2, -Map.LaneLength / 2));
            Ambience.SetWind(new Vector3((float)mid.X, 0, (float)-mid.Z));
        }
    }
}
