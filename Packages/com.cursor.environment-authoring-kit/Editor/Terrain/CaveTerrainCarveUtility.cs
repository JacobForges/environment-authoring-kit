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

        /// <summary>
        /// North-facing cliff portal + bore into the massif along <paramref name="intoMountain"/> (not a vertical summit pit).
        /// </summary>
        public static void CarveCliffTunnelMouth(
            Terrain terrain,
            Vector3 worldMouth,
            Vector3 intoMountain,
            float widthMeters,
            float heightMeters,
            float depthMeters)
        {
            if (terrain == null || terrain.terrainData == null)
                return;

            var forward = intoMountain;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.back;
            forward.Normalize();
            var outward = -forward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            CaveEditorUndo.RecordObject(terrain.terrainData, "Cliff tunnel mouth");
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var scaleY = Mathf.Max(0.01f, terrain.transform.lossyScale.y);
            var depthNorm = depthMeters / scaleY;
            var halfW = widthMeters * 0.5f;
            var halfH = heightMeters * 0.5f;
            var faceDepth = Mathf.Max(6f, widthMeters * 0.45f);
            var mouthGroundY = worldMouth.y;

            for (var z = 0; z < res; z++)
            {
                if ((z & 63) == 0)
                    CaveBuildActionPacing.TouchQueueActivity();

                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var p = new Vector3(wx, 0f, wz) - worldMouth;
                    var alongInto = Vector3.Dot(p, forward);
                    var alongOut = Vector3.Dot(p, outward);
                    var side = Mathf.Abs(Vector3.Dot(p, right));

                    if (side > halfW * 1.15f)
                        continue;

                    var wy = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y;
                    var relY = wy - mouthGroundY;
                    if (relY < -halfH * 1.1f || relY > halfH * 1.45f)
                        continue;

                    var sideFalloff = 1f - Mathf.Clamp01(side / halfW);
                    var vertFalloff = 1f - Mathf.Clamp01(Mathf.Abs(relY) / halfH);

                    // Cliff face slot (toward play) — opens the north face without a summit bowl.
                    if (alongOut >= -1.5f && alongOut <= faceDepth && alongInto <= halfW * 0.25f)
                    {
                        var faceT = Mathf.Clamp01(alongOut / faceDepth);
                        var cut = depthNorm * 0.55f * (1f - faceT * 0.65f) * sideFalloff * vertFalloff;
                        heights[z, x] = Mathf.Clamp01(heights[z, x] - cut);
                        continue;
                    }

                    // Tunnel bore into mountain (south / uphill).
                    if (alongInto < -1f || alongInto > depthMeters)
                        continue;

                    var depthT = Mathf.Clamp01(alongInto / Mathf.Max(1f, depthMeters));
                    var ceilingBias = Mathf.Clamp01((relY + halfH * 0.15f) / halfH);
                    var cutTunnel = depthNorm * (0.35f + 0.5f * (1f - depthT)) * sideFalloff * vertFalloff * ceilingBias;
                    heights[z, x] = Mathf.Clamp01(heights[z, x] - cutTunnel);
                }
            }

            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        /// <summary>Deprecated — use <see cref="CarveCliffTunnelMouth"/>; kept for older call sites.</summary>
        public static void CarveHorizontalTunnelMouth(
            Terrain terrain,
            Vector3 worldMouth,
            Vector3 tunnelForward,
            float widthMeters,
            float heightMeters,
            float depthMeters)
        {
            if (terrain == null || terrain.terrainData == null)
                return;

            var forward = tunnelForward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward).normalized;

            CaveEditorUndo.RecordObject(terrain.terrainData, "Horizontal tunnel mouth");
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var depthNorm = depthMeters / Mathf.Max(0.01f, terrain.transform.lossyScale.y);
            var halfW = widthMeters * 0.5f;
            var halfH = heightMeters * 0.5f;

            for (var z = 0; z < res; z++)
            {
                if ((z & 63) == 0)
                    CaveBuildActionPacing.TouchQueueActivity();

                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var p = new Vector3(wx, 0f, wz) - worldMouth;
                    var along = Vector3.Dot(p, forward);
                    var side = Vector3.Dot(p, right);
                    if (along < -depthMeters * 1.2f || along > depthMeters * 0.35f)
                        continue;
                    if (Mathf.Abs(side) > halfW)
                        continue;

                    var wy = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + origin.y;
                    var vertical = wy - (worldMouth.y + halfH * 0.25f);
                    if (vertical < -halfH || vertical > halfH * 1.35f)
                        continue;

                    var gate = 1f - Mathf.Clamp01(along / Mathf.Max(0.5f, depthMeters));
                    var sideFalloff = 1f - Mathf.Clamp01(Mathf.Abs(side) / halfW);
                    var vertFalloff = 1f - Mathf.Clamp01(Mathf.Abs(vertical) / halfH);
                    var cut = depthNorm * gate * sideFalloff * vertFalloff * 0.85f;
                    heights[z, x] = Mathf.Clamp01(heights[z, x] - cut);
                }
            }

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
