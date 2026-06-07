using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Interview / art-pass mode: gameplay layout + flat floor only. No ceiling meshes, block onion shells, or slab stacks.
    /// You sculpt walls and ceiling in Terrain or meshes afterward.
    /// </summary>
    public static class CaveLayoutPrototypeGenerator
    {
        public const string MarkersRootName = "CaveLayoutMarkers";
        public const string FlatFloorRootName = "LayoutWalkFloor";
        public const string ExportPath = "Assets/EnvironmentKit/Generated/CaveLayoutBlueprint.json";

        public static bool IsLayoutPrototypeRoot(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            return geometry != null && geometry.Find(FlatFloorRootName) != null;
        }

        public static LavaTubeCaveBuildReport Generate(
            Transform environmentRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubePrefabCatalog catalog,
            System.Func<float, string, bool> reportProgress = null)
        {
            bool Cancelled(float t, string label) =>
                reportProgress != null && reportProgress(t, label);

            var rng = new System.Random(request.Seed);
            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            var cavesRoot = CaveAdventureCaveGenerator.GetOrCreateCaveSystemRoot(environmentRoot);
            if (Cancelled(0.02f, "Clearing previous cave…"))
                return CancelledReport();

            CaveBuildSceneUtility.ClearChildrenFast(cavesRoot);
            CaveLegacyGeometryPurge.Purge(cavesRoot);
            CaveCompactLayerPurge.Purge(cavesRoot);

            var geometryEarly = CaveAdventureCaveGenerator.EnsureGeometryRoot(cavesRoot);
            CaveEnclosureShellBuilder.PurgeLayerOffenders(geometryEarly);
            CaveEnclosureShellBuilder.DestroyCeiling(geometryEarly);

            cavesRoot.position = SplineLavaTubeCaveGenerator.GetEntranceWorldPosition(ground);
            var entranceForward = ground.HasAnchor ? ground.HorizontalForward : Vector3.forward;
            cavesRoot.rotation = Quaternion.LookRotation(entranceForward, Vector3.up);
            cavesRoot.localScale = Vector3.one;

            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(cavesRoot);
            var entrance = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Entrance");
            entrance.localPosition = new Vector3(0f, CaveGeometryPaths.UndergroundDepthMeters, 0f);

            if (Cancelled(0.1f, "Entrance + layout…"))
                return CancelledReport();

            LavaTubeCaveGenerator.BuildEntranceForPipeline(entrance, catalog, rng);
            LavaTubeCaveGenerator.EnsureEntranceMarker(cavesRoot);

            var layout = CaveMazeLayoutGenerator.GeneratePrototype(
                request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);

            var spline = new CaveSplinePath();
            spline.SetKnots(layout.PathKnots);

            if (Cancelled(0.35f, "Flat walk floor…"))
                return CancelledReport();

            var floorPieces = BuildFlatWalkFloor(geometry, layout, floorMat);

            if (Cancelled(0.5f, "Layout markers (path / jumps / goal)…"))
                return CancelledReport();

            var markerCount = BuildLayoutMarkers(cavesRoot, geometry, layout);
            PlaceFinishGoal(cavesRoot, layout);
            ExportBlueprint(cavesRoot, layout, request.Seed);

            var authoring = cavesRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
                authoring = cavesRoot.gameObject.AddComponent<CaveSplinePathAuthoring>();
            authoring.SetPath(layout.PathKnots, spline.TotalLength);

            var meta = cavesRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                meta = cavesRoot.gameObject.AddComponent<CaveBuildMetadata>();
            meta.Set(request.Seed, request.CaveTunnelSegments, request.CaveChamberCount, hybrid: true);

            SplineCaveSpawnAligner.AlignEntranceSpawn(
                cavesRoot, entrance, spline, keepAtSurfaceMouth: false, layout);
            LavaTubeCaveBuildPipeline.EnsureGameplaySpawns(cavesRoot, ground);
            CaveAdventureCaveGenerator.EnsureSpawnGroundPad(cavesRoot, layout);
            CaveMobSpawnerPlacement.PlaceAlongRoute(cavesRoot, layout);

            var terrainNote = CaveLayoutTerrainPad.PrepareSculptSurface(environmentRoot, cavesRoot, layout, ground);

            LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(
                cavesRoot, SplineLavaTubeCaveGenerator.SamplePathNodes(spline, 24));

            EnvironmentSceneUtility.MarkSceneDirty();

            return new LavaTubeCaveBuildReport
            {
                PieceCount = floorPieces + markerCount,
                PathNodes = SplineLavaTubeCaveGenerator.SamplePathNodes(spline, 24),
                Message =
                    $"Layout prototype: {layout.SolutionPath.Count} steps, {layout.JumpGapCells?.Count ?? 0} jumps, " +
                    $"{floorPieces} flat floor piece(s), {markerCount} markers. " +
                    "No ceiling/block shells — sculpt art on Terrain or meshes yourself. " + terrainNote
            };
        }

        static int BuildFlatWalkFloor(Transform geometry, CaveMazeLayout layout, Material floorMat)
        {
            var existing = geometry.Find(FlatFloorRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(FlatFloorRootName);
            CaveEditorUndo.RegisterCreated(root, "Layout Walk Floor");
            root.transform.SetParent(geometry, false);

            var baseY = layout.SolutionPath.Count > 0
                ? layout.GetFloorSurfaceLocal(layout.SolutionPath[0].x, layout.SolutionPath[0].y).y
                : 0f;

            var placed = 0;
            const float thickness = 0.35f;
            foreach (var cell in layout.SolutionPath)
            {
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var center = layout.CellToLocal(cell.x, cell.y);
                center.y = baseY;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Layout Floor");
                go.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}Layout_{cell.x}_{cell.y}";
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = center + Vector3.up * (thickness * 0.5f);
                go.transform.localScale = new Vector3(layout.PlatformSpan, thickness, layout.PlatformDepth);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null && floorMat != null)
                    mr.sharedMaterial = floorMat;

                if (go.GetComponent<CaveWalkableMarker>() == null)
                    go.AddComponent<CaveWalkableMarker>();

                placed++;
            }

            return placed;
        }

        static int BuildLayoutMarkers(Transform cavesRoot, Transform geometry, CaveMazeLayout layout)
        {
            var existing = geometry.Find(MarkersRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(MarkersRootName);
            CaveEditorUndo.RegisterCreated(root, "Layout Markers");
            root.transform.SetParent(geometry, false);

            var count = 0;
            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                var pos = layout.GetFloorSurfaceLocal(cell.x, cell.y) + Vector3.up * 0.5f;
                var kind = CaveLayoutMarkerKind.Path;
                if (i == 0)
                    kind = CaveLayoutMarkerKind.Start;
                else if (i == layout.SolutionPath.Count - 1)
                    kind = CaveLayoutMarkerKind.Finish;
                else if (layout.IsJumpGap(cell.x, cell.y))
                    kind = CaveLayoutMarkerKind.JumpGap;
                else if (layout.HasLandmarkCell && cell == layout.LandmarkCell)
                    kind = CaveLayoutMarkerKind.Landmark;

                count += PlaceMarker(root.transform, $"Marker_{i:D2}_{kind}", pos, kind);
            }

            foreach (var cell in layout.JumpGapCells ?? new HashSet<Vector2Int>())
            {
                if (layout.SolutionPath.Contains(cell))
                    continue;
                var pos = layout.GetFloorSurfaceLocal(cell.x, cell.y) + Vector3.up * 0.5f;
                count += PlaceMarker(root.transform, $"Marker_Jump_{cell.x}_{cell.y}", pos, CaveLayoutMarkerKind.JumpGap);
            }

            return count;
        }

        static int PlaceMarker(Transform parent, string name, Vector3 localPos, CaveLayoutMarkerKind kind)
        {
            var go = new GameObject(name);
            CaveEditorUndo.RegisterCreated(go, "Layout Marker");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var marker = go.AddComponent<CaveLayoutMarker>();
            marker.kind = kind;
            return 1;
        }

        static void PlaceFinishGoal(Transform cavesRoot, CaveMazeLayout layout)
        {
            var c = layout.CavernCenter;
            var floor = layout.GetFloorSurfaceLocal(c.x, c.y);
            var go = new GameObject("CaveFinishGoal");
            CaveEditorUndo.RegisterCreated(go, "Finish Goal");
            go.transform.SetParent(cavesRoot, false);
            go.transform.localPosition = floor + Vector3.up * 2f;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(layout.CellSize * 1.2f, 4f, layout.CellSize * 1.2f);

            var feature = go.AddComponent<CaveFeatureMarker>();
            feature.featureKind = CaveFeatureKind.FinishGoal;
            feature.victoryMessage = "You reached the end of the cave route!";
        }

        static void ExportBlueprint(Transform cavesRoot, CaveMazeLayout layout, int seed)
        {
            if (layout?.SolutionPath == null)
                return;

            if (!Directory.Exists("Assets/EnvironmentKit/Generated"))
                Directory.CreateDirectory("Assets/EnvironmentKit/Generated");

            var lines = new List<string>
            {
                "{",
                $"  \"seed\": {seed},",
                $"  \"stepCount\": {layout.SolutionPath.Count},",
                $"  \"jumpGaps\": {layout.JumpGapCells?.Count ?? 0},",
                layout.HasLandmarkCell
                    ? $"  \"landmarkCell\": [{layout.LandmarkCell.x}, {layout.LandmarkCell.y}],"
                    : "  \"landmarkCell\": null,",
                "  \"path\": ["
            };

            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                var p = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var jump = layout.IsJumpGap(cell.x, cell.y);
                lines.Add(
                    $"    {{ \"cell\": [{cell.x}, {cell.y}], \"local\": [{p.x:F2}, {p.y:F2}, {p.z:F2}], \"jump\": {(jump ? "true" : "false")} }}" +
                    (i < layout.SolutionPath.Count - 1 ? "," : ""));
            }

            lines.Add("  ]");
            lines.Add("}");

            File.WriteAllLines(ExportPath, lines);
            CaveBuildDeferredAssetRefresh.RequestRefresh();
            Debug.Log($"[CaveLayout] Blueprint exported: {ExportPath}");
        }

        static LavaTubeCaveBuildReport CancelledReport() =>
            new() { Message = "Layout prototype build cancelled." };
    }
}
