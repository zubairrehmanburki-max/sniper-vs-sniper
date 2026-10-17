using UnityEngine;
using UnityEngine.Profiling;

namespace SniperDuel.Game
{
    /// <summary>Memory in use, for the player log: Unity's reserved and allocated memory, the managed heap, textures and (in detail) meshes.</summary>
    public static class Mem
    {
        const float MB = 1024f * 1024f;

        public static void Log(string what, bool detail = false)
        {
            string meshes = "";
            if (detail)
            {
                long verts = 0;
                var all = Resources.FindObjectsOfTypeAll<Mesh>();
                foreach (var m in all) verts += m.vertexCount;
                meshes = $", {all.Length} meshes with {verts:N0} vertices, {Resources.FindObjectsOfTypeAll<Texture>().Length} textures, {Resources.FindObjectsOfTypeAll<Material>().Length} materials";
            }
            Debug.Log($"[Memory] {what}: reserved {Profiler.GetTotalReservedMemoryLong() / MB:0} MB, allocated {Profiler.GetTotalAllocatedMemoryLong() / MB:0} MB, " +
                $"managed {Profiler.GetMonoHeapSizeLong() / MB:0} MB, textures {Texture.currentTextureMemory / MB:0} MB{meshes}");
        }
    }
}
