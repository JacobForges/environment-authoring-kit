using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>18 post-geometry steps: walkable floors, spawn pad, nav — no editor player teleport.</summary>
    public static class CaveAdventurePlayabilityPipeline
    {
        public const int StepCount = 18;

        public static readonly string[] StepLabels =
        {
            "Lock CaveGeometry space (Y-up, XZ floor)",
            "Verify build metadata (seed / segments)",
            "Mark adventure walkable floors",
            "Ensure spawn ground pad collider",
            "Align spawn to maze start floor",
            "Remove duplicate layer slabs",
            "Adventure shell floor colliders",
            "Thicken walk colliders (anti-fall-through)",
            "Strip outer-shell floor traps",
            "Block tunnel collider audit",
            "Remove invisible blocking colliders",
            "Ensure single route ceiling mesh",
            "Adventure visual pass (blocks + torches)",
            "NavMesh bake (walk surfaces)",
            "Verify NavMesh at spawn",
            "Spawn reachability repair",
            "NavMesh rebake after fixes",
            "Pre-grade walk floor audit"
        };

        public static void RunStep(int step, Transform caveRoot, WorldGenerationRequest request, SceneGroundInfo ground)
        {
            if (caveRoot == null)
                return;

            switch (step)
            {
                case 0: LockGeometryRoot(caveRoot); break;
                case 1: EnsureBuildMetadata(caveRoot, request); break;
                case 2:
                    MarkWalkableFloors(caveRoot);
                    CaveBuildWorkflowCoordinator.MarkWalkFloorsCommitted();
                    break;
                case 3: EnsureSpawnGroundPad(caveRoot, request); break;
                case 4: AlignSpawnToMazeFloor(caveRoot, request); break;
                case 5: CaveAdventureVisualPass.Apply(caveRoot); break;
                case 6: CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot); break;
                case 7: CaveFloorSafetyUtility.Apply(caveRoot); break;
                case 8: StripOuterShellFloorColliders(caveRoot); break;
                case 9: AuditBlockTunnelColliders(caveRoot); break;
                case 10: StripInvisibleBlockingColliders(caveRoot); break;
                case 11: EnsurePathCeilings(caveRoot, request); break;
                case 12: CaveAdventureVisualPass.Apply(caveRoot); break;
                case 13: LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, request); break;
                case 14: VerifyNavMeshAtSpawn(caveRoot); break;
                case 15:
                    CaveBuildWorkflowCoordinator.InvalidateNavMesh();
                    RepairSpawnReachability(caveRoot, request);
                    break;
                case 16: LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot, request, force: true); break;
                case 17: AuditWalkFloorCount(caveRoot, request); break;
            }

            if (step == StepCount - 1)
                EnvironmentSceneUtility.MarkSceneDirty();
        }

        public static int CountWalkFloors(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var count = 0;
            var walk = caveRoot.Find("Walkways");
            if (walk != null)
            {
                foreach (Transform c in walk)
                {
                    if (c.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                        count++;
                }
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
            {
                var routeFloor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
                if (routeFloor != null && routeFloor.GetComponent<Collider>() is { isTrigger: false })
                    count += 12;

                var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
                if (platforms != null)
                {
                    foreach (var col in platforms.GetComponentsInChildren<Collider>(true))
                    {
                        if (col != null && !col.isTrigger && col.enabled)
                            count++;
                    }
                }

                var shell = geometry.Find(CaveAdventureShellBuilder.ShellRootName);
                if (shell != null)
                {
                    foreach (var col in shell.GetComponentsInChildren<Collider>(true))
                    {
                        if (col == null || col.isTrigger)
                            continue;
                        if (!IsAdventureWalkFloorName(col.gameObject.name))
                            continue;
                        count++;
                    }
                }

            }

            var features = caveRoot.Find(CaveAdventureFeaturesBuilder.RootName);
            if (features != null)
            {
                foreach (var col in features.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null && !col.isTrigger &&
                        col.gameObject.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                        count++;
                }
            }

            var pad = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint/SpawnGroundPad");
            if (pad != null && pad.GetComponent<Collider>() != null)
                count++;

            return count;
        }

        public static bool CheckSpawnReachability(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
                return false;

            if (CountWalkFloors(caveRoot) < 6)
                return false;

            var spawnWorld = spawn.position;
            var hits = Physics.RaycastAll(spawnWorld + Vector3.up * 3f, Vector3.down, 16f, ~0,
                QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider != null && CaveWalkableSurface.IsWalkableCollider(hit.collider))
                    return true;
            }

            var nearestDist = float.PositiveInfinity;
            var nearestY = float.PositiveInfinity;
            var shell = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}");
            if (shell != null)
            {
                foreach (var col in shell.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null || !IsAdventureWalkFloorName(col.gameObject.name))
                        continue;
                    var d = Vector3.Distance(spawnWorld, col.bounds.center);
                    if (d < nearestDist)
                    {
                        nearestDist = d;
                        nearestY = Mathf.Abs(spawnWorld.y - col.bounds.center.y);
                    }
                }
            }

            return nearestDist < 30f && nearestY < 12f;
        }

        static bool IsAdventureWalkFloorName(string name) =>
            name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix) ||
            name.Contains("Floor") ||
            name.Contains("Entrance_Floor");

        static void LockGeometryRoot(Transform caveRoot)
        {
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return;

            geometry.localPosition = Vector3.zero;
            geometry.localRotation = Quaternion.identity;
            geometry.localScale = Vector3.one;
        }

        static void EnsureBuildMetadata(Transform caveRoot, WorldGenerationRequest request)
        {
            if (request == null)
                return;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                meta = caveRoot.gameObject.AddComponent<CaveBuildMetadata>();
            meta.Set(request.Seed, request.CaveTunnelSegments, request.CaveChamberCount, hybrid: true);
        }

        static void MarkWalkableFloors(Transform caveRoot)
        {
            CaveFloorSafetyUtility.Apply(caveRoot);

            var shell = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}");
            if (shell == null)
                return;

            foreach (var col in shell.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;
                if (!IsAdventureWalkFloorName(col.gameObject.name))
                    continue;
                if (col.GetComponent<CaveWalkableMarker>() == null)
                    col.gameObject.AddComponent<CaveWalkableMarker>();
            }
        }

        static void EnsureSpawnGroundPad(Transform caveRoot, WorldGenerationRequest request)
        {
            if (request == null)
                return;

            var layout = CaveMazeLayoutGenerator.Generate(
                request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null || layout.SolutionPath.Count == 0)
                return;

            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));
        }

        static void AlignSpawnToMazeFloor(Transform caveRoot, WorldGenerationRequest request)
        {
            var layout = CaveSpawnAlignmentUtility.TryResolveLayout(caveRoot, request);
            if (layout != null)
                CaveSpawnAlignmentUtility.AlignSpawnToMazeStart(caveRoot, layout);
        }

        static void StripOuterShellFloorColliders(Transform caveRoot) =>
            CaveFloorSafetyUtility.Apply(caveRoot);

        static void AuditBlockTunnelColliders(Transform caveRoot)
        {
            var tunnel = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            if (tunnel == null)
                return;

            foreach (var block in tunnel.GetComponentsInChildren<Transform>(true))
            {
                if (block == null || !block.name.StartsWith("CaveBlock_"))
                    continue;
                if (block.name.Contains("Shell") || !CaveRendererVisibility.HasVisibleRenderer(block, true))
                {
                    var shellCol = block.GetComponent<Collider>();
                    if (shellCol != null)
                        CaveEditorUndo.DestroyImmediate(shellCol);
                    continue;
                }

                if (block.GetComponent<Collider>() != null)
                    continue;
                var box = block.gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                box.center = Vector3.zero;
            }
        }

        static void StripInvisibleBlockingColliders(Transform caveRoot)
        {
            if (!CaveGeometryPaths.IsAdventureCave(caveRoot))
                return;

            var entrance = caveRoot.Find("Entrance");
            var geometryRoot = caveRoot.Find(CaveGeometryPaths.GeometryRoot);

            foreach (var col in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;
                if (entrance != null && col.transform.IsChildOf(entrance))
                    continue;
                if (geometryRoot != null && !col.transform.IsChildOf(geometryRoot))
                    continue;
                if (CaveColliderUtility.IsProtectedPlayCollider(col, caveRoot))
                    continue;
                if (CaveColliderUtility.IsAuthoredKitPiece(col, caveRoot))
                    continue;
                if (col.GetComponentInParent<MinableRock>() != null)
                    continue;

                if (!CaveRendererVisibility.HasVisibleRenderer(col.gameObject, true))
                    CaveEditorUndo.DestroyImmediate(col);
            }
        }

        static void EnsurePathCeilings(Transform caveRoot, WorldGenerationRequest request)
        {
            if (request == null || CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
                return;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return;

            var layout = CaveMazeLayoutGenerator.Generate(
                request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
            var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rock == null)
                return;

            CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);
            CaveEnclosureShellBuilder.EnsureSingleCeiling(geometry, layout, rock, request.Seed);
        }

        static void VerifyNavMeshAtSpawn(Transform caveRoot)
        {
            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
                return;

            if (!NavMesh.SamplePosition(spawn.position, out _, 12f, NavMesh.AllAreas))
                Debug.LogWarning("[CaveBuild] NavMesh missing near spawn — walk rebake may be required.");
        }

        static void RepairSpawnReachability(Transform caveRoot, WorldGenerationRequest request)
        {
            AlignSpawnToMazeFloor(caveRoot, request);
            CaveSpawnAlignmentUtility.SnapSpawnToWalkSurface(caveRoot);
            MarkWalkableFloors(caveRoot);
            EnsureSpawnGroundPad(caveRoot, request);
            CaveFloorSafetyUtility.Apply(caveRoot);
        }

        static void AuditWalkFloorCount(Transform caveRoot, WorldGenerationRequest request)
        {
            var count = CountWalkFloors(caveRoot);
            if (request == null)
                return;

            var layout = CaveMazeLayoutGenerator.Generate(
                request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
            var needed = Mathf.Max(8, layout.SolutionPath.Count - layout.JumpGapCells.Count);
            if (count >= needed)
                return;

            Debug.LogWarning(
                $"[CaveBuild] Walk floor count {count} below target {needed} — marking shell floors and rebaking nav.");
            MarkWalkableFloors(caveRoot);
            LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);
        }
    }
}
