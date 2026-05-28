using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    /// <summary>Carves terrain heightmap along cave splines and water basins for natural land integration.</summary>
    static class CaveTerrainCarveUtility
    {
        public static bool CarveForCaveSystem(Transform caveRoot, CaveSplinePath mainPath, CaveSplinePath branchPath = null)
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null || caveRoot == null || mainPath == null)
                return false;

            CaveEditorUndo.RecordObject(terrain.terrainData, "Carve Cave Tunnel");
            CarveTunnel(terrain, caveRoot, mainPath, depthMeters: 6f, radiusMul: 1.05f);

            if (branchPath != null)
                CarveTunnel(terrain, caveRoot, branchPath, depthMeters: 5f, radiusMul: 1f);

            var anchor = caveRoot.GetComponent<CaveWaterBranchAnchor>();
            if (anchor != null)
                CarveBasin(terrain, caveRoot, anchor.poolLocalPosition, radiusMeters: 9f, depthMeters: 3.5f);

            PaintRockAlongPath(terrain, caveRoot, mainPath);
            if (branchPath != null)
                PaintRockAlongPath(terrain, caveRoot, branchPath);

            terrain.Flush();
            return true;
        }

        static void PaintRockAlongPath(Terrain terrain, Transform caveRoot, CaveSplinePath spline)
        {
            var data = terrain.terrainData;
            var layers = data.terrainLayers;
            if (layers == null || layers.Length == 0)
                return;

            var rockLayer = 0;
            for (var i = 0; i < layers.Length; i++)
            {
                var name = layers[i] != null ? layers[i].diffuseTexture?.name ?? layers[i].name : string.Empty;
                var lower = name.ToLowerInvariant();
                if (lower.Contains("rock") || lower.Contains("cliff") || lower.Contains("stone") ||
                    lower.Contains("cave"))
                {
                    rockLayer = i;
                    break;
                }
            }

            if (layers.Length <= 1)
                rockLayer = 0;

            var w = data.alphamapWidth;
            var h = data.alphamapHeight;
            var maps = data.GetAlphamaps(0, 0, w, h);
            var steps = Mathf.Max(12, Mathf.CeilToInt(spline.TotalLength / 3f));

            for (var s = 0; s <= steps; s++)
            {
                var dist = s / (float)steps * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var world = caveRoot.TransformPoint(sample.Position);
                StampRockDisc(maps, w, h, data.size, terrain.transform.position, world, sample.RadiusX * 1.1f, rockLayer);
            }

            data.SetAlphamaps(0, 0, maps);
        }

        static void StampRockDisc(
            float[,,] maps,
            int w,
            int h,
            Vector3 terrainSize,
            Vector3 terrainOrigin,
            Vector3 worldCenter,
            float radiusMeters,
            int layerIndex)
        {
            var nx = (worldCenter.x - terrainOrigin.x) / terrainSize.x;
            var nz = (worldCenter.z - terrainOrigin.z) / terrainSize.z;
            var radiusX = radiusMeters / terrainSize.x;
            var radiusZ = radiusMeters / terrainSize.z;

            var x0 = Mathf.Clamp(Mathf.FloorToInt((nx - radiusX) * w), 0, w - 1);
            var x1 = Mathf.Clamp(Mathf.CeilToInt((nx + radiusX) * w), 0, w - 1);
            var z0 = Mathf.Clamp(Mathf.FloorToInt((nz - radiusZ) * h), 0, h - 1);
            var z1 = Mathf.Clamp(Mathf.CeilToInt((nz + radiusZ) * h), 0, h - 1);

            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var px = x / (float)(w - 1);
                    var pz = z / (float)(h - 1);
                    var dx = (px - nx) / Mathf.Max(0.001f, radiusX);
                    var dz = (pz - nz) / Mathf.Max(0.001f, radiusZ);
                    var d = dx * dx + dz * dz;
                    if (d > 1f)
                        continue;

                    var strength = 1f - Mathf.Sqrt(d);
                    for (var layer = 0; layer < maps.GetLength(2); layer++)
                        maps[z, x, layer] = layer == layerIndex ? Mathf.Lerp(maps[z, x, layer], 1f, strength) : maps[z, x, layer] * (1f - strength * 0.85f);
                }
            }
        }

        public static void CarveTunnel(
            Terrain terrain,
            Transform caveRoot,
            CaveSplinePath spline,
            float depthMeters,
            float radiusMul)
        {
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;

            var steps = Mathf.Max(16, Mathf.CeilToInt(spline.TotalLength / 2f));
            for (var s = 0; s <= steps; s++)
            {
                var dist = s / (float)steps * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var world = caveRoot.TransformPoint(sample.Position);
                CarveDisc(heights, res, size, origin, world, sample.RadiusX * radiusMul, depthMeters / terrain.transform.lossyScale.y);
            }

            data.SetHeights(0, 0, heights);
        }

        public static void CarveEntranceDepression(
            Terrain terrain,
            Vector3 worldMouth,
            float radiusMeters,
            float depthMeters)
        {
            if (terrain == null || terrain.terrainData == null)
                return;

            CaveEditorUndo.RecordObject(terrain.terrainData, "Carve entrance bowl");
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            CarveDisc(
                heights,
                res,
                data.size,
                terrain.transform.position,
                worldMouth,
                radiusMeters,
                depthMeters / Mathf.Max(0.01f, terrain.transform.lossyScale.y));
            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        public static void CarveBasin(
            Terrain terrain,
            Transform caveRoot,
            Vector3 poolLocal,
            float radiusMeters,
            float depthMeters)
        {
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var world = caveRoot.TransformPoint(poolLocal);
            CarveDisc(
                heights,
                res,
                data.size,
                terrain.transform.position,
                world,
                radiusMeters,
                depthMeters / terrain.transform.lossyScale.y);

            data.SetHeights(0, 0, heights);
        }

        public static float SampleCarvedFloorY(Terrain terrain, Transform caveRoot, Vector3 localPosition)
        {
            if (terrain == null)
                return localPosition.y;

            var world = caveRoot.TransformPoint(localPosition);
            return terrain.SampleHeight(world);
        }

        static void CarveDisc(
            float[,] heights,
            int res,
            Vector3 terrainSize,
            Vector3 terrainOrigin,
            Vector3 worldCenter,
            float radiusMeters,
            float depthNormalized)
        {
            var nx = (worldCenter.x - terrainOrigin.x) / terrainSize.x;
            var nz = (worldCenter.z - terrainOrigin.z) / terrainSize.z;
            var radiusX = radiusMeters / terrainSize.x;
            var radiusZ = radiusMeters / terrainSize.z;

            var x0 = Mathf.Clamp(Mathf.FloorToInt((nx - radiusX) * res), 0, res - 1);
            var x1 = Mathf.Clamp(Mathf.CeilToInt((nx + radiusX) * res), 0, res - 1);
            var z0 = Mathf.Clamp(Mathf.FloorToInt((nz - radiusZ) * res), 0, res - 1);
            var z1 = Mathf.Clamp(Mathf.CeilToInt((nz + radiusZ) * res), 0, res - 1);

            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var px = x / (float)(res - 1);
                    var pz = z / (float)(res - 1);
                    var dx = (px - nx) / Mathf.Max(0.001f, radiusX);
                    var dz = (pz - nz) / Mathf.Max(0.001f, radiusZ);
                    var d = dx * dx + dz * dz;
                    if (d > 1f)
                        continue;

                    var falloff = 1f - Mathf.Sqrt(d);
                    heights[z, x] = Mathf.Clamp01(heights[z, x] - depthNormalized * falloff);
                }
            }
        }
    }
}
