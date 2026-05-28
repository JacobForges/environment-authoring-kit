using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Continuous organic lava-tube mesh below grade; SUIMONO water only; strictly descending path.
    /// </summary>
    public static class SplineLavaTubeCaveGenerator
    {
        const string GeneratedMeshFolder = "Assets/EnvironmentKit/Generated";
        const string MainMeshAssetPath = GeneratedMeshFolder + "/CaveSplineMainMesh.asset";
        const string OuterMeshAssetPath = GeneratedMeshFolder + "/CaveSplineOuterShellMesh.asset";
        const string BranchMeshAssetPath = GeneratedMeshFolder + "/CaveSplineWaterBranchMesh.asset";

        public static LavaTubeCaveBuildReport Generate(
            Transform environmentRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            System.Func<float, string, bool> reportProgress = null)
        {
            var catalog = LavaTubePrefabCatalog.Load();
            if (!catalog.IsValid)
            {
                Debug.LogError("[SplineCave] Missing lava tube prefabs.");
                return new LavaTubeCaveBuildReport { Message = "Prefab catalog empty." };
            }

            bool Cancelled(float t, string label) =>
                reportProgress != null && reportProgress(t, label);

            if (request.UseLayoutPrototype)
            {
                return CaveLayoutPrototypeGenerator.Generate(
                    environmentRoot,
                    ground,
                    request,
                    catalog,
                    reportProgress);
            }

            if (request.UseTrue3DCaveSystem && request.UseBlockTunnel)
            {
                return CaveAdventureCaveGenerator.Generate(
                    environmentRoot,
                    ground,
                    request,
                    catalog,
                    reportProgress);
            }

            var rng = new System.Random(request.Seed);
            var cavesRoot = EnvironmentSceneUtility.GetOrCreateChild(environmentRoot, "LavaTubeCaveSystem");

            if (Cancelled(0.02f, "Clearing previous cave…"))
                return CancelledReport();

            CaveBuildSceneUtility.ClearChildrenFast(cavesRoot);
            CaveLegacyGeometryPurge.Purge(cavesRoot);

            var entranceForward = ground.HasAnchor ? ground.HorizontalForward : Vector3.forward;
            cavesRoot.position = GetEntranceWorldPosition(ground, cavesRoot);
            cavesRoot.rotation = Quaternion.LookRotation(entranceForward, Vector3.up);

            var entrance = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Entrance");
            entrance.localPosition = new Vector3(0f, CaveGeometryPaths.UndergroundDepthMeters, 0f);
            var meshRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "SplineMesh");
            var seamlessRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "SeamlessTunnel");
            var detailRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Details");
            var propsRoot = EnvironmentSceneUtility.GetOrCreateChild(detailRoot, "Props");
            var waterRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Water");

            if (Cancelled(0.08f, "Above-ground entrance…"))
                return CancelledReport();

            LavaTubeCaveGenerator.BuildEntranceForPipeline(entrance, catalog, rng);
            LavaTubeCaveGenerator.EnsureEntranceMarker(cavesRoot);

            if (Cancelled(0.15f, "Descending path…"))
                return CancelledReport();

            var useTrue3D = request.UseTrue3DCaveSystem;
            var useAdventureHybrid = useTrue3D && request.UseBlockTunnel;
            var useBlocks = request.UseBlockTunnel;
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            List<CavePathKnot> knots;
            CaveMazeLayout mazeLayout = null;
            if (useTrue3D)
            {
                mazeLayout = CaveMazeLayoutGenerator.Generate(
                    request.Seed,
                    request.CaveTunnelSegments,
                    request.CaveChamberCount,
                    request.MazeGenFlavor);
                knots = mazeLayout.PathKnots;
            }
            else
            {
                knots = CavePathFactory.BuildDescendingPath(
                    request.CaveTunnelSegments,
                    request.CaveChamberCount,
                    request.Seed,
                    request.CavePathStepLength > 0 ? request.CavePathStepLength : 11f,
                    request.CavePathDropPerStep > 0 ? request.CavePathDropPerStep : 0.32f,
                    request.CavePathYawVariance > 0 ? request.CavePathYawVariance : 22f,
                    request.CaveChamberSizeMultiplier > 0 ? request.CaveChamberSizeMultiplier : 2.35f,
                    request.CaveEntranceYawDegrees);
                var tunnelRx = knots[0].RadiusX;
                var tunnelRy = knots[0].RadiusY;
                knots[0] = new CavePathKnot(new Vector3(1f, -0.15f, 3.5f), tunnelRx, tunnelRy, false);
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(knots);
            var chamberCenters = new List<Vector3>();
            foreach (var knot in knots)
            {
                if (knot.IsChamber)
                    chamberCenters.Add(knot.Position);
            }

            if (Cancelled(0.24f, "Block tunnel shell…"))
                return CancelledReport();

            var blockCount = 0;
            if (useBlocks)
            {
                if (rockMat == null)
                    Debug.LogError("[SplineCave] Cave rock material missing — block tunnel skipped.");

                var blockSettings = CaveBlockTunnelBuilder.Settings.Default;
                if (useAdventureHybrid)
                {
                    blockSettings.RingSpacing = 2.1f;
                    blockSettings.AngularSteps = 16;
                    blockSettings.FloorLayers = 0;
                    blockSettings.CeilingLayers = 1;
                    blockSettings.WallThickness = 3;
                    blockSettings.InteriorHollow = 0.42f;
                    blockSettings.OuterWallMinable = true;
                }
                else
                {
                    blockSettings.RingSpacing = 3.2f;
                    blockSettings.AngularSteps = 10;
                    blockSettings.FloorLayers = 1;
                    blockSettings.CeilingLayers = 1;
                    blockSettings.WallThickness = 1;
                    blockSettings.OuterWallMinable = false;
                }

                blockCount = CaveBlockTunnelBuilder.Build(cavesRoot, spline, rockMat, request.Seed, blockSettings);
                if (blockCount < 40)
                    Debug.LogWarning($"[SplineCave] Block tunnel placed only {blockCount} cubes — check rock material and path length.");
            }

            if (Cancelled(0.28f, useTrue3D ? "Maze cave volume…" : "Organic tube liner…"))
                return CancelledReport();

            var pieceCount = 0;
            if (useTrue3D && mazeLayout != null)
            {
                ClearLegacyTubeMeshes(meshRoot);
                pieceCount = CaveMazeVolumeBuilder.Build(meshRoot, mazeLayout, rockMat, useAdventureHybrid);
                var mazeVol = meshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName);
                if (mazeVol != null)
                    pieceCount += CaveMazeCeilingCoverBuilder.Build(mazeVol, mazeLayout, rockMat);
                PlaceMazeTorches(meshRoot, mazeLayout, rng);
                if (useAdventureHybrid)
                    CaveAdventureVisualPass.Apply(cavesRoot);
                CaveBuildSceneUtility.ClearChildrenFast(seamlessRoot);
            }
            else
            {
                var settings = CaveTubeMeshSettings.DefaultOrganic;
                settings.Seed = request.Seed;
                settings.InteriorView = true;
                settings.RingSpacing = 2.1f;
                settings.SidesPerRing = 16;
                settings.VerticalWalls = false;
                settings.FloorFlatten = 0.12f;
                settings.HeightMultiplier = 1f;
                var mesh = CaveTubeMeshBuilder.Build(spline, knots, settings);
                pieceCount = CreateMeshObject(meshRoot, "MainCaveTube", mesh, deferSave: true);
                SetTubeRendererEnabled(meshRoot, "MainCaveTube", true);
                pieceCount += BuildSeamlessClosure(seamlessRoot, catalog, rng, spline, chamberCenters);
                pieceCount += CaveCeilingSealUtility.BuildAlongSpline(seamlessRoot, spline, rockMat, mazeMode: false);
            }

            var pathNodes = SamplePathNodes(spline, 24);
            var authoring = cavesRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
                authoring = cavesRoot.gameObject.AddComponent<CaveSplinePathAuthoring>();
            authoring.SetPath(knots, spline.TotalLength);

            var meta = cavesRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                meta = cavesRoot.gameObject.AddComponent<CaveBuildMetadata>();
            meta.Set(request.Seed, request.CaveTunnelSegments, request.CaveChamberCount, useAdventureHybrid);

            if (Cancelled(0.42f, "Walkways + spawn at surface mouth…"))
                return CancelledReport();

            if (useTrue3D && mazeLayout != null)
            {
                CaveMazeWalkwayBuilder.Build(cavesRoot, mazeLayout);
                if (useAdventureHybrid)
                    CaveAdventureFeaturesBuilder.Build(
                        cavesRoot, mazeLayout, rockMat,
                        CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial(), request.Seed);
            }
            else
                CaveWalkwayBuilder.Build(cavesRoot, spline);

            CaveFloorSafetyUtility.Apply(cavesRoot);
            SplineCaveSpawnAligner.AlignEntranceSpawn(
                cavesRoot, entrance, spline, keepAtSurfaceMouth: false, mazeLayout);
            CaveColliderUtility.EnsureMazeVolumeColliders(cavesRoot);
            CaveOrganicInteriorPass.Build(cavesRoot, spline, catalog, rng);

            if (!useBlocks && Cancelled(0.55f, "Minable wall blocks…"))
                return CancelledReport();

            if (!useBlocks)
                CaveMinableWallBuilder.Build(cavesRoot, spline, catalog, rng, spacingMeters: 16f);

            if (Cancelled(0.68f, "Props and lights…"))
                return CancelledReport();

            if (!useTrue3D)
                PlaceLightsAlongSpline(meshRoot, spline, rng);
            ScatterPropsAlongSpline(propsRoot, catalog, rng, spline,
                request.CavePropScatterCount > 0 ? request.CavePropScatterCount : 14);
            PlaceAdventureLoreBeats(detailRoot, spline, catalog, rng);
            PlaceChamberSpawners(cavesRoot, knots);
            PlaceMinablesNearSpline(detailRoot, catalog, rng, spline, 10);

            CaveSplinePath branchSpline = null;
            if (request.IncludeCaveWater)
            {
                if (Cancelled(0.82f, "Water branch geometry…"))
                    return CancelledReport();

                var branchKnots = BuildDescendingWaterBranchKnots(knots[knots.Count - 1], request.Seed + 77);
                branchSpline = new CaveSplinePath();
                branchSpline.SetKnots(branchKnots);
                var branchSettings = CaveTubeMeshSettings.DefaultOrganic;
                branchSettings.Seed = request.Seed + 77;
                branchSettings.InteriorView = true;
                branchSettings.VerticalWalls = useTrue3D;
                branchSettings.FloorFlatten = useTrue3D ? 0.55f : 0.12f;
                branchSettings.HeightMultiplier = useTrue3D ? 2.2f : 1f;
                var branchMesh = CaveTubeMeshBuilder.Build(branchSpline, branchKnots, branchSettings);
                pieceCount += CreateMeshObject(waterRoot, "WaterBranchTube", branchMesh, deferSave: true);
                SetTubeRendererEnabled(waterRoot, "WaterBranchTube", true);
                if (useBlocks)
                {
                    blockCount += CaveBlockTunnelBuilder.Build(
                        cavesRoot,
                        branchSpline,
                        rockMat,
                        request.Seed + 313,
                        new CaveBlockTunnelBuilder.Settings
                        {
                            BlockSize = CaveBlockTunnelBuilder.Settings.Default.BlockSize,
                            RingSpacing = 3.2f,
                            InteriorHollow = CaveBlockTunnelBuilder.Settings.Default.InteriorHollow,
                            AngularSteps = 10,
                            FloorLayers = 1,
                            CeilingLayers = 1,
                            WallThickness = 1,
                            MorphPosition = CaveBlockTunnelBuilder.Settings.Default.MorphPosition,
                            MorphRotation = CaveBlockTunnelBuilder.Settings.Default.MorphRotation,
                            MorphScaleMin = CaveBlockTunnelBuilder.Settings.Default.MorphScaleMin,
                            MorphScaleMax = CaveBlockTunnelBuilder.Settings.Default.MorphScaleMax,
                            OuterWallMinable = false
                        },
                        sectionName: "WaterBranch");
                    SetTubeRendererEnabled(waterRoot, "WaterBranchTube", true);
                }

                var endSample = branchSpline.SampleAtDistance(branchSpline.TotalLength);
                var poolFloor = endSample.Position - endSample.Up * (endSample.RadiusY * 0.74f);
                var fallSample = branchSpline.SampleAtDistance(branchSpline.TotalLength * 0.45f);

                var waterAnchor = cavesRoot.GetComponent<CaveWaterBranchAnchor>();
                if (waterAnchor == null)
                    waterAnchor = cavesRoot.gameObject.AddComponent<CaveWaterBranchAnchor>();
                waterAnchor.SetBranchPositions(poolFloor, fallSample.Position);
            }
            else
            {
                CaveWaterUtility.ClearAllWater(cavesRoot);
            }

            if (request.UseTerrainCarve)
            {
                CaveTerrainIntegrationUtility.EnsureForGroundPlacement(ground, cavesRoot, request.Seed, out _);
                CaveTerrainCarveUtility.CarveForCaveSystem(cavesRoot, spline, branchSpline);
                var terrain = ActiveSceneUtility.FindInActiveScene<Terrain>();
                CaveTerrainUtility.ApplyCaveEntranceMouth(terrain, request.Seed, cavesRoot);
                if (SurfaceWorldGenerator.FindCaveOpenings().Count > 0)
                    CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(cavesRoot, ground, out _);
                else
                    CaveGroundPlacementUtility.FinalizeGroundPlacement(cavesRoot, ground, out _, request.Seed);
            }

            FlushDeferredMeshAssets();

            if (Cancelled(0.95f, "Finishing geometry…"))
                return CancelledReport();

            EnvironmentSceneUtility.MarkSceneDirty();

            return new LavaTubeCaveBuildReport
            {
                PieceCount = pieceCount + blockCount,
                PathNodes = pathNodes,
                Message = useTrue3D
                    ? useAdventureHybrid
                        ? $"Adventure maze: flat floor/ceiling + {blockCount} minable block walls, {mazeLayout?.JumpGapCells?.Count ?? 0} jump gaps."
                        : $"Maze volume cave ({pieceCount} wall pieces) + grand cavern."
                    : useBlocks
                        ? $"Block tunnel ({blockCount} cubes) + terrain carve."
                        : "Organic spline mesh cave."
            };
        }

        static LavaTubeCaveBuildReport CancelledReport() =>
            new() { Message = "Cave build cancelled at stage 3." };

        static bool _meshAssetsDirty;

        static void FlushDeferredMeshAssets()
        {
            if (!_meshAssetsDirty)
                return;

            AssetDatabase.SaveAssets();
            _meshAssetsDirty = false;
        }

        static void SetTubeRendererEnabled(Transform parent, string objectName, bool enabled)
        {
            var child = parent != null ? parent.Find(objectName) : null;
            if (child == null)
                return;

            var mr = child.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = enabled;
        }

        static int CreateMeshObject(Transform parent, string objectName, Mesh mesh, bool deferSave = false)
        {
            if (mesh == null)
                return 0;

            EnsureGeneratedFolder();
            var assetPath = objectName.Contains("Water")
                ? BranchMeshAssetPath
                : objectName.Contains("OuterShell")
                    ? OuterMeshAssetPath
                    : MainMeshAssetPath;
            var savedMesh = SaveMeshAsset(mesh, assetPath, deferSave);

            var go = new GameObject(objectName);
            CaveEditorUndo.RegisterCreated(go, objectName);
            go.transform.SetParent(parent, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = savedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            // Visual shell only — walk on WalkFloor_* colliders (tube mesh collider traps player on walls/ceiling).
            go.isStatic = true;
            return 1;
        }

        static Mesh SaveMeshAsset(Mesh mesh, string path, bool deferSave)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(mesh, path);
            if (deferSave)
                _meshAssetsDirty = true;
            else
                AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder(GeneratedMeshFolder))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
        }

        public static List<Vector3> SamplePathNodes(CaveSplinePath spline, int count)
        {
            var nodes = new List<Vector3>(count);
            for (var i = 0; i < count; i++)
            {
                var t = count <= 1 ? 0f : i / (float)(count - 1);
                nodes.Add(spline.SampleAtNormalized(t).Position);
            }

            return nodes;
        }

        static int BuildSeamlessClosure(
            Transform seamlessRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplinePath spline,
            IReadOnlyList<Vector3> chamberCenters)
        {
            if (seamlessRoot == null || spline == null || catalog == null || !catalog.IsValid)
                return 0;

            CaveBuildSceneUtility.ClearChildrenFast(seamlessRoot);
            var dims = new CaveSeamlessTunnelBuilder.TunnelDimensions
            {
                Width = 9.2f,
                Height = 7.6f,
                NavClearance = 2.4f
            };

            var count = 0;
            var start = spline.SampleAtDistance(0f);
            count += CaveSeamlessTunnelBuilder.BridgeEntranceToPath(
                seamlessRoot,
                catalog,
                rng,
                new Vector3(1f, 0f, 4f),
                start,
                dims);
            count += CaveSeamlessTunnelBuilder.BuildAlongSpline(
                seamlessRoot,
                catalog,
                rng,
                spline,
                dims,
                chamberCenters,
                chamberRadius: 4.8f,
                placeLights: false);
            return count;
        }

        static int BuildTrue3DOuterShell(
            Transform meshRoot,
            CaveSplinePath spline,
            IReadOnlyList<CavePathKnot> knots,
            int seed)
        {
            if (meshRoot == null || spline == null || knots == null || knots.Count < 2)
                return 0;

            var outerKnots = new List<CavePathKnot>(knots.Count);
            for (var i = 0; i < knots.Count; i++)
            {
                var k = knots[i];
                outerKnots.Add(new CavePathKnot(
                    k.Position,
                    k.RadiusX + 1.35f,
                    k.RadiusY + 1.15f,
                    k.IsChamber));
            }

            var outerSettings = CaveTubeMeshSettings.DefaultOrganic;
            outerSettings.Seed = seed + 901;
            outerSettings.InteriorView = false;
            outerSettings.RingSpacing = 1.9f;
            outerSettings.SidesPerRing = 18;
            outerSettings.NoiseAmplitude = 0.42f;
            outerSettings.FloorFlatten = 0.55f;
            outerSettings.VerticalWalls = true;
            outerSettings.HeightMultiplier = 2.8f;
            var outerMesh = CaveTubeMeshBuilder.Build(spline, outerKnots, outerSettings);
            var added = CreateMeshObject(meshRoot, "MainCaveOuterShell", outerMesh, deferSave: true);
            SetTubeRendererEnabled(meshRoot, "MainCaveOuterShell", true);
            return added;
        }


        static void PlaceAdventureLoreBeats(
            Transform detailRoot,
            CaveSplinePath spline,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            if (detailRoot == null || spline == null || catalog == null)
                return;

            var beatsRoot = EnvironmentSceneUtility.GetOrCreateChild(detailRoot, "LoreBeats");
            CaveBuildSceneUtility.ClearChildrenFast(beatsRoot);

            var beatDistances = new[]
            {
                Mathf.Min(6f, spline.TotalLength * 0.12f),
                spline.TotalLength * 0.52f,
                spline.TotalLength * 0.86f
            };
            var beatNames = new[] { "ScoutCamp", "AncientRelay", "DeepSanctum" };

            for (var i = 0; i < beatDistances.Length; i++)
            {
                var d = Mathf.Clamp(beatDistances[i], 0f, spline.TotalLength);
                var s = spline.SampleAtDistance(d);
                var marker = new GameObject($"LoreBeat_{i:D2}_{beatNames[i]}");
                CaveEditorUndo.RegisterCreated(marker, "Cave Lore Beat");
                marker.transform.SetParent(beatsRoot, false);
                marker.transform.localPosition = s.Position;
                marker.transform.localRotation = Quaternion.LookRotation(s.Tangent, s.Up);
                var feature = marker.AddComponent<CaveFeatureMarker>();
                feature.featureKind = CaveFeatureKind.NavWaypoint;
                feature.notes = beatNames[i];

                if (catalog.Artifacts.Count > 0)
                    CavePrefabScatter.PlaceModule(marker.transform, catalog.Pick(catalog.Artifacts, rng), Vector3.zero, Quaternion.identity, Vector3.one * 0.85f, "LoreArtifact", false);
                if (catalog.GlowProps.Count > 0)
                    CavePrefabScatter.PlaceModule(marker.transform, catalog.Pick(catalog.GlowProps, rng), new Vector3(0f, 0.6f, 0f), Quaternion.identity, Vector3.one * 0.8f, "LoreGlow", false);
            }
        }

        static List<CavePathKnot> BuildDescendingWaterBranchKnots(CavePathKnot from, int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<CavePathKnot> { from };
            var pos = from.Position;
            var forward = Vector3.right;
            var prevY = pos.y;
            const float minDrop = 0.48f;

            for (var i = 0; i < 4; i++)
            {
                var pitch = 11f + (float)rng.NextDouble() * 12f;
                var yaw = (float)(rng.NextDouble() * 2 - 1) * 32f;
                var roll = (float)(rng.NextDouble() * 2 - 1) * 5f;
                forward = Quaternion.Euler(roll, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f) * forward;
                forward.Normalize();
                pos += forward * 9.5f + Vector3.down * minDrop;
                if (pos.y >= prevY - minDrop * 0.45f)
                    pos.y = prevY - minDrop;
                prevY = pos.y;

                var isFallChamber = i == 2;
                var mul = isFallChamber ? 1.15f : 0.92f;
                list.Add(new CavePathKnot(pos, from.RadiusX * mul, from.RadiusY * mul, isFallChamber));
            }

            return list;
        }

        static void PlaceLightsAlongSpline(Transform parent, CaveSplinePath spline, System.Random rng)
        {
            // Dense warm torch placement matches the reference fantasy cave look — torches every
            // ~8m on alternating sides so each lit area gives way to a shadowed zone before the
            // next torch reveals more cave, producing dramatic depth/contrast.
            var spacing = 8f;
            var count = Mathf.Max(5, Mathf.FloorToInt(spline.TotalLength / spacing));
            for (var i = 0; i < count; i++)
            {
                var dist = (i + 0.5f) / count * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);

                var sideSign = i % 2 == 0 ? 1f : -1f;
                var wallOffset = sample.Right * (sideSign * sample.RadiusX * 0.78f);
                var heightOffset = sample.Up * (sample.RadiusY * 1.1f);

                var lightGo = new GameObject($"SplineTorch_{i:D2}");
                CaveEditorUndo.RegisterCreated(lightGo, "Spline Torch");
                lightGo.transform.SetParent(parent, false);
                lightGo.transform.localPosition = sample.Position + wallOffset + heightOffset;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 16f;
                light.intensity = 3.2f + (float)rng.NextDouble() * 1.2f;
                light.color = new Color(1f, 0.62f, 0.28f);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.85f;
                light.shadowBias = 0.05f;
                light.shadowNormalBias = 0.4f;
                light.bounceIntensity = 0f;
            }
        }

        internal static void ScatterPropsAlongSpline(
            Transform propsRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplinePath spline,
            int count)
        {
            for (var i = 0; i < count; i++)
            {
                var dist = (float)rng.NextDouble() * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var cos = Mathf.Cos(angle);
                var sin = Mathf.Sin(angle);
                var offset = sample.Right * (cos * sample.RadiusX * 0.72f) + sample.Up * (sin * sample.RadiusY * 0.55f);
                CavePrefabScatter.PlaceRandomProp(propsRoot, catalog, rng, sample.Position + offset, 0.6f + (float)rng.NextDouble() * 0.5f);
            }
        }

        internal static void PlaceChamberSpawners(Transform cavesRoot, List<CavePathKnot> knots) =>
            CaveMobSpawnerPlacement.PlaceAlongRoute(cavesRoot, knots);

        internal static void PlaceMinablesNearSpline(
            Transform detailRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplinePath spline,
            int count)
        {
            for (var i = 0; i < count; i++)
            {
                var dist = (float)rng.NextDouble() * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var side = sample.Right * ((float)rng.NextDouble() * 2f - 1f) * sample.RadiusX * 0.65f;
                CavePrefabScatter.PlaceMinableRock(detailRoot, catalog, rng, sample.Position + side + Vector3.up * 0.4f);
            }

            CleanupBlockTunnelMinables(detailRoot.parent);
        }

        static void CleanupBlockTunnelMinables(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var blockRoot = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            if (blockRoot == null)
                return;

            foreach (var rock in blockRoot.GetComponentsInChildren<MinableRock>(true))
            {
                if (rock == null)
                    continue;
                CaveEditorUndo.DestroyImmediate(rock);
            }
        }

        /// <summary>
        /// Cave root Y = terrain surface at entrance edge − mouth offset (shaft + marker lift).
        /// Mouth snap translates the root; Entrance stays at <see cref="CaveGeometryPaths.UndergroundDepthMeters"/> local Y.
        /// </summary>
        internal static Vector3 GetEntranceWorldPosition(SceneGroundInfo ground, Transform caveRoot = null)
        {
            if (ground == null || !ground.HasAnchor)
                return Vector3.zero;

            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth.sqrMagnitude > 0.25f)
                {
                    // fl-bay-hillshade / DS 926 — expected root tracks mouth anchor whenever marker exists (packaging_readiness).
                    var mouthOffset = CaveGroundPlacementUtility.ResolveMouthOffsetForExpectedPlacement(caveRoot);
                    var expectedXz = mouth;
                    // IEEE CoG spatial anchor: locked build-site XZ must not be compared to marker XZ (avoids false 25m residual).
                    if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
                        expectedXz = caveRoot.position;
                    return new Vector3(
                        expectedXz.x,
                        CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, mouth) - mouthOffset,
                        expectedXz.z);
                }

                var mouthOffsetForRoot = CaveGroundPlacementUtility.ResolveMouthOffsetForPlacement(caveRoot);

                if (caveRoot.position.sqrMagnitude > 0.25f)
                {
                    var p = caveRoot.position;
                    return new Vector3(
                        p.x,
                        CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, p) - mouthOffsetForRoot,
                        p.z);
                }
            }

            var edge = CaveGroundPlacementUtility.ResolveEntranceEdgeXZ(ground, caveRoot);
            var edgeMouthOffset = CaveGroundPlacementUtility.ResolveMouthOffsetForPlacement(caveRoot);
            edge.y = CaveGroundPlacementUtility.SampleWalkableSurfaceWorldY(ground, edge) - edgeMouthOffset;
            return edge;
        }

        internal static void ClearLegacyTubeMeshes(Transform meshRoot)
        {
            if (meshRoot == null)
                return;

            foreach (var name in new[] { "MainCaveTube", "MainCaveOuterShell" })
            {
                var child = meshRoot.Find(name);
                if (child != null)
                    CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        internal static void PlaceMazeTorches(Transform meshRoot, CaveMazeLayout layout, System.Random rng)
        {
            if (meshRoot == null || layout == null)
                return;

            var torchRoot = EnvironmentSceneUtility.GetOrCreateChild(meshRoot, "MazeTorches");
            CaveBuildSceneUtility.ClearChildrenFast(torchRoot);

            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                var center = layout.CellToLocal(cell.x, cell.y);
                var inCavern = layout.IsCavernCell(cell.x, cell.y);
                var height = inCavern ? layout.CorridorHeight * 1.2f : layout.CorridorHeight * 0.55f;
                var intensity = inCavern ? 7f : 4.5f;
                var range = inCavern ? 22f : 14f;
                var offsetX = (i % 4 == 0 ? 1f : -1f) * layout.CellSize * 0.28f;

                var go = new GameObject($"MazeTorch_{cell.x}_{cell.y}");
                CaveEditorUndo.RegisterCreated(go, "Maze Torch");
                go.transform.SetParent(torchRoot, false);
                go.transform.localPosition = center + new Vector3(offsetX, height, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.62f, 0.28f);
                light.intensity = intensity + (float)rng.NextDouble() * 1.2f;
                light.range = range;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.85f;
                light.bounceIntensity = 0f;
            }

            // Bright cavern centerpiece.
            var cavernPos = layout.CellToLocal(layout.CavernCenter.x, layout.CavernCenter.y);
            var bonfire = new GameObject("Cavern_Bonfire");
            CaveEditorUndo.RegisterCreated(bonfire, "Cavern Bonfire");
            bonfire.transform.SetParent(torchRoot, false);
            bonfire.transform.localPosition = cavernPos + Vector3.up * (layout.CorridorHeight * 0.45f);
            var bonfireLight = bonfire.AddComponent<Light>();
            bonfireLight.type = LightType.Point;
            bonfireLight.color = new Color(1f, 0.55f, 0.22f);
            bonfireLight.intensity = 10f;
            bonfireLight.range = 28f;
            bonfireLight.shadows = LightShadows.Soft;
            bonfireLight.bounceIntensity = 0f;
        }

    }
}
