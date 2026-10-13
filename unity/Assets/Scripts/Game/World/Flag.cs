using UnityEngine;

namespace SniperDuel.Game.World
{
    /// <summary>
    /// A wind flag: a pole and a cloth that streams with the wind at its spot. The flags are the
    /// player's wind read, so the cloth's angle follows speed the way a real flag does: limp under
    /// 1 m/s, about 45° at 4 m/s, flying straight out from 8 m/s.
    /// </summary>
    public sealed class Flag : MonoBehaviour
    {
        const int Nx = 12, Ny = 7;
        const float Length = 1.8f, Height = 1.1f;

        /// <summary>Wind at the flag, Unity world m/s (set by the match every frame).</summary>
        public Vector3 Wind;
        Mesh mesh = null!;
        Vector3[] verts = null!;
        Transform cloth = null!;
        float phase;

        public static Flag Create(Vector3 basePos, float poleHeight, Color color, Transform parent)
        {
            var go = new GameObject("Flag");
            go.transform.SetParent(parent, false);
            go.transform.position = basePos;
            var pole = new MeshBuilder();
            pole.Cylinder(Vector3.zero, 0.05f, poleHeight, 8);
            pole.Object("Pole", Library.Plain(new Color(0.75f, 0.76f, 0.78f), 0.6f, 0.9f), go.transform);

            var flag = go.AddComponent<Flag>();
            flag.phase = Random.value * 10;
            var cloth = new GameObject("Cloth");
            cloth.transform.SetParent(go.transform, false);
            cloth.transform.localPosition = new Vector3(0, poleHeight - 0.05f, 0);
            flag.cloth = cloth.transform;
            flag.mesh = new Mesh { name = "flag" };
            flag.verts = new Vector3[Nx * Ny];
            var uv = new Vector2[Nx * Ny];
            var tris = new int[(Nx - 1) * (Ny - 1) * 6];
            int t = 0;
            for (int y = 0; y < Ny; y++)
            {
                for (int x = 0; x < Nx; x++)
                {
                    uv[y * Nx + x] = new Vector2(x / (Nx - 1f), y / (Ny - 1f));
                    if (x == Nx - 1 || y == Ny - 1) continue;
                    int i = y * Nx + x;
                    tris[t++] = i; tris[t++] = i + Nx; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + Nx; tris[t++] = i + Nx + 1;
                }
            }
            flag.Shape(0);
            flag.mesh.vertices = flag.verts;
            flag.mesh.uv = uv;
            flag.mesh.triangles = tris;
            flag.mesh.RecalculateNormals();
            cloth.AddComponent<MeshFilter>().sharedMesh = flag.mesh;
            var mat = new Material(Library.Template("Cloth"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.15f);
            cloth.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return flag;
        }

        void Shape(float time)
        {
            float speed = new Vector2(Wind.x, Wind.z).magnitude;
            // Angle of the cloth below horizontal: 80° limp → 0° flying.
            float lift = Mathf.Clamp01((speed - 0.8f) / 7.2f);
            float droop = Mathf.Lerp(80, 4, Mathf.Sqrt(lift)) * Mathf.Deg2Rad;
            float flutter = Mathf.Lerp(0.05f, 0.22f, lift);
            float freq = Mathf.Lerp(1.5f, 6f, lift);
            for (int y = 0; y < Ny; y++)
            {
                for (int x = 0; x < Nx; x++)
                {
                    float u = x / (Nx - 1f), v = y / (Ny - 1f);
                    float along = u * Length;
                    float wave = Mathf.Sin(along * 3.2f - time * freq + phase) * flutter * u;
                    // Cloth hangs from the pole top: +x along the flag (rotated to the wind below), −y down.
                    var p = new Vector3(along * Mathf.Cos(droop), -along * Mathf.Sin(droop) - (1 - v) * Height, wave);
                    verts[y * Nx + x] = p;
                }
            }
        }

        void Update()
        {
            Shape(Time.time);
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var flat = new Vector3(Wind.x, 0, Wind.z);
            if (flat.sqrMagnitude > 0.01f)
            {
                // Local +x points downwind.
                var target = Quaternion.FromToRotation(Vector3.right, flat.normalized);
                cloth.rotation = Quaternion.Slerp(cloth.rotation, target, Time.deltaTime * 2);
            }
        }
    }
}
