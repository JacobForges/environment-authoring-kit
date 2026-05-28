using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Path-aligned enclosed lava tube: continuous floor / walls / ceiling, chambers, props.
    /// Tuned for mobile / XR (fewer segments, lighter lights).
    /// </summary>
    public static class LavaTubeCaveGenerator
    {
        /// <summary>Tallest playable character (controller height + typical scale).</summary>
        const float CharacterHeightRef = 2.5f;
        /// <summary>User rule: tunnel interior ≥ 2.5× character height on width and height.</summary>
        const float MinSizeMultiplier = 2.5f;
        const float MinTunnelInterior = CharacterHeightRef * MinSizeMultiplier; // 6.25m

        const float StepLength = 11f;
        const float TunnelWidth = MinTunnelInterior + 0.5f;
        const float TunnelHeight = MinTunnelInterior;
        const float ChamberSize = MinTunnelInterior * 2.4f;
        const float NavClearance = 2.2f;
        const float FloorModuleSpan = 5f;

        struct PathNode
        {
            public Vector3 Position;
            public Vector3 Forward;
            public bool IsChamber;
        }

        /// <summary>Entrance prefab block used by spline mesh pipeline.</summary>
        public static void BuildEntranceForPipeline(
            Transform entranceParent,
            LavaTubePrefabCatalog catalog,
            System.Random rng)
        {
            BuildEntrance(entranceParent, catalog, rng, Vector3.down);
        }

        public static void EnsureEntranceMarker(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
            {
                if (entrance.Find(CaveEntranceTeleport.EntranceMarkerObjectName) != null)
                    return;

                PlaceEntranceMarker(entrance, Vector3.forward);
                return;
            }

            if (caveRoot.GetComponentInChildren<CaveFeatureMarker>(true) != null)
                return;

            PlaceEntranceMarker(caveRoot, Vector3.forward);
        }

        public static LavaTubeCaveBuildReport Generate(
            Transform environmentRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            var catalog = LavaTubePrefabCatalog.Load();
            if (!catalog.IsValid)
            {
                Debug.LogError(
                    "[LavaTubeCave] Missing prefabs under asset_reference: " + LavaTubePrefabCatalog.PrefabRoot);
                return new LavaTubeCaveBuildReport { Message = "Prefab catalog empty." };
            }

            var rng = new System.Random(request.Seed);
            var cavesRoot = EnvironmentSceneUtility.GetOrCreateChild(environmentRoot, "LavaTubeCaveSystem");
            CaveBuildSceneUtility.ClearChildrenFast(cavesRoot);

            var entranceForward = ground.HasAnchor ? ground.HorizontalForward : Vector3.forward;
            var origin = SplineLavaTubeCaveGenerator.GetEntranceWorldPosition(ground);
            cavesRoot.position = origin;
            cavesRoot.rotation = Quaternion.LookRotation(entranceForward, Vector3.up);

            var local = cavesRoot;
            var down = Vector3.down;
            var tunnelRoot = EnvironmentSceneUtility.GetOrCreateChild(local, "Tunnels");
            var chamberRoot = EnvironmentSceneUtility.GetOrCreateChild(local, "Chambers");
            var waterRoot = EnvironmentSceneUtility.GetOrCreateChild(local, "Water");
            var detailRoot = EnvironmentSceneUtility.GetOrCreateChild(local, "Details");

            var entrance = EnvironmentSceneUtility.GetOrCreateChild(local, "Entrance");
            BuildEntrance(entrance, catalog, rng, down);

            var stepLen = request.CavePathStepLength > 0f ? request.CavePathStepLength : 11f;
            var drop = request.CavePathDropPerStep > 0f ? request.CavePathDropPerStep : 0.32f;
            var yawVar = request.CavePathYawVariance > 0f ? request.CavePathYawVariance : 22f;
            var chamberMul = request.CaveChamberSizeMultiplier > 0f ? request.CaveChamberSizeMultiplier : 2.35f;

            var knots = CavePathFactory.BuildDescendingPath(
                request.CaveTunnelSegments,
                request.CaveChamberCount,
                request.Seed,
                stepLen,
                drop,
                yawVar,
                chamberMul,
                request.CaveEntranceYawDegrees);
            knots[0] = new CavePathKnot(new Vector3(2f, 0.45f, 6f), knots[0].RadiusX, knots[0].RadiusY, false);

            var spline = new CaveSplinePath();
            spline.SetKnots(knots);

            var nodes = new List<Vector3>();
            var pieceCount = 0;
            var minablePlaced = 0;
            var targetMinables = request.CaveMinableTarget > 0 ? request.CaveMinableTarget : 12;
            var waterAtSeg = request.CaveWaterBranchSegment >= 0
                ? request.CaveWaterBranchSegment
                : request.CaveTunnelSegments / 2;
            var propsRoot = EnvironmentSceneUtility.GetOrCreateChild(detailRoot, "Props");

            var chamberCenters = new List<Vector3>();
            foreach (var knot in knots)
            {
                if (knot.IsChamber)
                    chamberCenters.Add(knot.Position);
            }

            var tunnelDims = new CaveSeamlessTunnelBuilder.TunnelDimensions
            {
                Width = TunnelWidth,
                Height = TunnelHeight,
                NavClearance = NavClearance
            };

            var pathStart = spline.SampleAtDistance(0f);
            pieceCount += CaveSeamlessTunnelBuilder.BridgeEntranceToPath(
                tunnelRoot, catalog, rng, new Vector3(1f, 0f, 4f), pathStart, tunnelDims);

            pieceCount += CaveSeamlessTunnelBuilder.BuildAlongSpline(
                tunnelRoot, catalog, rng, spline, tunnelDims, chamberCenters,
                ChamberSize * 0.55f, placeLights: true);

            var waterDist = waterAtSeg * stepLen;
            for (var dist = 0f; dist < spline.TotalLength; dist += 5.5f)
            {
                var sample = spline.SampleAtDistance(dist);
                nodes.Add(sample.Position);
                ScatterSegmentProps(propsRoot, catalog, rng, sample.Position,
                    sample.Position + sample.Tangent * 2f, 1);

                if (Mathf.Abs(dist - waterDist) < stepLen * 0.6f)
                    pieceCount += BuildWaterBranch(waterRoot, tunnelRoot, catalog, rng,
                        sample.Position, sample.Tangent, request.CaveWaterBranchYaw);

                var ringIndex = Mathf.FloorToInt(dist / 5.5f);
                if (minablePlaced < targetMinables && ringIndex % 2 == 0)
                {
                    PlaceMinableRock(detailRoot, catalog, rng,
                        sample.Position + sample.Right * (TunnelWidth * 0.35f) + Vector3.up * 0.5f);
                    minablePlaced++;
                }
            }

            var chamberIdx = 0;
            foreach (var knot in knots)
            {
                if (!knot.IsChamber)
                    continue;
                var sample = spline.SampleAtDistance(FindClosestDistance(spline, knot.Position));
                pieceCount += BuildNaturalChamber(chamberRoot, catalog, rng, propsRoot,
                    sample.Position, sample.Tangent, chamberIdx++);
                minablePlaced += PlaceMinableCluster(chamberRoot, catalog, rng, sample.Position, 2);
                ScatterChamberProps(propsRoot, catalog, rng, sample.Position, sample.Tangent, 5);
                nodes.Add(sample.Position);
            }

            while (minablePlaced < targetMinables && nodes.Count > 0)
            {
                var node = nodes[rng.Next(nodes.Count)];
                PlaceMinableRock(detailRoot, catalog, rng, node + new Vector3(0f, 0.5f, (float)(rng.NextDouble() * 2 - 1) * 2f));
                minablePlaced++;
            }

            ScatterArtifacts(detailRoot, catalog, rng, nodes);
            PlaceEntranceMarker(local, spline.SampleAtDistance(Mathf.Min(4f, spline.TotalLength)).Tangent);

            var authoring = local.gameObject.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null)
                authoring = Undo.AddComponent<CaveSplinePathAuthoring>(local.gameObject);
            authoring.SetPath(knots, spline.TotalLength);

            SplineCaveSpawnAligner.AlignEntranceSpawn(local, entrance, spline);

            EnvironmentSceneUtility.MarkSceneDirty();

            return new LavaTubeCaveBuildReport
            {
                PieceCount = pieceCount,
                PathNodes = nodes,
                Message = $"Seamless tunnel rings along spline, seed {request.Seed}."
            };
        }

        static float FindClosestDistance(CaveSplinePath spline, Vector3 localPos)
        {
            var best = 0f;
            var bestDist = float.MaxValue;
            const int steps = 32;
            for (var i = 0; i <= steps; i++)
            {
                var d = i / (float)steps * spline.TotalLength;
                var p = spline.SampleAtDistance(d).Position;
                var sqr = (p - localPos).sqrMagnitude;
                if (sqr < bestDist)
                {
                    bestDist = sqr;
                    best = d;
                }
            }

            return best;
        }

        static List<PathNode> BuildNaturalPath(System.Random rng, WorldGenerationRequest request)
        {
            var segments = Mathf.Clamp(request.CaveTunnelSegments, 8, 14);
            var chamberCount = Mathf.Clamp(request.CaveChamberCount, 3, 5);
            var chamberEvery = Mathf.Max(2, segments / Mathf.Max(1, chamberCount));

            var path = new List<PathNode>(segments + 1);
            var pos = Vector3.zero;
            var forward = Vector3.forward;
            path.Add(new PathNode { Position = pos, Forward = forward, IsChamber = false });

            for (var i = 0; i < segments; i++)
            {
                var yaw = (float)(rng.NextDouble() * 22 - 11);
                forward = Quaternion.Euler(0f, yaw, 0f) * forward;
                forward.Normalize();
                pos += forward * StepLength + Vector3.down * 0.32f;
                var isChamber = i > 0 && (i + 1) % chamberEvery == 0;
                path.Add(new PathNode { Position = pos, Forward = forward, IsChamber = isChamber });
            }

            return path;
        }

        static void BuildEntrance(Transform parent, LavaTubePrefabCatalog catalog, System.Random rng, Vector3 down)
        {
            var floorY = 0f;
            var floorScale = new Vector3(TunnelWidth / FloorModuleSpan, 1f, TunnelWidth / FloorModuleSpan);

            for (var z = -1; z <= 2; z++)
            {
                PlaceModule(parent, catalog.Pick(catalog.Floors, rng),
                    new Vector3(z * 4.5f, floorY, 0f), Quaternion.identity, floorScale,
                    "asset_reference: SM_Floor01A", false);
            }

            PlaceModule(parent, catalog.Pick(catalog.Floors, rng),
                new Vector3(6f, floorY + 0.35f, 0f), Quaternion.Euler(-10f, 0f, 0f),
                floorScale * 1.15f, "asset_reference: SM_Floor02A", false);

            var halfW = TunnelWidth * 0.5f;
            for (var z = -1; z <= 2; z++)
            {
                var zPos = z * 4.5f;
                PlaceModule(parent, catalog.Pick(catalog.Walls, rng),
                    new Vector3(zPos, floorY + TunnelHeight * 0.5f, -halfW), Quaternion.identity,
                    new Vector3(1.2f, 1.2f, 1f), "asset_reference: SM_Wall02A", false);
                PlaceModule(parent, catalog.Pick(catalog.Walls, rng),
                    new Vector3(zPos, floorY + TunnelHeight * 0.5f, halfW), Quaternion.identity,
                    new Vector3(1.2f, 1.2f, 1f), "asset_reference: SM_Wall02B", false);
            }

            for (var z = 0; z <= 1; z++)
            {
                PlaceModule(parent, catalog.Pick(catalog.Ceilings, rng),
                    new Vector3(z * 5f, floorY + TunnelHeight, 0f), Quaternion.identity,
                    floorScale * 1.05f, "asset_reference: SM_Ceiling01A", false);
            }

            PlaceModule(parent, catalog.Pick(catalog.Walls, rng),
                new Vector3(-5f, floorY + TunnelHeight * 0.45f, 0f), Quaternion.Euler(0f, 90f, 0f),
                new Vector3(1.1f, 1.1f, 1f), "asset_reference: SM_Wall03A", false);

            PlaceMinableCluster(parent, catalog, rng, new Vector3(-2f, floorY, 0f), 4);

            var lightGo = new GameObject("EntranceKeyLight");
            Undo.RegisterCreatedObjectUndo(lightGo, "Entrance Light");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = new Vector3(4f, floorY + 3.5f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 22f;
            light.intensity = 1.4f;
            light.color = new Color(1f, 0.78f, 0.5f);
            light.shadows = LightShadows.None;

            var markerGo = new GameObject("CaveEntrance_Marker");
            Undo.RegisterCreatedObjectUndo(markerGo, "Cave Entrance Marker");
            markerGo.transform.SetParent(parent, false);
            markerGo.transform.localPosition = new Vector3(1f, floorY + 1.6f, 0f);
            markerGo.tag = CaveTags.Entrance;
            var marker = markerGo.AddComponent<CaveFeatureMarker>();
            marker.featureKind = CaveFeatureKind.Entrance;
            marker.notes = "Walk-in entrance above ground. Face into cave.";

            CreateEntranceSpawnPoint(parent, floorY);
        }

        static void CreateEntranceSpawnPoint(Transform entranceParent, float floorY)
        {
            var existing = entranceParent.Find("CaveEntrance_SpawnPoint");
            if (existing != null)
                return;

            var spawnGo = new GameObject("CaveEntrance_SpawnPoint");
            Undo.RegisterCreatedObjectUndo(spawnGo, "Cave Entrance Spawn");
            spawnGo.transform.SetParent(entranceParent, false);
            spawnGo.transform.localPosition = new Vector3(1f, floorY + 1.6f, 0f);
            spawnGo.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            spawnGo.tag = CaveTags.Entrance;

            spawnGo.AddComponent<CaveEntranceSpawnPoint>();
        }

        static int BuildTunnelSegment(
            Transform parent,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Transform propsRoot,
            Vector3 from,
            Vector3 to,
            string label,
            bool placeLight)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 0.01f)
                return 0;

            var forward = delta / length;
            var mid = (from + to) * 0.5f;
            var moduleRoot = new GameObject($"Tunnel_{label}");
            Undo.RegisterCreatedObjectUndo(moduleRoot, "Tunnel Segment");
            moduleRoot.transform.SetParent(parent, false);
            moduleRoot.transform.localPosition = mid;
            moduleRoot.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            var module = moduleRoot.transform;
            var count = 0;
            var along = length / FloorModuleSpan;
            var floorScale = new Vector3(TunnelWidth / FloorModuleSpan, 1f, along);
            var halfW = TunnelWidth * 0.5f;

            count += PlaceModule(module, catalog.Pick(catalog.Floors, rng), Vector3.zero, Quaternion.identity,
                floorScale, "SM_Floor", false) ? 1 : 0;
            count += PlaceModule(module, catalog.Pick(catalog.Ceilings, rng),
                new Vector3(0f, TunnelHeight, 0f), Quaternion.identity,
                floorScale * 1.02f, "SM_Ceiling", false) ? 1 : 0;

            CaveVerticalWallPlacer.PlaceTunnelWalls(module, catalog, rng, halfW, TunnelHeight, length, ref count);
            PlaceAngledRockFill(module, catalog, rng, halfW, ref count);

            if (catalog.Stalactites.Count > 0 && rng.NextDouble() > 0.55)
            {
                count += PlaceModule(module, catalog.Pick(catalog.Stalactites, rng),
                    new Vector3(0f, TunnelHeight - 0.2f, (float)(rng.NextDouble() * 2 - 1)),
                    Quaternion.Euler(180f, (float)(rng.NextDouble() * 360), 0f),
                    Vector3.one * 0.85f, "SM_Stalactite", false) ? 1 : 0;
            }

            if (placeLight)
                PlaceTunnelLight(module, rng);

            EnsureNavClearance(module, length);
            return count;
        }

        static void PlaceTunnelLight(Transform module, System.Random rng)
        {
            var lightGo = new GameObject("TunnelLight");
            Undo.RegisterCreatedObjectUndo(lightGo, "Tunnel Light");
            lightGo.transform.SetParent(module, false);
            lightGo.transform.localPosition = new Vector3(0f, TunnelHeight * 0.82f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = StepLength * 1.4f;
            light.intensity = 0.45f + (float)rng.NextDouble() * 0.25f;
            light.color = new Color(1f, 0.72f + (float)rng.NextDouble() * 0.12f, 0.45f);
            CaveLightingSettings.ApplyCaveLight(light);
        }

        static int BuildNaturalChamber(
            Transform parent,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Transform propsRoot,
            Vector3 localPos,
            Vector3 forward,
            int index)
        {
            var chamber = new GameObject($"Chamber_{index:D2}");
            Undo.RegisterCreatedObjectUndo(chamber, "Chamber");
            chamber.transform.SetParent(parent, false);
            chamber.transform.localPosition = localPos;
            chamber.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);

            var count = 0;
            var t = chamber.transform;
            var floorScale = new Vector3(ChamberSize / FloorModuleSpan, 1f, ChamberSize / FloorModuleSpan);
            var half = ChamberSize * 0.5f;

            var tile = ChamberSize / FloorModuleSpan * 0.52f;
            var tileScale = new Vector3(tile, 1.05f, tile);
            for (var fx = -1; fx <= 1; fx++)
            {
                for (var fz = -1; fz <= 1; fz++)
                {
                    count += PlaceModule(t, catalog.Pick(catalog.Floors, rng),
                        new Vector3(fx * ChamberSize * 0.32f, 0f, fz * ChamberSize * 0.32f),
                        Quaternion.identity, tileScale, "SM_Floor", false) ? 1 : 0;
                }
            }

            if (catalog.Cupolas.Count > 0)
            {
                count += PlaceModule(t, catalog.Pick(catalog.Cupolas, rng),
                    new Vector3(0f, TunnelHeight + 0.6f, 0f), Quaternion.identity,
                    floorScale * 0.95f, "SM_Cupola", false) ? 1 : 0;
            }
            else
            {
                count += PlaceModule(t, catalog.Pick(catalog.Ceilings, rng),
                    new Vector3(0f, TunnelHeight + 0.8f, 0f), Quaternion.identity,
                    floorScale * 1.05f, "SM_Ceiling", false) ? 1 : 0;
            }

            for (var side = 0; side < 8; side++)
            {
                var angle = side * 45f;
                var wallRot = Quaternion.Euler(0f, angle, 0f);
                var offset = wallRot * new Vector3(0f, TunnelHeight * 0.5f, half * 0.92f);
                count += PlaceModule(t, catalog.Pick(catalog.Walls, rng), offset, wallRot,
                    new Vector3(ChamberSize / FloorModuleSpan * 0.65f, 1.28f, 1.05f), "SM_Wall", false) ? 1 : 0;
            }

            for (var s = 0; s < 3; s++)
            {
                if (catalog.Stalactites.Count == 0)
                    break;
                var hang = new Vector3((float)(rng.NextDouble() * 4 - 2), TunnelHeight - 0.1f, (float)(rng.NextDouble() * 4 - 2));
                count += PlaceModule(t, catalog.Pick(catalog.Stalactites, rng), hang,
                    Quaternion.Euler(180f, (float)(rng.NextDouble() * 360), 0f),
                    Vector3.one * (0.7f + (float)rng.NextDouble() * 0.4f), "SM_Stalactite", false) ? 1 : 0;
            }

            var mobGo = new GameObject("MobSpawn");
            Undo.RegisterCreatedObjectUndo(mobGo, "Mob Spawn");
            mobGo.transform.SetParent(t, false);
            mobGo.transform.localPosition = Vector3.zero;
            mobGo.AddComponent<CaveMobSpawner>();

            var chamberLight = new GameObject("ChamberLight");
            Undo.RegisterCreatedObjectUndo(chamberLight, "Chamber Light");
            chamberLight.transform.SetParent(t, false);
            chamberLight.transform.localPosition = new Vector3(0f, TunnelHeight * 0.65f, 0f);
            var light = chamberLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = ChamberSize * 1.1f;
            light.intensity = 0.75f;
            light.color = new Color(0.7f, 0.82f, 1f);
            CaveLightingSettings.ApplyCaveLight(light, isChamber: true);
            chamberLight.AddComponent<CaveLightRangeClamp>().chamberLight = true;

            return count;
        }

        static void PlaceAngledRockFill(
            Transform module,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            float halfW,
            ref int count)
        {
            if (catalog.Rockfalls.Count == 0)
                return;

            var placements = new[]
            {
                (new Vector3(-halfW * 0.75f, TunnelHeight * 0.25f, -0.4f), new Vector3(0f, 35f, 12f)),
                (new Vector3(halfW * 0.75f, TunnelHeight * 0.3f, 0.35f), new Vector3(0f, -28f, -10f)),
                (new Vector3(0f, TunnelHeight * 0.92f, 0f), new Vector3(22f, (float)(rng.NextDouble() * 360), 8f)),
                (new Vector3(0f, 0.15f, 0.5f), new Vector3(-15f, 0f, 0f))
            };

            foreach (var (pos, euler) in placements)
            {
                if (rng.NextDouble() > 0.42)
                    continue;
                count += PlaceModule(module, catalog.Pick(catalog.Rockfalls, rng), pos,
                    Quaternion.Euler(euler), Vector3.one * (0.75f + (float)rng.NextDouble() * 0.35f),
                    "SM_Rockfall_Angle", false) ? 1 : 0;
            }
        }

        static void ScatterSegmentProps(
            Transform propsRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 from,
            Vector3 to,
            int count)
        {
            for (var i = 0; i < count; i++)
            {
                var t = 0.25f + (float)rng.NextDouble() * 0.5f;
                var basePos = Vector3.Lerp(from, to, t);
                var right = Vector3.Cross(Vector3.up, (to - from).normalized);
                var offset = right * (float)(rng.NextDouble() * 2 - 1) * (TunnelWidth * 0.32f);
                PlaceRandomProp(propsRoot, catalog, rng, basePos + offset + Vector3.up * 0.05f);
            }
        }

        static void ScatterChamberProps(
            Transform propsRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 center,
            Vector3 forward,
            int count)
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            for (var i = 0; i < count; i++)
            {
                var offset = right * (float)(rng.NextDouble() * 6 - 3) + forward * (float)(rng.NextDouble() * 6 - 3);
                PlaceRandomProp(propsRoot, catalog, rng, center + offset + Vector3.up * 0.05f, chamberScale: 0.9f + (float)rng.NextDouble() * 0.35f);
            }
        }

        static void PlaceRandomProp(
            Transform propsRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos,
            float chamberScale = 1f)
        {
            var roll = rng.NextDouble();
            GameObject prefab;
            if (roll < 0.45 && catalog.Mushrooms.Count > 0)
                prefab = catalog.Pick(catalog.Mushrooms, rng);
            else if (roll < 0.7 && catalog.Crystals.Count > 0)
                prefab = catalog.Pick(catalog.Crystals, rng);
            else if (catalog.MossProps.Count > 0)
                prefab = catalog.Pick(catalog.MossProps, rng);
            else
                return;

            var scale = Vector3.one * (0.55f + (float)rng.NextDouble() * 0.35f) * chamberScale;
            PlaceModule(propsRoot, prefab, localPos,
                Quaternion.Euler(0f, (float)(rng.NextDouble() * 360), 0f), scale, prefab.name, false);
        }

        static int BuildWaterBranch(
            Transform waterRoot,
            Transform tunnelRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 fromPos,
            Vector3 forward,
            float branchYawDegrees = 90f)
        {
            var branchDir = Quaternion.Euler(0f, branchYawDegrees, 0f) * forward;
            var cursor = fromPos;
            var count = 0;

            for (var i = 0; i < 4; i++)
            {
                var next = cursor + branchDir * StepLength + Vector3.down * 0.25f;
                count += BuildTunnelSegment(tunnelRoot, catalog, rng, tunnelRoot, cursor, next, $"Water_{i}", placeLight: false);
                cursor = next;
            }

            var pool = new GameObject("UndergroundRiver_Pool");
            Undo.RegisterCreatedObjectUndo(pool, "Water Pool");
            pool.transform.SetParent(waterRoot, false);
            pool.transform.localPosition = cursor;
            pool.tag = CaveTags.Water;
            var poolMarker = pool.AddComponent<CaveFeatureMarker>();
            poolMarker.featureKind = CaveFeatureKind.UndergroundWater;
            poolMarker.notes = "Underground water basin — attach Suimono or URP water volume here.";

            var poolTransform = pool.transform;
            var poolScale = new Vector3(8f / FloorModuleSpan, 1f, 10f / FloorModuleSpan);
            PlaceModule(poolTransform, catalog.Pick(catalog.Floors, rng), Vector3.zero, Quaternion.identity,
                poolScale, LavaTubePrefabCatalog.PrefabRoot + "SM_Floor05A.prefab", false);
            PlaceModule(poolTransform, catalog.Pick(catalog.Floors, rng), new Vector3(0f, -0.35f, 2f),
                Quaternion.identity, poolScale * 0.85f, LavaTubePrefabCatalog.PrefabRoot + "SM_Floor05C.prefab", false);

            if (catalog.WaterProps.Count > 0)
            {
                PlaceModule(poolTransform, catalog.Pick(catalog.WaterProps, rng),
                    new Vector3(0f, 0.15f, 1f), Quaternion.identity,
                    Vector3.one * 1.2f, "water_prop", false);
            }

            var box = pool.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(8f, 2f, 10f);
            box.center = new Vector3(0f, 0.5f, 0f);

            count += BuildHiddenWaterfall(waterRoot, catalog, rng, cursor + branchDir * 6f + Vector3.down * 2f);
            return count;
        }

        static int BuildHiddenWaterfall(
            Transform waterRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos)
        {
            var fall = new GameObject("HiddenWaterfall_Chamber");
            Undo.RegisterCreatedObjectUndo(fall, "Hidden Waterfall");
            fall.transform.SetParent(waterRoot, false);
            fall.transform.localPosition = localPos;
            fall.tag = CaveTags.HiddenWaterfall;
            var marker = fall.AddComponent<CaveFeatureMarker>();
            marker.featureKind = CaveFeatureKind.HiddenWaterfall;
            marker.notes = "Secret waterfall chamber behind the water branch.";

            var fallTransform = fall.transform;
            var count = 0;
            count += PlaceModule(fallTransform, catalog.Pick(catalog.Floors, rng), Vector3.zero, Quaternion.identity,
                new Vector3(6f / FloorModuleSpan, 1f, 6f / FloorModuleSpan), "asset_reference: SM_Floor02B", false)
                ? 1 : 0;

            for (var y = 0; y < 3; y++)
            {
                count += PlaceModule(fallTransform, catalog.Pick(catalog.Walls, rng),
                    new Vector3(0f, 1.5f + y * 2.2f, -2.8f), Quaternion.identity,
                    Vector3.one * 1.1f, "asset_reference: SM_Wall05A", false) ? 1 : 0;
            }

            for (var i = 0; i < 4; i++)
            {
                count += PlaceModule(fallTransform, catalog.Pick(catalog.Rockfalls, rng),
                    new Vector3(0.3f * i, 2f + i * 0.8f, -1.5f), Quaternion.Euler(0f, 0f, 8f * i),
                    Vector3.one, "asset_reference: SM_Rockfall03A", true) ? 1 : 0;
            }

            var fallLight = new GameObject("WaterfallLight");
            Undo.RegisterCreatedObjectUndo(fallLight, "Waterfall Light");
            fallLight.transform.SetParent(fallTransform, false);
            fallLight.transform.localPosition = new Vector3(0f, 4f, -1f);
            var light = fallLight.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 16f;
            light.intensity = 1.2f;
            light.color = new Color(0.5f, 0.75f, 1f);
            light.spotAngle = 55f;
            light.shadows = LightShadows.Soft;

            return count;
        }

        static void ScatterArtifacts(Transform parent, LavaTubePrefabCatalog catalog, System.Random rng, List<Vector3> nodes)
        {
            var count = Mathf.Min(6, catalog.Artifacts.Count);
            for (var i = 0; i < count; i++)
            {
                if (nodes.Count == 0)
                    break;
                var node = nodes[rng.Next(nodes.Count)];
                var offset = new Vector3((float)(rng.NextDouble() * 4 - 2), 0.8f, (float)(rng.NextDouble() * 4 - 2));
                PlaceModule(parent, catalog.Pick(catalog.Artifacts, rng), node + offset,
                    Quaternion.Euler(0f, (float)(rng.NextDouble() * 360), 0f),
                    Vector3.one * 0.8f, "asset_reference: SM_ArtifactFragments01", false);
            }
        }

        static void PlaceEntranceMarker(Transform parent, Vector3 forward)
        {
            var go = new GameObject(CaveEntranceTeleport.EntranceMarkerObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Entrance");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(
                1f,
                CaveGroundPlacementUtility.DefaultMarkerLiftAboveShaftMeters,
                0f);
            go.tag = CaveTags.Entrance;
            var m = go.AddComponent<CaveFeatureMarker>();
            m.featureKind = CaveFeatureKind.Entrance;
            m.notes = "Walk-in entrance above ground. Face into cave.";
        }

        static void PlaceMinableRock(Transform parent, LavaTubePrefabCatalog catalog, System.Random rng, Vector3 localPos)
        {
            PlaceModule(parent, catalog.Pick(catalog.Rockfalls, rng), localPos,
                Quaternion.Euler(0f, (float)(rng.NextDouble() * 360), 0f),
                Vector3.one, "asset_reference: SM_Rockfall01A", true);
        }

        static int PlaceMinableCluster(Transform parent, LavaTubePrefabCatalog catalog, System.Random rng, Vector3 center, int count)
        {
            var placed = 0;
            for (var i = 0; i < count; i++)
            {
                var offset = new Vector3(
                    (float)(rng.NextDouble() * 3 - 1.5),
                    0.4f + (float)rng.NextDouble() * 0.8f,
                    (float)(rng.NextDouble() * 3 - 1.5));
                PlaceMinableRock(parent, catalog, rng, center + offset);
                placed++;
            }

            return placed;
        }

        static bool PlaceModule(
            Transform parent,
            GameObject prefab,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 scale,
            string assetReferenceLabel,
            bool minable)
        {
            if (prefab == null)
                return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (instance == null)
                return false;

            Undo.RegisterCreatedObjectUndo(instance, "Cave Piece");

            var assetPath = AssetDatabase.GetAssetPath(prefab);
            var label = string.IsNullOrEmpty(assetPath)
                ? assetReferenceLabel
                : $"asset_reference: {assetPath}";

            var source = instance.GetComponent<CavePrefabSource>();
            if (source == null)
                source = instance.AddComponent<CavePrefabSource>();
            source.SetAssetPath(assetPath);

            instance.transform.localPosition = localPos;
            instance.transform.localRotation = localRot;
            instance.transform.localScale = scale;
            instance.layer = LayerMask.NameToLayer("Default");

            if (minable)
            {
                if (instance.GetComponent<MinableRock>() == null)
                    instance.AddComponent<MinableRock>();
                instance.tag = CaveTags.Minable;
            }

            CaveSceneMaterialRepair.ApplyModuleMaterials(instance, scale);

            return true;
        }

        static void EnsureNavClearance(Transform moduleRoot, float length)
        {
            var col = moduleRoot.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, NavClearance * 0.5f, 0f);
            col.size = new Vector3(length * 0.92f, NavClearance, TunnelWidth * 0.72f);
            col.gameObject.name = moduleRoot.name + "_NavClearance";
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        }
    }
}
