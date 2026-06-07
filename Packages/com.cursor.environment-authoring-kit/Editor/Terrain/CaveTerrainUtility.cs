using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    static class CaveTerrainUtility
    {
        static readonly Vector3 DefaultNormalizedMouth = new(0.08f, 0f, 0.5f);

        /// <summary>
        /// Carves a bowl-shaped depression and builds a hillside berm for a cave mouth at the terrain edge.
        /// </summary>
        public static void ApplyCaveEntranceMouth(UnityEngine.Terrain terrain, int seed, Vector3 normalizedMouth = default)
        {
            if (terrain == null)
                return;

            if (normalizedMouth == default)
                normalizedMouth = DefaultNormalizedMouth;

            var data = terrain.terrainData;
            CaveEditorUndo.RecordObject(data, "Cave Entrance Terrain");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            const float mouthRadiusN = 0.18f;
            const float bermRadiusN = 0.24f;
            var maxRadius = Mathf.Max(mouthRadiusN, bermRadiusN);
            var minX = Mathf.Clamp(Mathf.FloorToInt((normalizedMouth.x - maxRadius) * (res - 1)), 0, res - 1);
            var maxX = Mathf.Clamp(Mathf.CeilToInt((normalizedMouth.x + maxRadius) * (res - 1)), 0, res - 1);
            var minY = Mathf.Clamp(Mathf.FloorToInt((normalizedMouth.z - maxRadius) * (res - 1)), 0, res - 1);
            var maxY = Mathf.Clamp(Mathf.CeilToInt((normalizedMouth.z + maxRadius) * (res - 1)), 0, res - 1);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var nx = x / (float)(res - 1);
                    var nz = y / (float)(res - 1);
                    var dx = nx - normalizedMouth.x;
                    var dz = nz - normalizedMouth.z;
                    var dist = Mathf.Sqrt(dx * dx + dz * dz);

                    if (dist > maxRadius)
                        continue;

                    var mouth = Mathf.Exp(-dist * dist * 96f) * 0.18f;
                    var berm = Mathf.Exp(-Mathf.Pow(dist - 0.08f, 2f) * 180f) * 0.08f;
                    var ridge = Mathf.PerlinNoise(nx * 6f + seed * 0.01f, nz * 6f) * 0.02f;
                    var influence = Mathf.Clamp01(1f - dist / maxRadius);

                    heights[y, x] = Mathf.Clamp01(heights[y, x] + (berm + ridge - mouth) * influence);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        /// <summary>Places the terrain mouth bowl at the cave entrance world XZ (not a fixed UV corner).</summary>
        public static void ApplyCaveEntranceMouth(UnityEngine.Terrain terrain, int seed, Transform caveRoot)
        {
            if (terrain == null)
                return;

            var mouth = caveRoot != null
                ? CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot)
                : Vector3.zero;
            var normalized = mouth != Vector3.zero
                ? WorldToTerrainNormalized(terrain, mouth)
                : DefaultNormalizedMouth;
            ApplyCaveEntranceMouth(terrain, seed, normalized);
        }

        public static Vector3 WorldToTerrainNormalized(UnityEngine.Terrain terrain, Vector3 worldPos)
        {
            if (terrain == null)
                return DefaultNormalizedMouth;

            var data = terrain.terrainData;
            var size = data.size;
            var origin = terrain.transform.position;
            var nx = Mathf.Clamp01((worldPos.x - origin.x) / size.x);
            var nz = Mathf.Clamp01((worldPos.z - origin.z) / size.z);
            return new Vector3(nx, 0f, nz);
        }
    }
}
