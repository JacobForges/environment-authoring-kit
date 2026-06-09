#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Terraforms a hollow dead-tree stump bowl at the landmark site: raised bleached-bark rim + depressed interior,
    /// with a crown of sharp peaks in an almost-full circle and one entrance gap aligned to the floor plan.
    /// </summary>
    static class HollowTitanExteriorTerraform
    {
        public const string SculptMarkerName = "StumpTerraformDone";

        const float InnerRadiusFraction = 0.58f;
        const float OuterRadiusFraction = 1.04f;
        const float BowlDepthRadiusFraction = 0.14f;
        const float RimRiseRadiusFraction = 0.16f;
        const float OuterBlendFraction = 0.18f;

        const int CrownPeakCount = 15;
        const float CrownRingRadiusFraction = 1.42f;
        const float CrownPeakRadiusFraction = 0.12f;
        const float CrownPeakRiseFraction = 0.12f;
        const float EntranceGapHalfAngleDeg = 36f;
        const float CrownOuterReachFraction = 2.05f;

        public const int StumpSculptPhaseCount = 32;

        public static int SculptFromSceneLandmark(Terrain mainTerrain, bool force = false)
        {
            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            var data = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;
            if (data == null || mainTerrain == null)
                return 0;

            var entranceYaw = data.EntranceYawDegrees;
            if (Mathf.Abs(entranceYaw) < 0.01f)
            {
                var plan = HollowTitanLandmarkFloorPlanner.Build(
                    data.BuildSeed,
                    data.FloorCount,
                    data.TrunkRadius,
                    data.TrunkHeight,
                    data.FloorHeightMeters);
                entranceYaw = plan.EntranceYawDegrees;
                data.SetEntranceYaw(entranceYaw);
            }

            return SculptHollowStumpBowl(
                mainTerrain,
                data.SiteWorldPosition,
                data.TrunkRadius,
                data.TrunkHeight,
                data.BuildSeed,
                entranceYaw,
                root.transform,
                force);
        }

        /// <summary>One of 32 paced stump-sculpt passes (angular sectors + detail layers).</summary>
        public static int SculptStumpPhasePass(Terrain mainTerrain, int phaseIndex, bool force = true) =>
            SculptStumpPhasePass(mainTerrain, phaseIndex, force, onComplete: null);

        /// <summary>Paced path — waits for row-band queue before <paramref name="onComplete"/>.</summary>
        public static void QueueSculptStumpPhasePass(
            Terrain mainTerrain,
            int phaseIndex,
            bool force,
            System.Action onComplete)
        {
            if (onComplete == null)
            {
                SculptStumpPhasePass(mainTerrain, phaseIndex, force, null);
                return;
            }

            if (!CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                SculptStumpPhasePass(mainTerrain, phaseIndex, force, null);
                onComplete();
                return;
            }

            SculptStumpPhasePass(mainTerrain, phaseIndex, force, onComplete);
        }

        static int SculptStumpPhasePass(
            Terrain mainTerrain,
            int phaseIndex,
            bool force,
            System.Action onComplete)
        {
            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            var data = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;
            if (data == null || mainTerrain == null)
            {
                onComplete?.Invoke();
                return 0;
            }

            phaseIndex = Mathf.Clamp(phaseIndex, 0, StumpSculptPhaseCount - 1);
            if (phaseIndex == 0)
            {
                ApplyTitanLevelScaleBoost(data);
                onComplete?.Invoke();
                return 1;
            }

            if (phaseIndex >= StumpSculptPhaseCount - 1)
            {
                HollowTitanLandmarkMeatPhases.ResnapAfterTerrainSculpt(mainTerrain);
                HollowTitanLandmarkMeatPhases.EnsureLandmarkExteriorVisible();
                onComplete?.Invoke();
                return 1;
            }

            var entranceYaw = data.EntranceYawDegrees;
            if (Mathf.Abs(entranceYaw) < 0.01f)
            {
                var plan = HollowTitanLandmarkFloorPlanner.Build(
                    data.BuildSeed,
                    data.FloorCount,
                    data.TrunkRadius,
                    data.TrunkHeight,
                    data.FloorHeightMeters);
                entranceYaw = plan.EntranceYawDegrees;
                data.SetEntranceYaw(entranceYaw);
            }

            if (onComplete != null && CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                QueueSculptHollowStumpBowl(
                    mainTerrain,
                    data.SiteWorldPosition,
                    data.TrunkRadius,
                    data.TrunkHeight,
                    data.BuildSeed + phaseIndex * 997,
                    entranceYaw,
                    root.transform,
                    force,
                    phaseIndex,
                    () =>
                    {
                        if (phaseIndex is >= 24 and <= 27)
                        {
                            var extra = SculptNearTitanFoothillCurve(
                                mainTerrain,
                                data.SiteWorldPosition,
                                data.TrunkRadius,
                                phaseIndex - 24);
                            CaveBuildEditorLog.LogSurface(
                                $"[HollowTitan] Foothill curve pass {phaseIndex - 24} — {extra} cells.",
                                forceUnityConsole: false);
                        }

                        onComplete();
                    });
                return 0;
            }

            var touched = SculptHollowStumpBowl(
                mainTerrain,
                data.SiteWorldPosition,
                data.TrunkRadius,
                data.TrunkHeight,
                data.BuildSeed + phaseIndex * 997,
                entranceYaw,
                root.transform,
                force: force,
                phaseIndex: phaseIndex);

            if (phaseIndex is >= 24 and <= 27)
            {
                touched += SculptNearTitanFoothillCurve(
                    mainTerrain,
                    data.SiteWorldPosition,
                    data.TrunkRadius,
                    phaseIndex - 24);
            }

            return touched;
        }

        static void ApplyTitanLevelScaleBoost(HollowTitanLandmarkBuildData data)
        {
            if (data == null)
                return;

            var concept = HollowTitanBuildLadder.ConceptTrunkRadiusMeters;
            var target = concept * HollowTitanBuildLadder.TitanDisplayScaleMultiplier;
            if (data.TrunkRadius >= target * 0.92f)
                return;

            var boosted = Mathf.Max(data.TrunkRadius, target * 0.95f);
            data.SetTrunkRadius(boosted);
        }

        static int SculptNearTitanFoothillCurve(
            Terrain mainTerrain,
            Vector3 siteWorld,
            float trunkRadius,
            int directionIndex)
        {
            var reach = trunkRadius * 2.8f;
            var touched = 0;
            foreach (var tile in CollectOverlappingTerrains(mainTerrain, siteWorld, reach))
            {
                if (tile == null || !SurfaceTerrainTileExpansion.IsOuterRingTerrainName(tile.name))
                    continue;

                touched += SurfaceOuterRingMountainsAuthor.SculptCurvedApproachNearPoint(
                    tile,
                    mainTerrain,
                    siteWorld,
                    trunkRadius,
                    directionIndex);
            }

            return touched;
        }

        public static int SculptHollowStumpBowl(
            Terrain mainTerrain,
            Vector3 worldCenter,
            float trunkRadius,
            float trunkHeight,
            int seed,
            float entranceYawDegrees,
            Transform landmarkRoot = null,
            bool force = false,
            int phaseIndex = -1)
        {
            if (mainTerrain == null || trunkRadius < 1f)
                return 0;

            if (CaveBuildSurfaceTerrainLock.TryBlockSurfaceMutation("Hollow Titan stump sculpt"))
                return 0;

            if (!force && landmarkRoot != null && HasSculptMarker(landmarkRoot))
                return 0;

            var innerR = trunkRadius * InnerRadiusFraction;
            var outerR = trunkRadius * OuterRadiusFraction;
            var bowlDepthM = trunkRadius * BowlDepthRadiusFraction;
            var rimRiseM = trunkRadius * RimRiseRadiusFraction;
            var groundNorm = SampleCenterGroundNorm(mainTerrain, worldCenter);
            var totalTouched = 0;
            var sculptReach = trunkRadius * CrownOuterReachFraction;

            foreach (var tile in CollectOverlappingTerrains(mainTerrain, worldCenter, sculptReach))
            {
                totalTouched += SculptTile(
                    tile,
                    worldCenter,
                    innerR,
                    outerR,
                    bowlDepthM,
                    rimRiseM,
                    groundNorm,
                    trunkRadius,
                    entranceYawDegrees,
                    seed,
                    phaseIndex);
            }

            if (landmarkRoot != null && totalTouched > 0)
            {
                EnsureSculptMarker(landmarkRoot);
                SetStumpSilhouetteProxyVisible(landmarkRoot, true);
            }

            if (totalTouched > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[HollowTitan] Hollow stump terraform — {totalTouched} height cells " +
                    $"(rim {outerR:F0}m, bowl {innerR:F0}m, {CrownPeakCount} crown peaks, entrance gap {EntranceGapHalfAngleDeg * 2f:F0}°).",
                    forceUnityConsole: false);
            }

            return totalTouched;
        }

        static void QueueSculptHollowStumpBowl(
            Terrain mainTerrain,
            Vector3 worldCenter,
            float trunkRadius,
            float trunkHeight,
            int seed,
            float entranceYawDegrees,
            Transform landmarkRoot,
            bool force,
            int phaseIndex,
            System.Action onComplete)
        {
            if (mainTerrain == null || trunkRadius < 1f)
            {
                onComplete?.Invoke();
                return;
            }

            if (CaveBuildSurfaceTerrainLock.TryBlockSurfaceMutation("Hollow Titan stump sculpt"))
            {
                onComplete?.Invoke();
                return;
            }

            if (!force && landmarkRoot != null && HasSculptMarker(landmarkRoot))
            {
                onComplete?.Invoke();
                return;
            }

            var innerR = trunkRadius * InnerRadiusFraction;
            var outerR = trunkRadius * OuterRadiusFraction;
            var bowlDepthM = trunkRadius * BowlDepthRadiusFraction;
            var rimRiseM = trunkRadius * RimRiseRadiusFraction;
            var groundNorm = SampleCenterGroundNorm(mainTerrain, worldCenter);
            var sculptReach = trunkRadius * CrownOuterReachFraction;
            var tiles = CollectOverlappingTerrains(mainTerrain, worldCenter, sculptReach);

            void SculptTileAt(int tileIndex, System.Action next)
            {
                if (tileIndex >= tiles.Count)
                {
                    if (landmarkRoot != null)
                    {
                        EnsureSculptMarker(landmarkRoot);
                        SetStumpSilhouetteProxyVisible(landmarkRoot, true);
                    }

                    onComplete?.Invoke();
                    return;
                }

                QueueSculptTile(
                    tiles[tileIndex],
                    worldCenter,
                    innerR,
                    outerR,
                    bowlDepthM,
                    rimRiseM,
                    groundNorm,
                    trunkRadius,
                    entranceYawDegrees,
                    seed,
                    phaseIndex,
                    () => SculptTileAt(tileIndex + 1, next));
            }

            SculptTileAt(0, onComplete);
        }

        static void QueueSculptTile(
            Terrain terrain,
            Vector3 center,
            float innerR,
            float outerR,
            float bowlDepthM,
            float rimRiseM,
            float groundNorm,
            float trunkRadius,
            float entranceYawDegrees,
            int seed,
            int phaseIndex,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var size = data.size;
            var origin = terrain.transform.position;
            var scaleY = Mathf.Max(0.01f, terrain.transform.lossyScale.y);
            var bowlDepthNorm = bowlDepthM / (size.y * scaleY);
            var rimRiseNorm = rimRiseM / (size.y * scaleY);
            var crownPeakRiseNorm = (trunkRadius * CrownPeakRiseFraction) / (size.y * scaleY);

            CaveBuildMicroTerrainHeightmap.QueueMutateRowBands(
                terrain,
                "Hollow Titan stump sculpt",
                CaveBuildPipelineDomains.QueueLabel($"Hollow Titan stump sculpt — {terrain.name}"),
                (heights, rowStart, rowRes, bandOrigin, bandSize) =>
                {
                    for (var y = 0; y < heights.GetLength(0); y++)
                    {
                        var gz = rowStart + y;
                        for (var x = 0; x < rowRes; x++)
                        {
                            var target = TargetStumpHeight(
                                bandOrigin.x + x / (float)(rowRes - 1) * bandSize.x,
                                bandOrigin.z + gz / (float)(rowRes - 1) * bandSize.z,
                                center,
                                innerR,
                                outerR,
                                bowlDepthNorm,
                                rimRiseNorm,
                                groundNorm,
                                trunkRadius,
                                crownPeakRiseNorm,
                                entranceYawDegrees,
                                seed,
                                phaseIndex);
                            if (target.HasValue)
                                heights[y, x] = target.Value;
                        }
                    }
                },
                onComplete);
        }

        public static bool ShouldDeferUntilPostTerraform() =>
            CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive &&
            !SurfaceTerrainTileExpansion.IsTerrainReadyForHollowTitan;

        static bool HasSculptMarker(Transform root) =>
            root != null && root.Find($"Exterior/TrunkShell/{SculptMarkerName}") != null;

        static void EnsureSculptMarker(Transform root)
        {
            if (root == null)
                return;

            var shell = GetOrCreateChild(root, "Exterior/TrunkShell");
            if (shell.Find(SculptMarkerName) != null)
                return;

            var marker = new GameObject(SculptMarkerName);
            marker.transform.SetParent(shell, false);
            marker.hideFlags = HideFlags.HideInHierarchy;
        }

        /// <summary>Proxy mesh stump — hidden once terrain bowl sculpt succeeds.</summary>
        public static void SetStumpSilhouetteProxyVisible(Transform root, bool visible)
        {
            var sil = root != null ? root.Find("Exterior/StumpSilhouette") : null;
            if (sil == null)
                return;

            sil.gameObject.SetActive(visible);
        }

        static List<Terrain> CollectOverlappingTerrains(Terrain mainTerrain, Vector3 center, float radiusMeters)
        {
            var result = new List<Terrain>(4);
            var minX = center.x - radiusMeters;
            var maxX = center.x + radiusMeters;
            var minZ = center.z - radiusMeters;
            var maxZ = center.z + radiusMeters;

            foreach (var tile in SurfaceTerrainPlayRegion.CollectAllKitGroundTerrains(mainTerrain))
            {
                if (tile?.terrainData == null)
                    continue;

                var origin = tile.transform.position;
                var size = tile.terrainData.size;
                if (origin.x + size.x < minX || origin.x > maxX ||
                    origin.z + size.z < minZ || origin.z > maxZ)
                    continue;

                result.Add(tile);
            }

            if (result.Count == 0 && mainTerrain?.terrainData != null)
                result.Add(mainTerrain);

            return result;
        }

        static float SampleCenterGroundNorm(Terrain mainTerrain, Vector3 worldCenter)
        {
            if (!HollowTitanLandmarkTerrainSnap.TrySampleGroundY(
                    mainTerrain,
                    worldCenter.x,
                    worldCenter.z,
                    out var groundY,
                    out var tile) ||
                tile?.terrainData == null)
                return 0.42f;

            var sizeY = tile.terrainData.size.y * Mathf.Max(0.01f, tile.transform.lossyScale.y);
            return Mathf.Clamp01((groundY - tile.transform.position.y) / sizeY);
        }

        static int SculptTile(
            Terrain terrain,
            Vector3 center,
            float innerR,
            float outerR,
            float bowlDepthM,
            float rimRiseM,
            float groundNorm,
            float trunkRadius,
            float entranceYawDegrees,
            int seed,
            int phaseIndex = -1)
        {
            if (terrain?.terrainData == null)
                return 0;

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var size = data.size;
            var origin = terrain.transform.position;
            var scaleY = Mathf.Max(0.01f, terrain.transform.lossyScale.y);
            var bowlDepthNorm = bowlDepthM / (size.y * scaleY);
            var rimRiseNorm = rimRiseM / (size.y * scaleY);
            var crownPeakRiseNorm = (trunkRadius * CrownPeakRiseFraction) / (size.y * scaleY);
            var touched = 0;

            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                CaveBuildMicroTerrainHeightmap.QueueMutateRowBands(
                    terrain,
                    "Hollow Titan stump",
                    CaveBuildPipelineDomains.QueueLabel("Hollow Titan stump sculpt"),
                    (heights, rowStart, rowRes, bandOrigin, bandSize) =>
                    {
                        for (var y = 0; y < heights.GetLength(0); y++)
                        {
                            var gz = rowStart + y;
                            for (var x = 0; x < rowRes; x++)
                            {
                                var before = heights[y, x];
                                var target = TargetStumpHeight(
                                    bandOrigin.x + x / (float)(rowRes - 1) * bandSize.x,
                                    bandOrigin.z + gz / (float)(rowRes - 1) * bandSize.z,
                                    center,
                                    innerR,
                                    outerR,
                                    bowlDepthNorm,
                                    rimRiseNorm,
                                    groundNorm,
                                    trunkRadius,
                                    crownPeakRiseNorm,
                                    entranceYawDegrees,
                                    seed,
                                    phaseIndex);
                                if (!target.HasValue)
                                    continue;

                                heights[y, x] = target.Value;
                                if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                                    touched++;
                            }
                        }
                    },
                    () => { });
                return touched;
            }

            CaveEditorUndo.RecordObject(data, "Hollow Titan stump sculpt");
            var heights = data.GetHeights(0, 0, res, res);
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var before = heights[z, x];
                    var target = TargetStumpHeight(
                        origin.x + x / (float)(res - 1) * size.x,
                        origin.z + z / (float)(res - 1) * size.z,
                        center,
                        innerR,
                        outerR,
                        bowlDepthNorm,
                        rimRiseNorm,
                        groundNorm,
                        trunkRadius,
                        crownPeakRiseNorm,
                        entranceYawDegrees,
                        seed,
                        phaseIndex);
                    if (!target.HasValue)
                        continue;

                    heights[z, x] = target.Value;
                    if (Mathf.Abs(heights[z, x] - before) > 0.00005f)
                        touched++;
                }
            }

            if (touched > 0)
            {
                data.SetHeights(0, 0, heights);
                terrain.Flush();
            }

            return touched;
        }

        static float? TargetStumpHeight(
            float worldX,
            float worldZ,
            Vector3 center,
            float innerR,
            float outerR,
            float bowlDepthNorm,
            float rimRiseNorm,
            float groundNorm,
            float trunkRadius,
            float crownPeakRiseNorm,
            float entranceYawDegrees,
            int seed,
            int phaseIndex = -1)
        {
            var dx = worldX - center.x;
            var dz = worldZ - center.z;
            var dist = Mathf.Sqrt(dx * dx + dz * dz);
            var worldAngleDeg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            if (phaseIndex >= 0 && !PhaseOwnsCell(phaseIndex, worldAngleDeg, dist, innerR, outerR, trunkRadius))
                return null;

            var inEntranceGap = IsAngleInGap(worldAngleDeg, entranceYawDegrees, EntranceGapHalfAngleDeg);
            var crownOuter = trunkRadius * CrownOuterReachFraction;
            if (dist > crownOuter)
                return null;

            var wobble = 1f + TerrainWobble(seed, worldX, worldZ) * 0.05f;
            float height;

            if (dist < innerR)
            {
                var t = 1f - dist / Mathf.Max(0.01f, innerR);
                var curved = t * t * (3f - 2f * t);
                height = groundNorm - bowlDepthNorm * curved * wobble;
            }
            else if (dist <= outerR)
            {
                var ringT = (dist - innerR) / Mathf.Max(0.01f, outerR - innerR);
                var profile = Mathf.Sin(ringT * Mathf.PI);
                profile = profile * profile * (3f - 2f * profile);
                var rimScale = inEntranceGap ? EntranceRimScale(dist, innerR, outerR) : 1f;
                height = groundNorm + rimRiseNorm * profile * wobble * rimScale;
            }
            else
            {
                var outerBlend = outerR * OuterBlendFraction;
                var blendT = (dist - outerR) / Mathf.Max(0.01f, outerBlend);
                var smoothBlend = blendT * blendT * (3f - 2f * blendT);
                height = Mathf.Lerp(groundNorm + rimRiseNorm * 0.35f * wobble, groundNorm, smoothBlend);
            }

            if (inEntranceGap && dist > innerR)
                height = Mathf.Min(height, groundNorm + rimRiseNorm * 0.08f);

            if (!inEntranceGap && dist > innerR * 0.92f)
            {
                var peakAdd = SharpCrownPeakContribution(
                    worldX,
                    worldZ,
                    center,
                    trunkRadius,
                    crownPeakRiseNorm,
                    entranceYawDegrees,
                    seed);
                height += peakAdd;
            }

            return Mathf.Clamp01(height);
        }

        /// <summary>Suppress rim at entrance so the approach reads as a clear gap in the crown.</summary>
        static float EntranceRimScale(float dist, float innerR, float outerR)
        {
            var mid = (innerR + outerR) * 0.5f;
            var outward = Mathf.InverseLerp(innerR, outerR, dist);
            var centerCut = 1f - Mathf.Exp(-Mathf.Pow((dist - mid) / Mathf.Max(1f, outerR - innerR) * 3.5f, 2f));
            return Mathf.Lerp(0.12f, 0.55f, outward) * centerCut + 0.08f;
        }

        static bool IsAngleInGap(float worldAngleDeg, float entranceYawDeg, float halfAngleDeg) =>
            Mathf.Abs(Mathf.DeltaAngle(worldAngleDeg, entranceYawDeg)) <= halfAngleDeg;

        static float SharpCrownPeakContribution(
            float worldX,
            float worldZ,
            Vector3 center,
            float trunkRadius,
            float crownPeakRiseNorm,
            float entranceYawDegrees,
            int seed)
        {
            var ringRadius = trunkRadius * CrownRingRadiusFraction;
            var peakRadius = trunkRadius * CrownPeakRadiusFraction;
            var peakRadiusSq = peakRadius * peakRadius;
            var best = 0f;
            var step = 360f / CrownPeakCount;

            for (var i = 0; i < CrownPeakCount; i++)
            {
                var peakAngle = i * step;
                if (IsAngleInGap(peakAngle, entranceYawDegrees, EntranceGapHalfAngleDeg))
                    continue;

                var rad = peakAngle * Mathf.Deg2Rad;
                var peakX = center.x + Mathf.Sin(rad) * ringRadius;
                var peakZ = center.z + Mathf.Cos(rad) * ringRadius;
                var pdx = worldX - peakX;
                var pdz = worldZ - peakZ;
                var d2 = pdx * pdx + pdz * pdz;
                if (d2 > peakRadiusSq)
                    continue;

                var t = 1f - d2 / peakRadiusSq;
                var sharp = t * t * t * (t * (t * 6f - 15f) + 10f);
                var jitter = 0.9f + Hash01(seed + i * 131) * 0.22f;
                best = Mathf.Max(best, sharp * jitter);
            }

            return best * crownPeakRiseNorm;
        }

        static float Hash01(int seed)
        {
            var u = (seed & 0x7fffffff) / (float)int.MaxValue;
            return Mathf.Clamp01(u);
        }

        static bool PhaseOwnsCell(
            int phaseIndex,
            float worldAngleDeg,
            float dist,
            float innerR,
            float outerR,
            float trunkRadius)
        {
            if (phaseIndex < 1 || phaseIndex >= StumpSculptPhaseCount - 1)
                return true;

            var angle = Mathf.Repeat(worldAngleDeg + 360f, 360f);
            var sector = Mathf.FloorToInt(angle / 90f) % 4;

            if (phaseIndex is >= 1 and <= 4)
                return dist <= outerR && sector == phaseIndex - 1;

            if (phaseIndex is >= 5 and <= 8)
                return dist >= innerR * 0.82f && dist <= outerR * 1.05f && sector == phaseIndex - 5;

            if (phaseIndex is >= 9 and <= 12)
            {
                var cluster = Mathf.FloorToInt(angle / 90f) % 4;
                return dist > innerR * 0.9f && cluster == phaseIndex - 9;
            }

            if (phaseIndex == 13)
                return dist > innerR;

            if (phaseIndex is >= 14 and <= 15)
                return dist > outerR * 0.88f && (phaseIndex == 14 ? sector < 2 : sector >= 2);

            if (phaseIndex is >= 16 and <= 19)
                return dist > innerR * 0.75f && sector == phaseIndex - 16;

            if (phaseIndex is >= 20 and <= 23)
                return dist <= trunkRadius * 1.15f && sector == phaseIndex - 20;

            if (phaseIndex is >= 28 and <= 29)
                return dist > outerR * 0.65f;

            return true;
        }

        static float TerrainWobble(int seed, float worldX, float worldZ)
        {
            var n = Mathf.PerlinNoise(
                worldX * 0.018f + seed * 0.0013f,
                worldZ * 0.018f + seed * 0.0021f);
            return n * 2f - 1f;
        }

        static Transform GetOrCreateChild(Transform parent, string path)
        {
            var parts = path.Split('/');
            var current = parent;
            foreach (var part in parts)
            {
                var child = current.Find(part);
                if (child == null)
                {
                    var go = new GameObject(part);
                    go.transform.SetParent(current, false);
                    child = go.transform;
                }

                current = child;
            }

            return current;
        }
    }
}
#endif
