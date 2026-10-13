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

        public static Arena Build(DuelMap map)
        {
            var arena = new Arena { Map = map };
            var root = new GameObject("Arena " + map.Id).transform;
            arena.Root = root;
            arena.Sky = Atmos.Build(map, root);
            Land.Build(map, root);
            if (map.Water.HasValue) Land.Water(map, root);
            Structures.Build(map, root);
            Dressing.Build(map, root);

            var ground = map.Terrain ?? map.Ground;
            float water = (float)(map.Water ?? -1000);
            foreach (var f in map.Look.Flags)
            {
                float x = (float)f[0], s = (float)f[1];
                float y = Mathf.Max((float)ground(x, -s), water);
                bool afloat = y <= water + 0.01f;
                var basePos = new Vector3(x, y, s);
                if (afloat)
                {
                    // Over water the flag rides a marker buoy.
                    var buoy = Library.Place("lateral_sea_marker", basePos, 0, 1.4f, root);
                    Library.Ground(buoy, water - 0.6f);
                    basePos.y = Library.BoundsOf(buoy).max.y - 0.2f;
                }
                arena.Flags.Add(Flag.Create(basePos, afloat ? 5 : 7, new Color(0.85f, 0.25f, 0.12f), root));
            }

            for (int i = 0; i < 2; i++)
            {
                var side = i == 0 ? Side.A : Side.B;
                foreach (var spot in map.Spots[side])
                    Dressing.Kit(Library.ToUnity(spot), side == Side.A ? 0 : 180, root);
            }
            // Water and steel reflect the finished scene; the probe renders once per map.
            arena.Sky.Probe.RenderProbe();
            return arena;
        }

        /// <summary>Places (or re-poses) the enemy figure at a rules position, facing the other side.</summary>
        public Figure PlaceEnemy(Vec3 position, Stance stance, Side enemySide)
        {
            if (Enemy != null) Object.Destroy(Enemy.gameObject);
            Enemy = Figure.Create(stance, 1.8f, Root, new Color(0.35f, 0.38f, 0.29f));
            Enemy.transform.position = Library.ToUnity(position);
            Enemy.transform.rotation = Quaternion.Euler(0, enemySide == Side.A ? 0 : 180, 0);
            return Enemy;
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
