using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;

namespace SniperDuel.Game
{
    /// <summary>
    /// Memory in use, for the player log: Unity's reserved and allocated memory, the managed heap and
    /// textures; in detail also the meshes alive and the biggest textures by their own estimate.
    /// </summary>
    public static class Mem
    {
        const float MB = 1024f * 1024f;

        public static void Log(string what, bool detail = false)
        {
            string more = "";
            if (detail)
            {
                long verts = 0;
                var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
                foreach (var m in meshes) verts += m.vertexCount;
                var sized = new List<(string name, long bytes)>();
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture>()) sized.Add((t.name, Bytes(t)));
                long total = sized.Sum(t => t.bytes);
                var kinds = sized.GroupBy(t => Kind(t.name)).Select(g => (kind: g.Key, n: g.Count(), bytes: g.Sum(t => t.bytes))).OrderByDescending(g => g.bytes);
                more = $", {meshes.Length} meshes with {verts:N0} vertices, {sized.Count} textures ({total / MB:0} MB by size), " +
                    $"{Resources.FindObjectsOfTypeAll<Material>().Length} materials; textures by kind: " +
                    string.Join(", ", kinds.Select(g => $"{g.kind} {g.n}x {g.bytes / MB:0} MB")) + "; biggest textures: " +
                    string.Join(", ", sized.OrderByDescending(t => t.bytes).Take(12).Select(t => $"{t.name} {t.bytes / MB:0.0}")) + "; biggest model textures: " +
                    string.Join(", ", Resources.FindObjectsOfTypeAll<Texture>().Where(t => Kind(t.name) == "model textures").OrderByDescending(Bytes).Take(40)
                        .Select(t => $"{t.name} {t.width}x{t.height} {t.graphicsFormat} {Bytes(t) / MB:0.0}"));
            }
            Debug.Log($"[Memory] {what}: reserved {Profiler.GetTotalReservedMemoryLong() / MB:0} MB, allocated {Profiler.GetTotalAllocatedMemoryLong() / MB:0} MB, " +
                $"managed {Profiler.GetMonoHeapSizeLong() / MB:0} MB, textures {Texture.currentTextureMemory / MB:0} MB{more}");
        }

        static string Kind(string name)
        {
            if (name.EndsWith("(mask)")) return "repacked masks";
            if (name.EndsWith("(weathered)")) return "weathered";
            if (name.StartsWith("Impostor")) return "plant pictures";
            if (name.StartsWith("image_") || name.StartsWith("Image_")) return "model textures";
            if (name.Contains("_diff")) return "surface colour";
            if (name.Contains("_nor")) return "surface normal";
            if (name.Contains("_arm") || name.Contains("_mask")) return "surface mask";
            if (name.Contains("puresky")) return "sky";
            if (name.StartsWith("_")) return "render targets";
            return name.Length == 0 ? "unnamed" : "other";
        }

        /// <summary>A texture's size on the GPU, worked out from its format, size, mips and faces.</summary>
        static long Bytes(Texture t)
        {
            try
            {
                long one = GraphicsFormatUtility.ComputeMipmapSize(t.width, t.height, t.graphicsFormat);
                if (t.mipmapCount > 1) one = one * 4 / 3;
                if (t is Cubemap || t.dimension == UnityEngine.Rendering.TextureDimension.Cube) one *= 6;
                return one;
            }
            catch
            {
                return 0;
            }
        }
    }
}
