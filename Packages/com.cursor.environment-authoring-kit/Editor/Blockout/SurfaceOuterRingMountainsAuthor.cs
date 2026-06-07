#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.World;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// 7×7 FullWorld: 16 foothill tiles (Chebyshev 2) merge to play; 24 peak tiles (Chebyshev 3) weave into foothills.
    /// </summary>
    public static class SurfaceOuterRingMountainsAuthor
    {
        public enum OuterRingSculptTier
        {
            Foothill,
            Peak,
            /// <summary>Chebyshev 4 — softer distant massifs framing the 7×7 play+mountain core.</summary>
            Horizon,
        }

        public const float HorizonPeakRiseScale = 0.82f;

        public const float OuterBandMeters = 96f;
        public const float WildernessFoothillBlendMeters = 56f;
        /// <summary>No mountain mass below this distance from the nine-tile AABB — prevents 80m+ audit seams.</summary>
        public const float WildernessInnerLockMeters = 10f;
        /// <summary>Peak mass begins ramping this far outside the foothill AABB (no 10m dead flat band).</summary>
        public const float PeakRiseInnerMeters = 10f;
        public const float WildernessRiseSpanMeters = 72f;
        public const float PerimeterTransitionDepthMeters = 22f;
        public const float PerimeterHeightSoftening = 0.38f;
        public const float CornerPeakRiseMeters = 96f;
        public const float EdgePeakRiseMeters = 72f;
        public const float OuterBandRiseMeters = 46f;
        public const float CornerPeakRadiusMeters = 128f;
        public const float EdgePeakRadiusMeters = 156f;
        public const float RidgeNoiseAmplitudeMeters = 22f;
        public const float FoothillRidgeScale = 1.72f;
        public const float DetailNoiseAmplitudeMeters = 1.8f;
        public const float PeakCliffAccentMeters = 14f;
        const float SeamPreserveMeters = 6f;

        /// <summary>Smoother than SmoothStep — used for foothill/peak mass (never seam band).</summary>
        static float CurvedMassFalloff(float t01) =>
            Mathf.Clamp01(t01) * Mathf.Clamp01(t01) * (3f - 2f * Mathf.Clamp01(t01));

        /// <summary>Minimum normalized lift at play-disk edge so foothills rise outward (never dip into a bowl).</summary>
        const float FoothillPlayEdgeRiseNorm = 0.014f;
        /// <summary>Outer foothill edge dips toward peak ring (bowl back) before peaks rise again.</summary>
        public const float FoothillOuterBowlDipMeters = 26f;
        /// <summary>World meters from peak-facing foothill rim inward where the back bowl fades.</summary>
        public const float FoothillPeakBackBowlBandMeters = 104f;
        /// <summary>Legacy norm constants — prefer world-meter targets above.</summary>
        public const float PeakHeightNorm = 0.085f;

        public static bool TryApply(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            out string message)
        {
            message = string.Empty;
            if (mainTerrain?.terrainData == null || request == null || !request.SurfaceIncludeMountains)
                return false;

            if (!request.UseOuterRingMountains ||
                !SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                    request,
                    request.SurfaceScope == SurfaceBuildScope.FullWorld))
                return false;

            if (request.SurfaceScope == SurfaceBuildScope.FullWorld)
            {
                message =
                    "Nine-tile perimeter mountain sculpt skipped — foothill/peak/labyrinth wilderness tiles own outer relief.";
                return false;
            }

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (terrains.Count < 9)
            {
                message = $"Outer ring mountains skipped — {terrains.Count}/9 tiles present.";
                return false;
            }

            ComputeWorldBounds(terrains, out var minX, out var maxX, out var minZ, out var maxZ);
            var peakPositions = BuildPeakWorldPositions(minX, maxX, minZ, maxZ);
            var raised = 0;

            foreach (var terrain in terrains)
            {
                if (terrain?.terrainData == null)
                    continue;
                if (!TileIntersectsOuterBand(terrain, minX, maxX, minZ, maxZ, OuterBandMeters))
                    continue;

                raised += SculptMountainsOnTerrain(terrain, minX, maxX, minZ, maxZ, peakPositions);
                terrain.Flush();
            }

            message =
                $"Outer ring mountains — {peakPositions.Count} peaks + ridged band on 9-tile perimeter ({OuterBandMeters:F0}m).";
            Debug.Log("[CaveBuild] " + message);
            return raised > 0;
        }

        public static void QueueApplyFoothillRing(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            System.Action onComplete) =>
            QueueApplyRingTier(mainTerrain, request, OuterRingSculptTier.Foothill, onComplete);

        public static void QueueApplyPeakRing(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            System.Action onComplete) =>
            QueueApplyRingTier(mainTerrain, request, OuterRingSculptTier.Peak, onComplete);

        /// <summary>
        /// Sculpt → seam to play/neighbors → lock inner band for one outer-ring tile (paced).
        /// Used during wilderness spawn so each tile is finished before the next starts.
        /// </summary>
        public static void QueueCompleteSingleWildernessTile(
            Terrain mainTerrain,
            Terrain tile,
            WorldGenerationRequest request,
            System.Action onComplete,
            bool deferSeams = false)
        {
            if (mainTerrain?.terrainData == null || tile?.terrainData == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!request.SurfaceIncludeMountains ||
                !request.UseOuterRingMountains ||
                !SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                    request,
                    request.SurfaceScope == SurfaceBuildScope.FullWorld))
            {
                onComplete?.Invoke();
                return;
            }

            var tier = ResolveSculptTier(tile);
            if (!TryResolveNineTileWorldBounds(mainTerrain, out var nineMinX, out var nineMaxX, out var nineMinZ, out var nineMaxZ))
            {
                onComplete?.Invoke();
                return;
            }

            SurfaceTerrainTileExpansion.ComputeFoothillRingWorldBounds(
                mainTerrain,
                out var foothillMinX,
                out var foothillMaxX,
                out var foothillMinZ,
                out var foothillMaxZ);
            ComputeWorldBounds(new[] { tile }, out var wildMinX, out var wildMaxX, out var wildMinZ, out var wildMaxZ);
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> globalPeaks =
                tier == OuterRingSculptTier.Peak || tier == OuterRingSculptTier.Horizon
                    ? BuildPeakWorldPositions(wildMinX, wildMaxX, wildMinZ, wildMaxZ)
                    : Array.Empty<(Vector3 world, float radius, float riseMeters, int seed)>();
            var peakPositions = BuildPeaksForWildernessTile(tile, tier, globalPeaks);

            var subLabel = tier switch
            {
                OuterRingSculptTier.Foothill => deferSeams ? "foothill sculpt" : "foothill merge",
                OuterRingSculptTier.Horizon => deferSeams ? "horizon sculpt" : "horizon massif",
                _ => deferSeams ? "peak sculpt" : "peak mountains",
            };
            CaveBuildRunStatusPublisher.PulseSubOperation(
                subLabel,
                deferSeams ? $"sculpt {tile.name}" : $"sculpt+seam {tile.name}");
            QueueSculptWildernessOnTerrain(
                mainTerrain,
                tile,
                tier,
                nineMinX,
                nineMaxX,
                nineMinZ,
                nineMaxZ,
                foothillMinX,
                foothillMaxX,
                foothillMinZ,
                foothillMaxZ,
                wildMinX,
                wildMaxX,
                wildMinZ,
                wildMaxZ,
                peakPositions,
                _ =>
                {
                    var oneTile = new[] { tile };

                    void AfterDenoise()
                    {
                        if (deferSeams)
                        {
                            onComplete?.Invoke();
                            return;
                        }

                        SurfaceTerrainTileExpansion.QueueStitchSingleMountainWildernessTile(
                            mainTerrain,
                            tile,
                            () =>
                                SurfaceTerrainTileExpansion.QueueLockSingleMountainWildernessInnerEdge(
                                    mainTerrain,
                                    tile,
                                    onComplete));
                    }

                    if (tier == OuterRingSculptTier.Foothill)
                    {
                        AfterDenoise();
                        return;
                    }

                    if (deferSeams &&
                        SurfaceTerrainTileExpansion.IsLiveFullWorldTerraformPhase)
                    {
                        SurfaceTerrainTileExpansion.PulseLiveTerraformMicro("denoise", 3, 4, tile);
                        CaveBuildActionPacing.ScheduleLight(
                            () => QueueDenoiseWildernessTiles(mainTerrain, oneTile, AfterDenoise),
                            CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — micro denoise"));
                        return;
                    }

                    QueueDenoiseWildernessTiles(mainTerrain, oneTile, AfterDenoise);
                });
        }

        /// <summary>
        /// Legacy 9-tile foothill+peak chain when wilderness tiles are absent.
        /// FullWorld 81-tile builds use <see cref="SurfaceMountainResearchPipeline"/> per-tile sculpt instead.
        /// </summary>
        public static void QueueApply(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            System.Action onComplete)
        {
            QueueApplyFoothillRing(
                mainTerrain,
                request,
                () =>
                {
                    SurfaceTerrainTileExpansion.QueueLockFoothillInnerEdgesToPlayDisk(
                        mainTerrain,
                        () =>
                        {
                            QueueApplyPeakRing(
                                mainTerrain,
                                request,
                                () =>
                                {
                                    SurfaceTerrainTileExpansion.QueueLockPeakInnerEdgesToFoothillRing(
                                        mainTerrain,
                                        () => SurfaceTerrainTileExpansion.QueueStitchWildernessRing(
                                            mainTerrain,
                                            onComplete));
                                });
                        });
                });
        }

        static void QueueApplyRingTier(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            OuterRingSculptTier tier,
            System.Action onComplete)
        {
            if (mainTerrain?.terrainData == null || request == null || !request.SurfaceIncludeMountains ||
                !request.UseOuterRingMountains ||
                !SurfaceTerrainTileExpansion.UsesFixedNineTileSquare(
                    request,
                    request.SurfaceScope == SurfaceBuildScope.FullWorld))
            {
                onComplete?.Invoke();
                return;
            }

            var nineTiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (nineTiles.Count < 9)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] {tier} sculpt skipped — {nineTiles.Count}/9 play tiles present.",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var ringTiles = tier == OuterRingSculptTier.Foothill
                ? SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain)
                : SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain);
            if (ringTiles.Length == 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] {tier} sculpt skipped — no tiles in ring.",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            ComputeNineTileWorldBounds(nineTiles, out var nineMinX, out var nineMaxX, out var nineMinZ, out var nineMaxZ);
            SurfaceTerrainTileExpansion.ComputeFoothillRingWorldBounds(
                mainTerrain,
                out var foothillMinX,
                out var foothillMaxX,
                out var foothillMinZ,
                out var foothillMaxZ);
            ComputeWorldBounds(ringTiles, out var wildMinX, out var wildMaxX, out var wildMinZ, out var wildMaxZ);
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peakPositions =
                tier == OuterRingSculptTier.Peak
                    ? BuildPeakWorldPositions(wildMinX, wildMaxX, wildMinZ, wildMaxZ)
                    : Array.Empty<(Vector3 world, float radius, float riseMeters, int seed)>();

            var label = tier == OuterRingSculptTier.Foothill
                ? $"Foothill merge to play — {ringTiles.Length} tile(s), frame-paced steps"
                : $"Peak weave into foothills — {ringTiles.Length} tile(s), frame-paced steps";
            CaveBuildEditorLog.LogSurface("[Surface] " + label + ".", forceUnityConsole: true);

            QueueSculptWildernessTileAtIndex(
                mainTerrain,
                ringTiles,
                tier,
                0,
                nineMinX,
                nineMaxX,
                nineMinZ,
                nineMaxZ,
                foothillMinX,
                foothillMaxX,
                foothillMinZ,
                foothillMaxZ,
                wildMinX,
                wildMaxX,
                wildMinZ,
                wildMaxZ,
                peakPositions,
                onComplete);
        }

        static void QueueSculptWildernessTileAtIndex(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> tiles,
            OuterRingSculptTier tier,
            int index,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            float foothillMinX,
            float foothillMaxX,
            float foothillMinZ,
            float foothillMaxZ,
            float wildMinX,
            float wildMaxX,
            float wildMinZ,
            float wildMaxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks,
            System.Action onComplete)
        {
            if (index >= tiles.Count)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] {tier} ring sculpt complete — {tiles.Count} tile(s).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var terrain = tiles[index];
            var tilePeaks = BuildPeaksForWildernessTile(terrain, tier, peaks);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                tier == OuterRingSculptTier.Foothill ? "foothill merge" : "peak mountains",
                $"sculpt {index + 1}/{tiles.Count} ({terrain.name})");
            QueueSculptWildernessOnTerrain(
                mainTerrain,
                terrain,
                tier,
                nineMinX,
                nineMaxX,
                nineMinZ,
                nineMaxZ,
                foothillMinX,
                foothillMaxX,
                foothillMinZ,
                foothillMaxZ,
                wildMinX,
                wildMaxX,
                wildMinZ,
                wildMaxZ,
                tilePeaks,
                _ =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueSculptWildernessTileAtIndex(
                            mainTerrain,
                            tiles,
                            tier,
                            index + 1,
                            nineMinX,
                            nineMaxX,
                            nineMinZ,
                            nineMaxZ,
                            foothillMinX,
                            foothillMaxX,
                            foothillMinZ,
                            foothillMaxZ,
                            wildMinX,
                            wildMaxX,
                            wildMinZ,
                            wildMaxZ,
                            tilePeaks,
                            onComplete),
                        CaveBuildPipelineDomains.QueueLabel($"{tier} sculpt — next tile"));
                });
        }

        static List<(Vector3 world, float radius, float riseMeters, int seed)> BuildPeaksForWildernessTile(
            Terrain terrain,
            OuterRingSculptTier tier,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> globalPeaks)
        {
            var list = new List<(Vector3, float, float, int)>(8);
            if (terrain?.terrainData == null)
                return list;

            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            var tileCenter = origin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var tileRadius = Mathf.Max(size.x, size.z) * 0.62f;

            if (globalPeaks != null)
            {
                for (var i = 0; i < globalPeaks.Count; i++)
                {
                    var p = globalPeaks[i];
                    var d = Vector2.Distance(
                        new Vector2(p.world.x, p.world.z),
                        new Vector2(tileCenter.x, tileCenter.z));
                    if (d <= p.radius + tileRadius)
                        list.Add(p);
                }
            }

            if (tier != OuterRingSculptTier.Peak && tier != OuterRingSculptTier.Horizon)
                return list;

            var tileSeed = terrain.name.GetHashCode() ^ 9137;
            var variety = 0.68f + Hash01(tileSeed) * 0.62f;
            var rise = (tier == OuterRingSculptTier.Horizon ? EdgePeakRiseMeters * 0.55f : EdgePeakRiseMeters) * variety;
            var radius = CornerPeakRadiusMeters * (0.42f + Hash01(tileSeed + 17) * 0.22f);
            list.Add((tileCenter, radius, rise, tileSeed));
            AppendHollowTitanCrownPeaks(terrain, tier, list);
            return list;
        }

        /// <summary>Sharp radial peaks around the Hollow Titan site — full circle minus entrance gap.</summary>
        static void AppendHollowTitanCrownPeaks(
            Terrain terrain,
            OuterRingSculptTier tier,
            List<(Vector3 world, float radius, float riseMeters, int seed)> list)
        {
            if (tier != OuterRingSculptTier.Peak && tier != OuterRingSculptTier.Horizon)
                return;

            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            var data = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;
            if (data == null || terrain?.terrainData == null)
                return;

            if (!HollowTitanLandmarkCleanup.TerrainContainsWorldXZ(terrain, data.SiteWorldPosition))
                return;

            const int peakCount = 13;
            const float gapHalfAngle = 36f;
            const float ringRadiusFraction = 1.34f;
            const float peakRadiusFraction = 0.11f;
            const float peakRiseFraction = 0.14f;

            var center = data.SiteWorldPosition;
            var trunkR = data.TrunkRadius;
            var ringRadius = trunkR * ringRadiusFraction;
            var peakRadius = trunkR * peakRadiusFraction;
            var riseMeters = trunkR * peakRiseFraction;
            var entranceYaw = data.EntranceYawDegrees;
            var step = 360f / peakCount;

            for (var i = 0; i < peakCount; i++)
            {
                var peakAngle = i * step;
                if (Mathf.Abs(Mathf.DeltaAngle(peakAngle, entranceYaw)) <= gapHalfAngle)
                    continue;

                var rad = peakAngle * Mathf.Deg2Rad;
                var world = new Vector3(
                    center.x + Mathf.Sin(rad) * ringRadius,
                    center.y,
                    center.z + Mathf.Cos(rad) * ringRadius);
                list.Add((world, peakRadius, riseMeters * (0.88f + Hash01(data.BuildSeed + i * 47) * 0.28f), data.BuildSeed + i));
            }
        }

        static float Hash01(int seed)
        {
            var u = (seed & 0x7fffffff) / (float)int.MaxValue;
            return Mathf.Clamp01(u);
        }

        /// <summary>Paced denoise on a subset of wilderness tiles (between sculpt / labyrinth phases).</summary>
        public static void QueueDenoiseWildernessTiles(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> tiles,
            System.Action onComplete)
        {
            if (mainTerrain?.terrainData == null || tiles == null || tiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            ComputeNineTileWorldBounds(nine, out var nineMinX, out var nineMaxX, out var nineMinZ, out var nineMaxZ);
            QueueDenoiseWildernessTileAtIndex(mainTerrain, tiles, 0, nineMinX, nineMaxX, nineMinZ, nineMaxZ, onComplete);
        }

        static void QueueDenoiseWildernessTileAtIndex(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> tiles,
            int index,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            System.Action onComplete)
        {
            if (index >= tiles.Count)
            {
                onComplete?.Invoke();
                return;
            }

            var terrain = tiles[index];
            QueueDenoiseOnTerrain(
                terrain,
                nineMinX,
                nineMaxX,
                nineMinZ,
                nineMaxZ,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueDenoiseWildernessTileAtIndex(
                            mainTerrain,
                            tiles,
                            index + 1,
                            nineMinX,
                            nineMaxX,
                            nineMinZ,
                            nineMaxZ,
                            onComplete),
                        CaveBuildPipelineDomains.QueueLabel("wilderness denoise — next tile"));
                });
        }

        static void QueuePerimeterBlendOnNineTiles(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> nineTiles,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            int index,
            System.Action onComplete)
        {
            if (index >= nineTiles.Count)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Nine-tile perimeter edge soften complete (exposed sides only).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var terrain = nineTiles[index];
            if (!TryResolveNineTileGridOffset(mainTerrain, terrain, out var gridOff) ||
                !IsExposedNineTilePerimeter(gridOff))
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => QueuePerimeterBlendOnNineTiles(
                        mainTerrain, nineTiles, nineMinX, nineMaxX, nineMinZ, nineMaxZ, index + 1, onComplete),
                    CaveBuildPipelineDomains.QueueLabel("nine edge soften — skip interior"));
                return;
            }

            CaveBuildRunStatusPublisher.PulseSubOperation(
                "nine edge soften",
                $"tile {index + 1}/{nineTiles.Count} ({terrain.name})");
            QueuePerimeterBlendOnTerrain(
                terrain,
                gridOff,
                nineMinX,
                nineMaxX,
                nineMinZ,
                nineMaxZ,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueuePerimeterBlendOnNineTiles(
                            mainTerrain,
                            nineTiles,
                            nineMinX,
                            nineMaxX,
                            nineMinZ,
                            nineMaxZ,
                            index + 1,
                            onComplete),
                        CaveBuildPipelineDomains.QueueLabel("nine edge soften — next tile"));
                });
        }

        static bool IsExposedNineTilePerimeter(Vector2Int gridOff) =>
            Mathf.Max(Mathf.Abs(gridOff.x), Mathf.Abs(gridOff.y)) >= 1;

        static bool TryResolveNineTileGridOffset(Terrain mainTerrain, Terrain terrain, out Vector2Int gridOff)
        {
            gridOff = default;
            if (terrain == null)
                return false;

            if (terrain == mainTerrain)
            {
                gridOff = Vector2Int.zero;
                return true;
            }

            return SurfaceTerrainTileExpansion.TryParseTileOffset(terrain.name, out gridOff);
        }

        public static void ComputeNineTileWorldBounds(
            IReadOnlyList<Terrain> nineTiles,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ) =>
            ComputeWorldBounds(nineTiles, out minX, out maxX, out minZ, out maxZ);

        /// <summary>Play-disk AABB for foothill sculpt — falls back to main terrain 3×3 estimate when &lt;9 tiles exist yet.</summary>
        public static bool TryResolveNineTileWorldBounds(
            Terrain mainTerrain,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            if (mainTerrain?.terrainData == null)
                return false;

            var nineTiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (nineTiles.Count >= 9)
            {
                ComputeNineTileWorldBounds(nineTiles, out minX, out maxX, out minZ, out maxZ);
                return true;
            }

            var origin = mainTerrain.transform.position;
            var size = mainTerrain.terrainData.size;
            minX = origin.x - size.x;
            maxX = origin.x + size.x * 2f;
            minZ = origin.z - size.z;
            maxZ = origin.z + size.z * 2f;
            return true;
        }

        static List<(Vector3 world, float radius, float riseMeters, int seed)> BuildPeakWorldPositions(
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            var midX = (minX + maxX) * 0.5f;
            var midZ = (minZ + maxZ) * 0.5f;
            var qNorth = new Vector3(midX, 0f, minZ);
            var qEast = new Vector3(maxX, 0f, midZ);
            var qSouth = new Vector3(midX, 0f, maxZ);
            var qWest = new Vector3(minX, 0f, midZ);

            var spanX = maxX - minX;
            var spanZ = maxZ - minZ;
            return new List<(Vector3, float, float, int)>(16)
            {
                (new Vector3(minX, 0f, minZ), CornerPeakRadiusMeters, CornerPeakRiseMeters, 101),
                (new Vector3(maxX, 0f, minZ), CornerPeakRadiusMeters, CornerPeakRiseMeters * 0.96f, 203),
                (new Vector3(maxX, 0f, maxZ), CornerPeakRadiusMeters, CornerPeakRiseMeters * 1.04f, 307),
                (new Vector3(minX, 0f, maxZ), CornerPeakRadiusMeters, CornerPeakRiseMeters * 0.98f, 409),
                (qNorth, EdgePeakRadiusMeters, EdgePeakRiseMeters, 511),
                (qEast, EdgePeakRadiusMeters, EdgePeakRiseMeters * 0.94f, 613),
                (qSouth, EdgePeakRadiusMeters, EdgePeakRiseMeters * 1.02f, 719),
                (qWest, EdgePeakRadiusMeters, EdgePeakRiseMeters * 0.97f, 821),
                (new Vector3(minX + spanX * 0.25f, 0f, minZ), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.78f, 907),
                (new Vector3(minX + spanX * 0.75f, 0f, minZ), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.81f, 1009),
                (new Vector3(maxX, 0f, minZ + spanZ * 0.25f), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.79f, 1103),
                (new Vector3(maxX, 0f, minZ + spanZ * 0.75f), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.83f, 1201),
                (new Vector3(minX + spanX * 0.25f, 0f, maxZ), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.8f, 1307),
                (new Vector3(minX + spanX * 0.75f, 0f, maxZ), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.77f, 1409),
                (new Vector3(minX, 0f, minZ + spanZ * 0.25f), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.76f, 1511),
                (new Vector3(minX, 0f, minZ + spanZ * 0.75f), EdgePeakRadiusMeters * 0.82f, EdgePeakRiseMeters * 0.84f, 1613),
            };
        }

        public static bool TileIntersectsOuterBandPublic(
            Terrain terrain,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float bandMeters) =>
            TileIntersectsOuterBand(terrain, minX, maxX, minZ, maxZ, bandMeters);

        static bool TileIntersectsOuterBand(
            Terrain terrain,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float bandMeters)
        {
            var o = terrain.transform.position;
            var s = terrain.terrainData.size;
            var cx = o.x + s.x * 0.5f;
            var cz = o.z + s.z * 0.5f;
            var distIn = DistanceInsideAabb(cx, cz, minX, maxX, minZ, maxZ);
            var tileRadius = Mathf.Max(s.x, s.z) * 0.55f;
            return distIn <= bandMeters + tileRadius;
        }

        static float NormFromMeters(Terrain terrain, float meters)
        {
            var sy = terrain?.terrainData?.size.y ?? 80f;
            return Mathf.Clamp(meters / Mathf.Max(sy, 1f), 0f, 0.48f);
        }

        static void ComputeWorldBounds(
            IReadOnlyList<Terrain> terrains,
            out float minX,
            out float maxX,
            out float minZ,
            out float maxZ)
        {
            minX = minZ = float.PositiveInfinity;
            maxX = maxZ = float.NegativeInfinity;

            foreach (var t in terrains)
            {
                if (t?.terrainData == null)
                    continue;

                var o = t.transform.position;
                var s = t.terrainData.size;
                minX = Mathf.Min(minX, o.x);
                minZ = Mathf.Min(minZ, o.z);
                maxX = Mathf.Max(maxX, o.x + s.x);
                maxZ = Mathf.Max(maxZ, o.z + s.z);
            }
        }

        static int SculptMountainsOnTerrain(
            Terrain terrain,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks)
        {
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var touched = SculptMountainsOnTerrain(terrain, minX, maxX, minZ, maxZ, peaks, heights);
            if (touched > 0)
            {
                CaveEditorUndo.RecordObject(data, "Outer ring mountains");
                data.SetHeights(0, 0, heights);
            }

            return touched;
        }

        /// <summary>CPU sculpt into an in-memory height buffer (paced commit via micro heightmap writes).</summary>
        static int SculptMountainsOnTerrain(
            Terrain terrain,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks,
            float[,] heights)
        {
            if (terrain?.terrainData == null || heights == null)
                return 0;

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            if (heights.GetLength(0) != res || heights.GetLength(1) != res)
                return 0;

            var origin = terrain.transform.position;
            var size = data.size;
            var touched = 0;
            touched += SculptMountainsBandRows(
                terrain,
                heights,
                minX,
                maxX,
                minZ,
                maxZ,
                peaks,
                0,
                res);

            return touched;
        }

        static int SculptMountainsBandRows(
            Terrain terrain,
            float[,] heights,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks,
            int yStart,
            int yEnd)
        {
            if (terrain?.terrainData == null || heights == null)
                return 0;

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            if (heights.GetLength(0) != res || heights.GetLength(1) != res)
                return 0;

            var origin = terrain.transform.position;
            var size = data.size;
            var touched = 0;
            var bandNorm = NormFromMeters(terrain, OuterBandRiseMeters);
            var ridgeNorm = NormFromMeters(terrain, RidgeNoiseAmplitudeMeters);
            var detailNorm = NormFromMeters(terrain, DetailNoiseAmplitudeMeters);
            var cliffNorm = NormFromMeters(terrain, 6f);
            yStart = Mathf.Clamp(yStart, 0, res);
            yEnd = Mathf.Clamp(yEnd, 0, res);

            for (var y = yStart; y < yEnd; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var distIn = DistanceInsideAabb(wx, wz, minX, maxX, minZ, maxZ);
                    if (distIn > OuterBandMeters)
                        continue;

                    var edgeFactor = 1f - distIn / OuterBandMeters;
                    var relief = SurfaceMountainReliefSampler.SampleOuterBandRelief(
                        wx,
                        wz,
                        edgeFactor,
                        minX,
                        maxX,
                        minZ,
                        maxZ,
                        9137);
                    var ridge = relief * ridgeNorm * edgeFactor;
                    var detailFade = Mathf.Lerp(0.25f, 1f, 1f - edgeFactor);
                    var detail = SurfaceMountainReliefSampler.SampleRockDetail(wx, wz, 9137 + y) *
                                 detailNorm *
                                 edgeFactor *
                                 detailFade;
                    var bandBump = bandNorm * edgeFactor * edgeFactor * (0.65f + relief * 0.35f);
                    var cliffFace = cliffNorm * edgeFactor * edgeFactor * relief;
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Clamp01(before + ridge + detail + bandBump + cliffFace);
                    if (Mathf.Abs(heights[y, x] - before) > 0.0001f)
                        touched++;
                }
            }

            if (yEnd >= res && peaks != null)
            {
                foreach (var peak in peaks)
                {
                    touched += SculptGaussianPeak(
                        heights,
                        res,
                        size,
                        origin,
                        peak.world,
                        peak.radius,
                        NormFromMeters(terrain, peak.riseMeters),
                        peak.seed);
                }
            }

            return touched;
        }

        sealed class OuterRingSculptSession
        {
            public Terrain Terrain;
            public float[,] Heights;
            public int Res;
            public int SculptRow;
            public int CommitRow;
            public int Touched;
            public float MinX;
            public float MaxX;
            public float MinZ;
            public float MaxZ;
            public IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> Peaks;
            public System.Action<int> OnComplete;
        }

        static void QueueSculptMountainsOnTerrain(
            Terrain terrain,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks,
            System.Action<int> onComplete)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var session = new OuterRingSculptSession
            {
                Terrain = terrain,
                Res = res,
                Heights = data.GetHeights(0, 0, res, res),
                MinX = minX,
                MaxX = maxX,
                MinZ = minZ,
                MaxZ = maxZ,
                Peaks = peaks,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunOuterRingSculptCpu(session));
        }

        static void RunOuterRingSculptCpu(OuterRingSculptSession session)
        {
            session.SculptRow = 0;
            session.Touched = 0;
            RunOuterRingSculptRowBand(session);
        }

        static void RunOuterRingSculptRowBand(OuterRingSculptSession session)
        {
            var chunk = CaveBuildMicroTerrainHeightmap.BandRowsFor(session.Res);
            var yEnd = Mathf.Min(session.Res, session.SculptRow + chunk);
            session.Touched += SculptMountainsBandRows(
                session.Terrain,
                session.Heights,
                session.MinX,
                session.MaxX,
                session.MinZ,
                session.MaxZ,
                session.Peaks,
                session.SculptRow,
                yEnd);
            session.SculptRow = yEnd;
            CaveBuildActionPacing.TouchQueueActivity();
            SurfaceTerrainTileExpansion.SetLiveTerrainFocus(session.Terrain);

            if (session.SculptRow < session.Res)
            {
                if (session.SculptRow <= 1 || session.SculptRow % 8 == 0 || session.SculptRow >= session.Res)
                    CaveBuildLiveSceneFeedback.NotifyTerrainTile(
                        session.Terrain,
                        $"sculpt rows {session.SculptRow}/{session.Res}",
                        ping: false,
                        forceCamera: session.SculptRow % 8 == 0);
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "outer ring mountains",
                    $"sculpt rows {session.SculptRow}/{session.Res} ({session.Terrain.name})");
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunOuterRingSculptRowBand(session));
                return;
            }

            if (session.Touched <= 0)
            {
                session.OnComplete?.Invoke(0);
                return;
            }

            CaveEditorUndo.RecordObject(session.Terrain.terrainData, "Outer ring mountains");
            session.CommitRow = 0;
            QueueOuterRingCommitBand(session);
        }

        static void QueueOuterRingCommitBand(OuterRingSculptSession session)
        {
            var y0 = session.CommitRow;
            var y1 = Mathf.Min(session.Res, y0 + CaveBuildMicroTerrainHeightmap.BandRowsFor(session.Res));
            CaveBuildMicroTerrainHeightmap.QueueWriteBand(
                session.Terrain,
                session.Heights,
                y0,
                y1,
                delayLod: true,
                () =>
                {
                    session.CommitRow = y1;
                    if (session.CommitRow < session.Res)
                    {
                        CaveBuildRunStatusPublisher.PulseSubOperation(
                            "outer ring mountains",
                            $"write rows {session.CommitRow}/{session.Res} ({session.Terrain.name})");
                        QueueOuterRingCommitBand(session);
                        return;
                    }

                    CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                        session.Terrain,
                        () =>
                        {
                            CaveBuildTerrainHeightmapMemory.Release(ref session.Heights);
                            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(session.Terrain);
                            session.OnComplete?.Invoke(session.Touched);
                        });
                });
        }

        static int SculptGaussianPeak(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 peakWorld,
            float radiusMeters,
            float addNormalized,
            int seed)
        {
            var rng = new System.Random(seed);
            var r2 = radiusMeters * radiusMeters;
            var touched = 0;
            var stepX = size.x / Mathf.Max(1, res - 1);
            var stepZ = size.z / Mathf.Max(1, res - 1);
            var xMin = Mathf.Clamp(Mathf.FloorToInt((peakWorld.x - radiusMeters - origin.x) / stepX), 0, res - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt((peakWorld.x + radiusMeters - origin.x) / stepX), 0, res - 1);
            var zMin = Mathf.Clamp(Mathf.FloorToInt((peakWorld.z - radiusMeters - origin.z) / stepZ), 0, res - 1);
            var zMax = Mathf.Clamp(Mathf.CeilToInt((peakWorld.z + radiusMeters - origin.z) / stepZ), 0, res - 1);

            for (var z = zMin; z <= zMax; z++)
            {
                for (var x = xMin; x <= xMax; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var dx = wx - peakWorld.x;
                    var dz = wz - peakWorld.z;
                    var d2 = dx * dx + dz * dz;
                    if (d2 > r2)
                        continue;

                    var t = 1f - d2 / r2;
                    var bump = addNormalized * CurvedMassFalloff(t) * (0.88f + (float)rng.NextDouble() * 0.18f);
                    var before = heights[z, x];
                    heights[z, x] = Mathf.Clamp01(before + bump);
                    if (Mathf.Abs(heights[z, x] - before) > 0.0001f)
                        touched++;
                }
            }

            return touched;
        }

        static float DistanceInsideAabb(float x, float z, float minX, float maxX, float minZ, float maxZ)
        {
            var dx = Mathf.Min(x - minX, maxX - x);
            var dz = Mathf.Min(z - minZ, maxZ - z);
            return Mathf.Max(0f, Mathf.Min(dx, dz));
        }

        /// <summary>
        /// Carves a bowl-shaped back slope on the peak-facing side of foothill tiles (mountain connection).
        /// Uses distance to the outer foothill AABB — not radial spokes from play center.
        /// </summary>
        static float ApplyFoothillPeakBackBowl(
            Terrain terrain,
            float h,
            float wx,
            float wz,
            float foothillMinX,
            float foothillMaxX,
            float foothillMinZ,
            float foothillMaxZ,
            float distOutsidePlay,
            float riseRamp,
            float massMul)
        {
            if (wx < foothillMinX || wx > foothillMaxX || wz < foothillMinZ || wz > foothillMaxZ)
                return h;

            var distToPeakRingEdge = DistanceInsideAabb(
                wx,
                wz,
                foothillMinX,
                foothillMaxX,
                foothillMinZ,
                foothillMaxZ);
            var band = Mathf.Max(48f, FoothillPeakBackBowlBandMeters);
            var backBowlT = 1f - Mathf.Clamp01(distToPeakRingEdge / band);
            backBowlT = Mathf.SmoothStep(0.08f, 1f, backBowlT);
            backBowlT *= backBowlT;

            var bowlMeters = FoothillOuterBowlDipMeters * (0.5f + 0.5f * riseRamp) * massMul;
            var bowlNorm = NormFromMeters(terrain, bowlMeters * backBowlT);
            h = Mathf.Clamp01(h - bowlNorm);

            if (distOutsidePlay < WildernessFoothillBlendMeters * 1.15f)
            {
                var frontT = 1f - backBowlT;
                var frontLift = NormFromMeters(terrain, 8f * massMul * frontT);
                h = Mathf.Clamp01(h + frontLift * riseRamp * 0.42f);
            }

            return h;
        }

        static float DistanceOutsideNineAabb(float x, float z, float nineMinX, float nineMaxX, float nineMinZ, float nineMaxZ)
        {
            var dxIn = x - nineMinX;
            var dxOut = nineMaxX - x;
            var dzIn = z - nineMinZ;
            var dzOut = nineMaxZ - z;
            if (dxIn >= 0f && dxOut >= 0f && dzIn >= 0f && dzOut >= 0f)
                return 0f;

            var ox = 0f;
            var oz = 0f;
            if (dxIn < 0f)
                ox = -dxIn;
            if (dxOut < 0f)
                ox = Mathf.Max(ox, -dxOut);
            if (dzIn < 0f)
                oz = -dzIn;
            if (dzOut < 0f)
                oz = Mathf.Max(oz, -dzOut);
            return Mathf.Sqrt(ox * ox + oz * oz);
        }

        static float MapEdgeFalloffFactor(
            float wx,
            float wz,
            float wildMinX,
            float wildMaxX,
            float wildMinZ,
            float wildMaxZ)
        {
            var distIn = DistanceInsideAabb(wx, wz, wildMinX, wildMaxX, wildMinZ, wildMaxZ);
            const float edgeMeters = 48f;
            return Mathf.Clamp01(distIn / edgeMeters);
        }

        static float ExposedEdgeBlendStrength(
            float wx,
            float wz,
            Vector2Int gridOff,
            Vector3 origin,
            Vector3 size)
        {
            if (Mathf.Max(Mathf.Abs(gridOff.x), Mathf.Abs(gridOff.y)) < 1)
                return 0f;

            var depth = PerimeterTransitionDepthMeters;
            var best = 0f;

            if (gridOff.x <= -1)
            {
                var d = wx - origin.x;
                if (d < depth)
                    best = Mathf.Max(best, 1f - d / depth);
            }

            if (gridOff.x >= 1)
            {
                var d = origin.x + size.x - wx;
                if (d < depth)
                    best = Mathf.Max(best, 1f - d / depth);
            }

            if (gridOff.y <= -1)
            {
                var d = wz - origin.z;
                if (d < depth)
                    best = Mathf.Max(best, 1f - d / depth);
            }

            if (gridOff.y >= 1)
            {
                var d = origin.z + size.z - wz;
                if (d < depth)
                    best = Mathf.Max(best, 1f - d / depth);
            }

            return Mathf.Clamp01(best);
        }

        static void QueueSculptWildernessOnTerrain(
            Terrain mainTerrain,
            Terrain terrain,
            OuterRingSculptTier tier,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            float foothillMinX,
            float foothillMaxX,
            float foothillMinZ,
            float foothillMaxZ,
            float wildMinX,
            float wildMaxX,
            float wildMinZ,
            float wildMaxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks,
            System.Action<int> onComplete)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            CaveBuildTerrainHeightmapMemory.CommitTerrainHeightmap(terrain);

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var session = new WildernessSculptSession
            {
                MainTerrain = mainTerrain,
                Terrain = terrain,
                Tier = tier,
                Res = res,
                Heights = data.GetHeights(0, 0, res, res),
                NineMinX = nineMinX,
                NineMaxX = nineMaxX,
                NineMinZ = nineMinZ,
                NineMaxZ = nineMaxZ,
                FoothillMinX = foothillMinX,
                FoothillMaxX = foothillMaxX,
                FoothillMinZ = foothillMinZ,
                FoothillMaxZ = foothillMaxZ,
                WildMinX = wildMinX,
                WildMaxX = wildMaxX,
                WildMinZ = wildMinZ,
                WildMaxZ = wildMaxZ,
                Peaks = peaks,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunWildernessSculptCpu(session),
                CaveBuildPipelineDomains.QueueLabel($"{tier} sculpt — start"));
        }

        sealed class WildernessSculptSession
        {
            public Terrain MainTerrain;
            public Terrain Terrain;
            public OuterRingSculptTier Tier;
            public float[,] Heights;
            public int Res;
            public int SculptRow;
            public int CommitRow;
            public int Touched;
            public float NineMinX, NineMaxX, NineMinZ, NineMaxZ;
            public float FoothillMinX, FoothillMaxX, FoothillMinZ, FoothillMaxZ;
            public float WildMinX, WildMaxX, WildMinZ, WildMaxZ;
            public IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> Peaks;
            public System.Action<int> OnComplete;
        }

        static void RunWildernessSculptCpu(WildernessSculptSession session)
        {
            session.SculptRow = 0;
            session.Touched = 0;
            RunWildernessSculptRowBand(session);
        }

        static void RunWildernessSculptRowBand(WildernessSculptSession session)
        {
            var chunk = CaveBuildMicroTerrainHeightmap.BandRowsFor(session.Res);
            var yEnd = Mathf.Min(session.Res, session.SculptRow + chunk);
            session.Touched += SculptWildernessBandRows(
                session.MainTerrain,
                session.Terrain,
                session.Tier,
                session.Heights,
                session.NineMinX,
                session.NineMaxX,
                session.NineMinZ,
                session.NineMaxZ,
                session.FoothillMinX,
                session.FoothillMaxX,
                session.FoothillMinZ,
                session.FoothillMaxZ,
                session.WildMinX,
                session.WildMaxX,
                session.WildMinZ,
                session.WildMaxZ,
                session.Peaks,
                session.SculptRow,
                yEnd);
            session.SculptRow = yEnd;
            CaveBuildActionPacing.TouchQueueActivity();
            SurfaceTerrainTileExpansion.SetLiveTerrainFocus(session.Terrain);

            if (session.SculptRow < session.Res)
            {
                PulseWildernessTerraformMicro(session, "sculpt rows", session.SculptRow, session.Res);
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "wilderness mountains",
                    $"rows {session.SculptRow}/{session.Res} ({session.Terrain.name})");
                CaveBuildActionPacing.ScheduleLight(
                    () => RunWildernessSculptRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel("wilderness mountains — row band"));
                return;
            }

            if (session.Touched <= 0)
            {
                session.OnComplete?.Invoke(0);
                return;
            }

            CaveEditorUndo.RecordObject(session.Terrain.terrainData, "Wilderness mountains");
            session.CommitRow = 0;
            QueueWildernessCommitBand(session);
        }

        static void QueueWildernessCommitBand(WildernessSculptSession session)
        {
            var y0 = session.CommitRow;
            var y1 = Mathf.Min(session.Res, y0 + CaveBuildMicroTerrainHeightmap.BandRowsFor(session.Res));
            CaveBuildMicroTerrainHeightmap.QueueWriteBand(
                session.Terrain,
                session.Heights,
                y0,
                y1,
                delayLod: true,
                () =>
                {
                    session.CommitRow = y1;
                    if (session.CommitRow < session.Res)
                    {
                        PulseWildernessTerraformMicro(
                            session,
                            "write rows",
                            session.CommitRow,
                            session.Res);
                        CaveBuildActionPacing.ScheduleLight(
                            () => QueueWildernessCommitBand(session),
                            CaveBuildPipelineDomains.QueueLabel("wilderness mountains — write band"));
                        return;
                    }

                    CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                        session.Terrain,
                        () =>
                        {
                            CaveBuildTerrainHeightmapMemory.Release(ref session.Heights);
                            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(session.Terrain);
                            session.OnComplete?.Invoke(session.Touched);
                        });
                });
        }

        static void PulseWildernessTerraformMicro(
            WildernessSculptSession session,
            string microLabel,
            int microStep,
            int microTotal)
        {
            if (!SurfaceTerrainTileExpansion.IsLiveFullWorldTerraformPhase || session?.Terrain == null)
                return;

            SurfaceTerrainTileExpansion.SetLiveTerrainFocus(session.Terrain);

            if (microTotal > 64 && microStep % 16 != 0 && microStep != microTotal)
                return;

            SurfaceTerrainTileExpansion.PulseLiveTerraformMicro(
                microLabel,
                microStep,
                microTotal,
                session.Terrain);
        }

        static int SculptWildernessBandRows(
            Terrain mainTerrain,
            Terrain terrain,
            OuterRingSculptTier tier,
            float[,] heights,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            float foothillMinX,
            float foothillMaxX,
            float foothillMinZ,
            float foothillMaxZ,
            float wildMinX,
            float wildMaxX,
            float wildMinZ,
            float wildMaxZ,
            IReadOnlyList<(Vector3 world, float radius, float riseMeters, int seed)> peaks,
            int yStart,
            int yEnd)
        {
            if (terrain?.terrainData == null || heights == null)
                return 0;

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var origin = terrain.transform.position;
            var size = data.size;
            var touched = 0;
            var isFoothill = tier == OuterRingSculptTier.Foothill;
            var tileVariety = 0.72f + Hash01((terrain.name.GetHashCode() ^ 4403) & 0x7fffffff) * 0.56f;
            var massMul = tier switch
            {
                OuterRingSculptTier.Foothill => 0.92f * tileVariety,
                OuterRingSculptTier.Horizon => HorizonPeakRiseScale * tileVariety,
                _ => tileVariety,
            };
            var bandNorm = NormFromMeters(terrain, OuterBandRiseMeters * massMul);
            var ridgeNorm = NormFromMeters(
                terrain,
                RidgeNoiseAmplitudeMeters * massMul * (isFoothill ? FoothillRidgeScale : 1f));
            var detailNorm = isFoothill
                ? NormFromMeters(terrain, DetailNoiseAmplitudeMeters * massMul * 0.52f)
                : NormFromMeters(terrain, DetailNoiseAmplitudeMeters * massMul * 0.35f);
            var cliffNorm = isFoothill
                ? 0f
                : NormFromMeters(terrain, PeakCliffAccentMeters * massMul);
            var nineMidX = (nineMinX + nineMaxX) * 0.5f;
            var nineMidZ = (nineMinZ + nineMaxZ) * 0.5f;
            yStart = Mathf.Clamp(yStart, 0, res);
            yEnd = Mathf.Clamp(yEnd, 0, res);

            for (var y = yStart; y < yEnd; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var distOutsidePlay = DistanceOutsideNineAabb(wx, wz, nineMinX, nineMaxX, nineMinZ, nineMaxZ);
                    var distOutsideFoothill = DistanceOutsideNineAabb(
                        wx,
                        wz,
                        foothillMinX,
                        foothillMaxX,
                        foothillMinZ,
                        foothillMaxZ);

                    var riseDist = isFoothill ? distOutsidePlay : distOutsideFoothill;
                    var innerDead = isFoothill ? WildernessInnerLockMeters : PeakRiseInnerMeters;
                    var riseRamp = CurvedMassFalloff(
                        (riseDist - innerDead) /
                        Mathf.Max(8f, WildernessRiseSpanMeters - innerDead));

                    var h = heights[y, x];
                    if (mainTerrain != null && isFoothill &&
                        distOutsidePlay < WildernessFoothillBlendMeters &&
                        SurfaceTerrainTileExpansion.TrySamplePlayDiskNormAtWorld(mainTerrain, wx, wz, out var playNorm))
                    {
                        const float seamOnlyMeters = SeamPreserveMeters;
                        if (distOutsidePlay <= seamOnlyMeters)
                        {
                            h = Mathf.Max(h, playNorm + FoothillPlayEdgeRiseNorm);
                        }
                        else
                        {
                            var rollT = CurvedMassFalloff(
                                (distOutsidePlay - seamOnlyMeters) /
                                Mathf.Max(8f, WildernessFoothillBlendMeters - seamOnlyMeters));
                            var toe = playNorm + FoothillPlayEdgeRiseNorm * (1f - rollT * 0.22f);
                            h = Mathf.Max(h, Mathf.Lerp(toe, h, rollT));
                        }
                    }
                    else if (mainTerrain != null && !isFoothill &&
                             distOutsideFoothill < WildernessFoothillBlendMeters * 1.35f &&
                             SurfaceTerrainTileExpansion.TrySampleFoothillOrPlayNormAtWorld(
                                 mainTerrain,
                                 wx,
                                 wz,
                                 out var fhNorm))
                    {
                        var blendSpan = WildernessFoothillBlendMeters * 1.35f;
                        var peakT = CurvedMassFalloff(
                            distOutsideFoothill / Mathf.Max(8f, blendSpan));
                        var bowlFloor = fhNorm -
                                        NormFromMeters(terrain, FoothillOuterBowlDipMeters * 0.72f * massMul);
                        var peakToe = fhNorm + FoothillPlayEdgeRiseNorm * 0.18f;
                        var targetToe = Mathf.Lerp(bowlFloor, peakToe, peakT * peakT);
                        h = Mathf.Lerp(targetToe, h, CurvedMassFalloff(peakT));
                    }

                    if (isFoothill && riseRamp > 0.02f)
                        h = ApplyFoothillPeakBackBowl(
                            terrain,
                            h,
                            wx,
                            wz,
                            foothillMinX,
                            foothillMaxX,
                            foothillMinZ,
                            foothillMaxZ,
                            distOutsidePlay,
                            riseRamp,
                            massMul);

                    if (riseRamp <= 0.001f)
                    {
                        if (Mathf.Abs(h - heights[y, x]) > 0.0001f)
                        {
                            heights[y, x] = h;
                            touched++;
                        }

                        continue;
                    }

                    var mapEdge = MapEdgeFalloffFactor(wx, wz, wildMinX, wildMaxX, wildMinZ, wildMaxZ);
                    var relief = SurfaceMountainReliefSampler.SampleAppalachianRollingRelief(
                        wx,
                        wz,
                        riseRamp,
                        mapEdge,
                        9137);
                    var massScale = riseRamp * riseRamp * massMul;
                    var ridge = relief * ridgeNorm * massScale;
                    if (isFoothill)
                    {
                        var tileSeed = 9137 + (terrain.name.GetHashCode() & 0x7fffffff);
                        var omni = SurfaceMountainReliefSampler.SampleFoothillOmnidirectionalRelief(
                            wx,
                            wz,
                            tileSeed,
                            riseRamp);
                        var omniCross = SurfaceMountainReliefSampler.SampleFoothillOmnidirectionalRelief(
                            wz,
                            wx,
                            tileSeed + 331,
                            riseRamp * 0.82f);
                        ridge += (omni * 0.72f + omniCross * 0.48f) * ridgeNorm * massScale;
                    }

                    var detailStrength = isFoothill ? 0.92f : 0.12f;
                    var detail = detailNorm > 0.0001f
                        ? SurfaceMountainReliefSampler.SampleRockDetail(wx, wz, 9137 + y) *
                          detailNorm *
                          massScale *
                          detailStrength
                        : 0f;
                    var bandBump = bandNorm * massScale * (0.55f + relief * 0.45f);
                    var cliffFace = 0f;
                    if (cliffNorm > 0.0001f)
                    {
                        var outX = wx - nineMidX;
                        var outZ = wz - nineMidZ;
                        var cliffRelief = SurfaceMountainReliefSampler.SampleDirectionalEdgeRelief(
                            wx,
                            wz,
                            outX,
                            outZ,
                            9137 + (terrain.name.GetHashCode() & 0x7fffffff));
                        cliffFace = cliffNorm * massScale * cliffRelief * Mathf.SmoothStep(0.12f, 0.92f, riseRamp);
                    }

                    var before = h;
                    heights[y, x] = Mathf.Clamp01(before + ridge + detail + bandBump + cliffFace);
                    if (Mathf.Abs(heights[y, x] - before) > 0.0001f)
                        touched++;
                }
            }

            if (!isFoothill && yEnd >= res && peaks != null)
            {
                foreach (var peak in peaks)
                {
                    if (DistanceOutsideNineAabb(
                            peak.world.x,
                            peak.world.z,
                            foothillMinX,
                            foothillMaxX,
                            foothillMinZ,
                            foothillMaxZ) < WildernessFoothillBlendMeters + 12f)
                        continue;

                    var peakScale = tier == OuterRingSculptTier.Horizon ? HorizonPeakRiseScale * 0.85f : 0.92f;
                    touched += SculptGaussianPeak(
                        heights,
                        res,
                        size,
                        origin,
                        peak.world,
                        peak.radius,
                        NormFromMeters(terrain, peak.riseMeters) * peakScale,
                        peak.seed);
                }
            }

            return touched;
        }

        sealed class PerimeterBlendSession
        {
            public Terrain Terrain;
            public Vector2Int GridOff;
            public int Res;
            public float[,] Heights;
            public int YBase;
            public int YMin;
            public int YMax;
            public int XMin;
            public int XMax;
            public int Row;
            public System.Action OnComplete;
        }

        static void ComputePerimeterBlendRegion(
            Vector2Int gridOff,
            int res,
            Vector3 size,
            out int yMin,
            out int yMax,
            out int xMin,
            out int xMax)
        {
            yMin = 1;
            yMax = res - 1;
            xMin = 1;
            xMax = res - 1;
            var metersPerX = size.x / Mathf.Max(1, res - 1);
            var metersPerZ = size.z / Mathf.Max(1, res - 1);
            var marginX = Mathf.Clamp(Mathf.CeilToInt(PerimeterTransitionDepthMeters / Mathf.Max(0.01f, metersPerX)) + 2, 4, res / 2);
            var marginZ = Mathf.Clamp(Mathf.CeilToInt(PerimeterTransitionDepthMeters / Mathf.Max(0.01f, metersPerZ)) + 2, 4, res / 2);

            if (gridOff.x <= -1)
            {
                xMin = 1;
                xMax = Mathf.Min(res - 1, marginX);
            }

            if (gridOff.x >= 1)
            {
                xMin = Mathf.Max(1, res - 1 - marginX);
                xMax = res - 1;
            }

            if (gridOff.y <= -1)
            {
                yMin = 1;
                yMax = Mathf.Min(res - 1, marginZ);
            }

            if (gridOff.y >= 1)
            {
                yMin = Mathf.Max(1, res - 1 - marginZ);
                yMax = res - 1;
            }
        }

        static void QueuePerimeterBlendOnTerrain(
            Terrain terrain,
            Vector2Int gridOff,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildTerrainHeightmapMemory.CommitTerrainHeightmap(terrain);

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var size = data.size;
            ComputePerimeterBlendRegion(gridOff, res, size, out var yMin, out var yMax, out var xMin, out var xMax);
            var fetchY0 = Mathf.Max(0, yMin - 1);
            var fetchY1 = Mathf.Min(res, yMax + 1);
            var rowCount = Mathf.Max(0, fetchY1 - fetchY0);
            if (rowCount <= 0)
            {
                onComplete?.Invoke();
                return;
            }

            var session = new PerimeterBlendSession
            {
                Terrain = terrain,
                GridOff = gridOff,
                Res = res,
                YBase = fetchY0,
                YMin = yMin,
                YMax = yMax,
                XMin = xMin,
                XMax = xMax,
                Heights = data.GetHeights(0, fetchY0, res, rowCount),
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunPerimeterBlendRowBand(session),
                CaveBuildPipelineDomains.QueueLabel("nine edge soften — edge strip"));
        }

        static void RunPerimeterBlendRowBand(PerimeterBlendSession session)
        {
            var terrain = session.Terrain;
            var data = terrain.terrainData;
            var res = session.Res;
            var origin = terrain.transform.position;
            var size = data.size;
            var band = Mathf.Max(16, CaveBuildMicroTerrainHeightmap.BandRowsFor(res));
            var yEnd = Mathf.Min(session.YMax, session.YMin + session.Row + band);

            for (var y = session.YMin + session.Row; y < yEnd; y++)
            {
                var ly = y - session.YBase;
                for (var x = session.XMin; x < session.XMax; x++)
                {
                    if (x <= 0 || x >= res - 1 || y <= 0 || y >= res - 1)
                        continue;

                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var strength = ExposedEdgeBlendStrength(wx, wz, session.GridOff, origin, size);
                    if (strength <= 0.001f)
                        continue;

                    var h = session.Heights;
                    var avg = (h[ly, x] + h[Mathf.Max(0, ly - 1), x] + h[Mathf.Min(h.GetLength(0) - 1, ly + 1), x] +
                               h[ly, Mathf.Max(0, x - 1)] + h[ly, Mathf.Min(res - 1, x + 1)]) / 5f;
                    var soften = strength * PerimeterHeightSoftening;
                    h[ly, x] = Mathf.Lerp(h[ly, x], avg, soften);
                }
            }

            session.Row = yEnd - session.YMin;
            if (session.Row < session.YMax - session.YMin)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunPerimeterBlendRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel("nine edge soften — row band"));
                return;
            }

            CaveEditorUndo.RecordObject(data, "Nine-tile perimeter edge soften");
            data.SetHeights(0, session.YBase, session.Heights);
            CaveBuildTerrainHeightmapMemory.Release(ref session.Heights);
            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(terrain);
            session.OnComplete?.Invoke();
        }

        const float CliffAccentExtraMeters = 4.2f;

        /// <summary>Removes horizontal streak / drift noise on outer band while keeping macro cliffs.</summary>
        public static void QueueDenoiseOuterBand(Terrain mainTerrain, System.Action onComplete)
        {
            if (mainTerrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var nine = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            ComputeNineTileWorldBounds(nine, out var nineMinX, out var nineMaxX, out var nineMinZ, out var nineMaxZ);
            var wilderness = SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain);
            if (wilderness.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var queue = new Queue<Terrain>(wilderness);
            ProcessNextDenoiseTile(queue, nineMinX, nineMaxX, nineMinZ, nineMaxZ, onComplete);
        }

        static void ProcessNextDenoiseTile(
            Queue<Terrain> queue,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            System.Action onComplete)
        {
            if (queue.Count == 0)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Mountain outer band denoise complete.",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var terrain = queue.Dequeue();
            QueueDenoiseOnTerrain(
                terrain,
                minX,
                maxX,
                minZ,
                maxZ,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => ProcessNextDenoiseTile(queue, minX, maxX, minZ, maxZ, onComplete),
                        CaveBuildPipelineDomains.QueueLabel("wilderness denoise — next tile"));
                });
        }

        static bool IsMountainWildernessTerrainName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.StartsWith(SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix, StringComparison.Ordinal) ||
                   name.StartsWith(SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix, StringComparison.Ordinal) ||
                   name.StartsWith(SurfaceTerrainTileExpansion.MountainHorizonTileNamePrefix, StringComparison.Ordinal) ||
                   name.StartsWith(SurfaceTerrainTileExpansion.MountainWildernessTileNamePrefix, StringComparison.Ordinal);
        }

        static void QueueDenoiseOnTerrain(
            Terrain terrain,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null || !IsMountainWildernessTerrainName(terrain.name))
            {
                onComplete?.Invoke();
                return;
            }

            if (terrain.name.StartsWith(
                    SurfaceTerrainTileExpansion.MountainFoothillTileNamePrefix,
                    StringComparison.Ordinal))
            {
                onComplete?.Invoke();
                return;
            }

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var session = new DenoiseSession
            {
                Terrain = terrain,
                Res = res,
                Heights = data.GetHeights(0, 0, res, res),
                MinX = nineMinX,
                MaxX = nineMaxX,
                MinZ = nineMinZ,
                MaxZ = nineMaxZ,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunDenoiseRowBand(session),
                CaveBuildPipelineDomains.QueueLabel("wilderness denoise — rows"));
        }

        sealed class DenoiseSession
        {
            public Terrain Terrain;
            public int Res;
            public float[,] Heights;
            public float MinX, MaxX, MinZ, MaxZ;
            public int Row;
            public System.Action OnComplete;
        }

        static void RunDenoiseRowBand(DenoiseSession session)
        {
            var terrain = session.Terrain;
            var data = terrain.terrainData;
            var res = session.Res;
            var origin = terrain.transform.position;
            var size = data.size;
            const int band = 14;
            const float strength = 0.38f;
            var yEnd = Mathf.Min(session.Row + band, res);
            for (var y = Mathf.Max(1, session.Row); y < yEnd && y < res - 1; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var distOutside = DistanceOutsideNineAabb(
                        wx,
                        wz,
                        session.MinX,
                        session.MaxX,
                        session.MinZ,
                        session.MaxZ);
                    if (distOutside < 1f)
                        continue;

                    var edgeFactor = Mathf.Clamp01(distOutside / WildernessRiseSpanMeters);
                    var h = session.Heights;
                    var avg = (h[y, x] + h[y - 1, x] + h[y + 1, x] + h[y, x - 1] + h[y, x + 1]) / 5f;
                    h[y, x] = Mathf.Lerp(h[y, x], avg, strength * edgeFactor);
                }
            }

            session.Row = yEnd;
            if (session.Row < res)
            {
                if (SurfaceTerrainTileExpansion.IsLiveFullWorldTerraformPhase &&
                    (session.Row <= band || session.Row % 28 == 0))
                {
                    SurfaceTerrainTileExpansion.PulseLiveTerraformMicro(
                        "denoise rows",
                        session.Row,
                        res,
                        terrain);
                }

                CaveBuildActionPacing.ScheduleLight(
                    () => RunDenoiseRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel("wilderness denoise — row band"));
                return;
            }

            data.SetHeights(0, 0, session.Heights);
            CaveBuildTerrainHeightmapMemory.Release(ref session.Heights);
            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(terrain);
            session.OnComplete?.Invoke();
        }

        /// <summary>Extra cliff fall-off on outer band only (paced rows) — after full outer ring sculpt.</summary>
        public static void QueueCliffAccentOnTerrain(
            Terrain terrain,
            float nineMinX,
            float nineMaxX,
            float nineMinZ,
            float nineMaxZ,
            System.Action onComplete)
        {
            if (terrain?.terrainData == null || !IsMountainWildernessTerrainName(terrain.name))
            {
                onComplete?.Invoke();
                return;
            }

            var isPeak = terrain.name.StartsWith(
                SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                StringComparison.Ordinal);
            var cliffMeters = isPeak ? PeakCliffAccentMeters : CliffAccentExtraMeters;
            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            var session = new CliffAccentSession
            {
                Terrain = terrain,
                Res = res,
                Heights = data.GetHeights(0, 0, res, res),
                MinX = nineMinX,
                MaxX = nineMaxX,
                MinZ = nineMinZ,
                MaxZ = nineMaxZ,
                CliffNorm = NormFromMeters(terrain, cliffMeters),
                IsPeak = isPeak,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunCliffAccentRowBand(session),
                CaveBuildPipelineDomains.QueueLabel("wilderness cliffs — rows"));
        }

        sealed class CliffAccentSession
        {
            public Terrain Terrain;
            public int Res;
            public float[,] Heights;
            public float MinX, MaxX, MinZ, MaxZ, CliffNorm;
            public bool IsPeak;
            public int Row;
            public System.Action OnComplete;
        }

        static void RunCliffAccentRowBand(CliffAccentSession session)
        {
            var terrain = session.Terrain;
            var data = terrain.terrainData;
            var res = session.Res;
            var origin = terrain.transform.position;
            var size = data.size;
            const int band = 12;
            var yEnd = Mathf.Min(session.Row + band, res);
            var nineMidX = (session.MinX + session.MaxX) * 0.5f;
            var nineMidZ = (session.MinZ + session.MaxZ) * 0.5f;
            var cliffStrength = session.IsPeak ? 0.72f : 0.35f;

            for (var y = session.Row; y < yEnd; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var distOutside = DistanceOutsideNineAabb(
                        wx,
                        wz,
                        session.MinX,
                        session.MaxX,
                        session.MinZ,
                        session.MaxZ);
                    if (distOutside < 4f)
                        continue;

                    var wildernessFactor = Mathf.SmoothStep(4f, WildernessRiseSpanMeters, distOutside);
                    var relief = session.IsPeak
                        ? SurfaceMountainReliefSampler.SampleDirectionalEdgeRelief(
                            wx,
                            wz,
                            wx - nineMidX,
                            wz - nineMidZ,
                            9137 + (session.Terrain.name.GetHashCode() & 0x7fffffff))
                        : SurfaceMountainReliefSampler.SampleAppalachianRollingRelief(
                            wx,
                            wz,
                            wildernessFactor,
                            1f,
                            9137);
                    var before = session.Heights[y, x];
                    var cliff = session.CliffNorm * wildernessFactor * relief * cliffStrength;
                    session.Heights[y, x] = Mathf.Clamp01(before + cliff);
                }
            }

            session.Row = yEnd;
            if (session.Row < res)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunCliffAccentRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel("wilderness cliffs — row band"));
                return;
            }

            data.SetHeights(0, 0, session.Heights);
            CaveBuildTerrainHeightmapMemory.Release(ref session.Heights);
            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(terrain);
            session.OnComplete?.Invoke();
        }

        public static float SampleOuterEdgeMeanHeight(Terrain mainTerrain)
        {
            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain);
            if (terrains.Count < 9)
                return 0f;

            ComputeWorldBounds(terrains, out var minX, out var maxX, out var minZ, out var maxZ);
            var sum = 0f;
            var n = 0;
            const int samples = 32;

            for (var i = 0; i <= samples; i++)
            {
                var t = i / (float)samples;
                SampleEdge(minX, maxX, minZ, maxZ, t, mainTerrain, ref sum, ref n);
            }

            return n > 0 ? sum / n : 0f;
        }

        static void SampleEdge(
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            float t,
            Terrain mainTerrain,
            ref float sum,
            ref int n)
        {
            var perimeter = 2f * ((maxX - minX) + (maxZ - minZ));
            var d = t * perimeter;
            float wx;
            float wz;
            var w = maxX - minX;
            var h = maxZ - minZ;

            if (d < w)
            {
                wx = minX + d;
                wz = minZ;
            }
            else if (d < w + h)
            {
                wx = maxX;
                wz = minZ + (d - w);
            }
            else if (d < 2f * w + h)
            {
                wx = maxX - (d - w - h);
                wz = maxZ;
            }
            else
            {
                wx = minX;
                wz = maxZ - (d - 2f * w - h);
            }

            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, wx, wz, out var tile) || tile == null)
                return;

            sum += tile.SampleHeight(new Vector3(wx, 0f, wz)) + tile.transform.position.y;
            n++;
        }

        /// <summary>Curved foothill/peak lift near the Hollow Titan site (seams untouched).</summary>
        public static int SculptCurvedApproachNearPoint(
            Terrain tile,
            Terrain mainTerrain,
            Vector3 siteWorld,
            float trunkRadius,
            int directionIndex)
        {
            if (tile?.terrainData == null || mainTerrain?.terrainData == null)
                return 0;

            directionIndex = Mathf.Clamp(directionIndex, 0, 3);
            var data = tile.terrainData;
            var res = data.heightmapResolution;
            var size = data.size;
            var origin = tile.transform.position;
            var touched = 0;
            var reach = trunkRadius * 2.4f;
            var centerBias = directionIndex switch
            {
                0 => new Vector2(0f, 1f),
                1 => new Vector2(1f, 0f),
                2 => new Vector2(0f, -1f),
                _ => new Vector2(-1f, 0f),
            };

            CaveEditorUndo.RecordObject(data, "Titan approach curve");
            var heights = data.GetHeights(0, 0, res, res);
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var dx = wx - siteWorld.x;
                    var dz = wz - siteWorld.z;
                    var dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist < trunkRadius * 0.95f || dist > reach)
                        continue;

                    var dir = new Vector2(dx, dz).normalized;
                    if (Vector2.Dot(dir, centerBias) < 0.35f)
                        continue;

                    if (SurfaceTerrainTileExpansion.TrySamplePlayDiskNormAtWorld(mainTerrain, wx, wz, out _) &&
                        dist < trunkRadius + SeamPreserveMeters)
                        continue;

                    var t = CurvedMassFalloff(1f - (dist - trunkRadius) / Mathf.Max(8f, reach - trunkRadius));
                    var liftM = (ResolveSculptTier(tile) == OuterRingSculptTier.Peak ? 18f : 12f) * t;
                    var before = heights[z, x];
                    heights[z, x] = Mathf.Clamp01(before + NormFromMeters(tile, liftM));
                    if (Mathf.Abs(heights[z, x] - before) > 0.0001f)
                        touched++;
                }
            }

            if (touched > 0)
            {
                data.SetHeights(0, 0, heights);
                tile.Flush();
            }

            return touched;
        }

        static OuterRingSculptTier ResolveSculptTier(Terrain tile)
        {
            if (tile == null)
                return OuterRingSculptTier.Foothill;

            if (tile.name.StartsWith(
                    SurfaceTerrainTileExpansion.MountainHorizonTileNamePrefix,
                    StringComparison.Ordinal))
                return OuterRingSculptTier.Horizon;

            if (tile.name.StartsWith(
                    SurfaceTerrainTileExpansion.MountainPeakTileNamePrefix,
                    StringComparison.Ordinal))
                return OuterRingSculptTier.Peak;

            return OuterRingSculptTier.Foothill;
        }
    }
}
#endif
