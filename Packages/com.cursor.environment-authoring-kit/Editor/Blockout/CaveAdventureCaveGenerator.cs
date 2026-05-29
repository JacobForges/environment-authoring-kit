using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Underground cave course: 3D rock blocks + textured walk platforms, jump crevices, finish at the cavern.
    /// </summary>
    public static partial class CaveAdventureCaveGenerator
    {
        public const string GeometryRootName = "CaveGeometry";

        public static bool IsAdventureCave(Transform caveRoot) =>
            CaveGeometryPaths.IsAdventureCave(caveRoot);

        public static Transform FindBlockTunnel(Transform caveRoot) =>
            CaveGeometryPaths.FindBlockTunnel(caveRoot);

        public static LavaTubeCaveBuildReport Generate(
            Transform environmentRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubePrefabCatalog catalog,
            System.Func<float, string, bool> reportProgress = null)
        {
            var s = BeginQueued(environmentRoot, ground, request, catalog, reportProgress);
            if (QueuedStepClear(s) || QueuedStepEntrance(s) || QueuedStepMaze(s) ||
                QueuedStepAddTerrain(s) || QueuedStepPlatforms(s) || QueuedStepShell(s) ||
                QueuedStepGrandCavern(s) || QueuedStepBlocksPrepare(s))
            {
                return CancelledReport();
            }

            while (s.BlockRingIndex < s.BlockRingCount)
            {
                if (QueuedStepBlocksBatch(s))
                    return CancelledReport();
                CaveBuildActionPacing.SleepAfterStep(
                    CaveBuildActionPacing.ActionWeight.Normal,
                    "sync block ring batch");
            }

            if (QueuedStepBlocksFinish(s) || QueuedStepFeatures(s) || QueuedStepSurfaceWalkIn(s) ||
                QueuedStepSpawn(s) || QueuedStepPropsAndWater(s))
            {
                return CancelledReport();
            }

            return FinishQueuedReport(s);
        }

        public static Transform GetOrCreateCaveSystemRoot(Transform environmentRoot)
        {
            var legacy = environmentRoot.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            if (legacy != null)
            {
                CaveEditorUndo.RecordObject(legacy.gameObject, "Rename Cave System");
                legacy.gameObject.name = CaveGeometryPaths.CaveSystemRootName;
                return legacy;
            }

            return EnvironmentSceneUtility.GetOrCreateChild(environmentRoot, CaveGeometryPaths.CaveSystemRootName);
        }

        public static Transform EnsureGeometryRoot(Transform cavesRoot)
        {
            var geometry = cavesRoot.Find(GeometryRootName);
            if (geometry == null)
            {
                var go = new GameObject(GeometryRootName);
                CaveEditorUndo.RegisterCreated(go, "Cave Geometry Root");
                go.transform.SetParent(cavesRoot, false);
                geometry = go.transform;
            }

            geometry.localPosition = Vector3.zero;
            geometry.localRotation = Quaternion.identity;
            geometry.localScale = Vector3.one;
            return geometry;
        }

        static void PlaceTorchesOnGeometry(Transform geometry, CaveMazeLayout layout, System.Random rng)
        {
            var torchRoot = geometry.Find("MazeTorches");
            if (torchRoot == null)
            {
                var go = new GameObject("MazeTorches");
                CaveEditorUndo.RegisterCreated(go, "Maze Torches");
                go.transform.SetParent(geometry, false);
                torchRoot = go.transform;
            }

            for (var i = torchRoot.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(torchRoot.GetChild(i).gameObject);

            var step = Mathf.Max(1, layout.SolutionPath.Count / 12);
            for (var i = 0; i < layout.SolutionPath.Count; i += step)
            {
                var cell = layout.SolutionPath[i];
                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var go = new GameObject($"Torch_{cell.x}_{cell.y}");
                CaveEditorUndo.RegisterCreated(go, "Torch");
                go.transform.SetParent(torchRoot, false);
                go.transform.localPosition = floor + Vector3.up * 2.2f;

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.28f);
                light.intensity = 5.5f;
                light.range = 20f;
                light.shadows = LightShadows.Soft;
            }
        }

        public static void EnsureSpawnGroundPad(Transform cavesRoot, CaveMazeLayout layout)
        {
            var spawn = cavesRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null || layout.SolutionPath.Count == 0)
                return;

            var start = layout.SolutionPath[0];
            var floor = layout.GetFloorSurfaceLocal(start.x, start.y);
            var forward = Vector3.forward;
            if (layout.SolutionPath.Count > 1)
            {
                var next = layout.CellToLocal(layout.SolutionPath[1].x, layout.SolutionPath[1].y);
                forward = next - floor;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f)
                    forward.Normalize();
            }

            spawn.localPosition = floor + Vector3.up * 1.05f;
            spawn.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));

            var marker = spawn.GetComponent<CaveEntranceSpawnPoint>();
            if (marker == null)
                marker = spawn.gameObject.AddComponent<CaveEntranceSpawnPoint>();
            marker.snapPlayerOnStart = false;
            marker.positionOffset = Vector3.zero;
            marker.applyRotation = true;
        }

        static void EnsureMovementGuard(Transform cavesRoot)
        {
            var guard = cavesRoot.GetComponent<CavePlayerMovementGuard>();
            if (guard == null)
                guard = cavesRoot.gameObject.AddComponent<CavePlayerMovementGuard>();
            guard.snapNearbyPlayerOnPlay = false;
        }

        static void EnsureHybridRuntime(Transform cavesRoot)
        {
            CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(cavesRoot);

            if (cavesRoot.GetComponent<CaveTunnelVisibilityBootstrap>() == null)
                cavesRoot.gameObject.AddComponent<CaveTunnelVisibilityBootstrap>();

            if (cavesRoot.GetComponent<CaveMiningRegistry>() == null)
                cavesRoot.gameObject.AddComponent<CaveMiningRegistry>();

            if (cavesRoot.GetComponent<CaveRouteVoidFallRecovery>() == null)
                cavesRoot.gameObject.AddComponent<CaveRouteVoidFallRecovery>();

            if (cavesRoot.GetComponent<CavePlaytestRouteBot>() == null)
                cavesRoot.gameObject.AddComponent<CavePlaytestRouteBot>();
        }

        static void BuildHybridWater(Transform cavesRoot, CaveMazeLayout layout)
        {
            if (layout == null || layout.SolutionPath == null || layout.SolutionPath.Count == 0)
                return;

            var deepest = layout.CavernCenter;
            var floor = layout.GetFloorSurfaceLocal(deepest.x, deepest.y);
            var poolPos = floor + new Vector3(0f, 0.25f, 0f);
            var fallPos = poolPos + new Vector3(0f, layout.CorridorHeight * 0.55f, layout.CellSize * 0.15f);

            var anchor = cavesRoot.GetComponent<CaveWaterBranchAnchor>();
            if (anchor == null)
                anchor = cavesRoot.gameObject.AddComponent<CaveWaterBranchAnchor>();
            anchor.SetBranchPositions(poolPos, fallPos);

            var waterRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Water");
            CaveWaterBuilder.Build(
                waterRoot, poolPos, fallPos, poolExtentMeters: layout.PlatformSpan * 2.2f, cavesRoot);

            foreach (var pool in waterRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!pool.name.Contains("Pool") && !pool.name.Contains("Water"))
                    continue;

                try
                {
                    pool.gameObject.tag = CaveTags.Water;
                }
                catch (UnityException) { }
            }

            if (waterRoot.GetComponent<CaveWaterFxPlayer>() == null)
                waterRoot.gameObject.AddComponent<CaveWaterFxPlayer>();
        }

        static void ScatterHybridWallDetails(
            Transform geometry,
            CaveMazeLayout layout,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            if (geometry == null || catalog == null || !catalog.IsValid)
                return;

            var blockRoot = geometry.Find($"{BlockTunnelRootName()}/Main");
            if (blockRoot == null)
                return;

            var details = geometry.Find("WallDetails");
            if (details == null)
            {
                var go = new GameObject("WallDetails");
                CaveEditorUndo.RegisterCreated(go, "Wall Details");
                go.transform.SetParent(geometry, false);
                details = go.transform;
            }

            for (var i = details.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(details.GetChild(i).gameObject);

            var placed = 0;
            foreach (var block in blockRoot.GetComponentsInChildren<Transform>(true))
            {
                if (block == null || block == blockRoot || !block.name.StartsWith("CaveBlock_Minable"))
                    continue;
                if (rng.NextDouble() > 0.14)
                    continue;

                var offset = block.position + block.up * 0.35f + block.right * ((float)rng.NextDouble() * 0.4f - 0.2f);
                var local = details.InverseTransformPoint(offset);
                if (rng.NextDouble() < 0.45)
                    CavePrefabScatter.PlaceMinableRock(details, catalog, rng, local);
                else
                    CavePrefabScatter.PlaceRandomProp(details, catalog, rng, local, 0.65f);

                placed++;
                if (placed >= 48)
                    break;
            }
        }

        static string BlockTunnelRootName() => CaveAdventureBlockBuilder.RootName;

        static void BuildEntranceRampBlocks(
            Transform geometry,
            CaveMazeLayout layout,
            Material rockMat,
            Material floorMat)
        {
            if (layout.SolutionPath.Count == 0)
                return;

            var start = layout.SolutionPath[0];
            var endFloor = layout.GetFloorSurfaceLocal(start.x, start.y);
            var top = new Vector3(2f, CaveGeometryPaths.UndergroundDepthMeters - 1.2f, 5f);
            var mid = (top + endFloor) * 0.5f;
            var delta = endFloor - top;
            var length = delta.magnitude;
            if (length < 4f)
                return;

            var forward = delta;
            forward.y = 0f;
            forward.Normalize();
            var rot = Quaternion.LookRotation(forward, Vector3.up);

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(ramp, "Entrance Ramp");
            ramp.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}EntranceRamp";
            ramp.transform.SetParent(geometry, false);
            ramp.transform.localPosition = mid;
            ramp.transform.localRotation = rot;
            ramp.transform.localScale = new Vector3(layout.PlatformSpan * 1.1f, 0.5f, length + 1.2f);
            var mr = ramp.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = floorMat;
            if (ramp.GetComponent<CaveWalkableMarker>() == null)
                ramp.AddComponent<CaveWalkableMarker>();
        }

        static void PlaceFinishGoal(Transform cavesRoot, CaveMazeLayout layout)
        {
            var c = layout.CavernCenter;
            var floor = layout.GetFloorSurfaceLocal(c.x, c.y);
            var go = new GameObject("CaveFinishGoal");
            CaveEditorUndo.RegisterCreated(go, "Finish Goal");
            go.transform.SetParent(cavesRoot, false);
            go.transform.localPosition = floor + Vector3.up * 3f;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(layout.CellSize * 1.1f, 5f, layout.CellSize * 1.1f);

            var marker = go.AddComponent<CaveFeatureMarker>();
            marker.featureKind = CaveFeatureKind.FinishGoal;
            marker.victoryMessage = "You reached the end of the cave!";

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 0.9f, 1f);
            light.intensity = 10f;
            light.range = 24f;
        }

        static LavaTubeCaveBuildReport CancelledReport() =>
            new() { Message = "Cave build cancelled." };
    }
}
