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
            var arena = new Arena { Map = map };
            // This map's rifle report (with its echoes) and ambience, synthesised in the background.
            Sound.SetMap(map.Id);
            var root = new GameObject("Arena " + map.Id).transform;
            arena.Root = root;
            arena.Sky = Atmos.Build(map, root);
            Land.Build(map, root);
            if (map.Water.HasValue) Land.Water(map, root);
            Structures.Build(map, root);
            Dressing.Build(map, root);
            // Desert, harbor, alpine and range maps get their own terrain dressing.
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
            Scenery.Build(map, root);

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

            for (int i = 0; i < 2; i++)
            {
                var side = i == 0 ? Side.A : Side.B;
                foreach (var spot in map.Spots[side])
                    arena.Blocks.AddRange(Dressing.Kit(Library.ToUnity(spot), side == Side.A ? 0 : 180, root, !OnSmallTower(map, spot)));
            }
            // Water and steel reflect the finished scene; the probe renders once per map.
            arena.Sky.Probe.RenderProbe();
            Debug.Log($"[Arena] {map.Id}: {Wilds.Triangles(root.gameObject):N0} triangles");
            return arena;
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

        /// <summary>Places (or re-poses) the enemy figure at a rules position, facing the other side.</summary>
        public Figure PlaceEnemy(Vec3 position, Stance stance, Side enemySide)
        {
            if (Enemy != null) Object.Destroy(Enemy.gameObject);
            Enemy = Figure.Create(stance, 1.8f, Root, new Color(0.62f, 0.64f, 0.52f), false, EnemySoldier, EnemyRifle);
            Enemy.transform.position = Library.ToUnity(position);
            Enemy.transform.rotation = Quaternion.Euler(0, enemySide == Side.A ? 0 : 180, 0);
            return Enemy;
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
        }
    }
}
