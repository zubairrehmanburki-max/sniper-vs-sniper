/*
 * World frame (matches Three.js): x = east/right across the map, y = up, z = south.
 * Player yaw 0 looks north (−z); positive yaw turns right (toward +x).
 *
 * The ballistics solver works in a per-shot frame (x downrange, y up, z right, origin at the scope).
 * These helpers convert between the two.
 */
using System;
using System.Collections.Generic;
using SniperDuel.Ballistics;

namespace SniperDuel.Rules
{
    public struct Aim
    {
        /// <summary>Rotation from north toward east, rad.</summary>
        public double Yaw;
        /// <summary>Elevation above horizontal, rad.</summary>
        public double Pitch;

        public Aim(double yaw, double pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }
    }

    /// <summary>
    /// Layered wind along a lane in world terms. The lane runs from `Start` along `Direction` (unit,
    /// horizontal). Zone i covers lane distance [Edges[i-1], Edges[i]); the last zone runs on forever.
    /// </summary>
    public sealed class LaneWind
    {
        public Vec3 Start;
        public Vec3 Direction;
        /// <summary>Lane distances where zones end, ascending. Zones.Count = Edges.Length + 1.</summary>
        public double[] Edges = Array.Empty<double>();
        /// <summary>World wind vector per zone, m/s.</summary>
        public List<Vec3> Zones = new List<Vec3>();
        public GustModel? Gust;

        public LaneWind Clone()
        {
            var c = (LaneWind)MemberwiseClone();
            c.Zones = new List<Vec3>(Zones);
            return c;
        }
    }

    public static class World
    {
        public static Vec3 Forward(double yaw) => new Vec3(Math.Sin(yaw), 0, -Math.Cos(yaw));
        public static Vec3 RightOf(double yaw) => new Vec3(Math.Cos(yaw), 0, Math.Sin(yaw));

        /// <summary>Shot-frame point to world, given the scope position and aim yaw.</summary>
        public static Vec3 ShotToWorld(double px, double py, double pz, Vec3 eye, double yaw)
        {
            var f = Forward(yaw);
            var r = RightOf(yaw);
            return new Vec3(eye.X + px * f.X + pz * r.X, eye.Y + py, eye.Z + px * f.Z + pz * r.Z);
        }

        public static Vec3 ShotToWorld(Vec3 p, Vec3 eye, double yaw) => ShotToWorld(p.X, p.Y, p.Z, eye, yaw);
        public static Vec3 ShotToWorld(TrajectoryPoint p, Vec3 eye, double yaw) => ShotToWorld(p.X, p.Y, p.Z, eye, yaw);

        /// <summary>World point to shot frame.</summary>
        public static Vec3 WorldToShot(Vec3 p, Vec3 eye, double yaw)
        {
            var f = Forward(yaw);
            var r = RightOf(yaw);
            double dx = p.X - eye.X;
            double dz = p.Z - eye.Z;
            return new Vec3(dx * f.X + dz * f.Z, p.Y - eye.Y, dx * r.X + dz * r.Z);
        }

        /// <summary>World-frame vector (e.g. wind) expressed in the shot frame.</summary>
        public static Vec3 VectorToShot(Vec3 v, double yaw)
        {
            var f = Forward(yaw);
            var r = RightOf(yaw);
            return new Vec3(v.X * f.X + v.Z * f.Z, v.Y, v.X * r.X + v.Z * r.Z);
        }

        /// <summary>World wind at a point (no gust), for visual cues like grass and flags.</summary>
        public static Vec3 LaneWindAt(LaneWind wind, Vec3 p)
        {
            double s = (p.X - wind.Start.X) * wind.Direction.X + (p.Z - wind.Start.Z) * wind.Direction.Z;
            int i = 0;
            while (i < wind.Edges.Length && s >= wind.Edges[i]) i++;
            return wind.Zones[i];
        }

        /// <summary>
        /// Converts lane wind to the solver's shot-frame WindField for a shot fired from `eye` with `yaw`.
        /// Zone boundaries are mapped to downrange distances along the shot, so a shot fired from either
        /// end of the lane sees the zones in the right order.
        /// </summary>
        public static WindField ShotWindField(LaneWind wind, Vec3 eye, double yaw)
        {
            var f = Forward(yaw);
            // Lane distance changes by `rate` per metre downrange.
            double rate = f.X * wind.Direction.X + f.Z * wind.Direction.Z;
            double s0 = (eye.X - wind.Start.X) * wind.Direction.X + (eye.Z - wind.Start.Z) * wind.Direction.Z;
            int ZoneAt(double s)
            {
                int i = 0;
                while (i < wind.Edges.Length && s >= wind.Edges[i]) i++;
                return i;
            }
            var field = new WindField();
            var zones = field.Zones;
            if (Math.Abs(rate) < 1e-6)
            {
                zones.Add(new WindZone(double.PositiveInfinity, VectorToShot(wind.Zones[ZoneAt(s0)], yaw)));
            }
            else
            {
                // Downrange distances where the shot crosses each edge, in flight order.
                var crossings = new List<double>();
                foreach (double e in wind.Edges)
                {
                    double d = (e - s0) / rate;
                    if (d > 0) crossings.Add(d);
                }
                crossings.Sort();
                double prev = 0;
                foreach (double d in crossings)
                {
                    double mid = s0 + rate * ((prev + d) / 2);
                    zones.Add(new WindZone(d, VectorToShot(wind.Zones[ZoneAt(mid)], yaw)));
                    prev = d;
                }
                zones.Add(new WindZone(double.PositiveInfinity, VectorToShot(wind.Zones[ZoneAt(s0 + rate * (prev + 1))], yaw)));
            }
            field.Gust = wind.Gust;
            return field;
        }
    }
}
