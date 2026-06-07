using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Axis-aligned block rings on the maze grid (Y-up, XZ floor). No tilted spline frames.
    /// </summary>
    public static class CaveAdventureBlockBuilder
    {
        public const string RootName = "BlockTunnel";
        public const string PlatformsRootName = "PathPlatforms";
        public const string SolidRockRootName = "SolidRock";

        public const int DefaultRingBatchSize = 2;

        /// <summary>Block-tunnel settings for adventure / compact-route shells (grading rebuilds).</summary>
        /// <summary>Grader band: ≤16 blocks/ring (FDG HDPCG 2026); elliptical 12-step rings hit ~80/ring.</summary>
        public const int CompactBlocksPerRingMax = 16;

        public static CaveBlockTunnelBuilder.Settings CompactRouteSettings(CaveMazeLayout layout = null)
        {
            var settings = CaveBlockTunnelBuilder.Settings.Default;
            settings.RingSpacing = 1.1f;
            settings.BlockSize = 1f;
            settings.FloorLayers = 0;
            settings.CeilingLayers = 0;
            settings.WallThickness = 1;
            // unity6-mesh-data-procedural — cardinal shell (~4–16 blocks/cell), not 12-step onion (~80/ring).
            settings.CompactCardinalShell = true;
            settings.AngularSteps = 4;
            settings.InteriorHollow = layout != null
                ? Mathf.Clamp(
                    1f - (CaveMazeLayout.MinWalkClearanceMeters /
                          Mathf.Max(layout.CellSize * 0.85f, CaveMazeLayout.MinWalkClearanceMeters + 1.5f)),
                    0.48f,
                    0.62f)
                : 0.52f;
            settings.OuterWallMinable = true;
            settings.MorphPosition = 0.03f;
            settings.MorphRotation = 0f;
            return settings;
        }

        public static int Build(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material rockMat,
            int seed,
            CaveBlockTunnelBuilder.Settings settings)
        {
            if (geometryRoot == null || layout == null || rockMat == null)
                return 0;

            ApplyCompactRouteDefaults(ref settings);

            var section = PrepareBlockSection(geometryRoot);
            if (section == null)
                return 0;

            var path = layout.SolutionPath;
            if (path == null || path.Count == 0)
                return 0;

            return BuildRingCells(section, layout, 0, path.Count, rockMat, seed, settings);
        }

        public static Transform PrepareBlockSection(Transform geometryRoot)
        {
            if (geometryRoot == null)
                return null;

            var root = EnvironmentSceneUtility.GetOrCreateChild(geometryRoot, RootName);
            var section = root.Find("Main");
            if (section == null)
            {
                var go = new GameObject("Main");
                CaveEditorUndo.RegisterCreated(go, "Block Section");
                go.transform.SetParent(root, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                section = go.transform;
            }

            for (var i = section.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(section.GetChild(i).gameObject);

            return section;
        }

        /// <summary>Builds block rings for path cells [startIndex, startIndex + cellCount).</summary>
        public static int BuildRingCells(
            Transform section,
            CaveMazeLayout layout,
            int startIndex,
            int cellCount,
            Material rockMat,
            int seed,
            CaveBlockTunnelBuilder.Settings settings)
        {
            if (section == null || layout == null || rockMat == null || cellCount <= 0)
                return 0;

            if (settings.BlockSize <= 0f)
                settings = CaveBlockTunnelBuilder.Settings.Default;

            ApplyCompactRouteDefaults(ref settings);

            var rng = new System.Random(seed + startIndex * 31);
            var placed = 0;
            var path = layout.SolutionPath;
            if (path == null || path.Count == 0)
                return 0;

            var end = Mathf.Min(startIndex + cellCount, path.Count);
            for (var i = startIndex; i < end; i++)
            {
                var cell = path[i];
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var forward = ResolveForward(layout, i);
                var h = layout.GetCeilingClearanceAt(cell.x, cell.y);
                var rx = layout.IsCavernCell(cell.x, cell.y) ? layout.CellSize * 1.15f : layout.CellSize * 0.95f;
                var ry = h * 0.5f;
                var ring = new GameObject($"BlockRing_{cell.x}_{cell.y}");
                CaveEditorUndo.RegisterCreated(ring, "Block Ring");
                ring.transform.SetParent(section, false);
                ring.transform.localPosition = Vector3.zero;
                ring.transform.localRotation = Quaternion.identity;

                placed += FillRingGridAligned(ring.transform, floor, h, forward, rx, ry, rockMat, rng, settings);
            }

            return placed;
        }

        /// <summary>Deprecated — fills entire grid. Use route-only block rings instead.</summary>
        public static int FillSolidRockVolume(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material rockMat,
            int seed)
        {
            if (geometryRoot == null || layout == null || rockMat == null)
                return 0;

            var existing = geometryRoot.Find(SolidRockRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(SolidRockRootName);
            CaveEditorUndo.RegisterCreated(root, "Solid Rock Volume");
            root.transform.SetParent(geometryRoot, false);

            var rng = new System.Random(seed + 919);
            var block = CaveBlockTunnelBuilder.Settings.Default.BlockSize;
            var placed = 0;

            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                {
                    if (layout.IsPassage(x, z))
                        continue;

                    var floor = layout.GetFloorSurfaceLocal(x, z);
                    var h = layout.IsCavernCell(x, z) ? layout.CorridorHeight * 1.75f : layout.CorridorHeight;
                    var layers = Mathf.CeilToInt((h + 4f) / block);

                    for (var layer = 0; layer < layers; layer++)
                    {
                        var y = floor.y + layer * block;
                        placed += PlaceSolidRockBlock(root.transform, new Vector3(
                            layout.CellToLocal(x, z).x,
                            y,
                            layout.CellToLocal(x, z).z), block, rockMat, rng);
                    }
                }
            }

            return placed;
        }

        static int PlaceSolidRockBlock(
            Transform parent,
            Vector3 localPos,
            float size,
            Material rockMat,
            System.Random rng)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Solid Rock");
            go.name = "CaveBlock_Shell";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 8f - 4f, 0f);
            go.transform.localScale = Vector3.one * size * (0.98f + (float)rng.NextDouble() * 0.04f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = rockMat;

            if (go.GetComponent<CaveTunnelBlock>() == null)
                go.AddComponent<CaveTunnelBlock>();

            go.isStatic = true;
            return 1;
        }

        /// <summary>Walkable 3D floor cubes on the route (your floor texture) — no layered shell slabs.</summary>
        public static int BuildWalkPlatforms(
            Transform geometryRoot,
            CaveMazeLayout layout,
            Material floorMat)
        {
            if (geometryRoot == null || layout == null || floorMat == null)
                return 0;

            var existing = geometryRoot.Find(PlatformsRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(PlatformsRootName);
            CaveEditorUndo.RegisterCreated(root, "Path Platforms");
            root.transform.SetParent(geometryRoot, false);

            var placed = 0;
            var thickness = 0.55f;
            foreach (var cell in layout.SolutionPath)
            {
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var isCavern = layout.IsCavernCell(cell.x, cell.y);
                var span = isCavern ? layout.PlatformSpan * 1.35f : layout.PlatformSpan;
                var depth = isCavern ? layout.PlatformDepth * 1.2f : layout.PlatformDepth;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Walk Platform");
                go.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}{cell.x}_{cell.y}";
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = floor + Vector3.up * (thickness * 0.5f);
                go.transform.localScale = new Vector3(span, thickness, depth);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                    mr.sharedMaterial = floorMat;

                if (go.GetComponent<CaveWalkableMarker>() == null)
                    go.AddComponent<CaveWalkableMarker>();

                var col = go.GetComponent<BoxCollider>();
                if (col != null)
                    col.isTrigger = false;

                go.isStatic = true;
                placed++;
            }

            return placed;
        }

        /// <summary>3D rock blocks over the route so surface roads are not visible when looking up.</summary>
        public static int BuildSkyRockCap(Transform geometryRoot, CaveMazeLayout layout, Material rockMat)
        {
            if (geometryRoot == null || layout == null || rockMat == null)
                return 0;

            var existing = geometryRoot.Find("SkyRockCap");
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject("SkyRockCap");
            CaveEditorUndo.RegisterCreated(root, "Sky Rock Cap");
            root.transform.SetParent(geometryRoot, false);

            layout.ComputeLocalBounds(out var min, out var max);
            var block = CaveBlockTunnelBuilder.Settings.Default.BlockSize;
            var rng = new System.Random(layout.SolutionPath.Count + 31);
            var placed = 0;
            var y = max.y + block * 1.5f;
            var spanX = max.x - min.x;
            var spanZ = max.z - min.z;
            var stepsX = Mathf.Max(1, Mathf.CeilToInt(spanX / (block * 2f)));
            var stepsZ = Mathf.Max(1, Mathf.CeilToInt(spanZ / (block * 2f)));

            for (var ix = 0; ix <= stepsX; ix++)
            {
                for (var iz = 0; iz <= stepsZ; iz++)
                {
                    var tX = ix / (float)stepsX;
                    var tZ = iz / (float)stepsZ;
                    var p = new Vector3(Mathf.Lerp(min.x, max.x, tX), y, Mathf.Lerp(min.z, max.z, tZ));
                    placed += PlaceCapBlock(root.transform, p, block, rockMat, rng);
                }
            }

            return placed;
        }

        static int PlaceCapBlock(Transform parent, Vector3 localPos, float size, Material rockMat, System.Random rng)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Sky Cap Block");
            go.name = "CaveBlock_Shell";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * size * (0.95f + (float)rng.NextDouble() * 0.1f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = rockMat;

            var col = go.GetComponent<Collider>();
            if (col != null)
                CaveEditorUndo.DestroyImmediate(col);

            go.isStatic = true;
            return 1;
        }

        static Vector3 ResolveForward(CaveMazeLayout layout, int pathIndex)
        {
            var path = layout.SolutionPath;
            if (pathIndex < path.Count - 1)
            {
                var a = layout.CellToLocal(path[pathIndex].x, path[pathIndex].y);
                var b = layout.CellToLocal(path[pathIndex + 1].x, path[pathIndex + 1].y);
                var f = b - a;
                f.y = 0f;
                if (f.sqrMagnitude > 0.01f)
                    return f.normalized;
            }

            if (pathIndex > 0)
            {
                var a = layout.CellToLocal(path[pathIndex - 1].x, path[pathIndex - 1].y);
                var b = layout.CellToLocal(path[pathIndex].x, path[pathIndex].y);
                var f = b - a;
                f.y = 0f;
                if (f.sqrMagnitude > 0.01f)
                    return f.normalized;
            }

            return Vector3.forward;
        }

        /// <summary>Forces cardinal shells on grid route builds when callers pass Default/onion settings by mistake.</summary>
        internal static void ApplyCompactRouteDefaults(ref CaveBlockTunnelBuilder.Settings settings)
        {
            if (settings.CompactCardinalShell)
                return;

            if (settings.WallThickness <= 1 && settings.FloorLayers == 0 && settings.CeilingLayers == 0)
            {
                settings.CompactCardinalShell = true;
                settings.AngularSteps = 4;
            }
        }

        static int FillRingGridAligned(
            Transform ringRoot,
            Vector3 floorSurface,
            float corridorHeight,
            Vector3 forward,
            float rx,
            float ry,
            Material rockMaterial,
            System.Random rng,
            CaveBlockTunnelBuilder.Settings settings)
        {
            ApplyCompactRouteDefaults(ref settings);

            if (settings.CompactCardinalShell)
                return FillRingCompactCardinal(
                    ringRoot, floorSurface, corridorHeight, forward, rx, rockMaterial, rng, settings);

            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();

            var up = Vector3.up;
            var right = Vector3.Cross(up, forward).normalized;
            var placed = 0;
            var block = settings.BlockSize;
            var floorBase = floorSurface;
            var tunnelMidY = floorSurface.y + corridorHeight * 0.45f;

            for (var wall = 0; wall < settings.WallThickness; wall++)
            {
                var wallRx = rx * (0.72f + wall * 0.1f);
                var wallRy = ry * (0.72f + wall * 0.1f);

                for (var a = 0; a < settings.AngularSteps; a++)
                {
                    var angle = a / (float)settings.AngularSteps * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);
                    if (Mathf.Abs(sin) < 0.15f)
                        continue;

                    var offset = right * (cos * wallRx) + up * (sin * wallRy);

                    var heightSteps = Mathf.Clamp(Mathf.CeilToInt(corridorHeight / Mathf.Max(0.5f, block)), 2, 8);
                    for (var h = 0; h < heightSteps; h++)
                    {
                        var pos = floorBase + offset + up * (h * block + block * 0.5f);
                        var minable = settings.OuterWallMinable && wall == settings.WallThickness - 1 && h > 0;
                        placed += PlaceBlock(ringRoot, pos, forward, up, rockMaterial, rng, settings, minable);
                    }
                }
            }

            for (var layer = 0; layer < settings.CeilingLayers; layer++)
            {
                for (var a = 0; a < settings.AngularSteps; a++)
                {
                    var angle = a / (float)settings.AngularSteps * Mathf.PI * 2f;
                    var sin = Mathf.Sin(angle);
                    if (sin < 0.2f)
                        continue;

                    var cos = Mathf.Cos(angle);
                    var offset = right * (cos * rx * 0.92f) + up * (sin * ry * 0.95f);
                    var pos = new Vector3(floorSurface.x, tunnelMidY, floorSurface.z) + offset;
                    placed += PlaceBlock(ringRoot, pos, forward, up, rockMaterial, rng, settings, minable: false);
                }
            }

            return placed;
        }

        /// <summary>Four cardinal walls × capped height — compact-route grader band (FDG HDPCG 2026, not onion rings).</summary>
        static int FillRingCompactCardinal(
            Transform ringRoot,
            Vector3 floorSurface,
            float corridorHeight,
            Vector3 forward,
            float rx,
            Material rockMaterial,
            System.Random rng,
            CaveBlockTunnelBuilder.Settings settings)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.Normalize();

            var up = Vector3.up;
            var right = Vector3.Cross(up, forward).normalized;
            var block = settings.BlockSize;
            var wallRx = rx * 0.78f;
            var placed = 0;
            var heightSteps = Mathf.Clamp(Mathf.CeilToInt(corridorHeight / Mathf.Max(0.5f, block)), 2, 4);

            var offsets = new[]
            {
                right * wallRx,
                -right * wallRx,
                forward * wallRx * 0.92f,
                -forward * wallRx * 0.92f
            };

            for (var w = 0; w < settings.WallThickness; w++)
            {
                var layerScale = 1f - w * 0.08f;
                foreach (var offset in offsets)
                {
                    for (var h = 0; h < heightSteps; h++)
                    {
                        var pos = floorSurface + offset * layerScale + up * (h * block + block * 0.5f);
                        var minable = settings.OuterWallMinable && w == settings.WallThickness - 1 && h > 0;
                        placed += PlaceBlock(ringRoot, pos, forward, up, rockMaterial, rng, settings, minable);
                    }
                }
            }

            return placed;
        }

        static int PlaceBlock(
            Transform parent,
            Vector3 localPos,
            Vector3 forward,
            Vector3 up,
            Material rockMaterial,
            System.Random rng,
            CaveBlockTunnelBuilder.Settings settings,
            bool minable)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Cave Block");
            go.name = minable ? "CaveBlock_Minable" : "CaveBlock_Shell";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = SnapToBlockGrid(localPos, settings.BlockSize);
            go.transform.localRotation = Quaternion.identity;

            var mp = settings.MorphPosition;
            if (mp > 0.001f)
            {
                go.transform.localPosition += new Vector3(
                    (float)(rng.NextDouble() * 2 - 1) * mp,
                    (float)(rng.NextDouble() * 2 - 1) * mp,
                    (float)(rng.NextDouble() * 2 - 1) * mp);
                go.transform.localPosition = SnapToBlockGrid(go.transform.localPosition, settings.BlockSize);
            }

            var s = settings.BlockSize * Mathf.Lerp(settings.MorphScaleMin, settings.MorphScaleMax, (float)rng.NextDouble());
            go.transform.localScale = Vector3.one * s;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = rockMaterial;
                renderer.enabled = true;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            if (go.GetComponent<CaveTunnelBlock>() == null)
                go.AddComponent<CaveTunnelBlock>();

            if (minable)
            {
                go.tag = CaveTags.Minable;
                if (go.GetComponent<MinableRock>() == null)
                {
                    var rock = go.AddComponent<MinableRock>();
                    rock.hitPoints = 4;
                }
            }

            var col = go.GetComponent<BoxCollider>();
            if (col != null)
            {
                if (minable)
                {
                    col.size = Vector3.one;
                    col.center = Vector3.zero;
                    col.isTrigger = false;
                }
                else
                    CaveEditorUndo.DestroyImmediate(col);
            }

            go.isStatic = true;
            return 1;
        }

        static Vector3 SnapToBlockGrid(Vector3 localPos, float blockSize)
        {
            if (blockSize <= 0.01f)
                return localPos;

            var step = blockSize;
            return new Vector3(
                Mathf.Round(localPos.x / step) * step,
                Mathf.Round(localPos.y / step) * step,
                Mathf.Round(localPos.z / step) * step);
        }
    }
}
