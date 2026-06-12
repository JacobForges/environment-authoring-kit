#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Authors walkable bridge trails from planner layoutPlan.trails and rebakes surface NavMesh.</summary>
    public static class CaveBuildPlannerTrailAuthor
    {
        public const string TrailsRootName = "PlannerTrails";

        static bool _appliedThisBuild;

        public static void ResetBuildSession() => _appliedThisBuild = false;

        public static bool TryApply(
            SceneGroundInfo ground,
            Transform surfaceRoot,
            WorldGenerationRequest request,
            out string message)
        {
            message = null;
            if (_appliedThisBuild)
            {
                message = "Planner trails already applied this build.";
                return true;
            }

            if (ground?.Terrain == null || surfaceRoot == null)
            {
                message = "No terrain/surface root.";
                return false;
            }

            if (!CaveBuildPlannerLayoutBridge.TryLoad(out var brief, out message))
                return false;

            var plan = brief.layoutPlan;
            var trails = plan?.trails;
            if (trails == null || trails.Length == 0)
            {
                message = "No layoutPlan.trails in planner brief.";
                return false;
            }

            var specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(plan);
            var main = ground.Terrain;
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            var tileSize = main.terrainData.size;
            var waypoints = new List<Vector3>();

            var trailsRoot = surfaceRoot.Find(TrailsRootName);
            if (trailsRoot == null)
            {
                var go = new GameObject(TrailsRootName);
                CaveEditorUndo.RegisterCreated(go, "Planner trails");
                go.transform.SetParent(surfaceRoot, false);
                trailsRoot = go.transform;
            }

            ClearChildren(trailsRoot);

            var authored = 0;
            foreach (var tr in trails)
            {
                if (tr == null || string.IsNullOrEmpty(tr.to))
                    continue;

                if (!TryBuildTrailWaypoints(main, ground, specs, hubY, tileSize, tr, out var pts))
                    continue;

                waypoints.AddRange(pts);
                authored += SculptTrailCorridor(main, ground, pts, request?.Seed ?? 0);
                CreateTrailMarkerLine(trailsRoot, tr.label ?? tr.to, pts);
            }

            var env = surfaceRoot.GetComponentInParent<EnvironmentRoot>() ??
                      UnityEngine.Object.FindAnyObjectByType<EnvironmentRoot>();
            if (env != null)
            {
                SurfaceNavMeshBaker.BakePhase(env.transform, main, surfaceRoot, waypoints, out var navMsg);
                message =
                    $"{authored} trail corridor(s) from {trails.Length} link(s); NavMesh — {navMsg}";
            }
            else
            {
                message = $"{authored} trail corridor(s); NavMesh skipped (no EnvironmentRoot).";
            }

            _appliedThisBuild = true;
            Debug.Log("[CaveBuild] Planner trails — " + message);
            return authored > 0;
        }

        static bool TryBuildTrailWaypoints(
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize,
            CaveBuildPlannerLayoutBridge.TrailLink trail,
            out List<Vector3> points)
        {
            points = new List<Vector3>();
            var dest = trail.to ?? string.Empty;
            if (!dest.StartsWith("island-", StringComparison.OrdinalIgnoreCase))
                return false;

            var leg = dest.Substring(dest.Length - 1).ToUpperInvariant();
            var playCenter = TileCenter(main, Vector2Int.zero, hubY + specs.platformHeightM);
            var islandOff = CaveBuildPlannerLayoutBridge.CardinalDirToOffset(leg);
            var islandY = hubY + specs.plateauHeightM;
            var islandCenter = TileCenter(main, islandOff, islandY);

            const int steps = 8;
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var y = Mathf.Lerp(playCenter.y, islandCenter.y, t);
                var p = Vector3.Lerp(playCenter, islandCenter, t);
                p.y = y + 0.2f;
                points.Add(p);
            }

            return points.Count >= 2;
        }

        static int SculptTrailCorridor(
            Terrain main,
            SceneGroundInfo ground,
            List<Vector3> points,
            int seed)
        {
            if (points == null || points.Count < 2)
                return 0;

            var count = 0;
            foreach (var tile in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(main))
            {
                if (tile?.terrainData == null)
                    continue;

                if (!SculptTrailOnTerrain(tile, points, 3.2f, seed))
                    continue;
                count++;
            }

            return count > 0 ? 1 : 0;
        }

        static bool SculptTrailOnTerrain(Terrain terrain, List<Vector3> points, float halfWidth, int seed)
        {
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var size = data.size;
            var pos = terrain.transform.position;
            var heights = data.GetHeights(0, 0, res, res);
            var changed = false;

            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = pos.x + x / (float)(res - 1) * size.x;
                    var wz = pos.z + z / (float)(res - 1) * size.z;
                    var dist = DistanceToPolyline(new Vector3(wx, 0f, wz), points);
                    if (dist > halfWidth)
                        continue;

                    var targetY = SampleTrailY(wx, wz, points);
                    var norm = Mathf.Clamp01((targetY - pos.y) / Mathf.Max(size.y, 0.01f));
                    var blend = 1f - dist / halfWidth;
                    heights[z, x] = Mathf.Lerp(heights[z, x], norm, blend * 0.65f);
                    changed = true;
                }
            }

            if (!changed)
                return false;

            CaveEditorUndo.RecordObject(data, "Planner trail sculpt");
            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(terrain, 0, 0, heights, requestDelayLod: false);
            terrain.Flush();
            return true;
        }

        static float SampleTrailY(float wx, float wz, List<Vector3> points)
        {
            var best = points[0].y;
            var bestDist = float.MaxValue;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var d = (p.x - wx) * (p.x - wx) + (p.z - wz) * (p.z - wz);
                if (d >= bestDist)
                    continue;
                bestDist = d;
                best = p.y;
            }

            return best;
        }

        static float DistanceToPolyline(Vector3 p, List<Vector3> points)
        {
            var best = float.MaxValue;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                var ab = b - a;
                var t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.01f));
                var closest = a + ab * t;
                var d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(closest.x, closest.z));
                if (d < best)
                    best = d;
            }

            return best;
        }

        static void CreateTrailMarkerLine(Transform parent, string label, List<Vector3> points)
        {
            var go = new GameObject(SanitizeName(label ?? "trail"));
            CaveEditorUndo.RegisterCreated(go, "Planner trail marker");
            go.transform.SetParent(parent, false);
            for (var i = 0; i < points.Count; i++)
            {
                var m = new GameObject($"wp_{i:D2}");
                m.transform.SetParent(go.transform, false);
                m.transform.position = points[i];
            }
        }

        static Vector3 TileCenter(Terrain main, Vector2Int off, float y)
        {
            var tileSize = main.terrainData.size;
            var hub = main.transform.position + tileSize * 0.5f;
            return new Vector3(
                hub.x + off.x * tileSize.x,
                y,
                hub.z + off.y * tileSize.z);
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }

        static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "planner_trail";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }
}
#endif
