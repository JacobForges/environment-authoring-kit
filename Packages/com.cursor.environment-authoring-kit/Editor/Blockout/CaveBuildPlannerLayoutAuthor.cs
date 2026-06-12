#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Applies finalized <see cref="CaveBuildPlannerLayoutBridge"/> markers to the live scene
    /// (tile plateaus, jumping platforms, spawn, fall respawn).
    /// </summary>
    public static class CaveBuildPlannerLayoutAuthor
    {
        public const string LayoutRootName = "PlannerLayout";

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
                message = "Planner layout already applied this build.";
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
            var specs = CaveBuildPlannerLayoutBridge.ResolveSpecs(plan);
            var main = ground.Terrain;
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            var tileSize = main.terrainData.size;

            Undo.IncrementCurrentGroup();
            var undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply planner layout");

            var layoutRoot = GetOrCreateLayoutRoot(surfaceRoot);
            ClearLayoutChildren(layoutRoot);

            var spawnOk = ApplySpawnMarker(main, ground, plan, specs, hubY, tileSize);
            ApplyFallVolume(main, ground, layoutRoot, plan, specs, hubY, tileSize);
            var pruned = PrunePropsOffMazeTiles(surfaceRoot, main, ground, tileSize);

            CaveBuildPlannerMeshLandscapeAuthor.TryApply(ground, surfaceRoot, request, out var meshMsg);
            CaveBuildPlannerContentAuthor.TryApply(ground, surfaceRoot, request, out var contentMsg);
            CaveBuildPlannerTrailAuthor.TryApply(ground, surfaceRoot, request, out var trailMsg);

            Undo.CollapseUndoOperations(undo);

            message =
                $"\"{brief.title}\" — spawn/fall + 3D mesh landscape + content + trails (terrain sculpt unchanged). " +
                $"spawn={(spawnOk ? "ok" : "fallback")}, pruned {pruned} off-maze prop(s). " +
                $"{meshMsg} {contentMsg} {trailMsg} " +
                $"Targets: play +{specs.platformHeightM:F0}u, islands +{specs.plateauHeightM:F0}u.";
            _appliedThisBuild = true;
            Debug.Log("[CaveBuild] Planner layout applied — " + message);
            return true;
        }

        static int ApplyPlayDiskBaseElevation(Terrain main, CaveBuildPlannerLayoutBridge.TechnicalSpecs specs, float hubY)
        {
            var count = 0;
            var targetY = hubY + specs.platformHeightM;
            for (var y = -1; y <= 1; y++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    if (!TryFindPlayDiskTile(main, new Vector2Int(x, y), out var tile) || tile == null)
                        continue;

                    count += RaiseTileToY(tile, targetY, "Planner play-disk elevation");
                }
            }

            return count;
        }

        static int ApplyCardinalIslandPlateaus(Terrain main, CaveBuildPlannerLayoutBridge.TechnicalSpecs specs, float hubY)
        {
            var count = 0;
            var targetY = hubY + specs.plateauHeightM;
            var cardinal = new[]
            {
                new Vector2Int(0, 2),
                new Vector2Int(2, 0),
                new Vector2Int(0, -2),
                new Vector2Int(-2, 0),
            };

            foreach (var off in cardinal)
            {
                if (!TryFindPlayDiskTile(main, off, out var tile) || tile == null)
                    continue;

                count += RaiseTileToY(tile, targetY, "Planner cardinal island elevation");
            }

            return count;
        }

        static int RaiseTileToY(Terrain tile, float targetY, string undoLabel)
        {
            var pos = tile.transform.position;
            if (Mathf.Abs(pos.y - targetY) < 0.25f)
                return 1;

            CaveEditorUndo.RecordObject(tile.transform, undoLabel);
            tile.transform.position = new Vector3(pos.x, targetY, pos.z);
            return 1;
        }

        static Transform GetOrCreateLayoutRoot(Transform surfaceRoot)
        {
            var existing = surfaceRoot.Find(LayoutRootName);
            if (existing != null)
                return existing;

            var go = new GameObject(LayoutRootName);
            CaveEditorUndo.RegisterCreated(go, "Planner layout root");
            go.transform.SetParent(surfaceRoot, false);
            return go.transform;
        }

        static void ClearLayoutChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null)
                    continue;
                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        static int ApplyCornerPlateaus(
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY)
        {
            var corners = new HashSet<Vector2Int>();
            foreach (var m in plan.markers)
            {
                if (CaveBuildPlannerLayoutBridge.IsMazePlateauMarker(m) ||
                    (CaveBuildPlannerLayoutBridge.IsCornerCell(m.row, m.col) &&
                     (m.label ?? string.Empty).IndexOf("maze", StringComparison.OrdinalIgnoreCase) >= 0))
                    corners.Add(CaveBuildPlannerLayoutBridge.PlayRowColToOffset(m.row, m.col));
            }

            if (corners.Count == 0 && plan.playDisk != null && plan.playDisk.labyrinth)
            {
                corners.Add(new Vector2Int(-1, 1));
                corners.Add(new Vector2Int(1, 1));
                corners.Add(new Vector2Int(-1, -1));
                corners.Add(new Vector2Int(1, -1));
            }

            var count = 0;
            foreach (var off in corners)
            {
                if (!TryFindPlayDiskTile(main, off, out var tile) || tile == null)
                    continue;

                var pos = tile.transform.position;
                var targetY = hubY + specs.plateauHeightM;
                if (Mathf.Abs(pos.y - targetY) < 0.25f)
                {
                    count++;
                    continue;
                }

                CaveEditorUndo.RecordObject(tile.transform, "Planner plateau height");
                tile.transform.position = new Vector3(pos.x, targetY, pos.z);
                count++;
            }

            return count;
        }

        static int ApplyPlatforms(
            Terrain main,
            SceneGroundInfo ground,
            Transform layoutRoot,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            var platforms = new List<CaveBuildPlannerLayoutBridge.Marker>();
            foreach (var m in plan.markers)
            {
                if (CaveBuildPlannerLayoutBridge.IsPlatformMarker(m))
                    platforms.Add(m);
            }

            if (platforms.Count == 0)
                return 0;

            var platRoot = new GameObject("JumpPlatforms");
            CaveEditorUndo.RegisterCreated(platRoot, "Planner platforms");
            platRoot.transform.SetParent(layoutRoot, false);

            var hop = Mathf.Max(2f, specs.platformHopM);
            var thickness = 0.6f;
            var width = Mathf.Min(tileSize.x * 0.22f, hop * 1.1f);
            var count = 0;

            foreach (var m in platforms)
            {
                if (!TryResolvePlayMarkerWorld(main, ground, m, specs, hubY, tileSize, out var world))
                    continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = SanitizeName(m.label ?? $"plat_{count}");
                CaveEditorUndo.RegisterCreated(go, "Planner platform");
                go.transform.SetParent(platRoot.transform, false);
                go.transform.position = world;
                go.transform.localScale = new Vector3(width, thickness, width);

                var col = go.GetComponent<Collider>();
                if (col != null)
                    col.sharedMaterial = null;

                var rend = go.GetComponent<Renderer>();
                if (rend != null)
                    rend.sharedMaterial = GetPlatformMaterial();

                count++;
            }

            return count;
        }

        static int ApplyBriefDressingMarkers(
            Terrain main,
            SceneGroundInfo ground,
            Transform layoutRoot,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            if (plan?.markers == null || plan.markers.Length == 0)
                return 0;

            var hubRoot = new GameObject("HubDressing");
            CaveEditorUndo.RegisterCreated(hubRoot, "Planner hub dressing");
            hubRoot.transform.SetParent(layoutRoot, false);

            var switchRoot = new GameObject("HubSwitches");
            CaveEditorUndo.RegisterCreated(switchRoot, "Planner switches");
            switchRoot.transform.SetParent(layoutRoot, false);

            var islandRoot = new GameObject("IslandDressing");
            CaveEditorUndo.RegisterCreated(islandRoot, "Planner island dressing");
            islandRoot.transform.SetParent(layoutRoot, false);

            var count = 0;
            foreach (var m in plan.markers)
            {
                if (CaveBuildPlannerLayoutBridge.IsHubDressingMarker(m))
                {
                    if (!TryResolvePlayMarkerWorld(main, ground, m, specs, hubY, tileSize, out var world))
                        continue;

                    count += PlaceDressingProp(hubRoot.transform, m.label, world, 0.9f, 1.1f, GetDressingMaterial());
                    continue;
                }

                if (CaveBuildPlannerLayoutBridge.IsSwitchMarker(m))
                {
                    if (!TryResolvePlayMarkerWorld(main, ground, m, specs, hubY, tileSize, out var world))
                        continue;

                    count += PlaceSwitchProp(switchRoot.transform, m.label, world);
                    continue;
                }

                if (CaveBuildPlannerLayoutBridge.IsIslandJunctionMarker(m))
                {
                    if (!TryResolveIslandMarkerWorld(main, m, specs, hubY, tileSize, out var world))
                        continue;

                    count += PlaceDressingProp(islandRoot.transform, m.label, world, 0.75f, 0.95f, GetIslandDressingMaterial());
                }
            }

            return count;
        }

        static int PlaceDressingProp(
            Transform parent,
            string label,
            Vector3 world,
            float width,
            float height,
            Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = SanitizeName(label ?? "hub_prop");
            CaveEditorUndo.RegisterCreated(go, "Planner dressing");
            go.transform.SetParent(parent, false);
            go.transform.position = world + Vector3.up * (height * 0.5f);
            go.transform.localScale = new Vector3(width, height, width);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
                rend.sharedMaterial = mat;

            return 1;
        }

        static int PlaceSwitchProp(Transform parent, string label, Vector3 world)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = SanitizeName(label ?? "switch");
            CaveEditorUndo.RegisterCreated(go, "Planner switch");
            go.transform.SetParent(parent, false);
            go.transform.position = world + Vector3.up * 0.08f;
            go.transform.localScale = new Vector3(0.85f, 0.12f, 0.85f);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
                rend.sharedMaterial = GetSwitchMaterial();

            return 1;
        }

        static bool ApplySpawnMarker(
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            CaveBuildPlannerLayoutBridge.Marker spawn = null;
            foreach (var m in plan.markers)
            {
                if (string.Equals(m.kind, "spawn", StringComparison.OrdinalIgnoreCase))
                {
                    spawn = m;
                    break;
                }
            }

            Vector3 world;
            var floorY = hubY + (specs?.platformHeightM ?? 12f);
            if (spawn != null && TryResolvePlayMarkerWorld(main, ground, spawn, specs, hubY, tileSize, out world))
            {
                world = PlayerSpawnHeightUtility.ResolvePivotPosition(world, null);
            }
            else
            {
                var center = TileCenterXZ(main, Vector2Int.zero, floorY);
                world = PlayerSpawnHeightUtility.ResolvePivotPosition(
                    new Vector3(center.x, floorY, center.z),
                    null);
            }

            LavaTubeCaveBuildPipeline.EnsurePlayDiskCenterSpawn(ground);

            var go = GameObject.Find("PlayerSpawnPoint");
            if (go == null)
            {
                go = new GameObject("PlayerSpawnPoint");
                CaveEditorUndo.RegisterCreated(go, "Planner spawn");
            }

            CaveEditorUndo.RecordObject(go.transform, "Planner spawn");
            go.transform.position = world;
            go.transform.rotation = Quaternion.identity;

            try
            {
                if (!go.CompareTag("PlayerSpawn"))
                {
                    CaveEditorUndo.RecordObject(go, "PlayerSpawn tag");
                    go.tag = "PlayerSpawn";
                }
            }
            catch (UnityException)
            {
                Debug.LogWarning("[CaveBuild] Add tag 'PlayerSpawn' in Project Settings for respawn.");
            }

            return spawn != null;
        }

        static void ApplyFallVolume(
            Terrain main,
            SceneGroundInfo ground,
            Transform layoutRoot,
            CaveBuildPlannerLayoutBridge.LayoutPlan plan,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize)
        {
            var center = TileCenterXZ(main, Vector2Int.zero, hubY);
            var span = tileSize.x * 3.5f;
            var depth = Mathf.Max(12f, specs.plateauHeightM * 0.5f);

            var go = new GameObject("PlannerFallVolume");
            CaveEditorUndo.RegisterCreated(go, "Planner fall volume");
            go.transform.SetParent(layoutRoot, false);
            go.transform.position = new Vector3(center.x, hubY - depth * 0.5f - 2f, center.z);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(span, depth, span);

            var trigger = go.AddComponent<PlannerLayoutFallTrigger>();
            EditorUtility.SetDirty(trigger);
        }

        static int PrunePropsOffMazeTiles(
            Transform surfaceRoot,
            Terrain main,
            SceneGroundInfo ground,
            Vector3 tileSize)
        {
            var hubY = SurfaceTerrainTileExpansion.ResolvePlayDiskMainTerrainOrigin(ground, main).y;
            var mazeOffsets = new[]
            {
                new Vector2Int(-1, 1), new Vector2Int(1, 1),
                new Vector2Int(-1, -1), new Vector2Int(1, -1),
            };

            var removed = 0;
            var veg = surfaceRoot.Find(SurfaceIntelligentPropPlacer.VegetationLayerName);
            if (veg == null)
                return 0;

            for (var i = veg.childCount - 1; i >= 0; i--)
            {
                var child = veg.GetChild(i);
                if (child == null)
                    continue;

                var p = child.position;
                var onMaze = false;
                foreach (var off in mazeOffsets)
                {
                    var tileCenter = TileCenterXZ(main, off, hubY);
                    var half = tileSize * 0.45f;
                    if (Mathf.Abs(p.x - tileCenter.x) <= half.x &&
                        Mathf.Abs(p.z - tileCenter.z) <= half.z)
                    {
                        onMaze = true;
                        break;
                    }
                }

                if (!onMaze)
                    continue;

                CaveEditorUndo.DestroyImmediate(child.gameObject);
                removed++;
            }

            return removed;
        }

        public static bool TryResolvePlayMarkerWorld(
            Terrain main,
            SceneGroundInfo ground,
            CaveBuildPlannerLayoutBridge.Marker m,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize,
            out Vector3 world)
        {
            world = Vector3.zero;
            if (m == null || !string.Equals(m.zone, "play", StringComparison.OrdinalIgnoreCase))
                return false;

            var off = CaveBuildPlannerLayoutBridge.PlayRowColToOffset(m.row, m.col);
            var y = hubY;

            if (CaveBuildPlannerLayoutBridge.IsCornerCell(m.row, m.col))
                y += specs?.plateauHeightM ?? 35f;
            else
                y += specs?.platformHeightM ?? 12f;

            var tileCenter = TileCenterXZ(main, off, y);

            var label = m.label ?? string.Empty;
            var slot = Mathf.Max(0, m.slot);
            var offset = Vector3.zero;

            if (CaveBuildPlannerLayoutBridge.IsHubDressingMarker(m) ||
                CaveBuildPlannerLayoutBridge.IsSwitchMarker(m))
            {
                offset = ScatterOffsetInCell(slot, tileSize);
            }
            else if (label.IndexOf("-gap-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var leg = ResolveGapLeg(label);
                var steps = CountGapSteps(m, label);
                var t = steps <= 1 ? 0.5f : slot / (float)(steps - 1);
                offset = LegOffset(leg, tileSize, t);
            }
            else if (label.IndexOf("-exit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     label.IndexOf("-entry", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                offset = EdgeOffset(off, tileSize, label);
            }
            else if (CaveBuildPlannerLayoutBridge.IsPlatformMarker(m))
            {
                offset = EdgeOffset(off, tileSize, label) * 0.35f;
            }

            world = new Vector3(tileCenter.x + offset.x, y + 0.35f, tileCenter.z + offset.z);
            return true;
        }

        public static bool TryResolveIslandMarkerWorld(
            Terrain main,
            CaveBuildPlannerLayoutBridge.Marker m,
            CaveBuildPlannerLayoutBridge.TechnicalSpecs specs,
            float hubY,
            Vector3 tileSize,
            out Vector3 world)
        {
            world = Vector3.zero;
            if (m == null || !string.Equals(m.zone, "island", StringComparison.OrdinalIgnoreCase))
                return false;

            var off = CaveBuildPlannerLayoutBridge.CardinalDirToOffset(m.dir);
            if (off == Vector2Int.zero)
                return false;

            if (!TryFindPlayDiskTile(main, off, out var tile) || tile == null)
                return false;

            var y = tile.transform.position.y + 0.35f;
            var center = tile.transform.position + Vector3.up * (y - tile.transform.position.y);
            var slot = Mathf.Max(0, m.slot);
            var ring = 0.15f + slot * 0.12f;
            var angle = slot * 47f * Mathf.Deg2Rad;
            world = new Vector3(
                center.x + Mathf.Cos(angle) * ring * tileSize.x,
                y,
                center.z + Mathf.Sin(angle) * ring * tileSize.z);
            return true;
        }

        static Vector3 ScatterOffsetInCell(int slot, Vector3 tileSize)
        {
            var cols = 3;
            var row = slot / cols;
            var col = slot % cols;
            var nx = (col - 1) * 0.28f * tileSize.x;
            var nz = (row - 1) * 0.28f * tileSize.z;
            return new Vector3(nx, 0f, nz);
        }

        static Vector3 LegOffset(string leg, Vector3 tileSize, float t)
        {
            t = Mathf.Clamp01(t);
            return leg switch
            {
                "N" => new Vector3(Mathf.Lerp(-tileSize.x * 0.45f, tileSize.x * 0.45f, t), 0f, 0f),
                "S" => new Vector3(Mathf.Lerp(tileSize.x * 0.45f, -tileSize.x * 0.45f, t), 0f, 0f),
                "E" => new Vector3(0f, 0f, Mathf.Lerp(-tileSize.z * 0.45f, tileSize.z * 0.45f, t)),
                "W" => new Vector3(0f, 0f, Mathf.Lerp(tileSize.z * 0.45f, -tileSize.z * 0.45f, t)),
                _ => Vector3.zero,
            };
        }

        static string ResolveGapLeg(string label)
        {
            if (label.IndexOf("-N-gap", StringComparison.OrdinalIgnoreCase) >= 0) return "N";
            if (label.IndexOf("-S-gap", StringComparison.OrdinalIgnoreCase) >= 0) return "S";
            if (label.IndexOf("-E-gap", StringComparison.OrdinalIgnoreCase) >= 0) return "E";
            if (label.IndexOf("-W-gap", StringComparison.OrdinalIgnoreCase) >= 0) return "W";
            return "N";
        }

        static int CountGapSteps(CaveBuildPlannerLayoutBridge.Marker m, string label)
        {
            var maxSlot = m.slot;
            return Mathf.Max(2, maxSlot + 1);
        }

        static Vector3 EdgeOffset(Vector2Int tileOff, Vector3 tileSize, string label)
        {
            if (label.IndexOf("NW", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(-tileSize.x * 0.35f, 0f, tileSize.z * 0.35f);
            if (label.IndexOf("NE", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(tileSize.x * 0.35f, 0f, tileSize.z * 0.35f);
            if (label.IndexOf("SW", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(-tileSize.x * 0.35f, 0f, -tileSize.z * 0.35f);
            if (label.IndexOf("SE", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(tileSize.x * 0.35f, 0f, -tileSize.z * 0.35f);
            if (label.IndexOf("-N-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(0f, 0f, tileSize.z * 0.35f);
            if (label.IndexOf("-S-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(0f, 0f, -tileSize.z * 0.35f);
            if (label.IndexOf("-E-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(tileSize.x * 0.35f, 0f, 0f);
            if (label.IndexOf("-W-", StringComparison.OrdinalIgnoreCase) >= 0)
                return new Vector3(-tileSize.x * 0.35f, 0f, 0f);
            return Vector3.zero;
        }

        static bool TryFindPlayDiskTile(Terrain main, Vector2Int off, out Terrain tile)
        {
            tile = null;
            if (main == null)
                return false;

            if (off == Vector2Int.zero)
            {
                tile = main;
                return true;
            }

            var tilesRoot = SurfaceTerrainTileExpansion.FindTilesRootPublic(main);
            return SurfaceTerrainTileExpansion.TryResolveTerrainAtGridOffset(
                main,
                tilesRoot,
                SurfaceTerrainTileExpansion.FindMountainWildernessRootPublic(main),
                off,
                out tile);
        }

        static Material _platformMat;
        static Material _dressingMat;
        static Material _islandDressingMat;
        static Material _switchMat;

        static Material GetPlatformMaterial()
        {
            if (_platformMat != null)
                return _platformMat;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _platformMat = new Material(shader) { color = new Color(0.55f, 0.52f, 0.48f) };
            return _platformMat;
        }

        static Material GetDressingMaterial()
        {
            if (_dressingMat != null)
                return _dressingMat;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _dressingMat = new Material(shader) { color = new Color(0.42f, 0.34f, 0.22f) };
            return _dressingMat;
        }

        static Material GetIslandDressingMaterial()
        {
            if (_islandDressingMat != null)
                return _islandDressingMat;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _islandDressingMat = new Material(shader) { color = new Color(0.28f, 0.48f, 0.38f) };
            return _islandDressingMat;
        }

        static Material GetSwitchMaterial()
        {
            if (_switchMat != null)
                return _switchMat;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _switchMat = new Material(shader) { color = new Color(0.92f, 0.78f, 0.18f) };
            return _switchMat;
        }

        static Vector3 TileCenterXZ(Terrain main, Vector2Int off, float y)
        {
            var origin = SurfaceTerrainGridRegistry.ExpectedOrigin(main, off);
            var size = main.terrainData.size;
            return new Vector3(origin.x + size.x * 0.5f, y, origin.z + size.z * 0.5f);
        }

        static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "platform";
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
#endif
