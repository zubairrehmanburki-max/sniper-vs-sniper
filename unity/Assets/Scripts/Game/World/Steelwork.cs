using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// Fabricated steel shared by the towers, rigs and marsh: stair flights, guard rails, caged ladders
    /// and lattice trusses (crane booms, derricks, masts), all written into a caller's MeshBuilder so
    /// they merge with the rest of a structure's steel into one draw.
    /// </summary>
    public static class Steelwork
    {
        /// <summary>
        /// A straight stair flight from the bottom nosing to the top nosing (centre line): two channel
        /// stringers, a tread every ~19 cm of rise and a handrail with a knee rail on both sides.
        /// </summary>
        public static void Flight(MeshBuilder mb, Vector3 bottom, Vector3 top, float width, bool rails = true)
        {
            var run = top - bottom;
            var flat = new Vector3(run.x, 0, run.z);
            if (flat.sqrMagnitude < 1e-4f) return;
            var dir = flat.normalized;
            var side = Vector3.Cross(Vector3.up, dir) * (width / 2);
            var look = Quaternion.LookRotation(dir);
            foreach (float k in new[] { -1f, 1f })
            {
                // Stringers hang below the nosing line.
                var off = side * k + Vector3.down * 0.12f;
                mb.Beam(bottom + off, top + off, 0.05f, 0.24f);
                if (!rails) continue;
                var up = Vector3.up * 0.95f;
                mb.Beam(bottom + side * k + up, top + side * k + up, 0.05f, 0.05f);
                mb.Beam(bottom + side * k + up * 0.5f, top + side * k + up * 0.5f, 0.035f, 0.035f);
                int posts = Mathf.Max(2, Mathf.CeilToInt(flat.magnitude / 1.6f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    var p = Vector3.Lerp(bottom, top, i / (posts - 1f)) + side * k;
                    mb.Box(p + Vector3.up * 0.475f, new Vector3(0.05f, 0.95f, 0.05f));
                }
            }
            int steps = Mathf.Max(2, Mathf.RoundToInt(run.y / 0.19f));
            for (int i = 0; i < steps; i++)
            {
                var p = Vector3.Lerp(bottom, top, (i + 0.5f) / steps);
                mb.Box(p - Vector3.up * 0.03f, new Vector3(width - 0.04f, 0.04f, 0.26f), look);
            }
        }

        /// <summary>
        /// A guard rail along a deck edge: posts every ~1.5 m, a top rail at `height` and a knee rail at
        /// half height, with a toe board when asked.
        /// </summary>
        public static void Rail(MeshBuilder mb, Vector3 from, Vector3 to, float height = 1.1f, bool toeBoard = false)
        {
            float len = Vector3.Distance(from, to);
            if (len < 0.05f) return;
            var up = Vector3.up;
            mb.Beam(from + up * height, to + up * height, 0.05f, 0.05f);
            mb.Beam(from + up * height * 0.5f, to + up * height * 0.5f, 0.04f, 0.04f);
            if (toeBoard) mb.Beam(from + up * 0.075f, to + up * 0.075f, 0.01f, 0.15f);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 1.5f) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector3.Lerp(from, to, i / (posts - 1f));
                mb.Box(p + up * height / 2, new Vector3(0.05f, height, 0.05f));
            }
        }

        /// <summary>
        /// A vertical ladder at `foot`, climbing `height`, rungs every 30 cm; with `cageFrom` above the foot,
        /// safety hoops behind the climber from there up, on the side `wallNormal` points to.
        /// </summary>
        public static void Ladder(MeshBuilder mb, Vector3 foot, float height, Vector3 wallNormal, float cageFrom = -1)
        {
            var away = new Vector3(wallNormal.x, 0, wallNormal.z).normalized;
            var across = Vector3.Cross(Vector3.up, away) * 0.22f;
            foreach (float k in new[] { -1f, 1f })
                mb.Box(foot + across * k + Vector3.up * height / 2, new Vector3(0.06f, height, 0.06f));
            for (float y = 0.3f; y < height - 0.05f; y += 0.3f)
                mb.Beam(foot - across + Vector3.up * y, foot + across + Vector3.up * y, 0.03f, 0.03f);
            if (cageFrom < 0) return;
            const float R = 0.38f;
            var centre = foot + away * (R - 0.05f);
            for (float y = cageFrom; y < height; y += 0.9f)
            {
                Vector3? prev = null;
                for (int i = 0; i <= 6; i++)
                {
                    // Half hoop from one stile round behind the climber to the other.
                    float a = Mathf.PI * i / 6;
                    var p = centre + Vector3.up * y + (-across.normalized * Mathf.Cos(a) + away * Mathf.Sin(a)) * R;
                    if (prev.HasValue) mb.Beam(prev.Value, p, 0.04f, 0.04f);
                    prev = p;
                }
            }
            for (int i = 1; i <= 5; i++)
            {
                float a = Mathf.PI * i / 6;
                var off = (-across.normalized * Mathf.Cos(a) + away * Mathf.Sin(a)) * R;
                mb.Beam(centre + off + Vector3.up * cageFrom, centre + off + Vector3.up * (height - 0.1f), 0.035f, 0.035f);
            }
        }

        /// <summary>
        /// A square lattice truss from a to b, tapering from width wa to wb: four chords, a strut at
        /// every panel point and zigzag lacing on each face. Crane booms, derricks, flare booms, masts.
        /// </summary>
        public static void Lattice(MeshBuilder mb, Vector3 a, Vector3 b, float wa, float wb, float chord, float lace, int panels, Vector3 upHint)
        {
            var axis = (b - a).normalized;
            var s1 = Vector3.Cross(axis, upHint);
            if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.Cross(axis, Vector3.forward);
            s1.Normalize();
            var s2 = Vector3.Cross(axis, s1);
            var signs = new[] { (1f, 1f), (1f, -1f), (-1f, -1f), (-1f, 1f) };
            Vector3 Corner(int j, float t)
            {
                var (k1, k2) = signs[j & 3];
                float w = Mathf.Lerp(wa, wb, t) / 2;
                return Vector3.Lerp(a, b, t) + (s1 * k1 + s2 * k2) * w;
            }
            for (int j = 0; j < 4; j++) mb.Beam(Corner(j, 0), Corner(j, 1), chord, chord);
            for (int i = 0; i <= panels; i++)
            {
                float t0 = (float)i / panels, t1 = (float)(i + 1) / panels;
                for (int j = 0; j < 4; j++)
                {
                    mb.Beam(Corner(j, t0), Corner(j + 1, t0), lace, lace);
                    if (i == panels) continue;
                    if ((i + j) % 2 == 0) mb.Beam(Corner(j, t0), Corner(j + 1, t1), lace, lace);
                    else mb.Beam(Corner(j + 1, t0), Corner(j, t1), lace, lace);
                }
            }
        }
    }
}
