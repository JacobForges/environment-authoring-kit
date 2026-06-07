#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Optionally attaches up to 8 neighbor Terrain tiles around the Ground-centered main tile when it helps playable area.
    /// Each neighbor is edge-seeded from main, then Florida DEM + seams run once from the Ground anchor (no per-tile radial sculpt).
    /// </summary>
    static class SurfaceTerrainTileExpansion
    {
        public const int MaxExtraTiles = 8;
        public const string TilesRootName = "SurfaceTerrainTiles";
        public const string MainTerrainName = "SurfaceTerrainMain";
        public const string FlatTerrainHostName = "SurfaceTerrainFlatHost";
        public const string FullWorldGridAnchorName = "SurfaceFullWorldGridAnchor";

        /// <summary>Prefer authoritative main tile — never a neighbor SurfaceTerrainTile_* orphan.</summary>
        public static Terrain FindMainTerrainInScene()
        {
            if (!SceneManager.GetActiveScene().IsValid())
                return null;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    if (terrain != null && terrain.name == MainTerrainName)
                        return terrain;
                }
            }

            return null;
        }

        static void DestroyIfAllowed(UnityEngine.Object obj, string context)
        {
            if (obj == null)
                return;

            if (obj is GameObject go && !PipelineContentPreservePolicy.ShouldAllowDestroy(go, context))
                return;

            if (obj is Component component &&
                !PipelineContentPreservePolicy.ShouldAllowDestroy(component.gameObject, context))
                return;

            CaveEditorUndo.DestroyImmediate(obj);
        }

        static float ScoreTerrainKeeper(Terrain tile, Vector3 expected, Transform canonicalRoot)
        {
            if (tile == null)
                return float.MaxValue;

            var score = (tile.transform.position - expected).sqrMagnitude;
            if (canonicalRoot != null && tile.transform.IsChildOf(canonicalRoot))
                score -= 1_000_000f;

            if (PipelineContentPreservePolicy.IntegrateOnlyActive && tile.terrainData != null)
            {
                var res = Mathf.Min(tile.terrainData.heightmapResolution, 65);
                var heights = tile.terrainData.GetHeights(0, 0, res, res);
                var variance = 0f;
                for (var z = 0; z < res; z++)
                {
                    for (var x = 0; x < res; x++)
                        variance += heights[z, x];
                }

                variance /= res * res;
                score -= variance * 50_000f;
            }

            return score;
        }

        static bool _fullWorldTerrainPurgedThisBuild;
        static bool _fullWorldGroundLaidThisBuild;
        static FullWorldGridPhase _liveFullWorldGridPhase;
        static Terrain _liveFocusTerrain;
        static int _liveTerraformStep;
        static int _liveTerraformTotal;
        static string _liveTerraformArmLabel = string.Empty;
        static string _liveTerraformMicroLabel = string.Empty;
        static int _liveTerraformMicroStep;
        static int _liveTerraformMicroTotal;

        /// <summary>Scene camera + live bounds prefer this tile during terraform/sculpt.</summary>
        public static void SetLiveTerrainFocus(Terrain terrain) => _liveFocusTerrain = terrain;

        public static void ClearLiveTerrainFocus() => _liveFocusTerrain = null;

        public static Terrain LiveFocusTerrain => _liveFocusTerrain;

        public static void SetLiveTerraformProgress(int step, int total, string armLabel = null)
        {
            _liveTerraformStep = Mathf.Max(0, step);
            _liveTerraformTotal = Mathf.Max(0, total);
            if (!string.IsNullOrEmpty(armLabel))
                _liveTerraformArmLabel = armLabel;
        }

        public static int LiveTerraformStep => _liveTerraformStep;

        public static int LiveTerraformTotal => _liveTerraformTotal;

        public static bool IsLiveFullWorldTerraformPhase =>
            _liveFullWorldGridPhase == FullWorldGridPhase.Terraform;

        public static void PulseLiveTerraformMicro(
            string microLabel,
            int microStep,
            int microTotal,
            Terrain tile = null,
            string armLabel = null,
            int terraformStep = -1,
            int terraformTotal = -1)
        {
            if (!IsLiveFullWorldTerraformPhase)
                return;

            microLabel ??= string.Empty;
            _liveTerraformMicroLabel = microLabel;
            _liveTerraformMicroStep = microStep;
            _liveTerraformMicroTotal = microTotal;

            var step = terraformStep > 0 ? terraformStep : _liveTerraformStep;
            var total = terraformTotal > 0 ? terraformTotal : _liveTerraformTotal;
            var arm = !string.IsNullOrEmpty(armLabel)
                ? armLabel
                : _liveTerraformArmLabel;
            if (string.IsNullOrEmpty(arm))
                arm = "open world";

            var banner = microTotal > 0
                ? $"terraform {arm} {step}/{total} · micro {microLabel} {microStep}/{microTotal}"
                : $"terraform {arm} {step}/{total} · micro {microLabel}";

            CaveBuildStepCounter.PulseFlatGridTerraformMicroProgress(
                step,
                total,
                microLabel,
                microStep,
                microTotal);
            CaveBuildRunStatusPublisher.PulseSubOperation("FullWorld terraform", banner);
            CaveBuildHardwareMonitor.SetStepLabel(banner);
            if (tile != null)
            {
                SetLiveTerrainFocus(tile);
                var pulseCamera = microStep <= 1 ||
                                  microStep == microTotal ||
                                  microStep % 8 == 0;
                CaveBuildLiveSceneFeedback.NotifyTerrainTile(
                    tile,
                    banner,
                    ping: microStep <= 1,
                    forceCamera: pulseCamera);
            }
        }

        static void PulseTerraformTileMicro(
            FullWorldGridSession session,
            int index,
            Terrain tile,
            Vector2Int off,
            int microStep,
            int microTotal,
            string microLabel)
        {
            var step = index + 1;
            var total = session?.Offsets?.Length ?? _liveTerraformTotal;
            var armLabel = !string.IsNullOrEmpty(_liveTerraformArmLabel)
                ? _liveTerraformArmLabel
                : SurfaceFullWorldDirectionalBuild.ArmLabel(SurfaceFullWorldDirectionalBuild.ClassifyArm(off));
            PulseLiveTerraformMicro(microLabel, microStep, microTotal, tile, armLabel, step, total);
        }

        static bool TryResolveTerrainTileBounds(Terrain terrain, out Bounds bounds)
        {
            bounds = default;
            if (terrain?.terrainData == null)
                return false;

            bounds = new Bounds(
                terrain.transform.position + terrain.terrainData.bounds.center,
                terrain.terrainData.size);
            bounds.Expand(Vector3.one * Mathf.Max(bounds.size.x, bounds.size.z) * 0.18f);
            return true;
        }

        /// <summary>Call when a new FullWorld build session starts (before the first purge).</summary>
        public static void ResetFullWorldTerrainPurgeLatch()
        {
            _fullWorldTerrainPurgedThisBuild = false;
            _fullWorldGroundLaidThisBuild = false;
            _liveFullWorldGridPhase = FullWorldGridPhase.Research;
        }

        public static bool FullWorldTerrainPurgedThisBuild => _fullWorldTerrainPurgedThisBuild;

        public static bool FullWorldGroundTilesLaidThisBuild => _fullWorldGroundLaidThisBuild;

        /// <summary>Removes stale play + wilderness terrains after ladder invalidation (terrain-first FullWorld).</summary>
        public static int PurgeFullWorldTerrainForRebuild()
        {
            if (PipelineContentPreservePolicy.IntegrateOnlyActive)
            {
                _fullWorldTerrainPurgedThisBuild = true;
                CaveBuildEditorLog.LogSurface(
                    "[PipelineIntegrate] Skipped FullWorld terrain purge — repairing grid in place.",
                    forceUnityConsole: false);
                return 0;
            }

            var main = FindMainTerrainInScene();
            if (main?.terrainData != null &&
                SurfaceTerrainPlayRegion.CollectSurfaceTerrains(main).Count >= 9)
            {
                CaveBuildEditorLog.LogSurface(
                    "[PipelinePreserve] Skipped FullWorld terrain purge — play disk intact (repair only).",
                    forceUnityConsole: false);
                _fullWorldTerrainPurgedThisBuild = true;
                return 0;
            }

            var count = 0;
            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null)
                    continue;

                if (terrain.GetComponent<EnvironmentRoot>() != null)
                    continue;

                var name = terrain.name;
                if (name != MainTerrainName &&
                    !name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal) &&
                    !name.StartsWith(MountainFoothillTileNamePrefix, StringComparison.Ordinal) &&
                    !name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal) &&
                    !name.StartsWith(MountainHorizonTileNamePrefix, StringComparison.Ordinal) &&
                    !name.StartsWith(MountainWildernessTileNamePrefix, StringComparison.Ordinal) &&
                    name != "GeneratedTerrain" &&
                    name != CaveTerrainIntegrationUtility.IntegrationTerrainName)
                    continue;

                CaveEditorUndo.DestroyImmediate(terrain.gameObject);
                count++;
            }

            _fullWorldTerrainPurgedThisBuild = true;

            if (count > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Purged {count} stale terrain object(s) for FullWorld terrain-first rebuild.",
                    forceUnityConsole: true);
            }

            return count;
        }

        /// <summary>
        /// FullWorld must rebuild when ground is not on <see cref="MainTerrainName"/> or the 3×3 grid is broken/stacked.
        /// </summary>
        public static bool RequiresFullWorldTerrainFirstRebuild(SceneGroundInfo ground, WorldGenerationRequest request)
        {
            if (request == null || request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return false;

            if (SceneGroundResolver.IsModularKitBlockoutGrid(ground?.Anchor))
                return true;

            if (!CaveBuildPhaseContractRegistry.IsRungComplete(
                    CaveBuildPhaseContractRegistry.RungMacroTerrain,
                    request.Seed))
                return true;

            var main = FindMainTerrainInScene();
            if (main == null || main.terrainData == null)
                return true;

            if (ground?.Anchor != null)
            {
                var anchorTerrain = ground.Anchor.GetComponent<Terrain>();
                if (anchorTerrain != null && anchorTerrain != main)
                    return true;
            }

            if (!string.Equals(main.name, MainTerrainName, StringComparison.Ordinal))
                return true;

            if (SceneGroundResolver.IsInvalidFullWorldSurfaceGround(ground?.Anchor))
                return true;

            if (!IsPlayDiskWorldAxisAligned(main))
                return true;

            if (!IsPlayDiskAnchorAndSpacingValid(main, out _))
                return true;

            return !IsWildernessGridLayoutValid(main, out _);
        }

        static bool IsWildernessGridLayoutValid(Terrain mainTerrain, out string message)
        {
            message = string.Empty;
            if (mainTerrain?.terrainData == null)
                return true;

            var root = FindMountainWildernessRoot(mainTerrain);
            if (root == null)
                return true;

            var maxMismatch = 0f;
            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain || terrain.terrainData == null)
                    continue;

                if (!IsOuterRingTerrainName(terrain.name) ||
                    !TryParseOuterRingTileOffset(terrain.name, out var off))
                    continue;

                var pos = terrain.transform.position;
                var expected = ComputeGridSlotWorldOrigin(mainTerrain, off);
                maxMismatch = Mathf.Max(
                    maxMismatch,
                    new Vector2(pos.x - expected.x, pos.z - expected.z).magnitude);
            }

            if (maxMismatch <= 1.5f)
                return true;

            message = $"Wilderness grid misaligned — max {maxMismatch:F1}m off slot (expected contiguous 7×7).";
            return false;
        }

        static bool IsPlayDiskWorldAxisAligned(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return false;

            foreach (var tile in BuildPlayDiskTerrainGroup(mainTerrain))
            {
                if (tile == null)
                    continue;

                if (Vector3.Dot(tile.transform.up, Vector3.up) < 0.999f)
                    return false;
            }

            return true;
        }

        /// <summary>Removes duplicate play tiles outside the canonical <see cref="TilesRootName"/>.</summary>
        public static int PurgeOrphanPlayDiskTerrains(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return 0;

            var tilesRoot = FindTilesRoot(mainTerrain);
            var count = 0;
            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain)
                    continue;

                if (!IsPlayDiskTerrainName(terrain.name) ||
                    !TryParseTileOffset(terrain.name, out var off))
                    continue;

                if (tilesRoot != null && terrain.transform.IsChildOf(tilesRoot))
                    continue;

                if (tilesRoot != null)
                {
                    if (TryFindTileAtOffset(tilesRoot, off, out var existing) &&
                        existing != null &&
                        existing != terrain)
                    {
                        if (PipelineContentPreservePolicy.ShouldAllowDestroy(
                                terrain.gameObject,
                                "duplicate play disk tile"))
                        {
                            CaveEditorUndo.DestroyImmediate(terrain.gameObject);
                            count++;
                        }

                        continue;
                    }

                    terrain.transform.SetParent(tilesRoot, true);
                    continue;
                }

                if (PipelineContentPreservePolicy.ShouldAllowDestroy(
                        terrain.gameObject,
                        "orphan play disk tile"))
                {
                    CaveEditorUndo.DestroyImmediate(terrain.gameObject);
                    count++;
                }
            }

            if (count > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Removed {count} duplicate play terrain object(s); kept canonical {TilesRootName} slots.",
                    forceUnityConsole: true);
            }

            return count;
        }

        /// <summary>
        /// Collapses duplicate play/wilderness terrains onto canonical grid slots — removes scattered brown plates
        /// left from prior builds outside <see cref="TilesRootName"/> / <see cref="MountainWildernessTilesRootName"/>.
        /// </summary>
        public static int ConsolidateScatteredFullWorldTerrains(Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData == null)
                return 0;

            var removed = ConsolidateScatteredPlayNeighbors(mainTerrain);
            removed += ConsolidateScatteredWildernessTiles(mainTerrain);

            if (removed > 0)
            {
                EnforceWildernessGridLayout(mainTerrain);
                RefreshMountainTerrainConnectivity(mainTerrain);
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Consolidated scattered FullWorld terrains — removed {removed} duplicate/misplaced tile(s).",
                    forceUnityConsole: true);
            }

            return removed;
        }

        static int ConsolidateScatteredPlayNeighbors(Terrain mainTerrain)
        {
            var tilesRoot = FindTilesRoot(mainTerrain);
            var byOffset = new Dictionary<Vector2Int, List<Terrain>>();

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain || terrain.terrainData == null)
                    continue;

                if (!TryParseTileOffset(terrain.name, out var off))
                    continue;

                if (!IsFullWorldGridOffset(off))
                    continue;

                if (!byOffset.TryGetValue(off, out var list))
                {
                    list = new List<Terrain>(2);
                    byOffset[off] = list;
                }

                list.Add(terrain);
            }

            return CollapseTerrainSlotDuplicates(mainTerrain, tilesRoot, byOffset, ApplyPlayTileGridSlot);
        }

        static int ConsolidateScatteredWildernessTiles(Terrain mainTerrain)
        {
            var wildernessRoot = FindMountainWildernessRoot(mainTerrain);
            var byOffset = new Dictionary<Vector2Int, List<Terrain>>();
            var removed = 0;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain || terrain.terrainData == null)
                    continue;

                if (!IsOuterRingTerrainName(terrain.name))
                    continue;

                if (!TryParseOuterRingTileOffset(terrain.name, out var off) || !IsFullWorldGridOffset(off))
                {
                    CaveBuildEditorLog.LogSurfaceWarning(
                        $"[Surface] Keeping outer-ring terrain '{terrain.name}' — name did not map to a grid slot (no delete).",
                        forceUnityConsole: true);
                    continue;
                }

                if (!byOffset.TryGetValue(off, out var list))
                {
                    list = new List<Terrain>(2);
                    byOffset[off] = list;
                }

                list.Add(terrain);
            }

            return removed + CollapseTerrainSlotDuplicates(mainTerrain, wildernessRoot, byOffset, ApplyWildernessTileGridSlot);
        }

        static int CollapseTerrainSlotDuplicates(
            Terrain mainTerrain,
            Transform canonicalRoot,
            Dictionary<Vector2Int, List<Terrain>> byOffset,
            Action<Terrain, Terrain, Vector2Int> applyGridSlot)
        {
            var removed = 0;

            foreach (var kv in byOffset)
            {
                var off = kv.Key;
                var list = kv.Value;
                if (list.Count == 0)
                    continue;

                var expected = ComputeGridSlotWorldOrigin(mainTerrain, off);
                Terrain keeper = null;
                var bestScore = float.MaxValue;

                foreach (var tile in list)
                {
                    var score = ScoreTerrainKeeper(tile, expected, canonicalRoot);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        keeper = tile;
                    }
                }

                foreach (var tile in list)
                {
                    if (tile == keeper)
                        continue;

                    if (PipelineContentPreservePolicy.ShouldAllowDestroy(tile.gameObject, "duplicate grid slot tile"))
                    {
                        CaveEditorUndo.DestroyImmediate(tile.gameObject);
                        removed++;
                    }
                    else if (canonicalRoot != null)
                    {
                        tile.transform.SetParent(canonicalRoot, true);
                        tile.name = $"{tile.name}_PreservedMerge";
                    }
                }

                if (keeper == null)
                    continue;

                if (canonicalRoot != null && !keeper.transform.IsChildOf(canonicalRoot))
                    keeper.transform.SetParent(canonicalRoot, true);

                applyGridSlot(mainTerrain, keeper, off);
            }

            return removed;
        }

        static bool IsFullWorldGridOffset(Vector2Int off) =>
            Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)) <=
            Mathf.Max(FullWorldChebyshevRadius, SurfaceOpenWorldGridExpansion.GetBuiltChebyshevRadius());

        /// <summary>Removes or reparents foothill/peak tiles outside <see cref="MountainWildernessTilesRootName"/>.</summary>
        public static int PurgeOrphanWildernessTerrains(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return 0;

            var wildernessRoot = FindMountainWildernessRoot(mainTerrain);
            var tilesRoot = FindTilesRoot(mainTerrain);
            var removed = 0;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain)
                    continue;

                if (!IsOuterRingTerrainName(terrain.name) ||
                    !TryParseOuterRingTileOffset(terrain.name, out var off))
                    continue;

                if (wildernessRoot != null && terrain.transform.IsChildOf(wildernessRoot))
                {
                    ApplyWildernessTileGridSlot(mainTerrain, terrain, off);
                    continue;
                }

                if (wildernessRoot != null &&
                    !TryFindOuterRingTileAtOffset(wildernessRoot, off, null, out var existing))
                {
                    terrain.transform.SetParent(wildernessRoot, true);
                    ApplyWildernessTileGridSlot(mainTerrain, terrain, off);
                    ResolveWildernessRing(off, out _, out var ringId, out _);
                    SurfaceTerrainGridRegistry.ApplyIndex(
                        terrain,
                        ringId,
                        off,
                        ResolveWildernessMergeTargetId(mainTerrain, tilesRoot, wildernessRoot, off, terrain));
                    continue;
                }

                if (PipelineContentPreservePolicy.ShouldAllowDestroy(
                        terrain.gameObject,
                        "orphan wilderness terrain"))
                {
                    CaveEditorUndo.DestroyImmediate(terrain.gameObject);
                    removed++;
                }
                else if (wildernessRoot != null)
                {
                    terrain.transform.SetParent(wildernessRoot, true);
                    terrain.name = $"{terrain.name}_PreservedMerge";
                }
            }

            if (removed > 0 && !PipelineContentPreservePolicy.IntegrateOnlyActive)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Removed {removed} orphan wilderness terrain(s) — kept canonical grid slots.",
                    forceUnityConsole: true);
            }

            return removed;
        }

        static string ResolveWildernessMergeTargetId(
            Terrain mainTerrain,
            Transform tilesRoot,
            Transform wildernessRoot,
            Vector2Int off,
            Terrain tile)
        {
            var mergeOff = SurfaceTerrainGridRegistry.StepTowardOrigin(off);
            ResolveWildernessRing(off, out _, out var ringId, out _);
            var mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
            if (TryResolveTerrainAtGridOffset(mainTerrain, tilesRoot, wildernessRoot, mergeOff, out var mergeTerrain) &&
                mergeTerrain != null)
            {
                if (mergeTerrain == mainTerrain)
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                else if (TryParseTileOffset(mergeTerrain.name, out var mo))
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayNeighborRing, mo.x, mo.y);
                else if (TryParseOuterRingTileOffset(mergeTerrain.name, out var fo))
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(ringId, fo.x, fo.y);
            }

            return mergeId;
        }

        static bool IsPlayDiskTerrainName(string name) =>
            string.Equals(name, MainTerrainName, StringComparison.Ordinal) ||
            (name != null && name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal));

        /// <summary>Pre-neighbor checks: anchor validity, no stacked duplicates, aligned existing tiles.</summary>
        static bool IsPlayDiskAnchorAndSpacingValid(Terrain mainTerrain, out string message) =>
            ValidatePlayDiskGridLayout(mainTerrain, requireAllNeighbors: false, out message);

        /// <summary>Flat 3×3 grid: identity rotation, shared Y, neighbors on tile-size spacing.</summary>
        public static bool ValidatePlayDiskGridLayout(Terrain mainTerrain, out string message) =>
            ValidatePlayDiskGridLayout(mainTerrain, requireAllNeighbors: true, out message);

        static bool ValidatePlayDiskGridLayout(
            Terrain mainTerrain,
            bool requireAllNeighbors,
            out string message)
        {
            message = string.Empty;
            if (mainTerrain?.terrainData == null)
            {
                message = "No main terrain.";
                return false;
            }

            PurgeOrphanPlayDiskTerrains(mainTerrain);

            if (mainTerrain.transform.rotation != Quaternion.identity ||
                Vector3.Dot(mainTerrain.transform.up, Vector3.up) < 0.999f)
            {
                message = "Main terrain is tilted — play disk must be horizontal (check RouteTerrainFloor ground anchor).";
                return false;
            }

            var playCount = 0;
            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain != null && IsPlayDiskTerrainName(terrain.name))
                    playCount++;
            }

            if (playCount > 9)
            {
                message = $"Found {playCount} play terrains (expected ≤9) — duplicates/stacked tiles remain.";
                return false;
            }

            var tileSize = mainTerrain.terrainData.size;
            var mainOrigin = mainTerrain.transform.position;
            var baseY = mainOrigin.y;
            var maxYOffset = 0f;
            var tilesRoot = FindTilesRoot(mainTerrain);
            var neighborCount = 0;

            foreach (var off in NineTileRingOffsets)
            {
                if (!TryFindTileAtOffset(tilesRoot, off, out var neighbor) || neighbor == null)
                {
                    if (requireAllNeighbors)
                    {
                        message = $"Missing neighbor slot ({off.x},{off.y}).";
                        return false;
                    }

                    continue;
                }

                neighborCount++;

                if (neighbor.transform.rotation != Quaternion.identity)
                {
                    message = $"{neighbor.name} is rotated.";
                    return false;
                }

                var expected = mainOrigin + new Vector3(off.x * tileSize.x, 0f, off.y * tileSize.z);
                var delta = neighbor.transform.position - expected;
                if (new Vector2(delta.x, delta.z).magnitude > 1.5f)
                {
                    message = $"{neighbor.name} misaligned by {new Vector2(delta.x, delta.z).magnitude:F1}m.";
                    return false;
                }

                maxYOffset = Mathf.Max(maxYOffset, Mathf.Abs(neighbor.transform.position.y - baseY));
            }

            if (maxYOffset > 0.5f)
            {
                message = $"Play disk Y spread {maxYOffset:F1}m — tiles are stacked or stepped.";
                return false;
            }

            if (requireAllNeighbors && neighborCount < NineTileRingOffsets.Length)
            {
                message = $"Play disk incomplete — {neighborCount}/8 neighbors.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Snaps main + neighbors to a flat axis-aligned 3×3 at the main starter XZ (Y-only vertical sync to Ground).
        /// Reparents off tilted anchors (e.g. RouteTerrainFloor) so world rotation stays horizontal.
        /// </summary>
        public static void EnforcePlayDiskGridLayout(Terrain mainTerrain, SceneGroundInfo ground)
        {
            if (mainTerrain?.terrainData == null)
                return;

            EnsureMainTerrainIdentity(mainTerrain);

            var flatHost = GetOrCreateFlatTerrainHost();
            var playHost = EnsurePlayDiskHostUnderFlatHost(mainTerrain, flatHost);
            var tilesRoot = GetOrCreateTilesRoot(mainTerrain);
            ReparentStrayPlayTilesToTilesRoot(mainTerrain, tilesRoot);

            if (flatHost != null)
            {
                flatHost.rotation = Quaternion.identity;
                flatHost.localScale = Vector3.one;
            }

            if (playHost != null)
            {
                playHost.rotation = Quaternion.identity;
                playHost.localScale = Vector3.one;
            }

            if (tilesRoot != null)
            {
                tilesRoot.localRotation = Quaternion.identity;
                tilesRoot.localScale = Vector3.one;
            }

            var tileSize = mainTerrain.terrainData.size;
            var desiredMainOrigin = ResolvePlayDiskMainTerrainOrigin(ground, mainTerrain);
            var surfaceY = desiredMainOrigin.y +
                           SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight * tileSize.y;

            var group = BuildPlayDiskTerrainGroup(mainTerrain);
            foreach (var tile in group)
            {
                if (tile == null)
                    continue;

                tile.transform.rotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one;
            }

            var mainOrigin = mainTerrain.transform.position;
            var deltaY = desiredMainOrigin.y - mainOrigin.y;
            if (Mathf.Abs(deltaY) > 0.01f)
            {
                foreach (var tile in group)
                {
                    if (tile != null)
                        tile.transform.position += new Vector3(0f, deltaY, 0f);
                }

                mainOrigin = mainTerrain.transform.position;
            }

            var sharedY = mainOrigin.y;
            foreach (var off in NineTileRingOffsets)
            {
                if (!TryFindTileAtOffset(tilesRoot, off, out var neighbor) || neighbor == null)
                    continue;

                neighbor.transform.rotation = Quaternion.identity;
                neighbor.transform.localScale = Vector3.one;
                neighbor.transform.position = mainOrigin + new Vector3(off.x * tileSize.x, 0f, off.y * tileSize.z);
            }

            foreach (var tile in group)
            {
                if (tile == null || tile == mainTerrain)
                    continue;

                var p = tile.transform.position;
                tile.transform.position = new Vector3(p.x, sharedY, p.z);
            }

            RefreshTerrainConnectivity(mainTerrain, new List<Terrain>(CollectGameplayTiles(mainTerrain)));
            SnapWildernessAndPlayDiskToMainAnchor(mainTerrain);
            SurfaceTerrainGridRegistry.ReindexFromMain(mainTerrain, playDiskLocked: false);

            var neighborCount = CollectGameplayTiles(mainTerrain).Length;
            var centerXZ = ResolvePlayDiskCenterXZ(ground, mainTerrain);
            var anchorLabel = ground != null && ground.HasAnchor
                ? $"Ground '{ground.Anchor.name}'"
                : "fallback (no Ground)";
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Play disk flat — horizontal 3×3 ({1 + neighborCount} tile(s)) centered on {anchorLabel} " +
                $"at ({centerXZ.x:F0}, {centerXZ.z:F0}), Y≈{surfaceY:F1}m.",
                forceUnityConsole: true);
        }

        /// <summary>
        /// After the 3×3 play disk moves, move <see cref="FullWorldGridAnchorName"/> with it and re-seat all 49 grid slots
        /// (foothills + peaks included) so outer rings do not leave L-shaped ground gaps.
        /// </summary>
        public static void SnapWildernessAndPlayDiskToMainAnchor(Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData == null)
                return;

            EnsureMainTerrainIdentity(mainTerrain);
            var tilesRoot = FindTilesRoot(mainTerrain);
            var wildernessRoot = FindMountainWildernessRoot(mainTerrain);
            var anchor = FindFullWorldGridAnchor(mainTerrain);
            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            var mainPos = mainTerrain.transform.position;
            var desiredY = ResolveSharedTerrainOriginY(ground, mainTerrain);
            var originDelta = new Vector3(0f, desiredY - mainPos.y, 0f);
            if (originDelta.sqrMagnitude > 0.0001f)
            {
                foreach (var off in BuildFullWorldPlaceOrder())
                {
                    if (!TryResolveTerrainAtGridOffset(mainTerrain, tilesRoot, wildernessRoot, off, out var tile) ||
                        tile == null)
                        continue;
                    tile.transform.position += originDelta;
                }

                mainPos = mainTerrain.transform.position;
            }

            if (anchor != null)
            {
                anchor.SetPositionAndRotation(mainPos, Quaternion.identity);
                anchor.localScale = Vector3.one;

                if (mainTerrain.transform.parent != anchor)
                    mainTerrain.transform.SetParent(anchor, true);

                if (mainTerrain.transform.parent == anchor)
                {
                    mainTerrain.transform.localPosition = Vector3.zero;
                    mainTerrain.transform.localRotation = Quaternion.identity;
                    mainTerrain.transform.localScale = Vector3.one;
                }

                if (tilesRoot != null)
                {
                    tilesRoot.SetParent(anchor, false);
                    tilesRoot.localPosition = Vector3.zero;
                    tilesRoot.localRotation = Quaternion.identity;
                    tilesRoot.localScale = Vector3.one;
                }

                if (wildernessRoot != null)
                {
                    wildernessRoot.SetParent(anchor, false);
                    wildernessRoot.localPosition = Vector3.zero;
                    wildernessRoot.localRotation = Quaternion.identity;
                    wildernessRoot.localScale = Vector3.one;
                }
            }

            var moved = 0;
            foreach (var off in BuildFullWorldPlaceOrder())
            {
                if (!TryResolveTerrainAtGridOffset(mainTerrain, tilesRoot, wildernessRoot, off, out var tile) ||
                    tile == null)
                    continue;

                var before = tile.transform.position;
                if (off == Vector2Int.zero || IsNineTileGameplayOffset(off))
                    ApplyPlayTileGridSlot(mainTerrain, tile, off);
                else
                    ApplyWildernessTileGridSlot(mainTerrain, tile, off);

                if ((before - tile.transform.position).sqrMagnitude > 0.01f)
                    moved++;
            }

            RefreshMountainTerrainConnectivity(mainTerrain);

            if (moved > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    anchor != null
                        ? $"[Surface] FullWorld grid — re-snapped {moved} tile(s) to {FullWorldGridAnchorName} after play-disk move (foothills/peaks follow center 9)."
                        : $"[Surface] FullWorld grid — re-aligned {moved} outer-ring tile(s) to play disk.",
                    forceUnityConsole: true);
            }
        }

        /// <summary>Locked <see cref="FullWorldGridAnchorName"/> when the flat FullWorld host exists.</summary>
        public static Transform FindFullWorldGridAnchorPublic(Terrain mainTerrain) =>
            FindFullWorldGridAnchor(mainTerrain);

        /// <summary>Bounds of every terrain tile placed so far (play + wilderness + open world).</summary>
        public static bool TryResolvePlacedTerrainWorkBounds(out Bounds bounds, float paddingFraction = 0.22f)
        {
            bounds = default;
            var main = FindMainTerrainInScene();
            if (main == null)
                return false;

            var started = false;
            foreach (var terrain in CollectAllFullWorldTerrains(main))
            {
                if (terrain?.terrainData == null)
                    continue;

                var tileBounds = new Bounds(
                    terrain.transform.position + terrain.terrainData.bounds.center,
                    terrain.terrainData.size);
                if (!started)
                {
                    bounds = tileBounds;
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(tileBounds);
                }
            }

            if (!started)
                return false;

            var span = Mathf.Max(bounds.size.x, bounds.size.z);
            bounds.Expand(Vector3.one * Mathf.Max(span * paddingFraction, 96f));
            return true;
        }

        /// <summary>
        /// Live bounds for Scene camera: placed work area, with optional pivot bias toward the active sculpt tile.
        /// </summary>
        public static bool TryResolveLiveTerrainBounds(out Bounds bounds)
        {
            if (!TryResolvePlacedTerrainWorkBounds(out bounds))
                return false;

            if (_liveFocusTerrain != null && TryResolveTerrainTileBounds(_liveFocusTerrain, out var focusBounds))
                bounds.Encapsulate(focusBounds);

            return true;
        }

        /// <summary>World-space center of the active sculpt/terraform tile when set.</summary>
        public static bool TryResolveLiveTerrainFocusPivot(out Vector3 pivot)
        {
            pivot = default;
            if (_liveFocusTerrain == null || !TryResolveTerrainTileBounds(_liveFocusTerrain, out var focusBounds))
                return false;

            pivot = focusBounds.center;
            return true;
        }

        /// <summary>Bounds of the active sculpt/terraform tile only (tight Scene camera frame).</summary>
        public static bool TryResolveLiveTerrainFocusBounds(out Bounds bounds)
        {
            bounds = default;
            if (_liveFocusTerrain == null || !TryResolveTerrainTileBounds(_liveFocusTerrain, out bounds))
                return false;

            return true;
        }

        /// <summary>Full Chebyshev grid footprint (81-tile core or ~289 extended open world).</summary>
        public static bool TryResolveTargetGridBounds(Terrain mainTerrain, out Bounds bounds)
        {
            bounds = default;
            if (mainTerrain?.terrainData == null)
                return false;

            var radius = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                ? SurfaceOpenWorldGridExpansion.MaxChebyshevRadius
                : FullWorldChebyshevRadius;
            var tileSize = mainTerrain.terrainData.size;
            var minCorner = ComputeGridSlotWorldOrigin(mainTerrain, new Vector2Int(-radius, -radius));
            var maxCorner = ComputeGridSlotWorldOrigin(mainTerrain, new Vector2Int(radius, radius)) +
                            new Vector3(tileSize.x, tileSize.y, tileSize.z);
            var center = (minCorner + maxCorner) * 0.5f;
            var size = maxCorner - minCorner;
            bounds = new Bounds(center, size);
            return true;
        }

        /// <summary>
        /// One-shot recovery: consolidate duplicates, seat every FullWorld slot on the anchor, then re-index.
        /// Use after a partial build or when layout audit reports ~tile-size position gaps.
        /// </summary>
        public static void SnapFullWorldTerrainGridNow(Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData == null)
                return;

            PurgeOrphanPlayDiskTerrains(mainTerrain);
            ConsolidateScatteredFullWorldTerrains(mainTerrain);

            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            var session = new FullWorldGridSession
            {
                MainTerrain = mainTerrain,
                Ground = ground,
                PlaceOffsets = BuildFullWorldPlaceOrder(),
                TilesRoot = FindTilesRoot(mainTerrain),
                WildernessRoot = GetOrCreateMountainWildernessRoot(mainTerrain),
                GridAnchorLocked = true,
            };

            SyncFullWorldGridToGroundLevel(session);
            EnsureFullWorldGridAnchorTransform(session);
            ForceSnapEntireFullWorldGrid(session);
            EnforceWildernessGridLayout(mainTerrain);
            SnapWildernessAndPlayDiskToMainAnchor(mainTerrain);
            SurfaceTerrainGridRegistry.ReindexFromMain(mainTerrain, playDiskLocked: false);

            CaveBuildEditorLog.LogSurface(
                $"[Surface] FullWorld grid snap complete — all slots on {FullWorldGridAnchorName} (edge-to-edge).",
                forceUnityConsole: true);

            var surfaceRoot = GameObject.Find(SurfaceWorldPaths.RootName)?.transform;
            BiomeGrassScatterAuthor.EnsureBiomePropsScattered(
                mainTerrain,
                WorldGenerationRequest.LoadOrDefault(),
                surfaceRoot);
            LavaTubeCaveBuildPipeline.EnsurePlayDiskCenterSpawn(ground);
        }

        /// <summary>World origin for a grid slot (main at 0,0 plus tile-size spacing).</summary>
        public static Vector3 ComputeGridSlotWorldOrigin(Terrain mainTerrain, Vector2Int off) =>
            SurfaceTerrainGridRegistry.ExpectedOrigin(mainTerrain, off);

        /// <summary>Place a foothill/peak tile on its serialized grid slot before seed/stitch.</summary>
        public static void ApplyWildernessTileGridSlot(Terrain mainTerrain, Terrain tile, Vector2Int off)
        {
            if (mainTerrain?.terrainData == null || tile == null)
                return;

            if (TryApplyFullWorldGridSlotFromMain(mainTerrain, tile, off))
                return;

            var expected = ComputeGridSlotWorldOrigin(mainTerrain, off);
            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            var sharedY = ResolveSharedTerrainOriginY(ground, mainTerrain);
            tile.transform.rotation = Quaternion.identity;
            tile.transform.localScale = Vector3.one;
            tile.transform.position = new Vector3(expected.x, sharedY, expected.z);
        }

        static Transform EnsurePlayDiskHostUnderFlatHost(Terrain mainTerrain, Transform flatHost)
        {
            if (mainTerrain == null)
                return null;

            var host = mainTerrain.transform.parent;
            if (host == null)
            {
                var hostGo = new GameObject("EnvironmentTerrainHost");
                CaveEditorUndo.RegisterCreated(hostGo, "Play disk host");
                if (flatHost != null)
                    hostGo.transform.SetParent(flatHost, false);
                mainTerrain.transform.SetParent(hostGo.transform, false);
                return hostGo.transform;
            }

            if (flatHost != null && host != flatHost && !host.IsChildOf(flatHost))
                host.SetParent(flatHost, true);

            return host;
        }

        static void ReparentStrayPlayTilesToTilesRoot(Terrain mainTerrain, Transform tilesRoot)
        {
            if (mainTerrain == null || tilesRoot == null)
                return;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain)
                    continue;

                if (!IsPlayDiskGameplayTerrainPublic(terrain))
                    continue;

                if (terrain.transform.IsChildOf(tilesRoot))
                    continue;

                terrain.transform.SetParent(tilesRoot, true);
                terrain.transform.rotation = Quaternion.identity;
                terrain.transform.localScale = Vector3.one;
            }

            var playHost = tilesRoot.parent;
            if (playHost != null && mainTerrain.transform.parent != playHost)
                mainTerrain.transform.SetParent(playHost, true);
        }

        static float SamplePlayDiskSurfaceY(Terrain mainTerrain, SceneGroundInfo ground, Vector3 centerXZ)
        {
            if (ground != null && ground.HasAnchor &&
                ground.Anchor.GetComponent<Terrain>() == null)
                return ground.SurfaceY;

            var sample = new Vector3(centerXZ.x, 0f, centerXZ.z);
            if (mainTerrain != null)
                return mainTerrain.SampleHeight(sample) + mainTerrain.transform.position.y;

            if (ground?.Terrain != null)
                return ground.Terrain.SampleHeight(sample) + ground.Terrain.transform.position.y;

            return ground != null && ground.HasAnchor ? ground.SurfaceY : 0f;
        }

        static Transform GetOrCreateFlatTerrainHost()
        {
            return EnsureFlatTerrainHost();
        }

        /// <summary>Scene-root host for play disk + FullWorld grid terrains (not EnvironmentRoot).</summary>
        public static Transform EnsureFlatTerrainHost()
        {
            if (!SceneManager.GetActiveScene().IsValid())
                return null;

            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == FlatTerrainHostName)
                        return t;
                }
            }

            var go = new GameObject(FlatTerrainHostName);
            CaveEditorUndo.RegisterCreated(go, FlatTerrainHostName);
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        /// <summary>Pre-terrain defaults: flat host + grid anchor aligned to Ground tag (empty scene bootstrap).</summary>
        public static bool EnsureDefaultGridAnchors(SceneGroundInfo ground)
        {
            var flatHost = EnsureFlatTerrainHost();
            if (flatHost == null)
                return false;

            Transform gridAnchor = null;
            for (var i = 0; i < flatHost.childCount; i++)
            {
                var child = flatHost.GetChild(i);
                if (child.name == FullWorldGridAnchorName)
                {
                    gridAnchor = child;
                    break;
                }
            }

            var created = false;
            if (gridAnchor == null)
            {
                var go = new GameObject(FullWorldGridAnchorName);
                CaveEditorUndo.RegisterCreated(go, FullWorldGridAnchorName);
                go.transform.SetParent(flatHost, false);
                gridAnchor = go.transform;
                created = true;
            }

            var main = FindMainTerrainInScene();
            Vector3 origin;
            if (main != null && main.terrainData != null)
                origin = main.transform.position;
            else if (ground != null && ground.HasAnchor)
            {
                var height = ground.Terrain?.terrainData?.size.y ?? 80f;
                origin = new Vector3(
                    ground.AnchorWorld.x,
                    ground.SurfaceY - SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight * height,
                    ground.AnchorWorld.z);
            }
            else
            {
                origin = Vector3.zero;
            }

            gridAnchor.position = origin;
            gridAnchor.rotation = Quaternion.identity;
            gridAnchor.localScale = Vector3.one;
            flatHost.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            LogGridAnchorPlacement(ground, origin, main != null, created ? "created" : "updated");
            return created;
        }

        public static Vector3 ResolvePlayDiskCenterXZ(SceneGroundInfo ground, Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData != null)
            {
                var mainCenter = TileCenterWorld(mainTerrain);
                return new Vector3(mainCenter.x, 0f, mainCenter.z);
            }

            if (ground != null && ground.HasAnchor)
                return new Vector3(ground.Anchor.position.x, 0f, ground.Anchor.position.z);

            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth.sqrMagnitude > 0.01f)
                    return new Vector3(mouth.x, 0f, mouth.z);
            }

            var fallbackCenter = TileCenterWorld(mainTerrain);
            return new Vector3(fallbackCenter.x, 0f, fallbackCenter.z);
        }

        static void LogGridAnchorPlacement(
            SceneGroundInfo ground,
            Vector3 origin,
            bool hasMainTerrain,
            string action)
        {
            if (ground != null && ground.HasAnchor)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] {FullWorldGridAnchorName} {action} — centered on Ground '{ground.Anchor.name}' " +
                    $"(XZ=({ground.Anchor.position.x:F1}, {ground.Anchor.position.z:F1}), " +
                    $"surface Y≈{ground.SurfaceY:F1}m, main={(hasMainTerrain ? "yes" : "no")}).",
                    forceUnityConsole: true);
                return;
            }

            CaveBuildEditorLog.LogSurfaceWarning(
                $"[Surface] {FullWorldGridAnchorName} {action} — Ground fallback " +
                $"(origin=({origin.x:F1}, {origin.y:F1}, {origin.z:F1}), main={(hasMainTerrain ? "yes" : "no")}).");
        }

        /// <summary>
        /// Bottom-left world origin for the main play tile so flat 0.42 heightmap meets cave-mouth / Ground surface Y.
        /// </summary>
        public static Vector3 ResolvePlayDiskMainTerrainOrigin(SceneGroundInfo ground, Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData == null)
                return Vector3.zero;

            var tileSize = mainTerrain.terrainData.size;
            const float normalizedSurface = SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight;
            var centerXZ = ResolvePlayDiskCenterXZ(ground, mainTerrain);
            var surfaceY = SamplePlayDiskSurfaceY(mainTerrain, ground, centerXZ);
            return new Vector3(
                centerXZ.x - tileSize.x * 0.5f,
                surfaceY - normalizedSurface * tileSize.y,
                centerXZ.z - tileSize.z * 0.5f);
        }

        static float ResolveSharedTerrainOriginY(SceneGroundInfo ground, Terrain mainTerrain) =>
            ResolvePlayDiskMainTerrainOrigin(ground, mainTerrain).y;

        /// <summary>
        /// Shift main + every grid tile vertically so transform Y matches Ground surface (XZ stays on main starter).
        /// </summary>
        static void SyncFullWorldGridToGroundLevel(FullWorldGridSession session)
        {
            if (session?.MainTerrain?.terrainData == null)
                return;

            var ground = session.Ground ?? SceneGroundResolver.ResolveForFullWorld(session.MainTerrain.transform);
            var mainPos = session.MainTerrain.transform.position;
            var desiredY = ResolveSharedTerrainOriginY(ground, session.MainTerrain);
            var delta = new Vector3(0f, desiredY - mainPos.y, 0f);
            if (delta.sqrMagnitude < 0.0001f)
            {
                session.LockedMainOrigin = mainPos;
                return;
            }

            var tiles = CollectAllFullWorldTerrains(session.MainTerrain);
            foreach (var tile in tiles)
            {
                if (tile != null)
                    tile.transform.position += delta;
            }

            if (session.GridAnchor != null)
                session.GridAnchor.position += delta;

            session.LockedMainOrigin = session.MainTerrain.transform.position;
            session.GridAnchorLocked = true;

            CaveBuildEditorLog.LogSurface(
                $"[Surface] FullWorld grid aligned to play disk — {tiles.Length} tile(s) shifted " +
                $"Δ=({delta.x:F1},{delta.y:F1},{delta.z:F1})m (surface Y≈{desiredY + SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight * session.MainTerrain.terrainData.size.y:F1}m).",
                forceUnityConsole: true);
        }

        /// <summary>Sequential build order: cardinals (E→N→W→S) then diagonals for continuous 3×3 landscape.</summary>
        public static readonly Vector2Int[] NineTileRingOffsets =
        {
            new(1, 0),
            new(0, 1),
            new(-1, 0),
            new(0, -1),
            new(1, 1),
            new(-1, 1),
            new(-1, -1),
            new(1, -1),
        };

        public const int FoothillRingTileCount = 16;
        public const int PeakRingTileCount = 24;
        public const int FullWorldFlatGridPlaceBatchSize = 8;

        /// <summary>
        /// Flat 0.42 grid: skip per-tile seam queue (connectivity refresh at ring boundaries + post-grid weld).
        /// </summary>
        public static bool PreferSkipFlatGridPerTileSeams { get; set; } = true;

        /// <summary>
        /// FullWorld directional: sculpt/LiDAR every tile first; batch seam/lock only after terraform completes.
        /// </summary>
        public static bool PreferDeferSeamsUntilFullWorldTerraformComplete { get; set; } = true;

        public const int HorizonRingTileCount = 32;
        public const int MountainWildernessTileCount =
            FoothillRingTileCount + PeakRingTileCount + HorizonRingTileCount;
        public const int FullWorldChebyshevRadius = 4;
        public const int FullWorldTerrainTileCount = 9 + MountainWildernessTileCount;

        const string PrefSequentialFullWorldTerrain = "EnvironmentKit_SequentialFullWorldTerrain";

        /// <summary>
        /// Finish each tile's sculpt + seams + locks before the next; one heavy step per tile (no interleaved seam row queue).
        /// </summary>
        public static bool PreferSequentialFullWorldTerrain
        {
            get => EditorPrefs.GetBool(PrefSequentialFullWorldTerrain, true);
            set => EditorPrefs.SetBool(PrefSequentialFullWorldTerrain, value);
        }

        public const string MountainWildernessTilesRootName = "SurfaceMountainWildernessTiles";
        public const string MountainFoothillTileNamePrefix = "SurfaceMountainFoothillTile_";
        public const string MountainPeakTileNamePrefix = "SurfaceMountainPeakTile_";
        public const string MountainHorizonTileNamePrefix = "SurfaceMountainHorizonTile_";
        /// <summary>Chebyshev rings beyond <see cref="FullWorldChebyshevRadius"/> (incremental open world).</summary>
        public const string OpenWorldTileNamePrefix = "SurfaceOpenWorldTile_";
        /// <summary>Legacy prefix — removed on stale cleanup; treated as foothill for parsing.</summary>
        public const string MountainWildernessTileNamePrefix = "SurfaceMountainWildernessTile_";

        /// <summary>Chebyshev distance 2 — foothills merging to the nine-tile play disk.</summary>
        public static readonly Vector2Int[] FoothillRingOffsets = BuildChebyshevRingOffsets(2);

        /// <summary>Chebyshev distance 3 — peaks weaving into foothills.</summary>
        public static readonly Vector2Int[] PeakRingOffsets = BuildChebyshevRingOffsets(3);

        /// <summary>Chebyshev distance 4 — distant horizon massifs (9×9 world = 81 terrains).</summary>
        public static readonly Vector2Int[] HorizonRingOffsets = BuildChebyshevRingOffsets(4);

        /// <summary>Alias for foothill ring (backward compatible).</summary>
        public static readonly Vector2Int[] SixteenTileMountainRingOffsets = FoothillRingOffsets;

        public static int GetOuterRingChebyshevDistance(Vector2Int off) =>
            Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y));

        public static bool IsFoothillRingOffset(Vector2Int off) => GetOuterRingChebyshevDistance(off) == 2;

        public static bool IsPeakRingOffset(Vector2Int off) => GetOuterRingChebyshevDistance(off) == 3;

        public static bool IsHorizonRingOffset(Vector2Int off) => GetOuterRingChebyshevDistance(off) == 4;

        public static Vector2Int[] BuildChebyshevRingOffsets(int chebyshevDistance, int gridRadius = FullWorldChebyshevRadius)
        {
            var capacity = chebyshevDistance switch
            {
                2 => FoothillRingTileCount,
                3 => PeakRingTileCount,
                4 => HorizonRingTileCount,
                _ => Mathf.Max(8, chebyshevDistance * 8),
            };
            var list = new List<Vector2Int>(capacity);
            for (var x = -gridRadius; x <= gridRadius; x++)
            {
                for (var z = -gridRadius; z <= gridRadius; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != chebyshevDistance)
                        continue;
                    list.Add(new Vector2Int(x, z));
                }
            }

            return list.ToArray();
        }

        static bool IsEndOfChebyshevRingInPlaceOrder(Vector2Int[] placeOffsets, int globalIndex)
        {
            if (placeOffsets == null || globalIndex < 0 || globalIndex >= placeOffsets.Length)
                return false;

            var ring = GetOuterRingChebyshevDistance(placeOffsets[globalIndex]);
            return globalIndex == placeOffsets.Length - 1 ||
                   GetOuterRingChebyshevDistance(placeOffsets[globalIndex + 1]) != ring;
        }

        /// <summary>Center → ring 1 (9) → ring 2 (foothill) → ring 3 (peak) — each tile touches prior ring.</summary>
        public static Vector2Int[] BuildFullWorldPlaceOrder()
        {
            var list = new List<Vector2Int>(FullWorldTerrainTileCount) { Vector2Int.zero };
            for (var ring = 1; ring <= FullWorldChebyshevRadius; ring++)
                list.AddRange(BuildChebyshevRingOffsets(ring));

            return list.ToArray();
        }

        static string FullWorldPlaceRingLabel(Vector2Int off)
        {
            var ring = GetOuterRingChebyshevDistance(off);
            return ring switch
            {
                0 => "center (Ground)",
                1 => "play disk",
                2 => "foothill ring",
                3 => "peak ring",
                4 => "horizon ring",
                _ => $"ring {ring}",
            };
        }

        public static void ResolveWildernessRing(
            Vector2Int off,
            out string tilePrefix,
            out string gridRingId,
            out SurfaceOuterRingMountainsAuthor.OuterRingSculptTier sculptTier)
        {
            if (GetOuterRingChebyshevDistance(off) > FullWorldChebyshevRadius)
            {
                tilePrefix = OpenWorldTileNamePrefix;
                var openRing = GetOuterRingChebyshevDistance(off);
                gridRingId = openRing >= SurfaceOpenWorldGridExpansion.MaxChebyshevRadius - 1
                    ? SurfaceTerrainGridIndex.HorizonRing
                    : openRing >= FullWorldChebyshevRadius + 2
                        ? SurfaceTerrainGridIndex.PeakRing
                        : SurfaceTerrainGridIndex.FoothillRing;
                sculptTier = openRing >= SurfaceOpenWorldGridExpansion.MaxChebyshevRadius
                    ? SurfaceOuterRingMountainsAuthor.OuterRingSculptTier.Horizon
                    : openRing >= FullWorldChebyshevRadius + 2
                        ? SurfaceOuterRingMountainsAuthor.OuterRingSculptTier.Peak
                        : SurfaceOuterRingMountainsAuthor.OuterRingSculptTier.Foothill;
                return;
            }

            if (IsHorizonRingOffset(off))
            {
                tilePrefix = MountainHorizonTileNamePrefix;
                gridRingId = SurfaceTerrainGridIndex.HorizonRing;
                sculptTier = SurfaceOuterRingMountainsAuthor.OuterRingSculptTier.Horizon;
                return;
            }

            if (IsPeakRingOffset(off))
            {
                tilePrefix = MountainPeakTileNamePrefix;
                gridRingId = SurfaceTerrainGridIndex.PeakRing;
                sculptTier = SurfaceOuterRingMountainsAuthor.OuterRingSculptTier.Peak;
                return;
            }

            tilePrefix = MountainFoothillTileNamePrefix;
            gridRingId = SurfaceTerrainGridIndex.FoothillRing;
            sculptTier = SurfaceOuterRingMountainsAuthor.OuterRingSculptTier.Foothill;
        }

        public static bool IsNineTileGameplayOffset(Vector2Int off) =>
            Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)) <= 1;

        /// <summary>Center tile (0,0) + 8 neighbors — flat hometown; terraform sculpt skips these slots.</summary>
        public static bool IsHometownPlayDiskOffset(Vector2Int off) => IsNineTileGameplayOffset(off);

        public static bool UsesFixedNineTileSquare(WorldGenerationRequest request, bool fullWorld) =>
            request != null && (request.ForceNineTileSquareGrid || fullWorld);

        public readonly struct GameplayTileEntry
        {
            public readonly Terrain Tile;
            public readonly Vector2Int Offset;

            public GameplayTileEntry(Terrain tile, Vector2Int offset)
            {
                Tile = tile;
                Offset = offset;
            }
        }

        public static int TryAttachGameplayTiles(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            out string message,
            Action onSculptComplete = null)
        {
            message = string.Empty;
            if (mainTerrain == null || ground == null || !ground.HasAnchor || request == null)
                return 0;

            var extent = EnvironmentKitHardwareBudget.ClampSurfaceExtent(
                Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f));
            var fullWorld = request.SurfaceScope == SurfaceBuildScope.FullWorld;

            if (!fullWorld && extent < 200f)
            {
                message = "Tile expansion skipped — play extent under 200m.";
                return 0;
            }

            var score = ScoreExpansionNeed(mainTerrain, ground, request, extent);
            const float minScore = 0.40f;
            if (!fullWorld && score < minScore)
            {
                message = $"Tile expansion skipped — gameplay score {score:F2} (need {minScore:F2}+).";
                return 0;
            }

            var data = mainTerrain.terrainData;
            if (data == null)
                return 0;

            EnsureMainTerrainIdentity(mainTerrain);

            var tileSize = data.size;
            var mainOrigin = mainTerrain.transform.position;
            var tilesRoot = GetOrCreateTilesRoot(mainTerrain);
            var count = 0;
            var offsets = NeighborOffsets(score, fullWorld, request, request.SurfaceTileLayoutVariant, request.Seed);
            var added = new List<Terrain>();
            var entries = new List<GameplayTileEntry>();

            RemoveStaleGameplayTiles(tilesRoot);

            var tileCap = ResolveTileCap(fullWorld, request);
            var skipped = 0;
            for (var i = 0; i < offsets.Length && count < tileCap; i++)
            {
                var off = offsets[i];
                if (TryFindTileAtOffset(tilesRoot, off, out _))
                    continue;

                var pos = mainOrigin + new Vector3(off.x * tileSize.x, 0f, off.y * tileSize.z);
                if (!fullWorld && SlotBlockedByForeignTerrain(pos, tileSize, mainTerrain, tilesRoot))
                {
                    skipped++;
                    continue;
                }

                var tileData = CreateFreshTileData(data);
                tileData.name = $"SurfaceTileData_{off.x}_{off.y}";
                var go = Terrain.CreateTerrainGameObject(tileData);
                go.name = $"SurfaceTerrainTile_{off.x}_{off.y}";
                CaveEditorUndo.RegisterCreated(go, "Surface terrain tile");
                TryTagAsGround(go);
                go.transform.SetParent(tilesRoot, false);
                go.transform.position = pos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var tile = go.GetComponent<Terrain>();
                if (tile == null)
                    continue;

                SeedHeightsFromSharedEdges(tile, mainTerrain, off, mainTerrain);
                added.Add(tile);
                entries.Add(new GameplayTileEntry(tile, off));
                count++;

                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Created {go.name} at ({pos.x:F0}, {pos.z:F0}) — size {tileSize.x:F0}×{tileSize.z:F0}m.",
                    forceUnityConsole: true);
            }

            if (added.Count > 0)
            {
                RefreshTerrainConnectivity(mainTerrain, added);
                message =
                    $"Attached {count} neighbor terrain tile(s) under {TilesRootName} (main + {count} = {count + 1} terrains).";
                if (skipped > 0)
                    message += $" Skipped {skipped} blocked slot(s).";
                onSculptComplete?.Invoke();
            }
            else
            {
                message = fullWorld
                    ? $"FullWorld tile expansion failed — 0/{offsets.Length} slots created (check terrain size/position). Main at ({mainOrigin.x:F0},{mainOrigin.z:F0})."
                    : $"No free slots for extra terrain tiles ({skipped} blocked).";
                onSculptComplete?.Invoke();
            }

            CaveBuildEditorLog.LogSurface(message, forceUnityConsole: true);
            return count;
        }

        sealed class AttachTilesSession
        {
            public Terrain MainTerrain;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public Vector3 TileSize;
            public Vector3 MainOrigin;
            public Transform TilesRoot;
            public Vector2Int[] Offsets;
            public int OffsetIndex;
            public int TileCap;
            public int Count;
            public int Skipped;
            public bool FullWorld;
            public bool WaitingForSeed;
            public readonly List<Terrain> Added = new();
            public Action<int, string> OnComplete;
        }

        sealed class SeedHeightsSession
        {
            public Terrain Tile;
            public Terrain Main;
            public Terrain ConnectivityMain;
            public Vector2Int Off;
            public float[,] Heights;
            public int Res;
            public int Band;
            public int RowY;
            public int ProjectRow;
            public float PlayMinX;
            public float PlayMaxX;
            public float PlayMinZ;
            public float PlayMaxZ;
            public Action OnComplete;
        }

        enum SeedHeightsStep
        {
            PrepareEdges,
            UploadRows,
            Done,
        }

        static int SeedRowChunk(int res)
        {
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return res >= 1025 ? 64 : res >= 513 ? 128 : 128;
            return res >= 1025 ? 8 : res >= 513 ? 32 : 32;
        }

        /// <summary>
        /// Creates at most one neighbor terrain per editor frame (avoids freeze after peak normalize on FullWorld).
        /// </summary>
        public static void QueueAttachGameplayTiles(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action<int, string> onComplete)
        {
            if (onComplete == null)
                return;

            if (mainTerrain == null || ground == null || !ground.HasAnchor || request == null)
            {
                onComplete(0, "Tile expansion skipped — missing terrain or ground.");
                return;
            }

            var extent = EnvironmentKitHardwareBudget.ClampSurfaceExtent(
                Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f));
            var fullWorld = request.SurfaceScope == SurfaceBuildScope.FullWorld;
            if (!fullWorld && extent < 200f)
            {
                onComplete(0, "Tile expansion skipped — play extent under 200m.");
                return;
            }

            var score = ScoreExpansionNeed(mainTerrain, ground, request, extent);
            if (!fullWorld && score < 0.40f)
            {
                onComplete(0, $"Tile expansion skipped — gameplay score {score:F2}.");
                return;
            }

            var data = mainTerrain.terrainData;
            if (data == null)
            {
                onComplete(0, "Tile expansion skipped — no terrain data.");
                return;
            }

            EnsureMainTerrainIdentity(mainTerrain);
            var tilesRoot = GetOrCreateTilesRoot(mainTerrain);
            RemoveStaleGameplayTiles(tilesRoot);

            var offsets = NeighborOffsets(score, fullWorld, request, request.SurfaceTileLayoutVariant, request.Seed);
            var session = new AttachTilesSession
            {
                MainTerrain = mainTerrain,
                Ground = ground,
                Request = request,
                TileSize = data.size,
                MainOrigin = mainTerrain.transform.position,
                TilesRoot = tilesRoot,
                Offsets = offsets,
                FullWorld = fullWorld,
                TileCap = ResolveTileCap(fullWorld, request),
                OnComplete = onComplete,
            };

            CaveBuildEditorLog.LogSurface(
                request != null && UsesFixedNineTileSquare(request, fullWorld)
                    ? "[Surface] Nine-tile square queued — 8 neighbors in ring order, one tile per frame."
                    : $"[Surface] Neighbor tiles queued — up to {session.TileCap} slot(s), one tile + paced seam seed per slot.",
                forceUnityConsole: true);

            QueueRemoveStaleTilesPaced(tilesRoot, () =>
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunAttachTilesFrame(session)));
        }

        static void RunAttachTilesFrame(AttachTilesSession session)
        {
            if (session?.MainTerrain == null)
            {
                session?.OnComplete?.Invoke(0, "Tile expansion aborted.");
                return;
            }

            if (session.WaitingForSeed)
                return;

            if (session.FullWorld)
            {
                session.MainOrigin = session.MainTerrain.transform.position;
                session.TileSize = session.MainTerrain.terrainData.size;
            }

            var createdThisFrame = false;
            while (session.OffsetIndex < session.Offsets.Length &&
                   session.Count < session.TileCap &&
                   !createdThisFrame)
            {
                var off = session.Offsets[session.OffsetIndex++];
                if (TryFindTileAtOffset(session.TilesRoot, off, out _))
                    continue;

                var pos = session.FullWorld
                    ? ComputeGridSlotWorldOrigin(session.MainTerrain, off)
                    : session.MainOrigin + new Vector3(
                        off.x * session.TileSize.x,
                        0f,
                        off.y * session.TileSize.z);
                if (!session.FullWorld && SlotBlockedByForeignTerrain(
                        pos,
                        session.TileSize,
                        session.MainTerrain,
                        session.TilesRoot))
                {
                    session.Skipped++;
                    continue;
                }

                var tileData = CreateFreshTileData(session.MainTerrain.terrainData);
                tileData.name = $"SurfaceTileData_{off.x}_{off.y}";
                var go = Terrain.CreateTerrainGameObject(tileData);
                go.name = $"SurfaceTerrainTile_{off.x}_{off.y}";
                CaveEditorUndo.RegisterCreated(go, "Surface terrain tile");
                TryTagAsGround(go);
                go.transform.SetParent(session.TilesRoot, false);
                go.transform.position = pos;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var tile = go.GetComponent<Terrain>();
                if (tile == null)
                    continue;

                var mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                if (SurfaceTerrainTileExpansion.TryFindTileAtOffset(session.TilesRoot, SurfaceTerrainGridRegistry.StepTowardOrigin(off), out var mergeTile) &&
                    mergeTile != null)
                {
                    var mergeOff = SurfaceTerrainGridRegistry.StepTowardOrigin(off);
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(
                        mergeOff == Vector2Int.zero
                            ? SurfaceTerrainGridIndex.PlayMainRing
                            : SurfaceTerrainGridIndex.PlayNeighborRing,
                        mergeOff.x,
                        mergeOff.y);
                }

                SurfaceTerrainGridRegistry.ApplyIndex(
                    tile,
                    SurfaceTerrainGridIndex.PlayNeighborRing,
                    off,
                    mergeId);

                session.WaitingForSeed = true;
                createdThisFrame = true;
                var tileNum = session.Count + 1;
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Created {go.name} — seeding seams from {mergeId} ({tileNum}/{session.TileCap})…",
                    forceUnityConsole: true);
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    $"[Surface] neighbor tile {tileNum}/{session.TileCap} — seam seed",
                    0.86f);
                QueueSeedHeightsFromSharedEdges(
                    tile,
                    session.MainTerrain,
                    off,
                    session.MainTerrain,
                    () => QueueStitchPlayNeighborAfterSeed(session, tile, off, go.name, tileNum));
                return;
            }

            if (session.OffsetIndex < session.Offsets.Length && session.Count < session.TileCap)
            {
                EditorUtility.DisplayProgressBar(
                    "Environment Kit",
                    $"[Surface] neighbor tile {session.Count}/{session.TileCap}",
                    0.86f);
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunAttachTilesFrame(session));
                return;
            }

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                "[Surface] neighbor tiles — linking…",
                0.87f);

            if (session.Added.Count > 0)
                RefreshTerrainConnectivity(session.MainTerrain, session.Added);

            if (session.FullWorld && session.Count > 0)
                EnforcePlayDiskGridLayout(session.MainTerrain, session.Ground);

            if (session.FullWorld && session.Count >= NineTileRingOffsets.Length &&
                !SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild)
            {
                var preStitch = StitchAllPlayDiskSeamsSync(session.MainTerrain);
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Play disk pre-stitch after spawn — {preStitch} tile(s) (diagonal + cardinal).",
                    forceUnityConsole: false);
            }
            else if (session.FullWorld && session.Count >= NineTileRingOffsets.Length)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Skipping play disk pre-stitch — main has authoritative LiDAR; seam lock runs after neighbor DEM.",
                    forceUnityConsole: false);
            }

            SurfaceTerrainGridRegistry.ReindexFromMain(session.MainTerrain, playDiskLocked: false);

            var message = session.Count > 0
                ? $"Attached {session.Count} neighbor terrain tile(s) under {TilesRootName} (paced, one per frame)."
                : session.FullWorld
                    ? "FullWorld tile expansion — no new slots this pass."
                    : $"No free slots ({session.Skipped} blocked).";
            if (session.Skipped > 0)
                message += $" Skipped {session.Skipped} blocked slot(s).";

            CaveBuildEditorLog.LogSurface(message, forceUnityConsole: true);
            CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
            {
                EditorUtility.ClearProgressBar();
                session.OnComplete?.Invoke(session.Count, message);
            });
        }

        static void QueueStitchPlayNeighborAfterSeed(
            AttachTilesSession session,
            Terrain tile,
            Vector2Int off,
            string tileName,
            int tileNum)
        {
            if (session?.MainTerrain == null || tile == null)
            {
                session.WaitingForSeed = false;
                CaveBuildActionPacing.ScheduleLight(
                    () => RunAttachTilesFrame(session),
                    CaveBuildPipelineDomains.QueueLabel("neighbor tile — next"));
                return;
            }

            var playGroup = BuildPlayDiskTerrainGroup(session.MainTerrain);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] {tileName} — seam stitch after place ({tileNum}/{session.TileCap})…",
                forceUnityConsole: false);
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] neighbor tile {tileNum}/{session.TileCap} — seam",
                0.86f);
            QueueStitchTileSeamsPaced(
                tile,
                session.MainTerrain,
                off,
                playGroup,
                () =>
                {
                    session.WaitingForSeed = false;
                    session.Added.Add(tile);
                    session.Count++;
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] {tileName} seam stitch done ({session.Count}/{session.TileCap}).",
                        forceUnityConsole: false);
                    CaveBuildActionPacing.ScheduleLight(
                        () => RunAttachTilesFrame(session),
                        CaveBuildPipelineDomains.QueueLabel("neighbor tile — next"));
                });
        }

        static void QueueStitchWildernessAfterSeed(
            AttachOuterRingSession session,
            Terrain tile,
            Action onComplete)
        {
            if (session?.MainTerrain == null || tile == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildRunStatusPublisher.PulseSubOperation(
                $"mountain {session.RingLabel} tiles",
                $"seam after place {tile.name}");
            QueueStitchSingleMountainWildernessTile(session.MainTerrain, tile, () =>
            {
                onComplete?.Invoke();
            });
        }

        public static void EnsureMainTerrainIdentity(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return;

            // Never rename EnvironmentRoot — purge + collider bounds assume that GO stays named.
            if (mainTerrain.GetComponent<EnvironmentRoot>() != null)
                return;

            if (!mainTerrain.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal) &&
                mainTerrain.name != MainTerrainName)
            {
                mainTerrain.name = MainTerrainName;
                CaveEditorUndo.RecordObject(mainTerrain.gameObject, "Name main surface terrain");
            }

            TryTagAsGround(mainTerrain.gameObject);
            SurfaceTerrainGridRegistry.ApplyIndex(
                mainTerrain,
                SurfaceTerrainGridIndex.PlayMainRing,
                Vector2Int.zero,
                string.Empty);
        }

        static void TryTagAsGround(GameObject go)
        {
            if (go == null)
                return;

            foreach (var tag in InternalEditorUtility.tags)
            {
                if (!string.Equals(tag, "Ground", StringComparison.Ordinal))
                    continue;

                go.tag = "Ground";
                return;
            }
        }

        static Transform GetOrCreateTilesRoot(Terrain mainTerrain)
        {
            var host = mainTerrain.transform.parent;
            if (host == null)
            {
                var hostGo = new GameObject("EnvironmentTerrainHost");
                CaveEditorUndo.RegisterCreated(hostGo, "Terrain host");
                mainTerrain.transform.SetParent(hostGo.transform, false);
                host = hostGo.transform;
            }

            var existing = host.Find(TilesRootName);
            if (existing != null)
                return existing;

            var rootGo = new GameObject(TilesRootName);
            CaveEditorUndo.RegisterCreated(rootGo, TilesRootName);
            rootGo.transform.SetParent(host, false);
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            return rootGo.transform;
        }

        public static bool TryFindTileAtOffset(Transform tilesRoot, Vector2Int off, out Terrain tile)
        {
            tile = null;
            if (tilesRoot == null)
                return false;

            var expected = $"SurfaceTerrainTile_{off.x}_{off.y}";
            for (var i = 0; i < tilesRoot.childCount; i++)
            {
                var child = tilesRoot.GetChild(i);
                if (child.name != expected)
                    continue;

                tile = child.GetComponent<Terrain>();
                if (tile != null)
                    return true;
            }

            return false;
        }

        /// <summary>Neighbor tiles created by <see cref="TryAttachGameplayTiles"/> (not the main Ground-centered tile).</summary>
        public static Terrain[] CollectGameplayTiles(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return System.Array.Empty<Terrain>();

            var tilesRoot = FindTilesRoot(mainTerrain);
            if (tilesRoot == null)
                return System.Array.Empty<Terrain>();

            var tiles = new List<Terrain>(MaxExtraTiles);
            for (var i = 0; i < tilesRoot.childCount; i++)
            {
                var child = tilesRoot.GetChild(i);
                if (!child.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
                    continue;

                var tile = child.GetComponent<Terrain>();
                if (tile != null && tile.terrainData != null)
                    tiles.Add(tile);
            }

            return tiles.ToArray();
        }

        public static Transform FindTilesRootPublic(Terrain mainTerrain) => FindTilesRoot(mainTerrain);

        public static Transform FindMountainWildernessRootPublic(Terrain mainTerrain) =>
            FindMountainWildernessRoot(mainTerrain);

        static Transform FindTilesRoot(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return null;

            var parent = mainTerrain.transform.parent;
            if (parent != null)
            {
                var underParent = parent.Find(TilesRootName);
                if (underParent != null)
                    return underParent;
            }

            return mainTerrain.transform.Find(TilesRootName);
        }

        /// <summary>Gameplay neighbors with valid <c>SurfaceTerrainTile_x_y</c> names (excludes misnamed children).</summary>
        public static Terrain[] CollectValidatedGameplayTiles(Terrain mainTerrain, out List<string> misnamed)
        {
            misnamed = new List<string>();
            if (mainTerrain == null)
                return Array.Empty<Terrain>();

            var tilesRoot = FindTilesRoot(mainTerrain);
            if (tilesRoot == null)
                return Array.Empty<Terrain>();

            var tiles = new List<Terrain>(MaxExtraTiles);
            for (var i = 0; i < tilesRoot.childCount; i++)
            {
                var child = tilesRoot.GetChild(i);
                if (!child.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
                    continue;

                var tile = child.GetComponent<Terrain>();
                if (tile == null || tile.terrainData == null)
                    continue;

                if (!TryParseTileOffset(tile.name, out _))
                {
                    misnamed.Add(child.name);
                    continue;
                }

                tiles.Add(tile);
            }

            return tiles.ToArray();
        }

        /// <summary>World XZ center of a terrain tile (for per-tile LiDAR UV, not Ground-anchor UV).</summary>
        public static Vector3 TileCenterWorld(Terrain tile)
        {
            if (tile == null || tile.terrainData == null)
                return tile != null ? tile.transform.position : Vector3.zero;

            var pos = tile.transform.position;
            var size = tile.terrainData.size;
            return new Vector3(pos.x + size.x * 0.5f, pos.y, pos.z + size.z * 0.5f);
        }

        /// <summary>
        /// Re-sculpts existing gameplay neighbors (centered passes + per-tile LiDAR). Used when tiles already exist.
        /// </summary>
        public static int StampGameplayTiles(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            SceneGroundInfo ground)
        {
            if (mainTerrain == null || request == null)
                return 0;

            var tiles = CollectGameplayTiles(mainTerrain);
            if (tiles.Length == 0)
                return 0;

            var entries = new List<GameplayTileEntry>(tiles.Length);
            foreach (var tile in tiles)
            {
                if (!TryParseTileOffset(tile.name, out var off))
                    continue;
                entries.Add(new GameplayTileEntry(tile, off));
            }

            if (entries.Count == 0)
                return 0;

            var sculpted = 0;
            foreach (var entry in entries)
            {
                if (SculptGameplayTileSync(mainTerrain, ground, request, entry))
                    sculpted++;
            }

            if (sculpted > 0)
                RefreshTerrainConnectivity(mainTerrain, new List<Terrain>(tiles));

            return sculpted;
        }

        /// <summary>
        /// Paced per-tile sculpt (centered rings → LiDAR at tile center → seam stitch), one tile per queue chain.
        /// </summary>
        public static void QueueSculptGameplayTiles(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            IReadOnlyList<GameplayTileEntry> tiles,
            Action onComplete)
        {
            if (mainTerrain == null || request == null || tiles == null || tiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            QueueSculptTileAtIndex(mainTerrain, ground, request, tiles, 0, onComplete);
        }

        /// <summary>Legacy entry — routes to unified world polish (one Florida DEM pass for all neighbors, no per-tile circles).</summary>
        public static void QueueBackgroundNeighborSculpt(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request) =>
            QueueUnifiedSurfaceWorldPolish(mainTerrain, ground, request, null);

        /// <summary>
        /// Edge-match neighbor tiles to main + each other. Does not re-stamp DEM or full-tile smooth (preserves LiDAR + seam seed).
        /// </summary>
        /// <summary>Main + up to 8 neighbors for seam stitching (play disk only).</summary>
        public static Terrain[] BuildPlayDiskTerrainGroup(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return Array.Empty<Terrain>();

            var neighbors = CollectGameplayTiles(mainTerrain);
            var group = new Terrain[1 + neighbors.Length];
            group[0] = mainTerrain;
            Array.Copy(neighbors, 0, group, 1, neighbors.Length);
            return group;
        }

        public static bool TryParsePlayDiskGridOffset(Terrain mainTerrain, Terrain tile, out Vector2Int off)
        {
            off = default;
            if (tile == null)
                return false;

            if (tile == mainTerrain || tile.name == MainTerrainName)
            {
                off = Vector2Int.zero;
                return true;
            }

            return TryParseTileOffset(tile.name, out off);
        }

        /// <summary>Stitch every edge on the 3×3 play disk (main + neighbors), including main↔neighbor and neighbor↔neighbor.</summary>
        public static int StitchAllPlayDiskSeamsSync(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return 0;

            var group = BuildPlayDiskTerrainGroup(mainTerrain);
            if (group.Length <= 1)
                return 0;

            var stitched = 0;
            foreach (var tile in group)
            {
                if (tile?.terrainData == null || !TryParsePlayDiskGridOffset(mainTerrain, tile, out var off))
                    continue;

                StitchTileSeams(tile, mainTerrain, off, group);
                tile.Flush();
                stitched++;
            }

            RefreshTerrainConnectivity(mainTerrain, new List<Terrain>(group));
            return stitched;
        }

        public static int StitchAllNeighborSeamsSync(Terrain mainTerrain) => StitchAllPlayDiskSeamsSync(mainTerrain);

        public static void QueueStitchNeighborSeamsOnly(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var group = BuildPlayDiskTerrainGroup(mainTerrain);
            if (group.Length <= 1)
            {
                onComplete?.Invoke();
                return;
            }

            QueueStitchNeighborSeamAtIndex(mainTerrain, group, 0, 0, onComplete);
        }

        static void QueueStitchNeighborSeamAtIndex(
            Terrain mainTerrain,
            Terrain[] group,
            int index,
            int stitched,
            Action onComplete)
        {
            if (index >= group.Length)
            {
                if (stitched > 0)
                {
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Play-disk seam stitch ({stitched} tile(s), paced) — no per-tile DEM/smooth.",
                        forceUnityConsole: true);
                }

                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "surface finish",
                    "terrain connectivity (paced)");
                var neighbors = CollectGameplayTiles(mainTerrain);
                QueueRefreshTerrainConnectivity(
                    mainTerrain,
                    neighbors,
                    () =>
                    {
                        EnvironmentKitHardwareBudget.OnQueueStepCompleted();
                        onComplete?.Invoke();
                    });
                return;
            }

            var tile = group[index];
            if (tile == null || !TryParsePlayDiskGridOffset(mainTerrain, tile, out var off))
            {
                CaveBuildActionPacing.ScheduleNextEditorFrame(
                    () => QueueStitchNeighborSeamAtIndex(mainTerrain, group, index + 1, stitched, onComplete));
                return;
            }

            var tileNum = index + 1;
            var total = group.Length;
            var label = tile == mainTerrain ? MainTerrainName : tile.name;
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "surface finish",
                $"seam stitch {tileNum}/{total} ({label})");
            QueueStitchTileSeamsPaced(
                tile,
                mainTerrain,
                off,
                group,
                () =>
                {
                    CaveBuildActionPacing.ScheduleHeavyChain(
                        () => QueueStitchNeighborSeamAtIndex(
                            mainTerrain,
                            group,
                            index + 1,
                            stitched + 1,
                            onComplete),
                        CaveBuildPipelineDomains.SurfaceQueueLabel($"seam stitch {tileNum + 1}/{total}"));
                });
        }

        /// <summary>One terrain per editor frame — avoids 30–60s hitch from SetNeighbors × 9 + SetConnectivityDirty.</summary>
        public static void QueueRefreshTerrainConnectivity(
            Terrain mainTerrain,
            Terrain[] neighbors,
            Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var group = new List<Terrain>(neighbors != null ? neighbors.Length + 1 : 1) { mainTerrain };
            if (neighbors != null)
            {
                foreach (var t in neighbors)
                {
                    if (t != null && t != mainTerrain)
                        group.Add(t);
                }
            }

            QueueRefreshConnectivityAtIndex(group, 0, () =>
            {
                foreach (var t in group)
                    t?.Flush();
                onComplete?.Invoke();
            });
        }

        static void QueueRefreshConnectivityAtIndex(List<Terrain> group, int index, Action onComplete)
        {
            if (index >= group.Count)
            {
                CaveBuildRunStatusPublisher.PulseSubOperation("surface finish", "terrain connectivity done");
                CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                {
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                    {
                        Terrain.SetConnectivityDirty();
                        onComplete?.Invoke();
                    });
                });
                return;
            }

            CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
            {
                var terrain = group[index];
                if (terrain?.terrainData != null)
                {
                    var size = terrain.terrainData.size;
                    var origin = terrain.transform.position;
                    FindCardinalNeighbor(group, origin, size, out var left, out var top, out var right, out var bottom);
                    terrain.SetNeighbors(left, top, right, bottom);
                }

                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "surface finish",
                    $"terrain neighbors {index + 1}/{group.Count}");
                QueueRefreshConnectivityAtIndex(group, index + 1, onComplete);
            });
        }

        /// <summary>
        /// After neighbor tiles exist: optional Florida DEM per tile + stitch. When main LiDAR is already authoritative, neighbors are stitch-only.
        /// </summary>
        public static int ApplyUnifiedSurfaceWorldPolishSync(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (mainTerrain == null || ground == null || !ground.HasAnchor || request == null)
                return 0;

            if (SurfaceFloridaDemBuildState.NineTilePlayDiskPolishCompletedThisBuild ||
                SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild)
                return StitchAllPlayDiskSeamsSync(mainTerrain);

            var tiles = CollectGameplayTiles(mainTerrain);
            if (tiles.Length == 0)
                return 0;

            var group = BuildPlayDiskTerrainGroup(mainTerrain);
            var stamped = 0;

            foreach (var tile in tiles)
            {
                if (tile == null || !TryParseTileOffset(tile.name, out var off))
                    continue;

                var tileCenter = TileCenterWorld(tile);
                var extent = ResolveNeighborTileDemExtent(tile, request);
                if (SurfaceDemGeoreferenceAuthor.ApplyGeoreferencedStamp(
                        tile,
                        tileCenter,
                        extent,
                        TileDemSeed(request.Seed, off),
                        out _))
                    stamped++;

                StitchTileSeams(tile, mainTerrain, off, group);
                SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                    tile,
                    tileCenter,
                    extent,
                    strength: 0.22f);
                tile.Flush();
            }

            StitchTileSeams(mainTerrain, mainTerrain, Vector2Int.zero, group);
            mainTerrain.Flush();
            RefreshTerrainConnectivity(mainTerrain, new List<Terrain>(group));
            return stamped;
        }

        public static void QueueUnifiedSurfaceWorldPolish(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (mainTerrain == null || ground == null || !ground.HasAnchor || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            var tiles = CollectGameplayTiles(mainTerrain);
            if (tiles.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var entries = new List<GameplayTileEntry>(tiles.Length);
            foreach (var tile in tiles)
            {
                if (tile == null || !TryParseTileOffset(tile.name, out var off))
                    continue;
                entries.Add(new GameplayTileEntry(tile, off));
            }

            if (entries.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var lidarNote = SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild
                ? "main has Florida DEM — stamping each neighbor at its tile center, then nine-tile seam lock."
                : "Florida DEM per neighbor tile, then seam lock.";
            CaveBuildEditorLog.LogSurface("[Surface] Nine-tile polish — " + lidarNote, forceUnityConsole: true);
            QueueLidarTileAtIndex(mainTerrain, ground, request, entries, 0, onComplete);
        }

        static float ResolveNeighborTileDemExtent(Terrain tile, WorldGenerationRequest request)
        {
            if (tile?.terrainData != null)
            {
                var size = tile.terrainData.size;
                return Mathf.Max(size.x, size.z);
            }

            return EnvironmentKitHardwareBudget.ClampSurfaceExtent(
                Mathf.Clamp(request?.SurfaceExtentMeters ?? 220f, 80f, 512f));
        }

        static void QueueLidarTileAtIndex(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            IReadOnlyList<GameplayTileEntry> tiles,
            int index,
            Action onComplete)
        {
            if (index >= tiles.Count)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Nine-tile LiDAR complete ({tiles.Count} neighbor(s)) — locking seams across play disk.",
                    forceUnityConsole: true);
                QueueStitchNineTilePlayDisk(mainTerrain, ground, () =>
                    CaveBuildLayoutAuditMilestones.QueueAfterPlayDisk(
                        mainTerrain,
                        ground,
                        request,
                        onComplete));
                return;
            }

            var entry = tiles[index];
            if (entry.Tile == null)
            {
                QueueLidarTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete);
                return;
            }

            if (CaveBuildTerrainFingerprint.ShouldSkipNineTileNeighborDemStamp(
                    entry.Tile,
                    request.Seed,
                    out var demSkip))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Nine-tile LiDAR — skip {entry.Tile.name} ({demSkip}).",
                    forceUnityConsole: false);
                CaveBuildActionPacing.ScheduleLight(
                    () => QueueLidarTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete),
                    CaveBuildPipelineDomains.QueueLabel("nine-tile LiDAR — skip unchanged"));
                return;
            }

            var tileNum = index + 1;
            var tileCenter = TileCenterWorld(entry.Tile);
            var extent = ResolveNeighborTileDemExtent(entry.Tile, request);
            var lidarVerb = SurfaceLidarGuidedSculptPolicy.PreferSculptOverStamp ? "sculpt" : "stamp";
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "nine-tile LiDAR",
                $"{lidarVerb} {tileNum}/{tiles.Count} ({entry.Tile.name})");
            SurfaceDemGeoreferenceAuthor.QueueApplyGeoreferencedStamp(
                entry.Tile,
                tileCenter,
                extent,
                TileDemSeed(request.Seed, entry.Offset),
                _ =>
                {
                    if (entry.Tile != null)
                    {
                        CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(entry.Tile);
                        CaveBuildTerrainFingerprint.RecordWildernessTile(
                            request.Seed,
                            entry.Tile,
                            mainTerrain);
                    }

                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueLidarTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete),
                        CaveBuildPipelineDomains.QueueLabel($"nine-tile LiDAR — tile {tileNum}/{tiles.Count}"));
                });
        }

        /// <summary>Lock <see cref="MainTerrainName"/> + 8 neighbors after all tiles have DEM + edge seed.</summary>
        public static void QueueStitchNineTilePlayDisk(Terrain mainTerrain, Action onComplete) =>
            QueueStitchNineTilePlayDisk(mainTerrain, null, onComplete);

        public static void QueueStitchNineTilePlayDisk(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var group = BuildPlayDiskTerrainGroup(mainTerrain);
            if (group.Length <= 1)
            {
                QueueStitchTileSeamsPaced(
                    mainTerrain,
                    mainTerrain,
                    Vector2Int.zero,
                    new[] { mainTerrain },
                    () =>
                    {
                        EnforcePlayDiskGridLayout(mainTerrain, ground);
                        SurfaceFloridaDemBuildState.MarkNineTilePlayDiskPolishCompleted();
                        onComplete?.Invoke();
                    });
                return;
            }

            CaveBuildRunStatusPublisher.PulseSubOperation("nine-tile seams", "lock play disk (main + 8)");
            QueueStitchPlayDiskTileAtIndex(mainTerrain, ground, group, 0, onComplete);
        }

        static void QueueStitchPlayDiskTileAtIndex(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            Terrain[] group,
            int index,
            Action onComplete)
        {
            if (index >= group.Length)
            {
                EnforcePlayDiskGridLayout(mainTerrain, ground);

                if (ValidateNineTilePlayDiskForOuterRing(mainTerrain, out var lockMsg))
                {
                    SurfaceFloridaDemBuildState.MarkNineTilePlayDiskPolishCompleted();
                    SurfaceTerrainGridRegistry.ReindexFromMain(mainTerrain, playDiskLocked: true);
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Nine-tile play disk locked (main + 8 neighbors) — ready for wilderness ring.",
                        forceUnityConsole: true);
                }
                else
                {
                    CaveBuildEditorLog.LogSurfaceWarning("[Surface] " + lockMsg);
                    SurfaceTerrainGridRegistry.ReindexFromMain(mainTerrain, playDiskLocked: false);
                }

                var neighbors = CollectGameplayTiles(mainTerrain);
                QueueRefreshTerrainConnectivity(mainTerrain, neighbors, onComplete);
                return;
            }

            var tile = group[index];
            if (tile == null || !TryParsePlayDiskGridOffset(mainTerrain, tile, out var off))
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => QueueStitchPlayDiskTileAtIndex(mainTerrain, ground, group, index + 1, onComplete),
                    CaveBuildPipelineDomains.QueueLabel("nine-tile seams — next"));
                return;
            }

            var tileNum = index + 1;
            var label = tile == mainTerrain ? MainTerrainName : tile.name;
            CaveBuildRunStatusPublisher.PulseSubOperation("nine-tile seams", $"lock {tileNum}/{group.Length} ({label})");
            QueueStitchTileSeamsPaced(
                tile,
                mainTerrain,
                off,
                group,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueStitchPlayDiskTileAtIndex(mainTerrain, ground, group, index + 1, onComplete),
                        CaveBuildPipelineDomains.QueueLabel($"nine-tile seams — {tileNum + 1}/{group.Length}"));
                });
        }

        static void QueueSculptTileAtIndex(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            IReadOnlyList<GameplayTileEntry> tiles,
            int index,
            Action onComplete)
        {
            if (index >= tiles.Count)
            {
                var all = new List<Terrain>(tiles.Count + 1) { mainTerrain };
                for (var i = 0; i < tiles.Count; i++)
                    all.Add(tiles[i].Tile);
                RefreshTerrainConnectivity(mainTerrain, all);
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Neighbor tile sculpt complete ({tiles.Count} tile(s), each with centered passes + LiDAR).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var entry = tiles[index];
            if (entry.Tile == null)
            {
                QueueSculptTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete);
                return;
            }

            var tileCenter = ground != null && ground.HasAnchor
                ? ground.Anchor.position
                : TileCenterWorld(mainTerrain);

            var extent = Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f);
            var passes = SurfaceTerrainCenteredAuthor.PassCountForNeighborTile(
                SurfaceTerrainCenteredAuthor.ResolvePassCount(request.SurfaceTerrainBuildPasses));
            var tileSeed = TileSculptSeed(request.Seed, entry.Offset);
            var preserve = extent * 0.08f;

            var tileNum = index + 1;
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Neighbor {entry.Tile.name} ({tileNum}/{tiles.Count}): paced sculpt + LiDAR at ({tileCenter.x:F0}, {tileCenter.z:F0}).",
                forceUnityConsole: true);

            SurfaceTerrainCenteredAuthor.QueueCenteredPasses(
                entry.Tile,
                tileCenter,
                extent,
                tileSeed,
                SculptMountainsOnPlayDiskTiles(request),
                request.SurfaceIncludeWater,
                request.SurfaceIncludeRoads,
                preserve,
                passes,
                onComplete: () =>
                {
                    CaveBuildActionPacing.ScheduleHeavy(
                        () =>
                        {
                            ApplyTileLidarAndStitch(mainTerrain, ground, request, entry);
                            QueueSculptTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete);
                        },
                        CaveBuildPipelineDomains.SurfaceQueueLabel(
                            $"neighbor tile {entry.Tile.name} LiDAR + seams"));
                });
        }

        static void ApplyTileLidarAndStitch(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            GameplayTileEntry entry,
            Action onComplete = null)
        {
            if (entry.Tile == null)
            {
                onComplete?.Invoke();
                return;
            }

            var tileCenter = TileCenterWorld(entry.Tile);
            var extent = ResolveNeighborTileDemExtent(entry.Tile, request);
            void AfterLidar()
            {
                var group = CollectGameplayTiles(mainTerrain);
                StitchTileSeams(entry.Tile, mainTerrain, entry.Offset, group);
                onComplete?.Invoke();
            }

            if (SurfaceDemGeoreferenceAuthor.ApplyGeoreferencedStamp(
                    entry.Tile,
                    tileCenter,
                    extent,
                    request.Seed,
                    out _))
            {
                AfterLidar();
                return;
            }

            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
            {
                SurfaceLidarTerrainAuthor.QueueApplyFromResearchCache(
                    entry.Tile,
                    tileCenter,
                    extent,
                    request.Seed,
                    (_, _) => AfterLidar());
                return;
            }

            SurfaceLidarTerrainAuthor.ApplyFromResearchCache(
                entry.Tile,
                tileCenter,
                extent,
                request.Seed,
                out _);
            AfterLidar();
        }

        static bool SculptGameplayTileSync(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            GameplayTileEntry entry)
        {
            if (entry.Tile == null || request == null)
                return false;

            SculptGameplayTilePasses(mainTerrain, ground, request, entry);
            ApplyTileLidarAndStitch(mainTerrain, ground, request, entry);
            return true;
        }

        static bool SculptMountainsOnPlayDiskTiles(WorldGenerationRequest request) =>
            request != null &&
            request.SurfaceIncludeMountains &&
            request.SurfaceScope != SurfaceBuildScope.FullWorld;

        static void SculptGameplayTilePasses(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            GameplayTileEntry entry)
        {
            if (entry.Tile == null || request == null)
                return;

            SeedHeightsFromSharedEdges(entry.Tile, mainTerrain, entry.Offset, mainTerrain);

            var tileCenter = TileCenterWorld(entry.Tile);
            if (ground != null && ground.HasAnchor)
                tileCenter.y = ground.Anchor.position.y;

            var extent = EnvironmentKitHardwareBudget.ClampSurfaceExtent(
                Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f));
            var passes = SurfaceTerrainCenteredAuthor.PassCountForNeighborTile(
                SurfaceTerrainCenteredAuthor.ResolvePassCount(request.SurfaceTerrainBuildPasses));
            var tileSeed = TileSculptSeed(request.Seed, entry.Offset);
            var preserve = extent * 0.08f;

            SurfaceTerrainCenteredAuthor.ApplyCenteredPasses(
                entry.Tile,
                tileCenter,
                extent,
                tileSeed,
                SculptMountainsOnPlayDiskTiles(request),
                request.SurfaceIncludeWater,
                request.SurfaceIncludeRoads,
                preserve,
                passes,
                null);
        }

        static TerrainData CreateFreshTileData(TerrainData template)
        {
            var td = new TerrainData
            {
                heightmapResolution = template.heightmapResolution,
                size = template.size,
            };
            var layers = template.terrainLayers;
            if (layers != null && layers.Length > 0)
            {
                td.terrainLayers = layers;
                CopyAlphamapsFromTemplate(template, td);
            }

            return td;
        }

        /// <summary>Child tiles inherit layer paint from the play-disk template (avoids flat gray splats).</summary>
        static void CopyAlphamapsFromTemplate(TerrainData template, TerrainData tileData)
        {
            if (template == null || tileData == null || template.alphamapLayers <= 0)
                return;

            var w = template.alphamapWidth;
            var h = template.alphamapHeight;
            if (w <= 0 || h <= 0)
                return;

            if (tileData.alphamapWidth != w || tileData.alphamapHeight != h)
                return;

            tileData.SetAlphamaps(0, 0, template.GetAlphamaps(0, 0, w, h));
        }

        /// <summary>FullWorld 7×7 grid — same resolution as play disk so Unity allows SetNeighbors + edge seams.</summary>
        static TerrainData CreateFullWorldGridTileData(TerrainData template) => CreateFreshTileData(template);

        static bool TileHeightmapResolutionMatches(Terrain reference, Terrain tile)
        {
            if (reference?.terrainData == null || tile?.terrainData == null)
                return false;

            return reference.terrainData.heightmapResolution == tile.terrainData.heightmapResolution;
        }

        static TerrainData CreateWildernessTileData(TerrainData template)
        {
            var td = CreateFreshTileData(template);
            td.heightmapResolution = CaveBuildTerrainHeightmapMemory.WildernessHeightmapResolution(template);
            return td;
        }

        static void QueueRemoveStaleTilesPaced(Transform tilesRoot, Action onComplete)
        {
            if (tilesRoot == null)
            {
                onComplete?.Invoke();
                return;
            }

            var stale = new List<GameObject>();
            for (var i = tilesRoot.childCount - 1; i >= 0; i--)
            {
                var child = tilesRoot.GetChild(i);
                if (child == null ||
                    !child.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
                    continue;

                var terrain = child.GetComponent<Terrain>();
                if (terrain?.terrainData != null)
                    continue;

                stale.Add(child.gameObject);
            }

            if (stale.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            RemoveStaleTileAtIndex(stale, 0, onComplete);
        }

        static void RemoveStaleTileAtIndex(List<GameObject> stale, int index, Action onComplete)
        {
            if (index >= stale.Count)
            {
                onComplete?.Invoke();
                return;
            }

            if (stale[index] != null)
                CaveEditorUndo.DestroyImmediate(stale[index]);

            CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                RemoveStaleTileAtIndex(stale, index + 1, onComplete));
        }

        /// <summary>Row-band upload + deferred flush — avoids freeze on first neighbor tile after peak normalize.</summary>
        static void QueueSeedHeightsFromSharedEdges(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Terrain connectivityMain,
            Action onComplete)
        {
            if (tile?.terrainData == null || main?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var res = tile.terrainData.heightmapResolution;
            CaveEditorUndo.RecordObject(tile.terrainData, "Seed neighbor tile from seams");
            var session = new SeedHeightsSession
            {
                Tile = tile,
                Main = main,
                ConnectivityMain = connectivityMain,
                Off = off,
                Res = res,
                Band = Mathf.Max(4, res / 12),
                OnComplete = onComplete,
            };
            ScheduleSeedStep(session, SeedHeightsStep.PrepareEdges);
        }

        static void ScheduleSeedStep(SeedHeightsSession session, SeedHeightsStep step)
        {
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunSeedStep(session, step));
        }

        static void RunSeedStep(SeedHeightsSession session, SeedHeightsStep step)
        {
            if (session?.Tile?.terrainData == null)
            {
                session?.OnComplete?.Invoke();
                return;
            }

            switch (step)
            {
                case SeedHeightsStep.PrepareEdges:
                    session.Heights = BuildLegacySeedHeightsArray(
                        session.Tile,
                        session.Main,
                        session.Off,
                        session.ConnectivityMain,
                        session.Band,
                        session.Res);
                    session.RowY = 0;
                    ScheduleSeedStep(session, SeedHeightsStep.UploadRows);
                    break;

                case SeedHeightsStep.UploadRows:
                {
                    var chunk = SeedRowChunk(session.Res);
                    var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
                    FlushSeedRows(session, session.RowY, yEnd);
                    session.RowY = yEnd;
                    if (session.RowY < session.Res)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Environment Kit",
                            $"[Surface] neighbor seam rows {session.RowY}/{session.Res}",
                            0.865f);
                        ScheduleSeedStep(session, SeedHeightsStep.UploadRows);
                        return;
                    }

                    ScheduleSeedStep(session, SeedHeightsStep.Done);
                    break;
                }

                case SeedHeightsStep.Done:
                    CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                        session.Tile,
                        () => session.OnComplete?.Invoke());
                    break;
            }
        }

        static void FlushSeedRows(SeedHeightsSession session, int yStart, int yEnd)
        {
            var rowCount = yEnd - yStart;
            if (rowCount <= 0 || session.Heights == null)
                return;

            var slice = new float[rowCount, session.Res];
            for (var y = 0; y < rowCount; y++)
            {
                for (var x = 0; x < session.Res; x++)
                    slice[y, x] = session.Heights[yStart + y, x];
            }

            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(
                session.Tile,
                0,
                yStart,
                slice,
                requestDelayLod: false);
        }

        /// <summary>Match neighbor heights to main surface (not flat center sample — avoids sunken main).</summary>
        static void ProjectNeighborRowsFromMainSurface(
            Terrain tile,
            Terrain main,
            float[,] heights,
            int yStart,
            int yEnd)
        {
            if (tile?.terrainData == null || main?.terrainData == null || heights == null)
                return;

            var res = heights.GetLength(0);
            var origin = tile.transform.position;
            var size = tile.terrainData.size;
            var mainOrigin = main.transform.position;
            var mainSize = main.terrainData.size;
            var denom = Mathf.Max(1, res - 1);

            for (var z = yStart; z < yEnd; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)denom * size.x;
                    var wz = origin.z + z / (float)denom * size.z;
                    heights[z, x] = SampleMainNormForNeighborWorld(
                        main,
                        mainOrigin,
                        mainSize,
                        wx,
                        wz);
                }
            }
        }

        static float SampleMainNormForNeighborWorld(
            Terrain main,
            Vector3 mainOrigin,
            Vector3 mainSize,
            float worldX,
            float worldZ)
        {
            if (SurfaceTerrainPlayRegion.ContainsWorldXZ(main, worldX, worldZ, insetMeters: 0.05f) &&
                TrySamplePlayDiskNormAtWorld(main, worldX, worldZ, out var onMain))
                return onMain;

            var sx = Mathf.Clamp(worldX, mainOrigin.x, mainOrigin.x + mainSize.x);
            var sz = Mathf.Clamp(worldZ, mainOrigin.z, mainOrigin.z + mainSize.z);
            return SampleTerrainNormAtWorld(main, sx, sz);
        }

        /// <summary>Classic nine-tile seed: flat center norm + shared-edge copy + interior fill (LiDAR stamp follows).</summary>
        static float[,] BuildLegacySeedHeightsArray(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Terrain connectivityMain,
            int band,
            int res)
        {
            var heights = new float[res, res];
            var baseNorm = SampleTerrainNormAtCenter(main);
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                    heights[z, x] = baseNorm;
            }

            CopyTouchingEdge(main, tile, heights, off, band);

            foreach (var other in CollectGameplayTiles(connectivityMain))
            {
                if (other == null || other == tile || !TryParseTileOffset(other.name, out var otherOff))
                    continue;

                var delta = otherOff - off;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                    continue;

                CopyTouchingEdge(other, tile, heights, delta, band);
            }

            // FullWorld play neighbors: flat interior until per-tile LiDAR — bilinear wedge fill causes ramp slabs.
            if (!IsPlayDiskGameplayTerrain(tile))
                FillInteriorFromEdgeBands(heights, band, res);

            return heights;
        }

        static void SeedHeightsFromSharedEdges(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Terrain connectivityMain)
        {
            if (tile?.terrainData == null || main?.terrainData == null)
                return;

            var res = tile.terrainData.heightmapResolution;
            var band = Mathf.Max(4, res / 12);
            var heights = BuildLegacySeedHeightsArray(tile, main, off, connectivityMain, band, res);
            CaveEditorUndo.RecordObject(tile.terrainData, "Seed neighbor tile from seams");
            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(tile, 0, 0, heights, requestDelayLod: false);
            tile.Flush();
        }

        /// <summary>World-space edge copy — works when source and dest heightmap resolutions differ (513 play vs 257 wilderness).</summary>
        static void CopyTouchingEdge(
            Terrain source,
            Terrain destTerrain,
            float[,] dest,
            Vector2Int destOffsetFromSource,
            int band)
        {
            if (source?.terrainData == null || destTerrain?.terrainData == null || dest == null)
                return;

            var res = dest.GetLength(0);
            band = Mathf.Clamp(band, 1, res / 2);
            var destOrigin = destTerrain.transform.position;
            var destSize = destTerrain.terrainData.size;
            var denom = Mathf.Max(1, res - 1);

            if (destOffsetFromSource.x == 1 && destOffsetFromSource.y == 0)
            {
                for (var z = 0; z < res; z++)
                {
                    var wz = destOrigin.z + z / (float)denom * destSize.z;
                    for (var b = 0; b < band; b++)
                    {
                        var wx = destOrigin.x + b / (float)denom * destSize.x;
                        dest[z, b] = SampleTerrainNormAtWorld(source, wx, wz);
                    }
                }
            }
            else if (destOffsetFromSource.x == -1 && destOffsetFromSource.y == 0)
            {
                for (var z = 0; z < res; z++)
                {
                    var wz = destOrigin.z + z / (float)denom * destSize.z;
                    for (var b = 0; b < band; b++)
                    {
                        var wx = destOrigin.x + (res - 1 - b) / (float)denom * destSize.x;
                        dest[z, res - 1 - b] = SampleTerrainNormAtWorld(source, wx, wz);
                    }
                }
            }
            else if (destOffsetFromSource.x == 0 && destOffsetFromSource.y == 1)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = destOrigin.x + x / (float)denom * destSize.x;
                    for (var b = 0; b < band; b++)
                    {
                        var wz = destOrigin.z + b / (float)denom * destSize.z;
                        dest[b, x] = SampleTerrainNormAtWorld(source, wx, wz);
                    }
                }
            }
            else if (destOffsetFromSource.x == 0 && destOffsetFromSource.y == -1)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = destOrigin.x + x / (float)denom * destSize.x;
                    for (var b = 0; b < band; b++)
                    {
                        var wz = destOrigin.z + (res - 1 - b) / (float)denom * destSize.z;
                        dest[res - 1 - b, x] = SampleTerrainNormAtWorld(source, wx, wz);
                    }
                }
            }
        }

        static float SampleTerrainWorldY(Terrain terrain, float worldX, float worldZ)
        {
            if (terrain?.terrainData == null)
                return 0f;

            return terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));
        }

        static float SampleTerrainNormAtWorld(Terrain terrain, float worldX, float worldZ)
        {
            if (terrain?.terrainData == null)
                return 0.35f;

            var worldY = SampleTerrainWorldY(terrain, worldX, worldZ);
            return WorldYToNorm(terrain, worldY);
        }

        static float NormToWorldY(Terrain terrain, float norm)
        {
            if (terrain?.terrainData == null)
                return 0f;

            return terrain.transform.position.y + norm * terrain.terrainData.size.y;
        }

        static float WorldYToNorm(Terrain terrain, float worldY)
        {
            if (terrain?.terrainData == null)
                return 0.35f;

            return Mathf.Clamp01(
                (worldY - terrain.transform.position.y) / Mathf.Max(terrain.terrainData.size.y, 0.01f));
        }

        static bool IsPlayDiskGameplayTerrain(Terrain terrain)
        {
            if (terrain?.terrainData == null)
                return false;

            if (terrain.name == MainTerrainName)
                return true;

            return terrain.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal) &&
                   TryParseTileOffset(terrain.name, out var off) &&
                   IsNineTileGameplayOffset(off);
        }

        public static bool IsPlayDiskGameplayTerrainPublic(Terrain terrain) =>
            IsPlayDiskGameplayTerrain(terrain);

        /// <summary>Shared world elevation at seam — avoids 80m cliffs from blending mismatched normalized values.</summary>
        static float BlendSeamWorldY(
            float tileWorldY,
            float neighborWorldY,
            Terrain tile,
            Terrain neighbor,
            float strength)
        {
            if (IsPlayDiskGameplayTerrain(tile) && IsPlayDiskGameplayTerrain(neighbor))
                return (tileWorldY + neighborWorldY) * 0.5f;

            return Mathf.Lerp(tileWorldY, neighborWorldY, strength);
        }

        static (float tileNorm, float neighborNorm) BlendSeamNormPairFromNorms(
            Terrain tile,
            float tileNorm,
            Terrain neighbor,
            float neighborNorm,
            float strength)
        {
            var tileY = NormToWorldY(tile, tileNorm);
            var neighborY = NormToWorldY(neighbor, neighborNorm);
            var blendedY = BlendSeamWorldY(tileY, neighborY, tile, neighbor, strength);
            return (WorldYToNorm(tile, blendedY), WorldYToNorm(neighbor, blendedY));
        }

        static (float tileNorm, float neighborNorm) BlendSeamNormPair(
            Terrain tile,
            float tileNorm,
            Terrain neighbor,
            float worldX,
            float worldZ,
            float strength)
        {
            var tileY = NormToWorldY(tile, tileNorm);
            var neighborY = SampleTerrainWorldY(neighbor, worldX, worldZ);
            var blendedY = BlendSeamWorldY(tileY, neighborY, tile, neighbor, strength);
            return (WorldYToNorm(tile, blendedY), WorldYToNorm(neighbor, blendedY));
        }

        /// <summary>Plan B — sample neighbor height in world meters when resolutions or rings differ (513 play vs 257 wilderness).</summary>
        static bool PreferWorldSpaceSeamBlend(Terrain tile, Terrain neighbor)
        {
            if (tile?.terrainData == null || neighbor?.terrainData == null)
                return false;

            if (tile.terrainData.heightmapResolution != neighbor.terrainData.heightmapResolution)
                return true;

            return IsPlayDiskGameplayTerrain(tile) != IsPlayDiskGameplayTerrain(neighbor);
        }

        static (float tileNorm, float neighborNorm) BlendSeamPairAtEdge(
            Terrain tile,
            float tileNorm,
            Terrain neighbor,
            float neighborNorm,
            float worldX,
            float worldZ,
            float strength)
        {
            if (!PreferWorldSpaceSeamBlend(tile, neighbor))
                return BlendSeamNormPairFromNorms(tile, tileNorm, neighbor, neighborNorm, strength);

            return BlendSeamNormPair(tile, tileNorm, neighbor, worldX, worldZ, strength);
        }

        static bool IsOuterRingTerrainName(Terrain terrain) =>
            terrain != null &&
            (terrain.name.StartsWith(MountainFoothillTileNamePrefix, StringComparison.Ordinal) ||
             terrain.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal) ||
             terrain.name.StartsWith(MountainWildernessTileNamePrefix, StringComparison.Ordinal));

        static bool IsMountainWildernessTerrain(Terrain terrain) => IsOuterRingTerrainName(terrain);

        static int SeamWidthForPair(Terrain tile, Terrain neighbor)
        {
            if (tile?.terrainData == null)
                return 4;

            var tileRes = tile.terrainData.heightmapResolution;
            var neighborRes = neighbor?.terrainData?.heightmapResolution ?? tileRes;
            var seam = Mathf.Max(4, tileRes / 32, neighborRes / 32);
            if (tileRes != neighborRes)
                seam = Mathf.Max(seam, 12);

            return Mathf.Clamp(seam, 4, tileRes / 4);
        }

        static float SeamBlendStrength(Terrain tile, Terrain neighbor) =>
            IsMountainWildernessTerrain(tile) || IsMountainWildernessTerrain(neighbor) ? 0.88f : 0.5f;

        static int MapHeightmapIndex(int index, int fromRes, int toRes) =>
            Mathf.Clamp(
                Mathf.RoundToInt(index / (float)Mathf.Max(1, fromRes - 1) * (toRes - 1)),
                0,
                toRes - 1);

        static int MapSeamBandIndex(int bandIndex, int seamWidth, int targetSeamWidth) =>
            Mathf.Clamp(
                Mathf.RoundToInt(bandIndex / (float)Mathf.Max(1, seamWidth - 1) * (targetSeamWidth - 1)),
                0,
                targetSeamWidth - 1);

        static void FillInteriorFromEdgeBands(float[,] heights, int band, int res)
        {
            for (var z = band; z < res - band; z++)
            {
                var left = AverageColumn(heights, 0, band, z);
                var right = AverageColumn(heights, res - band, res, z);
                var t = (z - band) / (float)Mathf.Max(1, res - 2 * band);
                for (var x = band; x < res - band; x++)
                {
                    var u = (x - band) / (float)Mathf.Max(1, res - 2 * band);
                    var top = AverageRow(heights, 0, band, x);
                    var bottom = AverageRow(heights, res - band, res, x);
                    heights[z, x] = Mathf.Lerp(
                        Mathf.Lerp(left, right, u),
                        Mathf.Lerp(top, bottom, t),
                        0.5f);
                }
            }
        }

        static float AverageColumn(float[,] heights, int xStart, int xEnd, int z)
        {
            var sum = 0f;
            var n = 0;
            for (var x = xStart; x < xEnd; x++)
            {
                sum += heights[z, x];
                n++;
            }

            return n > 0 ? sum / n : 0.35f;
        }

        static float AverageRow(float[,] heights, int zStart, int zEnd, int x)
        {
            var sum = 0f;
            var n = 0;
            for (var z = zStart; z < zEnd; z++)
            {
                sum += heights[z, x];
                n++;
            }

            return n > 0 ? sum / n : 0.35f;
        }

        static float SampleTerrainNormAtCenter(Terrain terrain)
        {
            if (terrain?.terrainData == null)
                return 0.35f;

            var res = terrain.terrainData.heightmapResolution;
            var cx = res / 2;
            var cz = res / 2;
            return terrain.terrainData.GetHeight(cx, cz) / Mathf.Max(terrain.terrainData.size.y, 0.01f);
        }

        static void StitchTileSeams(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Terrain[] group)
        {
            var edges = CollectSeamEdges(tile, main, off, group);
            foreach (var edge in edges)
                BlendSharedEdge(edge.Tile, edge.Neighbor, edge.DeltaFromNeighbor, edge.Seam, delayLod: false);
        }

        struct SeamStitchEdgeWork
        {
            public Terrain Tile;
            public Terrain Neighbor;
            public Vector2Int DeltaFromNeighbor;
            public int Seam;
        }

        public static void QueueStitchMountainWildernessTileSeams(
            Terrain mainTerrain,
            Terrain tile,
            Action onComplete)
        {
            if (mainTerrain == null || tile == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!TryParseOuterRingTileOffset(tile.name, out var off))
                off = Vector2Int.zero;

            var group = BuildMountainWildernessStitchGroup(mainTerrain);
            QueueStitchTileSeamsPaced(tile, mainTerrain, off, group, () =>
            {
                RefreshMountainTerrainConnectivity(mainTerrain);
                onComplete?.Invoke();
            }, tile.name);
        }

        static void QueueStitchTileSeamsPaced(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Terrain[] group,
            Action onComplete,
            string emptySeamLabel = null,
            bool flatGridSkipSeamBlend = false)
        {
            if (flatGridSkipSeamBlend)
            {
                if (main != null)
                    RefreshMountainTerrainConnectivity(main);
                tile?.Flush();
                onComplete?.Invoke();
                return;
            }

            var edges = CollectSeamEdges(tile, main, off, group);
            if (edges.Count == 0)
            {
                if (!string.IsNullOrEmpty(emptySeamLabel))
                {
                    CaveBuildEditorLog.LogSurfaceWarning(
                        $"[Surface] Seam — no shared edges for {emptySeamLabel} at ({off.x},{off.y}).");
                }

                onComplete?.Invoke();
                return;
            }

            CaveBuildLiveSceneFlushUtility.EnterSeamPhase();
            QueueBlendSeamEdgeAtIndex(edges, 0, () =>
            {
                var flushAll = !CaveBuildActionPacing.IsBusy &&
                               !LavaTubeCaveBuildPipeline.IsPhasedBuildActive;
                CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                    tile,
                    () =>
                    {
                        CaveBuildLiveSceneFlushUtility.ExitSeamPhase();
                        onComplete?.Invoke();
                    },
                    flushAllSurfaceTerrains: flushAll);
            });
        }

        static List<SeamStitchEdgeWork> CollectSeamEdges(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Terrain[] group)
        {
            var edges = new List<SeamStitchEdgeWork>(4);
            if (tile?.terrainData == null)
                return edges;

            if (main != null && tile != main && Mathf.Abs(off.x) + Mathf.Abs(off.y) == 1)
            {
                edges.Add(new SeamStitchEdgeWork
                {
                    Tile = tile,
                    Neighbor = main,
                    DeltaFromNeighbor = off,
                    Seam = SeamWidthForPair(tile, main),
                });
            }

            if (group == null)
                return edges;

            foreach (var other in group)
            {
                if (other == null || other == tile)
                    continue;

                Vector2Int otherOff;
                if (!TryParsePlayDiskGridOffset(main, other, out otherOff) &&
                    !TryParseAnySurfaceTileOffset(other.name, out otherOff))
                    continue;

                var delta = otherOff - off;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                    continue;

                edges.Add(new SeamStitchEdgeWork
                {
                    Tile = tile,
                    Neighbor = other,
                    DeltaFromNeighbor = delta,
                    Seam = SeamWidthForPair(tile, other),
                });
            }

            return edges;
        }

        static void QueueBlendSeamEdgeAtIndex(List<SeamStitchEdgeWork> edges, int index, Action onComplete)
        {
            if (index >= edges.Count)
            {
                onComplete?.Invoke();
                return;
            }

            var edge = edges[index];
            QueueBlendSharedEdgePaced(edge, () =>
            {
                QueueBlendSeamEdgeAtIndex(edges, index + 1, onComplete);
            });
        }

        static void BlendSharedEdgeSync(SeamStitchEdgeWork edge)
        {
            if (edge.Tile?.terrainData == null || edge.Neighbor?.terrainData == null)
                return;

            var seam = Mathf.Max(edge.Seam, SeamWidthForPair(edge.Tile, edge.Neighbor));
            var strength = SeamBlendStrength(edge.Tile, edge.Neighbor);
            var tileRes = edge.Tile.terrainData.heightmapResolution;
            var neighborRes = edge.Neighbor.terrainData.heightmapResolution;
            var tileSeam = Mathf.Clamp(seam, 4, tileRes / 4);
            var neighborSeam = Mathf.Clamp(seam, 4, neighborRes / 4);
            var delta = edge.DeltaFromNeighbor;

            float[,] tileBand = null;
            float[,] neighborBand = null;
            var orientation = -1;

            if (delta.x == 1 && delta.y == 0)
            {
                orientation = 0;
                tileBand = edge.Tile.terrainData.GetHeights(0, 0, tileSeam, tileRes);
                neighborBand = edge.Neighbor.terrainData.GetHeights(neighborRes - neighborSeam, 0, neighborSeam, neighborRes);
            }
            else if (delta.x == -1 && delta.y == 0)
            {
                orientation = 1;
                tileBand = edge.Tile.terrainData.GetHeights(tileRes - tileSeam, 0, tileSeam, tileRes);
                neighborBand = edge.Neighbor.terrainData.GetHeights(0, 0, neighborSeam, neighborRes);
            }
            else if (delta.x == 0 && delta.y == 1)
            {
                orientation = 2;
                tileBand = edge.Tile.terrainData.GetHeights(0, 0, tileRes, tileSeam);
                neighborBand = edge.Neighbor.terrainData.GetHeights(0, neighborRes - neighborSeam, neighborRes, neighborSeam);
            }
            else if (delta.x == 0 && delta.y == -1)
            {
                orientation = 3;
                tileBand = edge.Tile.terrainData.GetHeights(0, tileRes - tileSeam, tileRes, tileSeam);
                neighborBand = edge.Neighbor.terrainData.GetHeights(0, 0, neighborRes, neighborSeam);
            }
            else
            {
                return;
            }

            var session = new SeamBlendBandSession
            {
                Edge = edge,
                TileBand = tileBand,
                NeighborBand = neighborBand,
                TileRes = tileRes,
                NeighborRes = neighborRes,
                TileSeam = tileSeam,
                NeighborSeam = neighborSeam,
                TileDenom = Mathf.Max(1, tileRes - 1),
                NeighborDenom = Mathf.Max(1, neighborRes - 1),
                Strength = strength,
                TileOrigin = edge.Tile.transform.position,
                TileSize = edge.Tile.terrainData.size,
                NeighborOrigin = edge.Neighbor.transform.position,
                Orientation = orientation,
            };

            session.Row = session.TileRes;
            switch (session.Orientation)
            {
                case 0:
                    BlendSeamRowsEast(session, edge.Tile, edge.Neighbor, session.TileRes);
                    break;
                case 1:
                    BlendSeamRowsWest(session, edge.Tile, edge.Neighbor, session.TileRes);
                    break;
                case 2:
                    BlendSeamRowsNorth(session, edge.Tile, edge.Neighbor, session.TileRes);
                    break;
                case 3:
                    BlendSeamRowsSouth(session, edge.Tile, edge.Neighbor, session.TileRes);
                    break;
            }

            CommitSeamBlendBands(session, edge.Tile, edge.Neighbor, delayLod: true);
        }

        sealed class SeamBlendBandSession
        {
            public SeamStitchEdgeWork Edge;
            public float[,] TileBand;
            public float[,] NeighborBand;
            public int TileRes;
            public int NeighborRes;
            public int TileSeam;
            public int NeighborSeam;
            public int TileDenom;
            public int NeighborDenom;
            public float Strength;
            public Vector3 TileOrigin;
            public Vector3 TileSize;
            public Vector3 NeighborOrigin;
            public int Row;
            public int SampleRow;
            public int Orientation;
            public Action OnComplete;
        }

        static int SeamBlendRowChunk(int res)
        {
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return res >= 1025 ? 32 : res >= 513 ? 24 : 64;

            return 48;
        }

        static void QueueBlendSharedEdgePaced(SeamStitchEdgeWork edge, Action onComplete)
        {
            if (edge.Tile?.terrainData == null || edge.Neighbor?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () => BeginSeamBlendBandSession(edge, onComplete),
                CaveBuildPipelineDomains.QueueLabel("seam edge — sample bands prep"));
        }

        static void BeginSeamBlendBandSession(SeamStitchEdgeWork edge, Action onComplete)
        {
            var seam = Mathf.Max(edge.Seam, SeamWidthForPair(edge.Tile, edge.Neighbor));
            var strength = SeamBlendStrength(edge.Tile, edge.Neighbor);
            var tileRes = edge.Tile.terrainData.heightmapResolution;
            var neighborRes = edge.Neighbor.terrainData.heightmapResolution;
            var tileSeam = Mathf.Clamp(seam, 4, tileRes / 4);
            var neighborSeam = Mathf.Clamp(seam, 4, neighborRes / 4);
            var delta = edge.DeltaFromNeighbor;
            var orientation = -1;
            int tileBandW;
            int tileBandH;
            int neighborBandW;
            int neighborBandH;

            if (delta.x == 1 && delta.y == 0)
            {
                orientation = 0;
                tileBandW = tileSeam;
                tileBandH = tileRes;
                neighborBandW = neighborSeam;
                neighborBandH = neighborRes;
            }
            else if (delta.x == -1 && delta.y == 0)
            {
                orientation = 1;
                tileBandW = tileSeam;
                tileBandH = tileRes;
                neighborBandW = neighborSeam;
                neighborBandH = neighborRes;
            }
            else if (delta.x == 0 && delta.y == 1)
            {
                orientation = 2;
                tileBandW = tileRes;
                tileBandH = tileSeam;
                neighborBandW = neighborRes;
                neighborBandH = neighborSeam;
            }
            else if (delta.x == 0 && delta.y == -1)
            {
                orientation = 3;
                tileBandW = tileRes;
                tileBandH = tileSeam;
                neighborBandW = neighborRes;
                neighborBandH = neighborSeam;
            }
            else
            {
                onComplete?.Invoke();
                return;
            }

            var session = new SeamBlendBandSession
            {
                Edge = edge,
                TileBand = new float[tileBandH, tileBandW],
                NeighborBand = new float[neighborBandH, neighborBandW],
                TileRes = tileRes,
                NeighborRes = neighborRes,
                TileSeam = tileSeam,
                NeighborSeam = neighborSeam,
                TileDenom = Mathf.Max(1, tileRes - 1),
                NeighborDenom = Mathf.Max(1, neighborRes - 1),
                Strength = strength,
                TileOrigin = edge.Tile.transform.position,
                TileSize = edge.Tile.terrainData.size,
                NeighborOrigin = edge.Neighbor.transform.position,
                Orientation = orientation,
                SampleRow = 0,
                Row = 0,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunSeamSampleRowChunk(session),
                CaveBuildPipelineDomains.QueueLabel("seam edge — sample bands"));
        }

        static void RunSeamSampleRowChunk(SeamBlendBandSession session)
        {
            if (session?.Edge.Tile == null || session.Edge.Neighbor == null)
            {
                session?.OnComplete?.Invoke();
                return;
            }

            var tile = session.Edge.Tile;
            var neighbor = session.Edge.Neighbor;
            var chunk = SeamBlendRowChunk(session.TileRes);
            var rowEnd = Mathf.Min(session.TileRes, session.SampleRow + chunk);
            CaveBuildActionPacing.TouchQueueActivity();

            switch (session.Orientation)
            {
                case 0:
                {
                    var rows = rowEnd - session.SampleRow;
                    var tileSlice = tile.terrainData.GetHeights(0, session.SampleRow, session.TileSeam, rows);
                    var neighborSlice = neighbor.terrainData.GetHeights(
                        session.NeighborRes - session.NeighborSeam,
                        session.SampleRow,
                        session.NeighborSeam,
                        rows);
                    CopyHeightSliceIntoBand(session.TileBand, session.SampleRow, tileSlice);
                    CopyHeightSliceIntoBand(session.NeighborBand, session.SampleRow, neighborSlice);
                    break;
                }
                case 1:
                {
                    var rows = rowEnd - session.SampleRow;
                    var tileSlice = tile.terrainData.GetHeights(
                        session.TileRes - session.TileSeam,
                        session.SampleRow,
                        session.TileSeam,
                        rows);
                    var neighborSlice = neighbor.terrainData.GetHeights(0, session.SampleRow, session.NeighborSeam, rows);
                    CopyHeightSliceIntoBand(session.TileBand, session.SampleRow, tileSlice);
                    CopyHeightSliceIntoBand(session.NeighborBand, session.SampleRow, neighborSlice);
                    break;
                }
                case 2:
                {
                    var cols = rowEnd - session.SampleRow;
                    var tileSlice = tile.terrainData.GetHeights(session.SampleRow, 0, cols, session.TileSeam);
                    var neighborSlice = neighbor.terrainData.GetHeights(
                        session.SampleRow,
                        session.NeighborRes - session.NeighborSeam,
                        cols,
                        session.NeighborSeam);
                    CopyHeightSliceIntoBandTransposed(session.TileBand, session.SampleRow, tileSlice);
                    CopyHeightSliceIntoBandTransposed(session.NeighborBand, session.SampleRow, neighborSlice);
                    break;
                }
                case 3:
                {
                    var cols = rowEnd - session.SampleRow;
                    var tileSlice = tile.terrainData.GetHeights(
                        session.SampleRow,
                        session.TileRes - session.TileSeam,
                        cols,
                        session.TileSeam);
                    var neighborSlice = neighbor.terrainData.GetHeights(session.SampleRow, 0, cols, session.NeighborSeam);
                    CopyHeightSliceIntoBandTransposed(session.TileBand, session.SampleRow, tileSlice);
                    CopyHeightSliceIntoBandTransposed(session.NeighborBand, session.SampleRow, neighborSlice);
                    break;
                }
            }

            session.SampleRow = rowEnd;
            CaveBuildActionPacing.TouchQueueActivity();
            if (session.SampleRow < session.TileRes)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunSeamSampleRowChunk(session),
                    CaveBuildPipelineDomains.QueueLabel("seam edge — sample bands"));
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () => RunSeamBlendRowChunk(session),
                CaveBuildPipelineDomains.QueueLabel("seam edge blend"));
        }

        static void CopyHeightSliceIntoBand(float[,] band, int rowStart, float[,] slice)
        {
            if (band == null || slice == null)
                return;

            var rows = slice.GetLength(0);
            var cols = slice.GetLength(1);
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                    band[rowStart + r, c] = slice[r, c];
            }
        }

        static void CopyHeightSliceIntoBandTransposed(float[,] band, int colStart, float[,] slice)
        {
            if (band == null || slice == null)
                return;

            // slice is [seamDepth, alongEdge] from GetHeights; band matches BlendSeamRowsNorth/South [seam, edge].
            var seamDepth = slice.GetLength(0);
            var alongEdge = slice.GetLength(1);
            for (var s = 0; s < seamDepth; s++)
            {
                for (var a = 0; a < alongEdge; a++)
                    band[s, colStart + a] = slice[s, a];
            }
        }

        static void RunSeamBlendRowChunk(SeamBlendBandSession session)
        {
            if (session?.TileBand == null || session.NeighborBand == null || session.Edge.Tile == null)
            {
                session?.OnComplete?.Invoke();
                return;
            }

            var tile = session.Edge.Tile;
            var neighbor = session.Edge.Neighbor;
            var rowEnd = Mathf.Min(session.TileRes, session.Row + SeamBlendRowChunk(session.TileRes));

            switch (session.Orientation)
            {
                case 0:
                    BlendSeamRowsEast(session, tile, neighbor, rowEnd);
                    break;
                case 1:
                    BlendSeamRowsWest(session, tile, neighbor, rowEnd);
                    break;
                case 2:
                    BlendSeamRowsNorth(session, tile, neighbor, rowEnd);
                    break;
                case 3:
                    BlendSeamRowsSouth(session, tile, neighbor, rowEnd);
                    break;
            }

            session.Row = rowEnd;
            CaveBuildActionPacing.TouchQueueActivity();

            if (session.Row < session.TileRes)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunSeamBlendRowChunk(session),
                    CaveBuildPipelineDomains.QueueLabel("seam edge blend"));
                return;
            }

            QueueCommitSeamBlendBands(session, tile, neighbor, delayLod: true, session.OnComplete);
        }

        static void QueueCommitSeamBlendBands(
            SeamBlendBandSession session,
            Terrain tile,
            Terrain neighbor,
            bool delayLod,
            Action onComplete)
        {
            if (session == null || tile == null || neighbor == null)
            {
                onComplete?.Invoke();
                return;
            }

            void CommitNeighbor()
            {
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        switch (session.Orientation)
                        {
                            case 0:
                                CommitSeamBand(neighbor, session.NeighborRes - session.NeighborSeam, 0, session.NeighborBand, delayLod);
                                break;
                            case 1:
                                CommitSeamBand(neighbor, 0, 0, session.NeighborBand, delayLod);
                                break;
                            case 2:
                                CommitSeamBand(neighbor, 0, session.NeighborRes - session.NeighborSeam, session.NeighborBand, delayLod);
                                break;
                            case 3:
                                CommitSeamBand(neighbor, 0, 0, session.NeighborBand, delayLod);
                                break;
                        }

                        onComplete?.Invoke();
                    },
                    CaveBuildPipelineDomains.QueueLabel("seam edge — commit neighbor"));
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    switch (session.Orientation)
                    {
                        case 0:
                            CommitSeamBand(tile, 0, 0, session.TileBand, delayLod);
                            break;
                        case 1:
                            CommitSeamBand(tile, session.TileRes - session.TileSeam, 0, session.TileBand, delayLod);
                            break;
                        case 2:
                            CommitSeamBand(tile, 0, 0, session.TileBand, delayLod);
                            break;
                        case 3:
                            CommitSeamBand(tile, 0, session.TileRes - session.TileSeam, session.TileBand, delayLod);
                            break;
                    }

                    CommitNeighbor();
                },
                CaveBuildPipelineDomains.QueueLabel("seam edge — commit tile"));
        }

        static void BlendSeamRowsEast(SeamBlendBandSession session, Terrain tile, Terrain neighbor, int rowEnd)
        {
            for (var z = session.Row; z < rowEnd; z++)
            {
                var nz = MapHeightmapIndex(z, session.TileRes, session.NeighborRes);
                for (var b = 0; b < session.TileSeam; b++)
                {
                    var neighborNorm = session.NeighborBand[nz, session.NeighborSeam - 1 - MapSeamBandIndex(b, session.TileSeam, session.NeighborSeam)];
                    var pair = BlendSeamNormPairFromNorms(tile, session.TileBand[z, b], neighbor, neighborNorm, session.Strength);
                    session.TileBand[z, b] = pair.tileNorm;
                    session.NeighborBand[nz, session.NeighborSeam - 1 - MapSeamBandIndex(b, session.TileSeam, session.NeighborSeam)] = pair.neighborNorm;
                }
            }
        }

        static void BlendSeamRowsWest(SeamBlendBandSession session, Terrain tile, Terrain neighbor, int rowEnd)
        {
            for (var z = session.Row; z < rowEnd; z++)
            {
                var nz = MapHeightmapIndex(z, session.TileRes, session.NeighborRes);
                for (var b = 0; b < session.TileSeam; b++)
                {
                    var nb = MapSeamBandIndex(b, session.TileSeam, session.NeighborSeam);
                    var neighborNorm = session.NeighborBand[nz, nb];
                    var pair = BlendSeamNormPairFromNorms(tile, session.TileBand[z, b], neighbor, neighborNorm, session.Strength);
                    session.TileBand[z, b] = pair.tileNorm;
                    session.NeighborBand[nz, nb] = pair.neighborNorm;
                }
            }
        }

        static void BlendSeamRowsNorth(SeamBlendBandSession session, Terrain tile, Terrain neighbor, int rowEnd)
        {
            for (var x = session.Row; x < rowEnd; x++)
            {
                var nx = MapHeightmapIndex(x, session.TileRes, session.NeighborRes);
                for (var b = 0; b < session.TileSeam; b++)
                {
                    var neighborNorm = session.NeighborBand[session.NeighborSeam - 1 - MapSeamBandIndex(b, session.TileSeam, session.NeighborSeam), nx];
                    var pair = BlendSeamNormPairFromNorms(tile, session.TileBand[b, x], neighbor, neighborNorm, session.Strength);
                    session.TileBand[b, x] = pair.tileNorm;
                    session.NeighborBand[session.NeighborSeam - 1 - MapSeamBandIndex(b, session.TileSeam, session.NeighborSeam), nx] = pair.neighborNorm;
                }
            }
        }

        static void BlendSeamRowsSouth(SeamBlendBandSession session, Terrain tile, Terrain neighbor, int rowEnd)
        {
            for (var x = session.Row; x < rowEnd; x++)
            {
                var nx = MapHeightmapIndex(x, session.TileRes, session.NeighborRes);
                for (var b = 0; b < session.TileSeam; b++)
                {
                    var nb = MapSeamBandIndex(b, session.TileSeam, session.NeighborSeam);
                    var neighborNorm = session.NeighborBand[nb, nx];
                    var pair = BlendSeamNormPairFromNorms(tile, session.TileBand[b, x], neighbor, neighborNorm, session.Strength);
                    session.TileBand[b, x] = pair.tileNorm;
                    session.NeighborBand[nb, nx] = pair.neighborNorm;
                }
            }
        }

        static void CommitSeamBlendBands(SeamBlendBandSession session, Terrain tile, Terrain neighbor, bool delayLod)
        {
            switch (session.Orientation)
            {
                case 0:
                    CommitSeamBand(tile, 0, 0, session.TileBand, delayLod);
                    CommitSeamBand(neighbor, session.NeighborRes - session.NeighborSeam, 0, session.NeighborBand, delayLod);
                    break;
                case 1:
                    CommitSeamBand(tile, session.TileRes - session.TileSeam, 0, session.TileBand, delayLod);
                    CommitSeamBand(neighbor, 0, 0, session.NeighborBand, delayLod);
                    break;
                case 2:
                    CommitSeamBand(tile, 0, 0, session.TileBand, delayLod);
                    CommitSeamBand(neighbor, 0, session.NeighborRes - session.NeighborSeam, session.NeighborBand, delayLod);
                    break;
                case 3:
                    CommitSeamBand(tile, 0, session.TileRes - session.TileSeam, session.TileBand, delayLod);
                    CommitSeamBand(neighbor, 0, 0, session.NeighborBand, delayLod);
                    break;
            }
        }

        static void BlendSharedEdge(
            Terrain tile,
            Terrain neighbor,
            Vector2Int tileOffsetFromNeighbor,
            int seam,
            bool delayLod = false)
        {
            if (tile?.terrainData == null || neighbor?.terrainData == null)
                return;

            seam = Mathf.Max(seam, SeamWidthForPair(tile, neighbor));
            var strength = SeamBlendStrength(tile, neighbor);
            var tileRes = tile.terrainData.heightmapResolution;
            var neighborRes = neighbor.terrainData.heightmapResolution;
            var tileOrigin = tile.transform.position;
            var tileSize = tile.terrainData.size;
            var neighborOrigin = neighbor.transform.position;
            var neighborSize = neighbor.terrainData.size;
            var tileDenom = Mathf.Max(1, tileRes - 1);
            var neighborDenom = Mathf.Max(1, neighborRes - 1);
            var tileSeam = Mathf.Clamp(seam, 4, tileRes / 4);
            var neighborSeam = Mathf.Clamp(seam, 4, neighborRes / 4);

            if (tileOffsetFromNeighbor.x == 1 && tileOffsetFromNeighbor.y == 0)
            {
                var tileBand = tile.terrainData.GetHeights(0, 0, tileSeam, tileRes);
                var neighborBand = neighbor.terrainData.GetHeights(neighborRes - neighborSeam, 0, neighborSeam, neighborRes);
                for (var z = 0; z < tileRes; z++)
                {
                    var wz = tileOrigin.z + z / (float)tileDenom * tileSize.z;
                    var nz = MapHeightmapIndex(z, tileRes, neighborRes);
                    for (var b = 0; b < tileSeam; b++)
                    {
                        var nb = MapSeamBandIndex(b, tileSeam, neighborSeam);
                        var neighborNorm = neighborBand[nz, neighborSeam - 1 - nb];
                        var wx = tileOrigin.x + b / (float)tileDenom * tileSize.x;
                        var pair = BlendSeamPairAtEdge(tile, tileBand[z, b], neighbor, neighborNorm, wx, wz, strength);
                        tileBand[z, b] = pair.tileNorm;
                        neighborBand[nz, neighborSeam - 1 - nb] = pair.neighborNorm;
                    }
                }

                CommitSeamBand(tile, 0, 0, tileBand, delayLod);
                CommitSeamBand(neighbor, neighborRes - neighborSeam, 0, neighborBand, delayLod);
                return;
            }

            if (tileOffsetFromNeighbor.x == -1 && tileOffsetFromNeighbor.y == 0)
            {
                var tileBand = tile.terrainData.GetHeights(tileRes - tileSeam, 0, tileSeam, tileRes);
                var neighborBand = neighbor.terrainData.GetHeights(0, 0, neighborSeam, neighborRes);
                for (var z = 0; z < tileRes; z++)
                {
                    var wz = tileOrigin.z + z / (float)tileDenom * tileSize.z;
                    var nz = MapHeightmapIndex(z, tileRes, neighborRes);
                    for (var b = 0; b < tileSeam; b++)
                    {
                        var nb = MapSeamBandIndex(b, tileSeam, neighborSeam);
                        var neighborNorm = neighborBand[nz, nb];
                        var wx = tileOrigin.x + (tileRes - 1 - b) / (float)tileDenom * tileSize.x;
                        var pair = BlendSeamPairAtEdge(tile, tileBand[z, b], neighbor, neighborNorm, wx, wz, strength);
                        tileBand[z, b] = pair.tileNorm;
                        neighborBand[nz, nb] = pair.neighborNorm;
                    }
                }

                CommitSeamBand(tile, tileRes - tileSeam, 0, tileBand, delayLod);
                CommitSeamBand(neighbor, 0, 0, neighborBand, delayLod);
                return;
            }

            if (tileOffsetFromNeighbor.x == 0 && tileOffsetFromNeighbor.y == 1)
            {
                var tileBand = tile.terrainData.GetHeights(0, 0, tileRes, tileSeam);
                var neighborBand = neighbor.terrainData.GetHeights(0, neighborRes - neighborSeam, neighborRes, neighborSeam);
                for (var x = 0; x < tileRes; x++)
                {
                    var wx = tileOrigin.x + x / (float)tileDenom * tileSize.x;
                    var nx = MapHeightmapIndex(x, tileRes, neighborRes);
                    for (var b = 0; b < tileSeam; b++)
                    {
                        var nb = MapSeamBandIndex(b, tileSeam, neighborSeam);
                        var neighborNorm = neighborBand[neighborSeam - 1 - nb, nx];
                        var wz = tileOrigin.z + b / (float)tileDenom * tileSize.z;
                        var pair = BlendSeamPairAtEdge(tile, tileBand[b, x], neighbor, neighborNorm, wx, wz, strength);
                        tileBand[b, x] = pair.tileNorm;
                        neighborBand[neighborSeam - 1 - nb, nx] = pair.neighborNorm;
                    }
                }

                CommitSeamBand(tile, 0, 0, tileBand, delayLod);
                CommitSeamBand(neighbor, 0, neighborRes - neighborSeam, neighborBand, delayLod);
                return;
            }

            if (tileOffsetFromNeighbor.x == 0 && tileOffsetFromNeighbor.y == -1)
            {
                var tileBand = tile.terrainData.GetHeights(0, tileRes - tileSeam, tileRes, tileSeam);
                var neighborBand = neighbor.terrainData.GetHeights(0, 0, neighborRes, neighborSeam);
                for (var x = 0; x < tileRes; x++)
                {
                    var wx = tileOrigin.x + x / (float)tileDenom * tileSize.x;
                    var nx = MapHeightmapIndex(x, tileRes, neighborRes);
                    for (var b = 0; b < tileSeam; b++)
                    {
                        var nb = MapSeamBandIndex(b, tileSeam, neighborSeam);
                        var neighborNorm = neighborBand[nb, nx];
                        var wz = tileOrigin.z + (tileRes - 1 - b) / (float)tileDenom * tileSize.z;
                        var pair = BlendSeamPairAtEdge(tile, tileBand[b, x], neighbor, neighborNorm, wx, wz, strength);
                        tileBand[b, x] = pair.tileNorm;
                        neighborBand[nb, nx] = pair.neighborNorm;
                    }
                }

                CommitSeamBand(tile, 0, tileRes - tileSeam, tileBand, delayLod);
                CommitSeamBand(neighbor, 0, 0, neighborBand, delayLod);
            }
        }

        static void CommitSeamBand(Terrain tile, int xBase, int yBase, float[,] band, bool delayLod)
        {
            if (tile?.terrainData == null || band == null)
                return;

            CaveEditorUndo.RecordObject(tile.terrainData, "Stitch neighbor terrain seam");
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                CaveBuildTerrainHeightmapMemory.ApplySeamHeightBand(tile, xBase, yBase, band);
            else
                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(tile, xBase, yBase, band, delayLod);
        }

        /// <summary>Automated grid weld — no per-edge undo (avoids undo stack overflow on 49-tile passes).</summary>
        static void ApplyHeightBandDirect(Terrain tile, int xBase, int yBase, float[,] band)
        {
            if (tile?.terrainData == null || band == null)
                return;

            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(tile, xBase, yBase, band, requestDelayLod: false);
        }

        static int TileSculptSeed(int baseSeed, Vector2Int off) =>
            baseSeed + off.x * 10_007 + off.y * 79_919;

        static int TileDemSeed(int baseSeed, Vector2Int off) =>
            baseSeed + off.x * 131 + off.y * 719;

        public static bool TryParseTileOffset(string name, out Vector2Int off)
        {
            off = default;
            const string prefix = "SurfaceTerrainTile_";
            if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var rest = name.Substring(prefix.Length);
            var parts = rest.Split('_');
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y))
                return false;

            off = new Vector2Int(x, y);
            return true;
        }

        static int ResolveTileCap(bool fullWorld, WorldGenerationRequest request)
        {
            if (UsesFixedNineTileSquare(request, fullWorld))
                return MaxExtraTiles;

            var budgetCap = EnvironmentKitHardwareBudget.Active.MaxExtraTerrainTiles;
            return Mathf.Min(MaxExtraTiles, budgetCap);
        }

        static Vector2Int[] NeighborOffsets(
            float score,
            bool fullWorld,
            WorldGenerationRequest request,
            int layoutVariant,
            int seed)
        {
            if (UsesFixedNineTileSquare(request, fullWorld))
                return NineTileRingOffsets;

            var all = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
                new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1),
                new Vector2Int(2, 0), new Vector2Int(-2, 0), new Vector2Int(0, 2), new Vector2Int(0, -2),
            };

            if (!fullWorld && score <= 0.72f)
            {
                return new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
                };
            }

            var rng = new System.Random(seed + 9137 + Mathf.Max(0, layoutVariant) * 31);
            var minTiles = fullWorld ? 4 + rng.Next(0, 3) : 2 + rng.Next(0, 2);
            var maxTiles = fullWorld ? 8 : 4;
            var count = Mathf.Clamp(minTiles + (layoutVariant >= 0 ? layoutVariant % 4 : rng.Next(0, 3)), minTiles, maxTiles);

            var picked = new List<Vector2Int>(count);
            var pool = new List<Vector2Int>(all);
            while (picked.Count < count && pool.Count > 0)
            {
                var idx = rng.Next(pool.Count);
                picked.Add(pool[idx]);
                pool.RemoveAt(idx);
            }

            return picked.ToArray();
        }

        static float ScoreExpansionNeed(
            Terrain terrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            float extentMeters)
        {
            var extent = extentMeters;
            var data = terrain.terrainData;
            var size = data != null ? Mathf.Max(data.size.x, data.size.z) : 256f;
            var coverage = extent / size;
            var trailNeed = request.SurfaceIncludeTrails ? 0.22f : 0f;
            var mountainNeed = request.SurfaceIncludeMountains ? 0.12f : 0f;
            var score = Mathf.Clamp01((coverage - 0.75f) * 0.9f + trailNeed + mountainNeed);
            if (ground.Terrain != null && ground.Terrain != terrain)
                score += 0.15f;
            return score;
        }

        static void RefreshTerrainConnectivity(Terrain main, List<Terrain> added)
        {
            var group = new List<Terrain>(added.Count + 1) { main };
            group.AddRange(added);

            foreach (var terrain in group)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;

                var size = terrain.terrainData.size;
                var origin = terrain.transform.position;
                FindCardinalNeighbor(group, origin, size, out var left, out var top, out var right, out var bottom);
                terrain.SetNeighbors(
                    FilterUnityNeighbor(terrain, left),
                    FilterUnityNeighbor(terrain, top),
                    FilterUnityNeighbor(terrain, right),
                    FilterUnityNeighbor(terrain, bottom));
            }

            Terrain.SetConnectivityDirty();
        }

        static void FindCardinalNeighbor(
            List<Terrain> group,
            Vector3 origin,
            Vector3 size,
            out Terrain left,
            out Terrain top,
            out Terrain right,
            out Terrain bottom)
        {
            left = top = right = bottom = null;
            const float eps = 0.5f;

            foreach (var other in group)
            {
                if (other == null)
                    continue;

                var delta = other.transform.position - origin;
                if (Mathf.Abs(delta.y) > 2f)
                    continue;

                if (Mathf.Abs(delta.x + size.x) < eps && Mathf.Abs(delta.z) < eps)
                    left = other;
                else if (Mathf.Abs(delta.x - size.x) < eps && Mathf.Abs(delta.z) < eps)
                    right = other;
                else if (Mathf.Abs(delta.z - size.z) < eps && Mathf.Abs(delta.x) < eps)
                    top = other;
                else if (Mathf.Abs(delta.z + size.z) < eps && Mathf.Abs(delta.x) < eps)
                    bottom = other;
            }
        }

        /// <summary>Snap/weld/consolidate grid — skip purge when repair succeeds.</summary>
        public static bool TryRepairFullWorldGridInPlace(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (mainTerrain?.terrainData == null || request == null ||
                request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return false;

            ConsolidateScatteredFullWorldTerrains(mainTerrain);
            PurgeOrphanPlayDiskTerrains(mainTerrain);
            PurgeOrphanWildernessTerrains(mainTerrain);

            var session = CreateMinimalFullWorldGridSession(mainTerrain);
            if (session == null)
                return false;

            session.GridAnchorLocked = true;
            session.LockedMainOrigin = mainTerrain.transform.position;
            EnsureFullWorldGridAnchorTransform(session);
            ForceSnapEntireFullWorldGrid(session);
            WeldAllFullWorldGridEdgesFromScene(mainTerrain);

            if (ground != null)
                EnforcePlayDiskGridLayout(mainTerrain, ground);

            var playTiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain).Count;
            if (playTiles < 9)
                return false;

            CaveBuildEditorLog.LogSurface(
                $"[PipelinePreserve] FullWorld terrain grid repaired in place ({playTiles} play tile(s)) — no purge.",
                forceUnityConsole: true);
            return true;
        }

        static void RemoveStaleGameplayTiles(Transform tilesRoot)
        {
            if (tilesRoot == null)
                return;

            for (var i = tilesRoot.childCount - 1; i >= 0; i--)
            {
                var child = tilesRoot.GetChild(i);
                if (child == null ||
                    !child.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
                    continue;

                var terrain = child.GetComponent<Terrain>();
                if (terrain?.terrainData != null)
                    continue;

                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        static void RemoveStaleMountainWildernessTiles(Transform wildernessRoot)
        {
            if (wildernessRoot == null)
                return;

            for (var i = wildernessRoot.childCount - 1; i >= 0; i--)
            {
                var child = wildernessRoot.GetChild(i);
                if (child == null || !IsOuterRingTerrainName(child.name))
                    continue;

                var terrain = child.GetComponent<Terrain>();
                if (terrain?.terrainData != null)
                    continue;

                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        public static bool IsOuterRingTerrainName(string name) =>
            !string.IsNullOrEmpty(name) &&
            (name.StartsWith(MountainFoothillTileNamePrefix, StringComparison.Ordinal) ||
             name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal) ||
             name.StartsWith(MountainHorizonTileNamePrefix, StringComparison.Ordinal) ||
             name.StartsWith(OpenWorldTileNamePrefix, StringComparison.Ordinal) ||
             name.StartsWith(MountainWildernessTileNamePrefix, StringComparison.Ordinal));

        public static bool TryParseOuterRingTileOffset(string name, out Vector2Int off) =>
            TryParsePrefixedOffset(name, MountainFoothillTileNamePrefix, out off) ||
            TryParsePrefixedOffset(name, MountainPeakTileNamePrefix, out off) ||
            TryParsePrefixedOffset(name, MountainHorizonTileNamePrefix, out off) ||
            TryParsePrefixedOffset(name, OpenWorldTileNamePrefix, out off) ||
            TryParsePrefixedOffset(name, MountainWildernessTileNamePrefix, out off);

        static bool TryParsePrefixedOffset(string name, string prefix, out Vector2Int off)
        {
            off = default;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var rest = name.Substring(prefix.Length);
            var parts = rest.Split('_');
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y))
                return false;

            off = new Vector2Int(x, y);
            return true;
        }

        /// <summary>Spawns foothill (16) then peak (24) rings — 49-tile FullWorld, one tile per editor frame.</summary>
        public static void QueueAttachMountainWildernessTiles(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Action<int, string> onComplete) =>
            QueueAttachFullWorldOuterRings(mainTerrain, request, onComplete);

        public static void QueueAttachFullWorldOuterRings(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Action<int, string> onComplete)
        {
            if (onComplete == null)
                return;

            request?.EnsureFullWorldSurfaceContract();

            if (mainTerrain == null || request == null || !request.SurfaceIncludeMountains ||
                !request.UseOuterRingMountains ||
                !UsesFixedNineTileSquare(request, request.SurfaceScope == SurfaceBuildScope.FullWorld))
            {
                onComplete(0, "Outer rings skipped — not FullWorld outer-ring build.");
                return;
            }

            if (!SurfaceTerrainGridRegistry.ValidatePlayDiskComplete(mainTerrain, out var playDiskMsg))
            {
                onComplete(0, playDiskMsg);
                return;
            }

            var gameplay = CollectValidatedGameplayTiles(mainTerrain, out var misnamed);
            if (misnamed.Count > 0)
            {
                onComplete(
                    0,
                    $"Outer rings skipped — misnamed play tile(s): {string.Join(", ", misnamed)}.");
                return;
            }

            if (gameplay.Length < NineTileRingOffsets.Length)
            {
                onComplete(
                    0,
                    $"Outer rings skipped — need 9-tile play square ({gameplay.Length + 1} terrains).");
                return;
            }

            var data = mainTerrain.terrainData;
            if (data == null)
            {
                onComplete(0, "Outer rings skipped — no main terrain data.");
                return;
            }

            var wildernessRoot = GetOrCreateMountainWildernessRoot(mainTerrain);
            RemoveStaleMountainWildernessTiles(wildernessRoot);

            CaveBuildEditorLog.LogSurface(
                $"[Surface] FullWorld outer rings — {FoothillRingTileCount} foothill + {PeakRingTileCount} peak + " +
                $"{HorizonRingTileCount} horizon ({FullWorldTerrainTileCount} terrains total); " +
                "each tile: seed → sculpt → seam → lock, then next.",
                forceUnityConsole: true);

            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            EnforcePlayDiskGridLayout(mainTerrain, ground);

            QueueAttachOuterRing(
                mainTerrain,
                request,
                wildernessRoot,
                FoothillRingOffsets,
                MountainFoothillTileNamePrefix,
                "foothill",
                foothillCount =>
                {
                    QueueFinalizeFoothillRingToPlayDisk(mainTerrain, () =>
                        CaveBuildLayoutAuditMilestones.QueueAfterFoothillRing(
                            mainTerrain,
                            ground,
                            request,
                            () =>
                            {
                                if (CaveBuildWorldLayoutAudit.LastReport?.blocksMountainRing == true &&
                                    !CaveBuildLayoutAuditMilestones.LayoutPlanForce)
                                {
                                    CaveBuildEditorLog.LogSurfaceWarning(
                                        "[Surface] Foothill-ring audit failed — continuing peak ring after targeted fix attempt.");
                                }

                                QueueAttachOuterRing(
                                    mainTerrain,
                                    request,
                                    wildernessRoot,
                                    PeakRingOffsets,
                                    MountainPeakTileNamePrefix,
                                    "peak",
                                    peakCount =>
                                    {
                                        QueueAttachOuterRing(
                                            mainTerrain,
                                            request,
                                            wildernessRoot,
                                            HorizonRingOffsets,
                                            MountainHorizonTileNamePrefix,
                                            "horizon",
                                            horizonCount =>
                                            {
                                                var total = foothillCount + peakCount + horizonCount;
                                                var msg =
                                                    $"FullWorld 9×9 outer rings — {foothillCount} foothill + {peakCount} peak + " +
                                                    $"{horizonCount} horizon ({total}/{MountainWildernessTileCount}); " +
                                                    "each tile sculpted, seamed, and locked before the next.";
                                                CaveBuildEditorLog.LogSurface(msg, forceUnityConsole: true);
                                                onComplete(total, msg);
                                            });
                                    });
                            }));
                });
        }

        /// <summary>
        /// After foothill ring spawn: snap tiles to the 7×7 grid, stitch play perimeter, lock inner edges.
        /// </summary>
        static void QueueFinalizeFoothillRingToPlayDisk(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    EnforceWildernessGridLayout(mainTerrain);
                    QueueStitchPlayPerimeterToFoothills(mainTerrain, () =>
                        QueueLockFoothillInnerEdgesToPlayDisk(mainTerrain, onComplete));
                },
                CaveBuildPipelineDomains.QueueLabel("foothill ring — play-disk seams"));
        }

        /// <summary>
        /// Snaps foothill/peak tiles to tile-size spacing from main, sharing play-disk Y.
        /// Scans the whole scene — not only children of the wilderness root (strays stay scattered otherwise).
        /// </summary>
        public static void EnforceWildernessGridLayout(Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData == null)
                return;

            var root = GetOrCreateMountainWildernessRoot(mainTerrain);
            var anchor = FindFullWorldGridAnchor(mainTerrain);
            if (anchor != null)
            {
                anchor.SetPositionAndRotation(mainTerrain.transform.position, Quaternion.identity);
                anchor.localScale = Vector3.one;
            }

            var moved = 0;
            var reparented = 0;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain || terrain.terrainData == null)
                    continue;

                if (!IsOuterRingTerrainName(terrain.name) ||
                    !TryParseOuterRingTileOffset(terrain.name, out var off))
                    continue;

                if (anchor != null)
                {
                    if (root != null && !terrain.transform.IsChildOf(root))
                    {
                        terrain.transform.SetParent(root, false);
                        reparented++;
                    }

                    var before = terrain.transform.position;
                    TryApplyFullWorldGridSlotFromMain(mainTerrain, terrain, off);
                    if ((before - terrain.transform.position).sqrMagnitude > 0.01f)
                        moved++;
                    continue;
                }

                if (root != null && !terrain.transform.IsChildOf(root))
                {
                    terrain.transform.SetParent(root, true);
                    reparented++;
                }

                var beforeLegacy = terrain.transform.position;
                ApplyWildernessTileGridSlot(mainTerrain, terrain, off);
                if ((beforeLegacy - terrain.transform.position).sqrMagnitude > 0.01f)
                    moved++;
            }

            if (anchor != null)
            {
                var tilesRoot = FindTilesRoot(mainTerrain);
                foreach (var off in NineTileRingOffsets)
                {
                    if (!TryFindTileAtOffset(tilesRoot, off, out var playTile) || playTile == null)
                        continue;

                    var beforePlay = playTile.transform.position;
                    ApplyPlayTileGridSlot(mainTerrain, playTile, off);
                    if ((beforePlay - playTile.transform.position).sqrMagnitude > 0.01f)
                        moved++;
                }
            }

            if (moved > 0 || reparented > 0)
            {
                RefreshMountainTerrainConnectivity(mainTerrain);
                CaveBuildEditorLog.LogSurface(
                    anchor != null
                        ? $"[Surface] Wilderness grid — anchor snap realigned {moved} tile(s), reparented {reparented} stray(s)."
                        : $"[Surface] Wilderness grid — realigned {moved} tile(s), reparented {reparented} stray(s) to {MountainWildernessTilesRootName}.",
                    forceUnityConsole: true);
            }
        }

        sealed class AttachOuterRingSession
        {
            public Terrain MainTerrain;
            public WorldGenerationRequest Request;
            public Vector3 TileSize;
            public Vector3 MainOrigin;
            public Transform OuterRoot;
            public Transform GameplayRoot;
            public Vector2Int[] Offsets;
            public string TilePrefix;
            public string RingLabel;
            public int OffsetIndex;
            public int Count;
            public bool WaitingForSeed;
            public bool GridEnforced;
            public Action<int> OnComplete;
        }

        static void QueueAttachOuterRing(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Transform outerRoot,
            Vector2Int[] offsets,
            string tilePrefix,
            string ringLabel,
            Action<int> onComplete)
        {
            var data = mainTerrain.terrainData;
            var session = new AttachOuterRingSession
            {
                MainTerrain = mainTerrain,
                Request = request,
                TileSize = data.size,
                MainOrigin = mainTerrain.transform.position,
                OuterRoot = outerRoot,
                GameplayRoot = FindTilesRoot(mainTerrain),
                Offsets = offsets,
                TilePrefix = tilePrefix,
                RingLabel = ringLabel,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunAttachOuterRingFrame(session),
                CaveBuildPipelineDomains.QueueLabel($"mountain {ringLabel} — spawn ring"));
        }

        static void RunAttachOuterRingFrame(AttachOuterRingSession session)
        {
            if (session?.MainTerrain == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            if (session.WaitingForSeed)
                return;

            session.MainOrigin = session.MainTerrain.transform.position;
            session.TileSize = session.MainTerrain.terrainData.size;

            if (!session.GridEnforced)
            {
                EnforceWildernessGridLayout(session.MainTerrain);
                session.GridEnforced = true;
            }

            while (session.OffsetIndex < session.Offsets.Length)
            {
                var off = session.Offsets[session.OffsetIndex++];
                if (TryFindOuterRingTileAtOffset(session.OuterRoot, off, session.TilePrefix, out var existing) &&
                    existing != null)
                {
                    ApplyWildernessTileGridSlot(session.MainTerrain, existing, off);
                    var existingPos = ComputeGridSlotWorldOrigin(session.MainTerrain, off);
                    session.WaitingForSeed = true;
                    CaveBuildRunStatusPublisher.PulseSubOperation(
                        $"mountain {session.RingLabel} tiles",
                        $"re-seed existing {existing.name} at ({existingPos.x:F0}, {existingPos.z:F0})");
                    QueueSeedWildernessHeightsFromInnerRing(
                        existing,
                        session.MainTerrain,
                        off,
                        session.GameplayRoot,
                        session.OuterRoot,
                        () => QueueStitchWildernessAfterSeed(session, existing, () =>
                        {
                            EnforceWildernessGridLayout(session.MainTerrain);
                            QueueCompleteOuterRingTileAfterSeed(session, existing, () =>
                            {
                                session.WaitingForSeed = false;
                                session.Count++;
                                CaveBuildActionPacing.ScheduleLight(
                                    () => RunAttachOuterRingFrame(session),
                                    CaveBuildPipelineDomains.QueueLabel($"mountain {session.RingLabel} — next tile"));
                            });
                        }));
                    return;
                }

                var tileData = CreateWildernessTileData(session.MainTerrain.terrainData);
                tileData.name = $"SurfaceMountainOuterData_{session.RingLabel}_{off.x}_{off.y}";
                var go = Terrain.CreateTerrainGameObject(tileData);
                go.name = $"{session.TilePrefix}{off.x}_{off.y}";
                CaveEditorUndo.RegisterCreated(go, $"Mountain {session.RingLabel} terrain tile");
                TryTagAsGround(go);
                go.transform.SetParent(session.OuterRoot, false);
                go.transform.localScale = Vector3.one;
                var tile = go.GetComponent<Terrain>();
                if (tile == null)
                    continue;

                ApplyWildernessTileGridSlot(session.MainTerrain, tile, off);
                var pos = tile.transform.position;

                var ringId = session.TilePrefix.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal)
                    ? SurfaceTerrainGridIndex.PeakRing
                    : SurfaceTerrainGridIndex.FoothillRing;
                var mergeOff = SurfaceTerrainGridRegistry.StepTowardOrigin(off);
                var mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                if (TryResolveTerrainAtGridOffset(
                        session.MainTerrain,
                        session.GameplayRoot,
                        session.OuterRoot,
                        mergeOff,
                        out var mergeTerrain) &&
                    mergeTerrain != null)
                {
                    if (mergeTerrain == session.MainTerrain)
                        mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                    else if (TryParseTileOffset(mergeTerrain.name, out var mo))
                        mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayNeighborRing, mo.x, mo.y);
                    else if (TryParseOuterRingTileOffset(mergeTerrain.name, out var fo))
                        mergeId = SurfaceTerrainGridIndex.BuildTileId(ringId, fo.x, fo.y);
                }

                SurfaceTerrainGridRegistry.ApplyIndex(tile, ringId, off, mergeId);

                session.WaitingForSeed = true;
                var total = session.Offsets.Length;
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    $"mountain {session.RingLabel} tiles",
                    $"spawn {session.Count + 1}/{total} ({go.name}) merge←{mergeId}");

                QueueSeedWildernessHeightsFromInnerRing(
                    tile,
                    session.MainTerrain,
                    off,
                    session.GameplayRoot,
                    session.OuterRoot,
                    () => QueueStitchWildernessAfterSeed(session, tile, () =>
                    {
                        EnforceWildernessGridLayout(session.MainTerrain);
                        QueueCompleteOuterRingTileAfterSeed(session, tile, () =>
                        {
                            session.WaitingForSeed = false;
                            session.Count++;
                            CaveBuildEditorLog.LogSurface(
                                $"[Surface] {session.RingLabel} tile {go.name} at ({pos.x:F0}, {pos.z:F0}) — sculpted, seamed, inner edge locked.",
                                forceUnityConsole: false);
                            CaveBuildActionPacing.ScheduleLight(
                                () => RunAttachOuterRingFrame(session),
                                CaveBuildPipelineDomains.QueueLabel($"mountain {session.RingLabel} — next tile"));
                        });
                    }));
                return;
            }

            if (session.Count > 0)
            {
                EnforceWildernessGridLayout(session.MainTerrain);
                RefreshMountainTerrainConnectivity(session.MainTerrain);
            }

            SurfaceTerrainGridRegistry.ReindexFromMain(
                session.MainTerrain,
                SurfaceFloridaDemBuildState.NineTilePlayDiskPolishCompletedThisBuild);

            session.OnComplete?.Invoke(session.Count);
        }

        static bool ValidateNineTilePlayDiskForOuterRing(Terrain mainTerrain, out string message) =>
            SurfaceTerrainGridRegistry.ValidatePlayDiskComplete(mainTerrain, out message);

        public static bool TryResolveTerrainAtGridOffset(
            Terrain main,
            Transform gameplayRoot,
            Transform wildernessRoot,
            Vector2Int off,
            out Terrain terrain) =>
            TryResolveTerrainAtGridOffsetInternal(main, gameplayRoot, wildernessRoot, off, out terrain);

        public static Terrain[] CollectMountainFoothillTiles(Terrain mainTerrain) =>
            CollectOuterRingTiles(mainTerrain, MountainFoothillTileNamePrefix);

        public static Terrain[] CollectMountainPeakTiles(Terrain mainTerrain) =>
            CollectOuterRingTiles(mainTerrain, MountainPeakTileNamePrefix);

        public static Terrain[] CollectMountainHorizonTiles(Terrain mainTerrain) =>
            CollectOuterRingTiles(mainTerrain, MountainHorizonTileNamePrefix);

        public static Terrain[] CollectOpenWorldTiles(Terrain mainTerrain) =>
            CollectOuterRingTiles(mainTerrain, OpenWorldTileNamePrefix);

        public static Terrain[] CollectMountainWildernessTiles(Terrain mainTerrain)
        {
            var foothill = CollectMountainFoothillTiles(mainTerrain);
            var peak = CollectMountainPeakTiles(mainTerrain);
            var horizon = CollectMountainHorizonTiles(mainTerrain);
            var openWorld = CollectOpenWorldTiles(mainTerrain);
            if (foothill.Length == 0 && peak.Length == 0 && horizon.Length == 0 && openWorld.Length == 0)
                return Array.Empty<Terrain>();

            var all = new Terrain[
                foothill.Length + peak.Length + horizon.Length + openWorld.Length];
            var at = 0;
            Array.Copy(foothill, 0, all, at, foothill.Length);
            at += foothill.Length;
            Array.Copy(peak, 0, all, at, peak.Length);
            at += peak.Length;
            Array.Copy(horizon, 0, all, at, horizon.Length);
            at += horizon.Length;
            Array.Copy(openWorld, 0, all, at, openWorld.Length);
            return all;
        }

        static Terrain[] CollectOuterRingTiles(Terrain mainTerrain, string prefix)
        {
            if (mainTerrain == null)
                return Array.Empty<Terrain>();

            var root = FindMountainWildernessRoot(mainTerrain);
            if (root == null)
                return Array.Empty<Terrain>();

            var tiles = new List<Terrain>(PeakRingTileCount);
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var tile = child.GetComponent<Terrain>();
                if (tile != null && tile.terrainData != null)
                    tiles.Add(tile);
            }

            return tiles.ToArray();
        }

        public static bool TryFindMountainWildernessTileAtOffset(
            Transform wildernessRoot,
            Vector2Int off,
            out Terrain tile) =>
            TryFindOuterRingTileAtOffset(wildernessRoot, off, null, out tile);

        static bool TryFindOuterRingTileAtOffset(
            Transform wildernessRoot,
            Vector2Int off,
            string prefix,
            out Terrain tile)
        {
            tile = null;
            if (wildernessRoot == null)
                return false;

            if (!string.IsNullOrEmpty(prefix))
            {
                var expected = $"{prefix}{off.x}_{off.y}";
                for (var i = 0; i < wildernessRoot.childCount; i++)
                {
                    var child = wildernessRoot.GetChild(i);
                    if (child.name != expected)
                        continue;

                    tile = child.GetComponent<Terrain>();
                    if (tile != null)
                        return true;
                }

                return false;
            }

            if (TryFindOuterRingTileAtOffset(wildernessRoot, off, MountainFoothillTileNamePrefix, out tile))
                return true;

            if (TryFindOuterRingTileAtOffset(wildernessRoot, off, MountainPeakTileNamePrefix, out tile))
                return true;

            return TryFindOuterRingTileAtOffset(wildernessRoot, off, MountainHorizonTileNamePrefix, out tile);
        }

        public static bool TryParseMountainWildernessOffset(string name, out Vector2Int off) =>
            TryParseOuterRingTileOffset(name, out off);

        public static bool TryParseAnySurfaceTileOffset(string name, out Vector2Int off) =>
            TryParseTileOffset(name, out off) || TryParseOuterRingTileOffset(name, out off);

        public static bool TrySamplePlayDiskNormAtWorld(Terrain mainTerrain, float worldX, float worldZ, out float norm)
        {
            norm = -1f;
            if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, worldX, worldZ, out var tile) ||
                tile?.terrainData == null)
                return false;

            if (tile != mainTerrain)
            {
                if (!tile.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal) ||
                    !TryParseTileOffset(tile.name, out var off) ||
                    !IsNineTileGameplayOffset(off))
                    return false;
            }

            var worldY = SampleTerrainWorldY(tile, worldX, worldZ);
            norm = WorldYToNorm(tile, worldY);
            return true;
        }

        /// <summary>Sample play-disk height at the point on the nine-tile AABB closest to world XZ (for wilderness foothills).</summary>
        public static bool TrySamplePlayDiskNormNearAabb(
            Terrain mainTerrain,
            float worldX,
            float worldZ,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            out float norm)
        {
            var sx = Mathf.Clamp(worldX, playMinX, playMaxX);
            var sz = Mathf.Clamp(worldZ, playMinZ, playMaxZ);
            return TrySamplePlayDiskNormAtWorld(mainTerrain, sx, sz, out norm);
        }

        public static void ComputePlayDiskWorldBounds(Terrain mainTerrain, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = maxX = minZ = maxZ = 0f;
            if (mainTerrain?.terrainData == null)
                return;

            var boundsMinX = 0f;
            var boundsMaxX = 0f;
            var boundsMinZ = 0f;
            var boundsMaxZ = 0f;
            var first = true;

            void Include(Terrain t)
            {
                if (t?.terrainData == null)
                    return;

                var o = t.transform.position;
                var s = t.terrainData.size;
                var tx0 = o.x;
                var tx1 = o.x + s.x;
                var tz0 = o.z;
                var tz1 = o.z + s.z;
                if (first)
                {
                    boundsMinX = tx0;
                    boundsMaxX = tx1;
                    boundsMinZ = tz0;
                    boundsMaxZ = tz1;
                    first = false;
                    return;
                }

                boundsMinX = Mathf.Min(boundsMinX, tx0);
                boundsMaxX = Mathf.Max(boundsMaxX, tx1);
                boundsMinZ = Mathf.Min(boundsMinZ, tz0);
                boundsMaxZ = Mathf.Max(boundsMaxZ, tz1);
            }

            Include(mainTerrain);
            foreach (var tile in CollectGameplayTiles(mainTerrain))
                Include(tile);

            minX = boundsMinX;
            maxX = boundsMaxX;
            minZ = boundsMinZ;
            maxZ = boundsMaxZ;
        }

        static float DistanceOutsidePlayAabb(
            float worldX,
            float worldZ,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            var dx = worldX < playMinX ? playMinX - worldX : worldX > playMaxX ? worldX - playMaxX : 0f;
            var dz = worldZ < playMinZ ? playMinZ - worldZ : worldZ > playMaxZ ? worldZ - playMaxZ : 0f;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static void ComputeFoothillRingWorldBounds(Terrain mainTerrain, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            ComputePlayDiskWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);
            if (mainTerrain?.terrainData == null)
                return;

            var pad = Mathf.Max(mainTerrain.terrainData.size.x, mainTerrain.terrainData.size.z);
            minX -= pad;
            maxX += pad;
            minZ -= pad;
            maxZ += pad;
        }

        public static bool TrySampleFoothillOrPlayNormAtWorld(Terrain mainTerrain, float worldX, float worldZ, out float norm)
        {
            if (TrySamplePlayDiskNormAtWorld(mainTerrain, worldX, worldZ, out norm))
                return true;

            foreach (var foothill in CollectMountainFoothillTiles(mainTerrain))
            {
                if (foothill?.terrainData == null)
                    continue;
                if (!SurfaceTerrainPlayRegion.ContainsWorldXZ(foothill, worldX, worldZ, insetMeters: 0.05f))
                    continue;

                norm = SampleTerrainNormAtWorld(foothill, worldX, worldZ);
                return true;
            }

            norm = -1f;
            return false;
        }

        static void QueueCompleteOuterRingTileAfterSeed(
            AttachOuterRingSession session,
            Terrain tile,
            Action onComplete)
        {
            if (session?.MainTerrain == null || tile == null || session.Request == null)
            {
                onComplete?.Invoke();
                return;
            }

            void FinishTile()
            {
                if (tile.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal))
                {
                    if (IsLiveFullWorldTerraformPhase)
                        PulseLiveTerraformMicro("dress", 4, 4, tile);
                    var surfaceRoot = SurfaceTerrainQualityGrader.ResolveSurfaceRoot();
                    SurfaceMountainPeakDressingAuthor.QueueDressSinglePeakTile(
                        tile,
                        surfaceRoot,
                        session.Request.Seed,
                        onComplete);
                    return;
                }

                if (IsLiveFullWorldTerraformPhase)
                    PulseLiveTerraformMicro("done", 4, 4, tile);
                onComplete?.Invoke();
            }

            var deferSeams = PreferDeferSeamsUntilFullWorldTerraformComplete &&
                             _liveFullWorldGridPhase == FullWorldGridPhase.Terraform;

            if (CaveBuildTerrainFingerprint.ShouldSkipWildernessTileSculpt(
                    tile,
                    session.MainTerrain,
                    session.Request.Seed,
                    out var skipReason))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] {tile.name} — sculpt skipped ({skipReason}).",
                    forceUnityConsole: false);
                if (deferSeams)
                {
                    CaveBuildTerrainFingerprint.RecordWildernessTile(
                        session.Request.Seed,
                        tile,
                        session.MainTerrain);
                    QueueIncrementalFullWorldTileEdgeWeld(session.MainTerrain, tile, FinishTile);
                    return;
                }

                QueueStitchLayoutTile(
                    session.MainTerrain,
                    tile,
                    () =>
                    {
                        QueueLockSingleMountainWildernessInnerEdge(
                            session.MainTerrain,
                            tile,
                            () =>
                            {
                                CaveBuildTerrainFingerprint.RecordWildernessTile(
                                    session.Request.Seed,
                                    tile,
                                    session.MainTerrain);
                                FinishTile();
                            });
                    });
                return;
            }

            SurfaceOuterRingMountainsAuthor.QueueCompleteSingleWildernessTile(
                session.MainTerrain,
                tile,
                session.Request,
                () =>
                {
                    CaveBuildTerrainFingerprint.RecordWildernessTile(
                        session.Request.Seed,
                        tile,
                        session.MainTerrain);
                    if (deferSeams)
                        QueueIncrementalFullWorldTileEdgeWeld(session.MainTerrain, tile, FinishTile);
                    else
                        FinishTile();
                },
                deferSeams: deferSeams);
        }

        public static Terrain[] BuildMountainWildernessStitchGroup(Terrain mainTerrain)
        {
            var wilderness = CollectMountainWildernessTiles(mainTerrain);
            var group = new List<Terrain>(1 + NineTileRingOffsets.Length + wilderness.Length)
            {
                mainTerrain,
            };
            group.AddRange(CollectGameplayTiles(mainTerrain));
            group.AddRange(wilderness);
            return group.ToArray();
        }

        /// <summary>Seam one wilderness tile to play ring and already-placed neighbors (paced).</summary>
        public static void QueueStitchSingleMountainWildernessTile(
            Terrain mainTerrain,
            Terrain tile,
            Action onComplete) =>
            QueueStitchLayoutTile(mainTerrain, tile, onComplete);

        /// <summary>Seam any play or wilderness tile (layout audit targeted fix).</summary>
        public static void QueueStitchLayoutTile(Terrain mainTerrain, Terrain tile, Action onComplete)
        {
            if (mainTerrain == null || tile?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var off))
            {
                onComplete?.Invoke();
                return;
            }

            var group = BuildMountainWildernessStitchGroup(mainTerrain);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                _liveFullWorldGridPhase == FullWorldGridPhase.SeamAll ? "FullWorld seams" : "layout seams",
                $"stitch {tile.name}");
            QueueStitchTileSeamsPaced(tile, mainTerrain, off, group, onComplete);
        }

        static int LockSingleMountainWildernessInnerEdgeSync(Terrain mainTerrain, Terrain tile)
        {
            if (mainTerrain == null || tile?.terrainData == null)
                return 0;

            var isPeakOrHorizon =
                tile.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal) ||
                tile.name.StartsWith(MountainHorizonTileNamePrefix, StringComparison.Ordinal) ||
                tile.name.StartsWith(OpenWorldTileNamePrefix, StringComparison.Ordinal);
            float minX, maxX, minZ, maxZ;
            if (isPeakOrHorizon)
                ComputeFoothillRingWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);
            else
                ComputePlayDiskWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);

            var lockMeters = SurfaceOuterRingMountainsAuthor.WildernessInnerLockMeters + 4f;
            return LockWildernessInnerEdgeBandSync(
                mainTerrain,
                tile,
                minX,
                maxX,
                minZ,
                maxZ,
                lockMeters,
                sampleFoothillForPeaks: isPeakOrHorizon);
        }

        /// <summary>Lock inner band of one foothill/peak tile to play or foothill heights (paced).</summary>
        public static void QueueLockSingleMountainWildernessInnerEdge(
            Terrain mainTerrain,
            Terrain tile,
            Action onComplete)
        {
            if (mainTerrain == null || tile?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var isPeakOrHorizon =
                tile.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal) ||
                tile.name.StartsWith(MountainHorizonTileNamePrefix, StringComparison.Ordinal) ||
                tile.name.StartsWith(OpenWorldTileNamePrefix, StringComparison.Ordinal);
            float minX, maxX, minZ, maxZ;
            if (isPeakOrHorizon)
                ComputeFoothillRingWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);
            else
                ComputePlayDiskWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);

            var lockMeters = SurfaceOuterRingMountainsAuthor.WildernessInnerLockMeters + 4f;
            QueueLockWildernessInnerEdgeBand(
                mainTerrain,
                tile,
                minX,
                maxX,
                minZ,
                maxZ,
                lockMeters,
                sampleFoothillForPeaks: isPeakOrHorizon,
                touched =>
                {
                    if (touched > 0)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] {tile.name} — inner edge locked ({touched} samples).",
                            forceUnityConsole: false);
                        tile.Flush();
                    }

                    onComplete?.Invoke();
                });
        }

        const int CorridorSeamStitchBatchMaxTiles = 16;

        static void QueueStitchSurfaceTilesBatchSync(
            Terrain mainTerrain,
            Terrain[] tiles,
            Terrain[] group,
            bool stitchPlayPerimeterAfter,
            Action onComplete)
        {
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    for (var i = 0; i < tiles.Length; i++)
                    {
                        var tile = tiles[i];
                        if (tile?.terrainData == null)
                            continue;

                        if (!TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var off))
                            off = Vector2Int.zero;

                        CaveBuildRunStatusPublisher.PulseSubOperation("surface seams", $"stitch {tile.name}");
                        var edges = CollectSeamEdges(tile, mainTerrain, off, group);
                        for (var e = 0; e < edges.Count; e++)
                            BlendSharedEdgeSync(edges[e]);
                        tile.Flush();
                    }

                    RefreshMountainTerrainConnectivity(mainTerrain);
                    if (stitchPlayPerimeterAfter)
                        QueueStitchPlayPerimeterToWilderness(mainTerrain, onComplete);
                    else
                        onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel($"surface seams — batch ({tiles.Length} tiles)"));
        }

        /// <summary>After labyrinth corridor carve: seam only tiles touched by that segment.</summary>
        public static void QueueStitchMountainTilesTouchedByCorridor(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> touched,
            Action onComplete)
        {
            if (mainTerrain == null || touched == null || touched.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var stitchTiles = new List<Terrain>(touched.Count);
            foreach (var tile in touched)
            {
                if (tile?.terrainData == null)
                    continue;
                if (tile == mainTerrain ||
                    tile.name.StartsWith(MountainFoothillTileNamePrefix, StringComparison.Ordinal) ||
                    tile.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal) ||
                    TryParseTileOffset(tile.name, out _))
                    stitchTiles.Add(tile);
            }

            if (stitchTiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var group = BuildMountainWildernessStitchGroup(mainTerrain);
            QueueStitchSurfaceTilesAtIndex(
                mainTerrain,
                stitchTiles.ToArray(),
                group,
                0,
                stitchPlayPerimeterAfter: false,
                onComplete,
                forceBatchAllTiles: true);
        }

        static void QueueStitchSurfaceTilesAtIndex(
            Terrain mainTerrain,
            Terrain[] tiles,
            Terrain[] group,
            int index,
            bool stitchPlayPerimeterAfter,
            Action onComplete,
            bool forceBatchAllTiles = false)
        {
            if (index >= tiles.Length)
            {
                if (stitchPlayPerimeterAfter)
                {
                    RefreshMountainTerrainConnectivity(mainTerrain);
                    QueueStitchPlayPerimeterToWilderness(mainTerrain, onComplete);
                }
                else
                {
                    RefreshMountainTerrainConnectivity(mainTerrain);
                    onComplete?.Invoke();
                }

                return;
            }

            var tile = tiles[index];
            if (tile?.terrainData == null ||
                !TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var off))
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => QueueStitchSurfaceTilesAtIndex(
                        mainTerrain,
                        tiles,
                        group,
                        index + 1,
                        stitchPlayPerimeterAfter,
                        onComplete),
                    CaveBuildPipelineDomains.QueueLabel("surface seams — next tile"));
                return;
            }

            CaveBuildRunStatusPublisher.PulseSubOperation("surface seams", $"stitch {tile.name}");
            QueueStitchTileSeamsPaced(
                tile,
                mainTerrain,
                off,
                group,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueStitchSurfaceTilesAtIndex(
                            mainTerrain,
                            tiles,
                            group,
                            index + 1,
                            stitchPlayPerimeterAfter,
                            onComplete),
                        CaveBuildPipelineDomains.QueueLabel("surface seams — next tile"));
                });
        }

        internal static bool TryResolveSurfaceTileGridOffset(Terrain mainTerrain, Terrain tile, out Vector2Int off)
        {
            off = default;
            if (tile == null)
                return false;

            var index = tile.GetComponent<SurfaceTerrainGridIndex>();
            if (index != null)
            {
                off = new Vector2Int(index.gridX, index.gridZ);
                return true;
            }

            if (TryParseMountainWildernessOffset(tile.name, out off))
                return true;
            if (TryParsePlayDiskGridOffset(mainTerrain, tile, out off))
                return true;
            return TryParseTileOffset(tile.name, out off);
        }

        public static void QueueLockFoothillInnerEdgesToPlayDisk(Terrain mainTerrain, Action onComplete) =>
            QueueLockOuterRingInnerEdges(
                mainTerrain,
                CollectMountainFoothillTiles(mainTerrain),
                useFoothillBounds: false,
                "foothill → play",
                onComplete);

        public static void QueueLockPeakInnerEdgesToFoothillRing(Terrain mainTerrain, Action onComplete) =>
            QueueLockOuterRingInnerEdges(
                mainTerrain,
                CollectMountainPeakTiles(mainTerrain),
                useFoothillBounds: true,
                "peak → foothill",
                onComplete);

        /// <summary>Raises sub-foothill samples on peak inner band — removes bowed trough at peak/foothill base.</summary>
        public static void QueueSealPeakFoothillBaseGaps(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    ComputeFoothillRingWorldBounds(
                        mainTerrain,
                        out var fhMinX,
                        out var fhMaxX,
                        out var fhMinZ,
                        out var fhMaxZ);
                    var peaks = CollectMountainPeakTiles(mainTerrain);
                    var touched = 0;
                    for (var i = 0; i < peaks.Length; i++)
                        touched += SealPeakFoothillBaseGapSync(mainTerrain, peaks[i], fhMinX, fhMaxX, fhMinZ, fhMaxZ);

                    if (touched > 0)
                    {
                        RefreshMountainTerrainConnectivity(mainTerrain);
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Peak/foothill base seal — {touched} height sample(s) on {peaks.Length} peak tile(s).",
                            forceUnityConsole: true);
                    }

                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel("peak foothill base seal"));
        }

        static int SealPeakFoothillBaseGapSync(
            Terrain mainTerrain,
            Terrain peak,
            float fhMinX,
            float fhMaxX,
            float fhMinZ,
            float fhMaxZ)
        {
            if (peak?.terrainData == null || mainTerrain == null)
                return 0;

            if (!peak.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal))
                return 0;

            var data = peak.terrainData;
            var res = data.heightmapResolution;
            var origin = peak.transform.position;
            var size = data.size;
            var heights = data.GetHeights(0, 0, res, res);
            var touched = 0;
            const float sealBandMeters = 14f;

            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + z / (float)(res - 1) * size.z;
                    var distOutsideFoothill = DistanceOutsidePlayAabb(wx, wz, fhMinX, fhMaxX, fhMinZ, fhMaxZ);
                    if (distOutsideFoothill > sealBandMeters)
                        continue;

                    if (!TrySampleFoothillOrPlayNormAtWorld(mainTerrain, wx, wz, out var fhNorm))
                        continue;

                    var h = heights[z, x];
                    var toe = fhNorm + 0.0035f;
                    if (h >= toe - 0.0004f)
                        continue;

                    var blend = 1f - distOutsideFoothill / sealBandMeters;
                    heights[z, x] = Mathf.Lerp(h, toe, blend * 0.95f);
                    touched++;
                }
            }

            if (touched > 0)
            {
                CaveEditorUndo.RecordObject(data, "Seal peak foothill base");
                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(peak, 0, 0, heights, requestDelayLod: false);
                peak.Flush();
            }

            return touched;
        }

        /// <summary>After sculpt: lock inner band of all outer-ring tiles to play (foothill + peak).</summary>
        public static void QueueLockWildernessInnerEdgesToPlayDisk(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            QueueLockFoothillInnerEdgesToPlayDisk(mainTerrain, () =>
                QueueLockPeakInnerEdgesToFoothillRing(mainTerrain, onComplete));
        }

        static void QueueLockOuterRingInnerEdges(
            Terrain mainTerrain,
            Terrain[] tiles,
            bool useFoothillBounds,
            string label,
            Action onComplete)
        {
            if (mainTerrain == null || tiles == null || tiles.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            float minX, maxX, minZ, maxZ;
            if (useFoothillBounds)
                ComputeFoothillRingWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);
            else
                ComputePlayDiskWorldBounds(mainTerrain, out minX, out maxX, out minZ, out maxZ);

            CaveBuildEditorLog.LogSurface($"[Surface] Locking {label} inner edges (paced).", forceUnityConsole: true);
            QueueLockWildernessInnerEdgeTileAtIndex(
                mainTerrain,
                tiles,
                0,
                minX,
                maxX,
                minZ,
                maxZ,
                useFoothillBounds,
                onComplete);
        }

        static void QueueLockWildernessInnerEdgeTileAtIndex(
            Terrain mainTerrain,
            Terrain[] wilderness,
            int index,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            bool sampleFoothillForPeaks,
            Action onComplete)
        {
            if (index >= wilderness.Length)
            {
                onComplete?.Invoke();
                return;
            }

            var tile = wilderness[index];
            if (tile?.terrainData == null)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => QueueLockWildernessInnerEdgeTileAtIndex(
                        mainTerrain,
                        wilderness,
                        index + 1,
                        playMinX,
                        playMaxX,
                        playMinZ,
                        playMaxZ,
                        sampleFoothillForPeaks,
                        onComplete),
                    CaveBuildPipelineDomains.QueueLabel("wilderness inner lock — next"));
                return;
            }

            var lockMeters = SurfaceOuterRingMountainsAuthor.WildernessInnerLockMeters + 4f;
            QueueLockWildernessInnerEdgeBand(
                mainTerrain,
                tile,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ,
                lockMeters,
                sampleFoothillForPeaks,
                touched =>
                {
                    if (touched > 0)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] {tile.name} — inner edge locked to play disk ({touched} samples).",
                            forceUnityConsole: false);
                        tile.Flush();
                    }

                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueLockWildernessInnerEdgeTileAtIndex(
                            mainTerrain,
                            wilderness,
                            index + 1,
                            playMinX,
                            playMaxX,
                            playMinZ,
                            playMaxZ,
                            sampleFoothillForPeaks,
                            onComplete),
                        CaveBuildPipelineDomains.QueueLabel($"wilderness inner lock — {index + 1}/{wilderness.Length}"));
                });
        }

        static int LockWildernessInnerEdgeBandSync(
            Terrain mainTerrain,
            Terrain wilderness,
            float boundsMinX,
            float boundsMaxX,
            float boundsMinZ,
            float boundsMaxZ,
            float lockMeters,
            bool sampleFoothillForPeaks)
        {
            if (wilderness?.terrainData == null || mainTerrain == null)
                return 0;

            var res = wilderness.terrainData.heightmapResolution;
            var origin = wilderness.transform.position;
            var size = wilderness.terrainData.size;
            var heights = wilderness.terrainData.GetHeights(0, 0, res, res);
            var touched = 0;
            var denom = Mathf.Max(1, res - 1);

            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)denom * size.x;
                    var wz = origin.z + z / (float)denom * size.z;
                    var distOutside = DistanceOutsidePlayAabb(
                        wx,
                        wz,
                        boundsMinX,
                        boundsMaxX,
                        boundsMinZ,
                        boundsMaxZ);
                    const float seamOnlyMeters = 5f;
                    if (distOutside > seamOnlyMeters)
                        continue;

                    float norm;
                    var gotNorm = sampleFoothillForPeaks
                        ? TrySampleFoothillOrPlayNormAtWorld(mainTerrain, wx, wz, out norm)
                        : TrySamplePlayDiskNormAtWorld(mainTerrain, wx, wz, out norm);
                    if (!gotNorm)
                        continue;

                    var blend = 1f - distOutside / seamOnlyMeters;
                    var h = heights[z, x];
                    float target;
                    if (sampleFoothillForPeaks)
                    {
                        target = Mathf.Lerp(h, norm + 0.0025f, blend * 0.85f);
                    }
                    else
                    {
                        target = Mathf.Max(norm + 0.004f, h);
                        target = Mathf.Lerp(h, target, blend * 0.45f);
                    }

                    if (Mathf.Abs(h - target) > 0.0001f)
                    {
                        heights[z, x] = target;
                        touched++;
                    }
                }
            }

            if (touched > 0)
            {
                CaveEditorUndo.RecordObject(wilderness.terrainData, "Lock wilderness inner edge to play disk");
                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(wilderness, 0, 0, heights, requestDelayLod: false);
            }

            return touched;
        }

        sealed class WildernessInnerLockSession
        {
            public Terrain MainTerrain;
            public Terrain Wilderness;
            public float BoundsMinX;
            public float BoundsMaxX;
            public float BoundsMinZ;
            public float BoundsMaxZ;
            public float LockMeters;
            public bool SampleFoothillForPeaks;
            public int Row;
            public int Touched;
            public Action<int> OnComplete;
        }

        static void QueueLockWildernessInnerEdgeBand(
            Terrain mainTerrain,
            Terrain wilderness,
            float boundsMinX,
            float boundsMaxX,
            float boundsMinZ,
            float boundsMaxZ,
            float lockMeters,
            bool sampleFoothillForPeaks,
            Action<int> onComplete)
        {
            if (wilderness?.terrainData == null || mainTerrain == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var session = new WildernessInnerLockSession
            {
                MainTerrain = mainTerrain,
                Wilderness = wilderness,
                BoundsMinX = boundsMinX,
                BoundsMaxX = boundsMaxX,
                BoundsMinZ = boundsMinZ,
                BoundsMaxZ = boundsMaxZ,
                LockMeters = lockMeters,
                SampleFoothillForPeaks = sampleFoothillForPeaks,
                Row = 0,
                Touched = 0,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLight(
                () => RunWildernessInnerLockRowBand(session),
                CaveBuildPipelineDomains.QueueLabel($"wilderness inner lock rows — {wilderness.name}"));
        }

        static void RunWildernessInnerLockRowBand(WildernessInnerLockSession session)
        {
            if (session?.Wilderness?.terrainData == null)
            {
                session?.OnComplete?.Invoke(session?.Touched ?? 0);
                return;
            }

            var wilderness = session.Wilderness;
            var res = wilderness.terrainData.heightmapResolution;
            var chunk = CaveBuildMicroTerrainHeightmap.BandRowsFor(res);
            var rowEnd = Mathf.Min(res, session.Row + chunk);
            var origin = wilderness.transform.position;
            var size = wilderness.terrainData.size;
            var denom = Mathf.Max(1, res - 1);
            var bandRows = rowEnd - session.Row;
            var heights = wilderness.terrainData.GetHeights(0, session.Row, res, bandRows);
            var bandTouched = 0;

            for (var z = 0; z < bandRows; z++)
            {
                var gz = session.Row + z;
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)denom * size.x;
                    var wz = origin.z + gz / (float)denom * size.z;
                    var distOutside = DistanceOutsidePlayAabb(
                        wx,
                        wz,
                        session.BoundsMinX,
                        session.BoundsMaxX,
                        session.BoundsMinZ,
                        session.BoundsMaxZ);
                    const float seamOnlyMeters = 5f;
                    if (distOutside > seamOnlyMeters)
                        continue;

                    float norm;
                    var gotNorm = session.SampleFoothillForPeaks
                        ? TrySampleFoothillOrPlayNormAtWorld(session.MainTerrain, wx, wz, out norm)
                        : TrySamplePlayDiskNormAtWorld(session.MainTerrain, wx, wz, out norm);
                    if (!gotNorm)
                        continue;

                    var blend = 1f - distOutside / seamOnlyMeters;
                    var h = heights[z, x];
                    float target;
                    if (session.SampleFoothillForPeaks)
                        target = Mathf.Lerp(h, norm + 0.0025f, blend * 0.85f);
                    else
                    {
                        target = Mathf.Max(norm + 0.004f, h);
                        target = Mathf.Lerp(h, target, blend * 0.45f);
                    }

                    if (Mathf.Abs(h - target) <= 0.0001f)
                        continue;

                    heights[z, x] = target;
                    bandTouched++;
                }
            }

            if (bandTouched > 0)
            {
                if (session.Row == 0)
                    CaveEditorUndo.RecordObject(wilderness.terrainData, "Lock wilderness inner edge to play disk");
                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(wilderness, 0, session.Row, heights, requestDelayLod: false);
                session.Touched += bandTouched;
            }

            CaveBuildActionPacing.TouchQueueActivity();
            session.Row = rowEnd;
            if (session.Row < res)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunWildernessInnerLockRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel($"wilderness inner lock rows — {wilderness.name}"));
                return;
            }

            if (session.Touched > 0)
                wilderness.Flush();

            session.OnComplete?.Invoke(session.Touched);
        }

        public static void QueueStitchWildernessRing(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var wilderness = CollectMountainWildernessTiles(mainTerrain);
            if (wilderness.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var group = new List<Terrain>(1 + NineTileRingOffsets.Length + MountainWildernessTileCount)
            {
                mainTerrain,
            };
            group.AddRange(CollectGameplayTiles(mainTerrain));
            group.AddRange(wilderness);
            var groupArr = group.ToArray();

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Wilderness seam stitch — {wilderness.Length} tile(s) to play ring + neighbors (paced).",
                forceUnityConsole: true);
            QueueStitchSurfaceTilesAtIndex(mainTerrain, wilderness, groupArr, 0, stitchPlayPerimeterAfter: true, onComplete);
        }

        /// <summary>Stitch play-ring perimeter to foothill ring only (Chebyshev 2) — frame-paced steps.</summary>
        public static void QueueStitchPlayPerimeterToFoothills(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var gameplay = CollectGameplayTiles(mainTerrain);
            var foothills = CollectMountainFoothillTiles(mainTerrain);
            if (foothills.Length == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var perimeter = new List<Terrain>(8);
            foreach (var tile in gameplay)
            {
                if (tile == null || !TryParseTileOffset(tile.name, out var off))
                    continue;

                if (Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)) == 1)
                    perimeter.Add(tile);
            }

            if (perimeter.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var group = new List<Terrain>(1 + gameplay.Length + foothills.Length)
            {
                mainTerrain,
            };
            group.AddRange(gameplay);
            group.AddRange(foothills);

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Play-ring → foothill seam stitch ({perimeter.Count} perimeter tile(s), paced).",
                forceUnityConsole: true);
            QueueStitchPlayPerimeterTileAtIndex(mainTerrain, perimeter, group.ToArray(), 0, onComplete);
        }

        /// <summary>Stitch outward-facing edges of the 9-tile play ring to all outer rings (foothill + peak).</summary>
        static void QueueStitchPlayPerimeterToWilderness(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var gameplay = CollectGameplayTiles(mainTerrain);
            var perimeter = new List<Terrain>(8);
            foreach (var tile in gameplay)
            {
                if (tile == null || !TryParseTileOffset(tile.name, out var off))
                    continue;

                if (Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.y)) == 1)
                    perimeter.Add(tile);
            }

            if (perimeter.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            var group = new List<Terrain>(1 + gameplay.Length + MountainWildernessTileCount)
            {
                mainTerrain,
            };
            group.AddRange(gameplay);
            group.AddRange(CollectMountainWildernessTiles(mainTerrain));
            var groupArr = group.ToArray();

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Play-ring → wilderness seam stitch ({perimeter.Count} perimeter tile(s), paced).",
                forceUnityConsole: true);
            QueueStitchPlayPerimeterTileAtIndex(mainTerrain, perimeter, groupArr, 0, onComplete);
        }

        static void QueueStitchPlayPerimeterTileAtIndex(
            Terrain mainTerrain,
            List<Terrain> perimeter,
            Terrain[] group,
            int index,
            Action onComplete)
        {
            if (index >= perimeter.Count)
            {
                onComplete?.Invoke();
                return;
            }

            var tile = perimeter[index];
            if (tile == null || !TryParseTileOffset(tile.name, out var off))
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => QueueStitchPlayPerimeterTileAtIndex(mainTerrain, perimeter, group, index + 1, onComplete),
                    CaveBuildPipelineDomains.QueueLabel("play perimeter seams — next"));
                return;
            }

            CaveBuildRunStatusPublisher.PulseSubOperation(
                "play perimeter seams",
                $"stitch {index + 1}/{perimeter.Count} ({tile.name})");
            QueueStitchTileSeamsPaced(
                tile,
                mainTerrain,
                off,
                group,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueStitchPlayPerimeterTileAtIndex(mainTerrain, perimeter, group, index + 1, onComplete),
                        CaveBuildPipelineDomains.QueueLabel("play perimeter seams — next"));
                });
        }

        public static void RefreshMountainTerrainConnectivity(Terrain mainTerrain)
        {
            var group = new List<Terrain> { mainTerrain };
            group.AddRange(CollectGameplayTiles(mainTerrain));
            group.AddRange(CollectMountainWildernessTiles(mainTerrain));

            foreach (var terrain in group)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;

                var size = terrain.terrainData.size;
                var origin = terrain.transform.position;
                FindCardinalNeighbor(group, origin, size, out var left, out var top, out var right, out var bottom);
                terrain.SetNeighbors(
                    FilterUnityNeighbor(terrain, left),
                    FilterUnityNeighbor(terrain, top),
                    FilterUnityNeighbor(terrain, right),
                    FilterUnityNeighbor(terrain, bottom));
            }

            Terrain.SetConnectivityDirty();
        }

        /// <summary>Unity refuses SetNeighbors when heightmap resolutions differ — omit until unified.</summary>
        static Terrain FilterUnityNeighbor(Terrain terrain, Terrain neighbor)
        {
            if (neighbor == null || terrain?.terrainData == null || neighbor.terrainData == null)
                return neighbor;

            return TileHeightmapResolutionMatches(terrain, neighbor) ? neighbor : null;
        }

        static Transform GetOrCreateMountainWildernessRoot(Terrain mainTerrain)
        {
            var existing = FindMountainWildernessRoot(mainTerrain);
            if (existing != null)
                return existing;

            var parent = mainTerrain.transform.parent;
            var rootGo = new GameObject(MountainWildernessTilesRootName);
            CaveEditorUndo.RegisterCreated(rootGo, "Mountain wilderness tiles root");
            if (parent != null)
                rootGo.transform.SetParent(parent, false);
            else
                rootGo.transform.SetParent(mainTerrain.transform.parent, false);

            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            return rootGo.transform;
        }

        static Transform FindMountainWildernessRoot(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return null;

            var parent = mainTerrain.transform.parent;
            if (parent != null)
            {
                var underParent = parent.Find(MountainWildernessTilesRootName);
                if (underParent != null)
                    return underParent;
            }

            return mainTerrain.transform.Find(MountainWildernessTilesRootName);
        }

        static void QueueSeedWildernessHeightsFromInnerRing(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Transform gameplayRoot,
            Transform wildernessRoot,
            Action onComplete)
        {
            if (tile?.terrainData == null || main?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            ApplyWildernessTileGridSlot(main, tile, off);

            var res = tile.terrainData.heightmapResolution;
            ComputePlayDiskWorldBounds(main, out var playMinX, out var playMaxX, out var playMinZ, out var playMaxZ);
            var session = new SeedHeightsSession
            {
                Tile = tile,
                Main = main,
                ConnectivityMain = main,
                Off = off,
                Res = res,
                Band = Mathf.Max(Mathf.Max(4, res / 12), res / 4),
                PlayMinX = playMinX,
                PlayMaxX = playMaxX,
                PlayMinZ = playMinZ,
                PlayMaxZ = playMaxZ,
                OnComplete = onComplete,
            };

            session.RowY = 0;
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    session.Heights = BuildWildernessSeedHeightsArray(
                        tile,
                        main,
                        off,
                        gameplayRoot,
                        wildernessRoot,
                        session.Band,
                        session.Res,
                        session.PlayMinX,
                        session.PlayMaxX,
                        session.PlayMinZ,
                        session.PlayMaxZ);
                    CaveBuildActionPacing.ScheduleLight(
                        () => RunWildernessSeedUpload(session),
                        CaveBuildPipelineDomains.QueueLabel("mountain wilderness — seam seed"));
                },
                CaveBuildPipelineDomains.QueueLabel("mountain wilderness — build seed heights"));
        }

        static void RunWildernessSeedUpload(SeedHeightsSession session)
        {
            var chunk = SeedRowChunk(session.Res);
            var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
            FlushSeedRows(session, session.RowY, yEnd);
            session.RowY = yEnd;
            if (session.RowY < session.Res)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunWildernessSeedUpload(session),
                    CaveBuildPipelineDomains.QueueLabel("mountain wilderness — seed rows"));
                return;
            }

            CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(session.Tile, () => session.OnComplete?.Invoke());
        }

        static float[,] BuildWildernessSeedHeightsArray(
            Terrain tile,
            Terrain main,
            Vector2Int off,
            Transform gameplayRoot,
            Transform wildernessRoot,
            int band,
            int res,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            var heights = new float[res, res];
            var baseNorm = SampleTerrainNormAtCenter(main);
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                    heights[z, x] = baseNorm;
            }

            var towardX = off.x == 0 ? 0 : (off.x > 0 ? -1 : 1);
            var towardZ = off.y == 0 ? 0 : (off.y > 0 ? -1 : 1);
            if (Mathf.Abs(off.x) >= Mathf.Abs(off.y))
                towardZ = 0;
            else
                towardX = 0;

            var innerOff = new Vector2Int(off.x + towardX, off.y + towardZ);
            if (TryResolveTerrainAtGridOffset(main, gameplayRoot, wildernessRoot, innerOff, out var inner) &&
                inner != null &&
                inner != tile)
            {
                var delta = innerOff - off;
                CopyTouchingEdge(inner, tile, heights, delta, band);
            }

            var ringDist = GetOuterRingChebyshevDistance(off);
            if (ringDist >= 2 && Mathf.Abs(off.x) == ringDist && Mathf.Abs(off.y) == ringDist)
            {
                var altX = new Vector2Int(off.x + (off.x > 0 ? -1 : 1), off.y);
                var altZ = new Vector2Int(off.x, off.y + (off.y > 0 ? -1 : 1));
                if (TryResolveTerrainAtGridOffset(main, gameplayRoot, wildernessRoot, altX, out var tx) &&
                    tx != null &&
                    tx != tile)
                    CopyTouchingEdge(tx, tile, heights, altX - off, band);
                if (TryResolveTerrainAtGridOffset(main, gameplayRoot, wildernessRoot, altZ, out var tz) &&
                    tz != null &&
                    tz != tile)
                    CopyTouchingEdge(tz, tile, heights, altZ - off, band);
            }

            foreach (var otherOff in GetSameRingNeighborOffsets(off))
            {
                if (otherOff == off)
                    continue;

                var delta = otherOff - off;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                    continue;

                if (!TryResolveTerrainAtGridOffset(main, gameplayRoot, wildernessRoot, otherOff, out var other) ||
                    other == null ||
                    other == tile)
                    continue;

                CopyTouchingEdge(other, tile, heights, delta, band);
            }

            FillInteriorFromEdgeBands(heights, band, res);
            ApplyWildernessFoothillFromPlayDisk(
                heights,
                tile,
                main,
                res,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ);
            return heights;
        }

        const float WildernessFoothillBlendMeters = 56f;

        static void ApplyWildernessFoothillFromPlayDisk(
            float[,] heights,
            Terrain tile,
            Terrain main,
            int res,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            if (heights == null || tile?.terrainData == null || main == null)
                return;

            var origin = tile.transform.position;
            var size = tile.terrainData.size;
            var denom = Mathf.Max(1, res - 1);
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)denom * size.x;
                    var wz = origin.z + z / (float)denom * size.z;
                    var distOutside = DistanceOutsidePlayAabb(wx, wz, playMinX, playMaxX, playMinZ, playMaxZ);
                    if (distOutside > WildernessFoothillBlendMeters)
                        continue;

                    if (!TrySamplePlayDiskNormNearAabb(
                            main,
                            wx,
                            wz,
                            playMinX,
                            playMaxX,
                            playMinZ,
                            playMaxZ,
                            out var playNorm))
                        continue;

                    var t = Mathf.SmoothStep(0f, 1f, distOutside / WildernessFoothillBlendMeters);
                    var minFoothillRise = 0.012f + t * 0.028f;
                    var floor = playNorm + minFoothillRise;
                    heights[z, x] = Mathf.Max(heights[z, x], Mathf.Lerp(floor, heights[z, x], t));
                }
            }
        }

        static bool TryResolveTerrainAtGridOffsetInternal(
            Terrain main,
            Transform gameplayRoot,
            Transform wildernessRoot,
            Vector2Int off,
            out Terrain terrain)
        {
            terrain = null;
            if (off == Vector2Int.zero)
            {
                terrain = main;
                return true;
            }

            if (IsNineTileGameplayOffset(off) && gameplayRoot != null &&
                TryFindTileAtOffset(gameplayRoot, off, out terrain))
                return terrain != null;

            if (TryFindOuterRingTileAtOffset(wildernessRoot, off, null, out terrain))
                return terrain != null;

            return false;
        }

        static Vector2Int[] GetSameRingNeighborOffsets(Vector2Int off)
        {
            if (IsPeakRingOffset(off))
                return PeakRingOffsets;
            if (IsFoothillRingOffset(off))
                return FoothillRingOffsets;
            return FoothillRingOffsets;
        }

        static bool SlotBlockedByForeignTerrain(
            Vector3 slotOrigin,
            Vector3 tileSize,
            Terrain mainTerrain,
            Transform tilesRoot)
        {
            const float edgeTol = 0.25f;
            var slotMinX = slotOrigin.x;
            var slotMaxX = slotOrigin.x + tileSize.x;
            var slotMinZ = slotOrigin.z;
            var slotMaxZ = slotOrigin.z + tileSize.z;
            var slotArea = Mathf.Max(1f, tileSize.x * tileSize.z);

            if (tilesRoot != null)
            {
                for (var i = 0; i < tilesRoot.childCount; i++)
                {
                    var child = tilesRoot.GetChild(i);
                    if (Mathf.Abs(child.position.x - slotOrigin.x) < edgeTol &&
                        Mathf.Abs(child.position.z - slotOrigin.z) < edgeTol)
                        return true;
                }
            }

            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
            {
                if (t == null || t == mainTerrain || t.terrainData == null)
                    continue;

                if (t.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
                    continue;

                var otherOrigin = t.transform.position;
                var otherSize = t.terrainData.size;
                var overlapX = Mathf.Min(slotMaxX, otherOrigin.x + otherSize.x) -
                               Mathf.Max(slotMinX, otherOrigin.x);
                var overlapZ = Mathf.Min(slotMaxZ, otherOrigin.z + otherSize.z) -
                               Mathf.Max(slotMinZ, otherOrigin.z);
                if (overlapX <= edgeTol || overlapZ <= edgeTol)
                    continue;

                if (overlapX * overlapZ > slotArea * 0.08f)
                    return true;
            }

            return false;
        }

        // ── FullWorld: research → place all → terraform all → seam all ─────────────

        internal enum FullWorldGridPhase
        {
            Research,
            PlaceAll,
            PlaceAndSeam,
            Terraform,
            SeamAll,
        }

        internal sealed class FullWorldGridSession
        {
            public FullWorldGridPhase Phase = FullWorldGridPhase.Research;
            public Terrain MainTerrain;
            public SceneGroundInfo Ground;
            public WorldGenerationRequest Request;
            public Vector2Int[] Offsets;
            public Vector2Int[] PlaceOffsets;
            public Transform TilesRoot;
            public Transform WildernessRoot;
            public int Index;
            public int SpawnedNew;
            public bool PreparedScene;
            public bool WaitingForSeam;
            public bool WaitingForHeight;
            public bool GridAnchorLocked;
            public Vector3 LockedMainOrigin;
            public Transform GridAnchor;
            public bool LayOnlyPass;
            public Action<int, string> OnComplete;
        }

        static Vector3 FullWorldGridLocalPosition(Terrain mainTerrain, Vector2Int off)
        {
            var size = mainTerrain.terrainData.size;
            return new Vector3(off.x * size.x, 0f, off.y * size.z);
        }

        static Transform FindFullWorldGridAnchor(Terrain mainTerrain = null)
        {
            var flatHost = GetOrCreateFlatTerrainHost();
            if (flatHost != null)
            {
                for (var i = 0; i < flatHost.childCount; i++)
                {
                    var child = flatHost.GetChild(i);
                    if (child.name == FullWorldGridAnchorName)
                        return child;
                }
            }

            if (mainTerrain == null)
                return null;

            for (var t = mainTerrain.transform.parent; t != null; t = t.parent)
            {
                if (t.name == FullWorldGridAnchorName)
                    return t;
            }

            return null;
        }

        /// <summary>World snap from <see cref="FullWorldGridAnchorName"/> when present; false = use legacy main-origin math.</summary>
        static bool TryApplyFullWorldGridSlotFromMain(Terrain mainTerrain, Terrain tile, Vector2Int off)
        {
            if (mainTerrain?.terrainData == null || tile == null)
                return false;

            var anchor = FindFullWorldGridAnchor(mainTerrain);
            if (anchor == null)
                return false;

            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            var worldPos = anchor.TransformPoint(FullWorldGridLocalPosition(mainTerrain, off));
            worldPos = SnapWorldPosToMillimeterGrid(worldPos);
            worldPos.y = ResolveSharedTerrainOriginY(ground, mainTerrain);
            tile.transform.rotation = Quaternion.identity;
            tile.transform.localScale = Vector3.one;
            tile.transform.position = worldPos;
            return true;
        }

        /// <summary>
        /// One anchor at the locked main corner; every tile uses localPosition = offset × tileSize (edge-to-edge, no gaps).
        /// </summary>
        internal static Transform EnsureFullWorldGridAnchorTransform(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return null;

            var flatHost = GetOrCreateFlatTerrainHost();
            Transform anchor = null;
            if (flatHost != null)
            {
                for (var i = 0; i < flatHost.childCount; i++)
                {
                    var child = flatHost.GetChild(i);
                    if (child.name == FullWorldGridAnchorName)
                    {
                        anchor = child;
                        break;
                    }
                }
            }

            if (anchor == null)
            {
                var go = new GameObject(FullWorldGridAnchorName);
                CaveEditorUndo.RegisterCreated(go, FullWorldGridAnchorName);
                if (flatHost != null)
                    go.transform.SetParent(flatHost, false);
                anchor = go.transform;
            }

            var ground = session.Ground ??
                         SceneGroundResolver.ResolveForFullWorld(session.MainTerrain.transform);
            session.LockedMainOrigin = session.MainTerrain.transform.position;
            session.GridAnchorLocked = true;

            anchor.position = session.LockedMainOrigin;
            anchor.rotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            session.GridAnchor = anchor;
            LogGridAnchorPlacement(ground, session.LockedMainOrigin, session.MainTerrain != null, "locked-to-main");

            RefreshFullWorldGridSessionRoots(session);

            session.MainTerrain.transform.SetParent(anchor, true);
            session.MainTerrain.transform.localRotation = Quaternion.identity;
            session.MainTerrain.transform.localScale = Vector3.one;
            if (session.MainTerrain.transform.parent == anchor)
                session.MainTerrain.transform.localPosition = Vector3.zero;

            if (session.TilesRoot != null)
            {
                session.TilesRoot.SetParent(anchor, false);
                session.TilesRoot.localPosition = Vector3.zero;
                session.TilesRoot.localRotation = Quaternion.identity;
                session.TilesRoot.localScale = Vector3.one;
            }

            if (session.WildernessRoot != null)
            {
                session.WildernessRoot.SetParent(anchor, false);
                session.WildernessRoot.localPosition = Vector3.zero;
                session.WildernessRoot.localRotation = Quaternion.identity;
                session.WildernessRoot.localScale = Vector3.one;
            }

            return anchor;
        }

        internal static void ApplyFullWorldGridSlot(FullWorldGridSession session, Terrain tile, Vector2Int off)
        {
            if (session?.MainTerrain == null || tile?.terrainData == null)
                return;

            var anchor = session.GridAnchor ?? EnsureFullWorldGridAnchorTransform(session);
            NormalizeFullWorldGridRootTransforms(session);

            if (session.TilesRoot != null)
                session.TilesRoot.localPosition = Vector3.zero;
            if (session.WildernessRoot != null)
                session.WildernessRoot.localPosition = Vector3.zero;

            Transform parent;
            if (off == Vector2Int.zero || tile == session.MainTerrain)
                parent = anchor;
            else if (IsNineTileGameplayOffset(off))
                parent = session.TilesRoot != null ? session.TilesRoot : anchor;
            else
                parent = session.WildernessRoot != null ? session.WildernessRoot : anchor;

            var ground = session.Ground ?? SceneGroundResolver.ResolveForFullWorld(session.MainTerrain.transform);
            var worldPos = anchor.TransformPoint(FullWorldGridLocalPosition(session.MainTerrain, off));
            worldPos = SnapWorldPosToMillimeterGrid(worldPos);
            worldPos.y = ResolveSharedTerrainOriginY(ground, session.MainTerrain);

            tile.transform.SetParent(parent, false);
            tile.transform.localRotation = Quaternion.identity;
            tile.transform.localScale = Vector3.one;
            tile.transform.position = worldPos;
        }

        static void RefreshFullWorldGridSessionRoots(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            session.TilesRoot = GetOrCreateTilesRoot(session.MainTerrain);
            session.WildernessRoot = GetOrCreateMountainWildernessRoot(session.MainTerrain);
        }

        /// <summary>Anchor XZ once at prepare; restore if anything drifts main mid-build.</summary>
        static void LockFullWorldGridAnchor(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            RefreshFullWorldGridSessionRoots(session);

            if (!session.GridAnchorLocked)
            {
                NormalizeFullWorldGridRootTransforms(session);
                SyncFullWorldGridToGroundLevel(session);
                EnsureFullWorldGridAnchorTransform(session);
                ForceSnapEntireFullWorldGrid(session);
                return;
            }

            var delta = session.LockedMainOrigin - session.MainTerrain.transform.position;
            if (delta.sqrMagnitude <= 0.01f &&
                session.GridAnchor != null &&
                (session.GridAnchor.position - session.LockedMainOrigin).sqrMagnitude <= 0.01f)
                return;

            session.MainTerrain.transform.SetParent(session.GridAnchor != null ? session.GridAnchor : EnsureFullWorldGridAnchorTransform(session), false);
            session.MainTerrain.transform.localPosition = Vector3.zero;
            if (session.GridAnchor != null)
                session.GridAnchor.position = session.LockedMainOrigin;
            ForceSnapEntireFullWorldGrid(session);
            CaveBuildEditorLog.LogSurfaceWarning(
                $"[Surface] FullWorld grid anchor drifted ({delta.magnitude:F1}m) — reset main and re-snapped all 49 slot(s).");
        }

        static void NormalizeFullWorldGridRootTransforms(FullWorldGridSession session)
        {
            if (session?.TilesRoot != null)
            {
                session.TilesRoot.localRotation = Quaternion.identity;
                session.TilesRoot.localScale = Vector3.one;
            }

            if (session?.WildernessRoot != null)
            {
                session.WildernessRoot.localRotation = Quaternion.identity;
                session.WildernessRoot.localScale = Vector3.one;
            }
        }

        static bool TryForceResolveFullWorldTileAtOffset(
            FullWorldGridSession session,
            Vector2Int off,
            out Terrain tile)
        {
            tile = null;
            if (session?.MainTerrain == null)
                return false;

            RefreshFullWorldGridSessionRoots(session);

            if (off == Vector2Int.zero)
            {
                tile = session.MainTerrain;
                return true;
            }

            if (TryResolveTerrainAtGridOffset(
                    session.MainTerrain,
                    session.TilesRoot,
                    session.WildernessRoot,
                    off,
                    out tile) &&
                tile != null)
                return true;

            Terrain found = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (candidate == null || candidate == session.MainTerrain)
                    continue;

                if (IsNineTileGameplayOffset(off))
                {
                    if (!TryParseTileOffset(candidate.name, out var playOff) || playOff != off)
                        continue;
                }
                else if (!TryParseOuterRingTileOffset(candidate.name, out var wildOff) || wildOff != off)
                {
                    continue;
                }
                else if (GetOuterRingChebyshevDistance(off) > FullWorldChebyshevRadius)
                {
                    if (!candidate.name.StartsWith(OpenWorldTileNamePrefix, StringComparison.Ordinal))
                        continue;
                }
                else if (IsHorizonRingOffset(off))
                {
                    if (!candidate.name.StartsWith(MountainHorizonTileNamePrefix, StringComparison.Ordinal))
                        continue;
                }
                else if (IsPeakRingOffset(off))
                {
                    if (!candidate.name.StartsWith(MountainPeakTileNamePrefix, StringComparison.Ordinal))
                        continue;
                }
                else if (IsFoothillRingOffset(off) &&
                         !candidate.name.StartsWith(MountainFoothillTileNamePrefix, StringComparison.Ordinal) &&
                         !candidate.name.StartsWith(MountainWildernessTileNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                found = candidate;
                break;
            }

            if (found == null)
                return false;

            tile = found;
            return true;
        }

        /// <summary>Pull every FullWorld slot onto the locked 7×7 grid, reparent strays, purge duplicates.</summary>
        static void ForceSnapEntireFullWorldGrid(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            EnsureFullWorldGridAnchorTransform(session);
            ConsolidateScatteredFullWorldTerrains(session.MainTerrain);

            var order = session.PlaceOffsets ?? BuildFullWorldPlaceOrder();
            var snapped = 0;

            foreach (var off in order)
            {
                if (!TryForceResolveFullWorldTileAtOffset(session, off, out var tile) || tile == null)
                {
                    if (off == Vector2Int.zero)
                    {
                        ApplyFullWorldGridSlot(session, session.MainTerrain, Vector2Int.zero);
                        snapped++;
                    }

                    continue;
                }

                ApplyFullWorldGridSlot(session, tile, off);
                snapped++;
            }

            ConsolidateScatteredFullWorldTerrains(session.MainTerrain);
            foreach (var off in order)
            {
                if (TryForceResolveFullWorldTileAtOffset(session, off, out var tile) && tile != null)
                    ApplyFullWorldGridSlot(session, tile, off);
            }

            RefreshMountainTerrainConnectivity(session.MainTerrain);

            CaveBuildEditorLog.LogSurface(
                $"[Surface] FullWorld grid snap — {snapped}/{order.Length} slot(s) edge-to-edge on {FullWorldGridAnchorName}.",
                forceUnityConsole: true);

            PurgeMisalignedFullWorldStrays(session);
        }

        /// <summary>Destroy foothill/peak duplicates that stayed off the 7×7 anchor grid after snap.</summary>
        static int PurgeMisalignedFullWorldStrays(FullWorldGridSession session)
        {
            if (session?.MainTerrain?.terrainData == null || session.GridAnchor == null)
                return 0;

            var mainTerrain = session.MainTerrain;
            var anchor = session.GridAnchor;
            var tileSize = mainTerrain.terrainData.size.x;
            var tolerance = Mathf.Max(tileSize * 0.05f, 1.5f);
            var removed = 0;
            var keepers = new Dictionary<Vector2Int, Terrain>();

            foreach (var off in session.PlaceOffsets ?? BuildFullWorldPlaceOrder())
            {
                if (TryForceResolveFullWorldTileAtOffset(session, off, out var keeper) && keeper != null)
                    keepers[off] = keeper;
            }

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain || terrain.terrainData == null)
                    continue;

                if (!IsOuterRingTerrainName(terrain.name) ||
                    !TryParseOuterRingTileOffset(terrain.name, out var off) ||
                    !IsFullWorldGridOffset(off))
                    continue;

                if (keepers.TryGetValue(off, out var keeper) && keeper != terrain)
                {
                    if (PipelineContentPreservePolicy.ShouldAllowDestroy(
                            terrain.gameObject,
                            "misaligned wilderness duplicate"))
                    {
                        CaveEditorUndo.DestroyImmediate(terrain.gameObject);
                        removed++;
                    }
                    else
                    {
                        ApplyFullWorldGridSlot(session, terrain, off);
                    }

                    continue;
                }

                var expected = anchor.TransformPoint(FullWorldGridLocalPosition(mainTerrain, off));
                var delta = new Vector2(
                    terrain.transform.position.x - expected.x,
                    terrain.transform.position.z - expected.z).magnitude;
                if (delta <= tolerance)
                    continue;

                ApplyFullWorldGridSlot(session, terrain, off);
                delta = new Vector2(
                    terrain.transform.position.x - expected.x,
                    terrain.transform.position.z - expected.z).magnitude;
                if (delta > tolerance)
                {
                    CaveBuildEditorLog.LogSurfaceWarning(
                        $"[Surface] {terrain.name} still {delta:F1}m off slot ({off.x},{off.y}) after snap — kept (no delete).",
                        forceUnityConsole: true);
                }
            }

            if (removed > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Removed {removed} duplicate FullWorld wilderness tile(s) (same grid slot only).",
                    forceUnityConsole: true);
            }

            return removed;
        }

        static Vector3 SnapWorldPosToMillimeterGrid(Vector3 worldPos) =>
            new(
                Mathf.Round(worldPos.x * 1000f) / 1000f,
                worldPos.y,
                Mathf.Round(worldPos.z * 1000f) / 1000f);

        static readonly Vector2Int[] GridCardinalDeltas =
        {
            new(1, 0),
            new(-1, 0),
            new(0, 1),
            new(0, -1),
        };

        /// <summary>Hard-lock shared edge heightmap pixels — removes visible dark lines between adjacent tiles.</summary>
        static void WeldSharedEdgeExact(Terrain tile, Terrain neighbor, Vector2Int deltaFromTileToNeighbor)
        {
            if (tile?.terrainData == null || neighbor?.terrainData == null)
                return;

            if (!TileHeightmapResolutionMatches(tile, neighbor))
                return;

            var res = tile.terrainData.heightmapResolution;
            if (neighbor.terrainData.heightmapResolution != res)
                return;

            if (deltaFromTileToNeighbor.x == 1 && deltaFromTileToNeighbor.y == 0)
            {
                var tileCol = tile.terrainData.GetHeights(res - 1, 0, 1, res);
                var neighborCol = neighbor.terrainData.GetHeights(0, 0, 1, res);
                for (var z = 0; z < res; z++)
                {
                    var welded = (tileCol[z, 0] + neighborCol[z, 0]) * 0.5f;
                    tileCol[z, 0] = welded;
                    neighborCol[z, 0] = welded;
                }

                ApplyHeightBandDirect(tile, res - 1, 0, tileCol);
                ApplyHeightBandDirect(neighbor, 0, 0, neighborCol);
                return;
            }

            if (deltaFromTileToNeighbor.x == -1 && deltaFromTileToNeighbor.y == 0)
            {
                var tileCol = tile.terrainData.GetHeights(0, 0, 1, res);
                var neighborCol = neighbor.terrainData.GetHeights(res - 1, 0, 1, res);
                for (var z = 0; z < res; z++)
                {
                    var welded = (tileCol[z, 0] + neighborCol[z, 0]) * 0.5f;
                    tileCol[z, 0] = welded;
                    neighborCol[z, 0] = welded;
                }

                ApplyHeightBandDirect(tile, 0, 0, tileCol);
                ApplyHeightBandDirect(neighbor, res - 1, 0, neighborCol);
                return;
            }

            if (deltaFromTileToNeighbor.x == 0 && deltaFromTileToNeighbor.y == 1)
            {
                var tileRow = tile.terrainData.GetHeights(0, res - 1, res, 1);
                var neighborRow = neighbor.terrainData.GetHeights(0, 0, res, 1);
                for (var x = 0; x < res; x++)
                {
                    var welded = (tileRow[0, x] + neighborRow[0, x]) * 0.5f;
                    tileRow[0, x] = welded;
                    neighborRow[0, x] = welded;
                }

                ApplyHeightBandDirect(tile, 0, res - 1, tileRow);
                ApplyHeightBandDirect(neighbor, 0, 0, neighborRow);
                return;
            }

            if (deltaFromTileToNeighbor.x == 0 && deltaFromTileToNeighbor.y == -1)
            {
                var tileRow = tile.terrainData.GetHeights(0, 0, res, 1);
                var neighborRow = neighbor.terrainData.GetHeights(0, res - 1, res, 1);
                for (var x = 0; x < res; x++)
                {
                    var welded = (tileRow[0, x] + neighborRow[0, x]) * 0.5f;
                    tileRow[0, x] = welded;
                    neighborRow[0, x] = welded;
                }

                ApplyHeightBandDirect(tile, 0, 0, tileRow);
                ApplyHeightBandDirect(neighbor, 0, res - 1, neighborRow);
            }
        }

        static void WeldFullWorldTileToCardinalNeighbors(FullWorldGridSession session, Terrain tile, Vector2Int off)
        {
            if (session?.MainTerrain == null || tile?.terrainData == null)
                return;

            foreach (var delta in GridCardinalDeltas)
            {
                var neighborOff = off + delta;
                if (!IsFullWorldGridOffset(neighborOff))
                    continue;

                if (!TryForceResolveFullWorldTileAtOffset(session, neighborOff, out var neighbor) ||
                    neighbor == null)
                    continue;

                WeldSharedEdgeExact(tile, neighbor, delta);
            }

            tile.Flush();
        }

        static void WeldFullWorldGridEdgesSync(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            var order = session.PlaceOffsets ?? BuildFullWorldPlaceOrder();
            if (order == null)
                return;

            for (var i = 0; i < order.Length; i++)
            {
                var off = order[i];
                if (!TryForceResolveFullWorldTileAtOffset(session, off, out var tile) || tile == null)
                    continue;

                WeldFullWorldTileToCardinalNeighbors(session, tile, off);
            }

            CaveBuildTerrainHeightmapMemory.FlushAllSurfaceTerrains(session.MainTerrain);
            RefreshMountainTerrainConnectivity(session.MainTerrain);
        }

        static void QueueWeldFullWorldGridEdges(FullWorldGridSession session, Action onComplete)
        {
            WeldFullWorldGridEdgesSync(session);
            onComplete?.Invoke();
        }

        internal static void QueueIncrementalFullWorldTileEdgeWeld(
            Terrain mainTerrain,
            Terrain tile,
            Action onComplete)
        {
            if (mainTerrain == null || tile == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    var session = CreateMinimalFullWorldGridSession(mainTerrain);
                    if (session == null || tile?.terrainData == null)
                    {
                        onComplete?.Invoke();
                        return;
                    }

                    if (!TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var off))
                    {
                        CaveBuildEditorLog.LogSurfaceWarning(
                            $"[Surface] Incremental weld — could not resolve offset for {tile.name}; skipping.");
                        onComplete?.Invoke();
                        return;
                    }

                    ApplyFullWorldGridSlot(session, tile, off);
                    WeldFullWorldTileToCardinalNeighbors(session, tile, off);
                    CaveBuildTerrainHeightmapMemory.FlushAllSurfaceTerrains(mainTerrain);
                    RefreshMountainTerrainConnectivity(mainTerrain);
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel("FullWorld — incremental tile edge weld"));
        }

        public static void WeldAllFullWorldGridEdgesFromScene(Terrain mainTerrain, Action onComplete = null)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var session = CreateMinimalFullWorldGridSession(mainTerrain);
            if (session == null)
            {
                onComplete?.Invoke();
                return;
            }

            WeldFullWorldGridEdgesSync(session);
            onComplete?.Invoke();
        }

        static void BeginFullWorldTerraform(FullWorldGridSession session)
        {
            if (HollowTitanLandmarkMeatPhases.IsBlockingSurfaceTerraform ||
                HollowTitanStumpSculptPhases.IsRunning)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Waiting for Hollow Titan meat + stump sculpt before terraform…",
                    forceUnityConsole: false);
                CaveBuildActionPacing.ScheduleHeavy(
                    () => BeginFullWorldTerraform(session),
                    CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — wait Hollow Titan"));
                return;
            }

            var titanRoot = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (session?.Request != null &&
                session.Request.SurfaceScope == SurfaceBuildScope.FullWorld &&
                CaveBuildCursorSettings.LoadOrCreate().requireHollowTitanOnSurface &&
                (titanRoot == null || !HollowTitanLandmarkAuthor.IsMeatBuildComplete(titanRoot.transform)))
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Hollow Titan not complete — building sync before terraform.",
                    forceUnityConsole: true);
                HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(
                    session.MainTerrain,
                    session.Request,
                    onComplete: () => BeginFullWorldTerraform(session));
                return;
            }

            if (!CaveBuildQualityFastGates.RequireHollowTitanOnSurface(session.Request, out var titanMsg))
            {
                if (!string.IsNullOrEmpty(titanMsg))
                    CaveBuildEditorLog.LogSurface("[FastGate] " + titanMsg, forceUnityConsole: false);
                CaveBuildQualityFastGates.EnsureHollowTitanOnSurface(session.MainTerrain, session.Request);
            }
            if (session?.MainTerrain == null)
                return;

            var tileCount = session.PlaceOffsets?.Length ?? FullWorldTerrainTileCount;
            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                $"[Surface] directional LiDAR/sculpt ({tileCount} tiles)…",
                0.90f);
            session.Phase = FullWorldGridPhase.Terraform;
            _liveFullWorldGridPhase = FullWorldGridPhase.Terraform;
            session.Index = 0;
            TrackFullWorldGridProgress(session, "terraform begin");
            CaveBuildEditorLog.LogSurface(
                $"[Surface] FullWorld flat grid ready — {tileCount} tile(s). " +
                (PreferDeferSeamsUntilFullWorldTerraformComplete
                    ? "Terraform all tiles first; batch seams after sculpt…"
                    : PreferSequentialFullWorldTerrain
                        ? "Sequential terraform (one tile fully done before next)…"
                        : "Slow terraform…"),
                forceUnityConsole: true);
            CaveBuildActionPacing.SchedulePipelineFirstStep(
                () => QueueDirectionalTerraformTileAtIndex(session, 0),
                CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — center"));
        }

        static void QueueAaaPreTerraformLandmarkAndSeams(FullWorldGridSession session, Action onComplete)
        {
            if (session?.MainTerrain == null || session.Request == null)
            {
                onComplete?.Invoke();
                return;
            }

            var deferSeams = PreferDeferSeamsUntilFullWorldTerraformComplete;
            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    Cc0ContentImportUtility.EnsureAll(importItems: true);
                    ForceSnapEntireFullWorldGrid(session);
                    HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(
                        session.MainTerrain,
                        session.Request,
                        onComplete: () =>
                        {
                            SurfaceWorldBiomeAuthor.TagAllGroundTerrains(session.MainTerrain, session.Request.Seed);
                            if (deferSeams)
                            {
                                onComplete?.Invoke();
                                return;
                            }

                            SurfaceTerrainSeamWelds.QueuePeakFoothillSeamWeld(
                                session.MainTerrain,
                                () => QueueFullWorldProceedToTerraformAfterOuterRingSeam(onComplete));
                        });
                },
                CaveBuildPipelineDomains.QueueLabel(
                    deferSeams
                        ? "Full AAA — boss tree + biomes (seams after terraform)"
                        : "Full AAA — boss tree + biomes + peak/foothill weld"));
        }

        static void QueueFullWorldProceedAfterFlatGridComplete(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            ForceSnapEntireFullWorldGrid(session);

            var snapTileCount = session.PlaceOffsets?.Length ?? FullWorldTerrainTileCount;
            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                $"[Surface] snapping all {snapTileCount} tiles to ground level…",
                0.855f);

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    SnapAllFortyNineTilesToGroundLevel(session);
                    ForceSnapEntireFullWorldGrid(session);

                    CaveBuildProgressUI.ShowThrottled(
                        "Environment Kit",
                        "[Surface] welding 9×9 grid edges (remove tile lines)…",
                        0.86f);

                    QueueWeldFullWorldGridEdges(session, () =>
                    {
                        SurfaceTerrainGridRegistry.ReindexFromMain(session.MainTerrain, playDiskLocked: false);
                        var builtRadius = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                            ? SurfaceOpenWorldGridExpansion.MaxChebyshevRadius
                            : FullWorldChebyshevRadius;
                        SurfaceOpenWorldGridExpansion.WriteManifest(new SurfaceOpenWorldGridExpansion.Manifest
                        {
                            builtChebyshevRadius = builtRadius,
                            targetChebyshevRadius = SurfaceOpenWorldGridExpansion.GetTargetChebyshevRadius(),
                            targetTileCount = SurfaceOpenWorldGridExpansion.TargetTileCount,
                        });

                        var foothills = CollectMountainFoothillTiles(session.MainTerrain).Length;
                        var peaks = CollectMountainPeakTiles(session.MainTerrain).Length;
                        var tileCount = session.PlaceOffsets?.Length ?? FullWorldTerrainTileCount;
                        CaveBuildEditorLog.LogSurface(
                            CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                                ? $"[Surface] Full AAA flat grid welded — {tileCount} tile(s) edge-to-edge (9 play + {foothills} foothill + {peaks} peak + open world)."
                                : $"[Surface] FullWorld 9×9 grid complete — {FullWorldTerrainTileCount} tiles " +
                                  $"(9 play + {foothills} foothill + {peaks} peak). " +
                                  (PreferDeferSeamsUntilFullWorldTerraformComplete
                                      ? "Grid edges welded; LiDAR sculpt all tiles, then batch seams…"
                                      : "Grid edges welded; starting outer ring stitch, then LiDAR sculpt…"),
                            forceUnityConsole: true);

                        var placedCount = CollectAllFullWorldTerrains(session.MainTerrain).Length;
                        CaveBuildQualityFastGates.ValidateFlatGridTileCount(
                            placedCount,
                            tileCount,
                            out _);

                        void ProceedAfterHollowTitan()
                        {
                            if (PreferDeferSeamsUntilFullWorldTerraformComplete)
                            {
                                BeginFullWorldTerraform(session);
                                return;
                            }

                            CaveBuildActionPacing.ScheduleHeavyChain(
                                () => QueueFullWorldOuterRingSeamPipeline(session.MainTerrain, () =>
                                    QueueFullWorldProceedToTerraformAfterOuterRingSeam(() =>
                                        BeginFullWorldTerraform(session))),
                                CaveBuildPipelineDomains.QueueLabel("FullWorld outer ring seam"));
                        }

                        if (CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
                        {
                            QueueAaaPreTerraformLandmarkAndSeams(session, ProceedAfterHollowTitan);
                            return;
                        }

                        HollowTitanLandmarkAuthor.PlaceAaaBeforeTerraform(
                            session.MainTerrain,
                            session.Request,
                            onComplete: ProceedAfterHollowTitan);
                    });
                },
                CaveBuildPipelineDomains.QueueLabel(
                    $"FullWorld grid ground snap (~{session.PlaceOffsets?.Length ?? FullWorldTerrainTileCount} tiles)"));
        }

        /// <summary>
        /// One-shot Y alignment for every grid slot to <see cref="SceneGroundInfo.SurfaceY"/> (play-disk ground), then re-seat slots.
        /// </summary>
        static void SnapAllFortyNineTilesToGroundLevel(FullWorldGridSession session) =>
            SyncFullWorldGridToGroundLevel(session);

        static Terrain[] CollectAllFullWorldTerrains(Terrain mainTerrain)
        {
            var set = new HashSet<Terrain>();
            if (mainTerrain != null)
                set.Add(mainTerrain);

            foreach (var t in CollectGameplayTiles(mainTerrain))
            {
                if (t != null)
                    set.Add(t);
            }

            foreach (var t in CollectMountainWildernessTiles(mainTerrain))
            {
                if (t != null)
                    set.Add(t);
            }

            if (CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
            {
                foreach (var t in CollectOpenWorldTiles(mainTerrain))
                {
                    if (t != null)
                        set.Add(t);
                }
            }

            var list = new List<Terrain>(set);
            list.Sort((a, b) => string.CompareOrdinal(a?.name, b?.name));
            return list.ToArray();
        }

        /// <summary>
        /// Foothill + peak ring: stitch all wilderness tiles to play disk and neighbors, lock inner edges.
        /// Same chain as <see cref="SurfaceOuterRingMountainsAuthor.QueueApply"/> seam phases (without re-sculpt).
        /// </summary>
        /// <summary>
        /// After uniform flat 0.42 grid, skip per-edge height blends (hundreds of paced steps) — connect + lock only.
        /// </summary>
        public static bool PreferFastFlatGridOuterRingSeams { get; set; } = true;

        /// <summary>Lock foothill/peak/horizon inner bands in one heavy step instead of one queue step per tile.</summary>
        public static bool PreferBatchOuterRingLocks { get; set; }

        /// <summary>Stop outer-ring lock pass when exceeded; connectivity still runs (seconds, editor-time).</summary>
        public static float OuterRingSeamBudgetSeconds { get; set; } = 120f;

        /// <summary>
        /// Terraform must not start until outer-ring seam/lock queue + height flush are fully idle (prevents editor freeze).
        /// </summary>
        static void QueueFullWorldProceedToTerraformAfterOuterRingSeam(Action beginTerraform)
        {
            if (beginTerraform == null)
                return;

            CaveBuildProgressUI.ClearIfShown();
            CaveBuildActionPacing.QueueWhenIdle(
                beginTerraform,
                CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — wait idle"));
        }

        static void ShowOuterRingSeamProgress()
        {
            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                "[Surface] foothill+peak ring seam pass…",
                0.88f);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                _liveFullWorldGridPhase == FullWorldGridPhase.SeamAll ? "FullWorld seams" : "surface",
                "foothill+peak ring seam pass");
        }

        public static void QueueFullWorldOuterRingSeamPipeline(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            ShowOuterRingSeamProgress();

            if (PreferFastFlatGridOuterRingSeams)
            {
                QueueFullWorldOuterRingSeamPipelineFast(mainTerrain, onComplete);
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    EnforceWildernessGridLayout(mainTerrain);
                    RefreshMountainTerrainConnectivity(mainTerrain);

                    var foothills = CollectMountainFoothillTiles(mainTerrain).Length;
                    var peaks = CollectMountainPeakTiles(mainTerrain).Length;
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Outer ring seam pipeline — {foothills} foothill + {peaks} peak tile(s): " +
                        "wilderness ring → play↔foothill → peak↔foothill locks.",
                        forceUnityConsole: true);

                    QueueStitchWildernessRing(mainTerrain, () =>
                        QueueFinalizeFoothillRingToPlayDisk(mainTerrain, () =>
                            QueueLockPeakInnerEdgesToFoothillRing(mainTerrain, () =>
                            {
                                RefreshMountainTerrainConnectivity(mainTerrain);
                                CaveBuildEditorLog.LogSurface(
                                    "[Surface] Outer ring seam pipeline — foothill+peak+labyrinth ring stitched and locked.",
                                    forceUnityConsole: true);
                                CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                                    mainTerrain,
                                    onComplete,
                                    flushAllSurfaceTerrains: true);
                            })));
                },
                CaveBuildPipelineDomains.QueueLabel("FullWorld outer ring — paced seam start"));
        }

        /// <summary>
        /// Flat-grid fast path: connectivity + inner locks one wilderness tile per heavy step (no single-frame 72-tile stall).
        /// </summary>
        static void QueueFullWorldOuterRingSeamPipelineFast(Terrain mainTerrain, Action onComplete)
        {
            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    EnforceWildernessGridLayout(mainTerrain);
                    RefreshMountainTerrainConnectivity(mainTerrain);

                    var foothills = CollectMountainFoothillTiles(mainTerrain);
                    var peaks = CollectMountainPeakTiles(mainTerrain);
                    var horizons = CollectMountainHorizonTiles(mainTerrain);
                    ComputePlayDiskWorldBounds(mainTerrain, out var playMinX, out var playMaxX, out var playMinZ, out var playMaxZ);
                    ComputeFoothillRingWorldBounds(mainTerrain, out var fhMinX, out var fhMaxX, out var fhMinZ, out var fhMaxZ);
                    var lockMeters = SurfaceOuterRingMountainsAuthor.WildernessInnerLockMeters + 4f;
                    var lockSamples = 0;
                    var budgetSeconds = Mathf.Max(15f, OuterRingSeamBudgetSeconds);
                    var budgetStart = System.Diagnostics.Stopwatch.StartNew();
                    var budgetExceeded = false;

                    bool WithinBudget() => budgetStart.Elapsed.TotalSeconds < budgetSeconds;

                    int LockWildernessTilesSync(Terrain[] tiles)
                    {
                        if (tiles == null)
                            return 0;

                        var touched = 0;
                        for (var i = 0; i < tiles.Length; i++)
                        {
                            if (!WithinBudget())
                            {
                                budgetExceeded = true;
                                break;
                            }

                            var tile = tiles[i];
                            if (tile == null)
                                continue;

                            touched += LockSingleMountainWildernessInnerEdgeSync(mainTerrain, tile);
                        }

                        return touched;
                    }

                    void FinishFastOuterRing()
                    {
                        RefreshMountainTerrainConnectivity(mainTerrain);
                        var budgetNote = budgetExceeded
                            ? $"budget {budgetSeconds:F0}s exceeded — partial locks"
                            : $"budget {budgetStart.Elapsed.TotalSeconds:F0}s";
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Outer ring FAST seam — {foothills.Length} foothill + {peaks.Length} peak + " +
                            $"{horizons.Length} horizon: connectivity + {lockSamples} lock samples ({budgetNote}; no edge blend).",
                            forceUnityConsole: true);
                        CaveBuildMicroTerrainHeightmap.QueueFinalizeDelayedLod(
                            mainTerrain,
                            onComplete,
                            flushAllSurfaceTerrains: !CaveBuildEditorResponsiveness.IsLongBuildActive);
                    }

                    if (PreferBatchOuterRingLocks)
                    {
                        CaveBuildActionPacing.ScheduleHeavy(
                            () =>
                            {
                                lockSamples += LockWildernessTilesSync(foothills);
                                lockSamples += LockWildernessTilesSync(peaks);
                                lockSamples += LockWildernessTilesSync(horizons);
                                FinishFastOuterRing();
                            },
                            CaveBuildPipelineDomains.QueueLabel("FullWorld outer ring — batch lock"));
                        return;
                    }

                    var pacedSamples = new int[1];
                    void LockWildernessTilesPaced(
                        Terrain[] tiles,
                        float boundsMinX,
                        float boundsMaxX,
                        float boundsMinZ,
                        float boundsMaxZ,
                        bool sampleFoothillForPeaks,
                        int index,
                        Action afterAll)
                    {
                        if (tiles == null || index >= tiles.Length || !WithinBudget())
                        {
                            if (!WithinBudget())
                                budgetExceeded = true;
                            afterAll?.Invoke();
                            return;
                        }

                        var tile = tiles[index];
                        var step = index + 1;
                        CaveBuildActionPacing.ScheduleLight(
                            () =>
                            {
                                if (tile == null)
                                {
                                    LockWildernessTilesPaced(
                                        tiles,
                                        boundsMinX,
                                        boundsMaxX,
                                        boundsMinZ,
                                        boundsMaxZ,
                                        sampleFoothillForPeaks,
                                        index + 1,
                                        afterAll);
                                    return;
                                }

                                QueueLockWildernessInnerEdgeBand(
                                    mainTerrain,
                                    tile,
                                    boundsMinX,
                                    boundsMaxX,
                                    boundsMinZ,
                                    boundsMaxZ,
                                    lockMeters,
                                    sampleFoothillForPeaks,
                                    touched =>
                                    {
                                        pacedSamples[0] += touched;
                                        LockWildernessTilesPaced(
                                            tiles,
                                            boundsMinX,
                                            boundsMaxX,
                                            boundsMinZ,
                                            boundsMaxZ,
                                            sampleFoothillForPeaks,
                                            index + 1,
                                            afterAll);
                                    });
                            },
                            CaveBuildPipelineDomains.QueueLabel(
                                $"outer ring lock {step}/{tiles.Length} {tile?.name ?? "skip"}"));
                    }

                    LockWildernessTilesPaced(
                        foothills,
                        playMinX,
                        playMaxX,
                        playMinZ,
                        playMaxZ,
                        sampleFoothillForPeaks: false,
                        0,
                        () => LockWildernessTilesPaced(
                            peaks,
                            fhMinX,
                            fhMaxX,
                            fhMinZ,
                            fhMaxZ,
                            sampleFoothillForPeaks: true,
                            0,
                            () => LockWildernessTilesPaced(
                                horizons,
                                fhMinX,
                                fhMaxX,
                                fhMinZ,
                                fhMaxZ,
                                sampleFoothillForPeaks: true,
                                0,
                                () =>
                                {
                                    lockSamples = pacedSamples[0];
                                    FinishFastOuterRing();
                                })));
                },
                CaveBuildPipelineDomains.QueueLabel("FullWorld outer ring — fast connect prep"));
        }

        static void ScheduleFullWorldStep(FullWorldGridSession session) =>
            CaveBuildActionPacing.ScheduleLight(
                () => RunFullWorldGridStep(session),
                CaveBuildPipelineDomains.QueueLabel("FullWorld grid step"));

        /// <summary>
        /// Flat grid (81-tile core or ~289 extended): place all → terraform all → batch seam/lock.
        /// </summary>
        /// <summary>Lay every ground terrain slot on the grid (flat height only). No seams, sculpt, or terraform.</summary>
        public static void QueueFullWorldLayAllGroundTilesOnly(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action<int, string> onComplete)
        {
            if (mainTerrain == null || ground == null || request == null || onComplete == null)
            {
                onComplete?.Invoke(0, "Ground lay skipped — missing inputs.");
                return;
            }

            request.EnsureFullWorldSurfaceContract();
            if (request.SurfaceScope != SurfaceBuildScope.FullWorld ||
                !UsesFixedNineTileSquare(request, fullWorld: true))
            {
                onComplete(0, "Ground lay skipped — not FullWorld nine-tile build.");
                return;
            }

            PrepareExtendedOpenWorldManifestIfNeeded();
            var placeOffsets = ResolveFullWorldPlaceOffsets();
            var tilePlanCount = placeOffsets.Length;
            CaveBuildEditorLog.LogSurface(
                $"[Surface] LAY ALL GROUND FIRST — placing {tilePlanCount} flat terrain tile(s) on grid. " +
                "No seams, LiDAR, sculpt, or terraform until every slot exists.",
                forceUnityConsole: true);

            var session = CreateFullWorldGridSession(
                mainTerrain,
                ground,
                request,
                placeOffsets,
                onComplete,
                markGridPipelineFinished: false);
            session.LayOnlyPass = true;
            session.Phase = FullWorldGridPhase.PlaceAll;
            _liveFullWorldGridPhase = FullWorldGridPhase.PlaceAll;
            session.PreparedScene = true;
            session.Index = 0;
            EnforcePlayDiskGridLayout(mainTerrain, ground);
            SyncFullWorldGridToGroundLevel(session);
            LockFullWorldGridAnchor(session);
            EnsureFullWorldGridAnchorTransform(session);
            BeginFullWorldGroundLayPhase(session);
            ScheduleFullWorldStep(session);
        }

        public static void QueueFullWorldDirectionalPipeline(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action<int, string> onComplete)
        {
            if (mainTerrain == null || ground == null || request == null || onComplete == null)
            {
                onComplete?.Invoke(0, "FullWorld directional pipeline skipped — missing inputs.");
                return;
            }

            request.EnsureFullWorldSurfaceContract();
            if (request.SurfaceScope != SurfaceBuildScope.FullWorld ||
                !UsesFixedNineTileSquare(request, fullWorld: true))
            {
                onComplete(0, "FullWorld directional pipeline skipped — not FullWorld nine-tile build.");
                return;
            }

            if (_fullWorldGroundLaidThisBuild)
            {
                QueueFullWorldPostLayWorkPipeline(mainTerrain, ground, request, onComplete);
                return;
            }

            PrepareExtendedOpenWorldManifestIfNeeded();
            var placeOffsets = ResolveFullWorldPlaceOffsets();
            var tilePlanCount = placeOffsets.Length;
            CaveBuildEditorLog.LogSurface(
                CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                    ? $"[Surface] Full AAA grid — weld + sculpt after all {tilePlanCount} ground tiles exist."
                    : $"[Surface] FullWorld grid — place then weld/sculpt ({tilePlanCount} tiles).",
                forceUnityConsole: true);

            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildSurfaceCompletionGate.MarkFullWorldGridPipelineStarted(tilePlanCount);

            var session = CreateFullWorldGridSession(mainTerrain, ground, request, placeOffsets, onComplete);
            BeginFullWorldGridFailSafeSession(session, tilePlanCount, "pipeline start");
            CaveBuildActionPacing.SchedulePipelineFirstStep(
                () => RunFullWorldGridPrepare(session),
                CaveBuildPipelineDomains.SurfaceQueueLabel("FullWorld grid prepare"));
        }

        static void QueueFullWorldPostLayWorkPipeline(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action<int, string> onComplete)
        {
            var placeOffsets = ResolveFullWorldPlaceOffsets();
            var tilePlanCount = placeOffsets.Length;
            CaveBuildEditorLog.LogSurface(
                $"[Surface] All {tilePlanCount} ground tiles already laid — starting weld/terraform (seams after sculpt).",
                forceUnityConsole: true);

            CaveBuildActionPacing.PreparePipelineChainKickoff();
            CaveBuildSurfaceCompletionGate.MarkFullWorldGridPipelineStarted(tilePlanCount);

            var session = CreateFullWorldGridSession(mainTerrain, ground, request, placeOffsets, onComplete);
            BeginFullWorldGridFailSafeSession(session, tilePlanCount, "post-lay resume");
            session.PreparedScene = true;
            session.Index = placeOffsets.Length;
            CaveBuildActionPacing.SchedulePipelineFirstStep(
                () =>
                {
                    PurgeOrphanPlayDiskTerrains(session.MainTerrain);
                    PurgeOrphanWildernessTerrains(session.MainTerrain);
                    ConsolidateScatteredFullWorldTerrains(session.MainTerrain);
                    ForceSnapEntireFullWorldGrid(session);
                    QueueFullWorldProceedAfterFlatGridComplete(session);
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel("FullWorld post-lay work"));
        }

        static Vector2Int[] ResolveFullWorldPlaceOffsets() =>
            CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                ? SurfaceOpenWorldGridExpansion.BuildAaaExtendedPlaceOrder()
                : BuildFullWorldPlaceOrder();

        static void PrepareExtendedOpenWorldManifestIfNeeded()
        {
            if (!CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
                return;

            PreferSkipFlatGridPerTileSeams = true;
            SurfaceOpenWorldGridExpansion.WriteManifest(new SurfaceOpenWorldGridExpansion.Manifest
            {
                builtChebyshevRadius = 0,
                targetChebyshevRadius = SurfaceOpenWorldGridExpansion.GetTargetChebyshevRadius(),
                targetTileCount = SurfaceOpenWorldGridExpansion.TargetTileCount,
                placedTileCount = 0,
            });
        }

        static FullWorldGridSession CreateFullWorldGridSession(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Vector2Int[] placeOffsets,
            Action<int, string> onComplete,
            bool markGridPipelineFinished = true)
        {
            var terraformOffsets = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                ? placeOffsets
                : SurfaceFullWorldDirectionalBuild.BuildDirectionalOffsetOrder();

            return new FullWorldGridSession
            {
                MainTerrain = mainTerrain,
                Ground = ground,
                Request = request,
                Offsets = terraformOffsets,
                PlaceOffsets = placeOffsets,
                TilesRoot = FindTilesRoot(mainTerrain),
                WildernessRoot = GetOrCreateMountainWildernessRoot(mainTerrain),
                OnComplete = (count, msg) =>
                {
                    CaveBuildFullWorldGridCheckpoint.Clear("pipeline complete");
                    if (markGridPipelineFinished)
                        CaveBuildSurfaceCompletionGate.MarkFullWorldGridPipelineFinished(true);
                    onComplete?.Invoke(count, msg);
                },
            };
        }

        static void BeginFullWorldGridFailSafeSession(
            FullWorldGridSession session,
            int tilePlanCount,
            string reason)
        {
            if (session == null)
                return;

            CaveBuildMemoryGuard.ApplyExtendedGridMemoryProfile(tilePlanCount);
            CaveBuildFullWorldGridCheckpoint.BindActiveSession(session);
            CaveBuildFullWorldGridCheckpoint.Save(session, reason);
        }

        static void TrackFullWorldGridProgress(FullWorldGridSession session, string reason)
        {
            if (session == null)
                return;

            var tileCount = session.PlaceOffsets?.Length ?? session.Offsets?.Length ?? 0;
            if (tileCount > 81)
                CaveBuildFullWorldGridCheckpoint.Save(session, reason);
            CaveBuildMemoryGuard.OnFullWorldQueueStepCompleted(tileCount);
        }

        /// <summary>Reconstruct session after crash/memory pause and continue the grid chain.</summary>
        internal static FullWorldGridSession CreateResumeFullWorldGridSession(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            FullWorldGridPhase phase,
            int index,
            bool layOnlyPass,
            bool groundLaid,
            Action<int, string> onComplete)
        {
            _fullWorldGroundLaidThisBuild = groundLaid;
            var placeOffsets = ResolveFullWorldPlaceOffsets();
            var session = CreateFullWorldGridSession(mainTerrain, ground, request, placeOffsets, onComplete);
            session.Phase = phase;
            _liveFullWorldGridPhase = phase;
            session.Index = Mathf.Max(0, index);
            session.LayOnlyPass = layOnlyPass;
            session.PreparedScene = true;
            RefreshFullWorldGridSessionRoots(session);
            LockFullWorldGridAnchor(session);
            EnsureFullWorldGridAnchorTransform(session);
            BeginFullWorldGridFailSafeSession(session, placeOffsets.Length, $"resume {phase} @{index}");
            return session;
        }

        internal static void ResumeFullWorldGridFromCheckpoint(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            CaveBuildMemoryGuard.ClearMemoryPause();
            CaveBuildPauseController.Continue();
            BeginFullWorldGridFailSafeSession(
                session,
                session.PlaceOffsets?.Length ?? session.Offsets?.Length ?? 0,
                $"resume {session.Phase} @{session.Index}");

            switch (session.Phase)
            {
                case FullWorldGridPhase.PlaceAll:
                    ScheduleFullWorldStep(session);
                    break;
                case FullWorldGridPhase.PlaceAndSeam:
                    ScheduleFullWorldStep(session);
                    break;
                case FullWorldGridPhase.Terraform:
                    CaveBuildActionPacing.SchedulePipelineFirstStep(
                        () => QueueDirectionalTerraformTileAtIndex(session, session.Index),
                        CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — resume"));
                    break;
                case FullWorldGridPhase.SeamAll:
                    QueueFinalizeDirectionalFullWorldBuild(session);
                    break;
                default:
                    QueueFullWorldProceedAfterFlatGridComplete(session);
                    break;
            }
        }

        static int PurgeFullWorldWildernessResolutionMismatches(Terrain mainTerrain)
        {
            if (mainTerrain?.terrainData == null)
                return 0;

            var expected = mainTerrain.terrainData.heightmapResolution;
            var repaired = 0;

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                if (terrain == null || terrain == mainTerrain || terrain.terrainData == null)
                    continue;

                if (!IsOuterRingTerrainName(terrain.name) ||
                    !TryParseOuterRingTileOffset(terrain.name, out _) ||
                    TileHeightmapResolutionMatches(mainTerrain, terrain))
                    continue;

                if (TryRepairWildernessTileHeightmapResolution(mainTerrain, terrain))
                    repaired++;
            }

            if (repaired > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[PipelinePreserve] Repaired {repaired} wilderness tile heightmap resolution(s) in place (no delete).",
                    forceUnityConsole: true);
            }

            return repaired;
        }

        static bool TryRepairWildernessTileHeightmapResolution(Terrain mainTerrain, Terrain tile)
        {
            if (mainTerrain?.terrainData == null || tile?.terrainData == null)
                return false;

            if (TileHeightmapResolutionMatches(mainTerrain, tile))
                return true;

            var oldData = tile.terrainData;
            var oldRes = oldData.heightmapResolution;
            var newRes = mainTerrain.terrainData.heightmapResolution;
            var oldHeights = oldData.GetHeights(0, 0, oldRes, oldRes);
            var newHeights = ResampleHeightmapBilinear(oldHeights, oldRes, newRes);

            var newData = CreateFullWorldGridTileData(mainTerrain.terrainData);
            newData.SetHeights(0, 0, newHeights);
            tile.terrainData = newData;
            tile.Flush();
            return true;
        }

        static float[,] ResampleHeightmapBilinear(float[,] source, int sourceRes, int targetRes)
        {
            if (sourceRes == targetRes)
                return source;

            var result = new float[targetRes, targetRes];
            for (var z = 0; z < targetRes; z++)
            {
                for (var x = 0; x < targetRes; x++)
                {
                    var u = x / (float)(targetRes - 1);
                    var v = z / (float)(targetRes - 1);
                    var sx = u * (sourceRes - 1);
                    var sz = v * (sourceRes - 1);
                    var x0 = Mathf.FloorToInt(sx);
                    var z0 = Mathf.FloorToInt(sz);
                    var x1 = Mathf.Min(x0 + 1, sourceRes - 1);
                    var z1 = Mathf.Min(z0 + 1, sourceRes - 1);
                    var tx = sx - x0;
                    var tz = sz - z0;
                    var h00 = source[z0, x0];
                    var h10 = source[z0, x1];
                    var h01 = source[z1, x0];
                    var h11 = source[z1, x1];
                    var h0 = Mathf.Lerp(h00, h10, tx);
                    var h1 = Mathf.Lerp(h01, h11, tx);
                    result[z, x] = Mathf.Lerp(h0, h1, tz);
                }
            }

            return result;
        }

        static void RunFullWorldGridPrepare(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    PurgeOrphanPlayDiskTerrains(session.MainTerrain);
                    PurgeOrphanWildernessTerrains(session.MainTerrain);
                    if (session.TilesRoot != null)
                        RemoveStaleGameplayTiles(session.TilesRoot);
                    if (session.WildernessRoot != null)
                        RemoveStaleMountainWildernessTiles(session.WildernessRoot);
                    EnsureMainTerrainIdentity(session.MainTerrain);
                    PurgeFullWorldWildernessResolutionMismatches(session.MainTerrain);
                    if (session.Ground != null)
                        EnforcePlayDiskGridLayout(session.MainTerrain, session.Ground);
                    SyncFullWorldGridToGroundLevel(session);
                    LockFullWorldGridAnchor(session);
                    ConsolidateScatteredFullWorldTerrains(session.MainTerrain);

                    session.PreparedScene = true;
                    session.Phase = FullWorldGridPhase.PlaceAll;
                    _liveFullWorldGridPhase = FullWorldGridPhase.PlaceAll;
                    session.Index = 0;
                    BeginFullWorldGroundLayPhase(session);
                    ScheduleFullWorldStep(session);
                },
                CaveBuildPipelineDomains.QueueLabel("FullWorld grid purge"));
        }

        static void BeginFullWorldGroundLayPhase(FullWorldGridSession session)
        {
            var total = (session?.PlaceOffsets ?? session?.Offsets)?.Length ?? FullWorldTerrainTileCount;
            var batchSize = ResolveFullWorldGroundLayBatchSize();
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Ground lay — {total} tile(s), up to {batchSize} per queue step " +
                "(flat height only; flush deferred to batch end).",
                forceUnityConsole: true);
        }

        /// <summary>4–8 tiles per queue step — lower on conserve-GPU / low RAM budgets.</summary>
        static int ResolveFullWorldGroundLayBatchSize()
        {
            if (EnvironmentKitHardwareBudget.Active.ConserveGpuMemory)
                return 4;

            var ramGb = EnvironmentKitHardwareBudget.ResolveEditorRamBudgetGb();
            if (ramGb >= 18f)
                return FullWorldFlatGridPlaceBatchSize;
            if (ramGb >= 16f)
                return 6;
            return 4;
        }

        static void BeginFullWorldFlatGridPlacePhase(FullWorldGridSession session) =>
            BeginFullWorldGroundLayPhase(session);

        static void RunFullWorldGridStep(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
                return;

            if (session.Phase == FullWorldGridPhase.PlaceAll)
            {
                RunFullWorldPlaceAllStep(session);
                return;
            }

            if (session.Phase == FullWorldGridPhase.PlaceAndSeam)
            {
                RunFullWorldPlaceAndSeamStep(session);
                return;
            }

            if (session.Phase == FullWorldGridPhase.Terraform)
                QueueDirectionalTerraformTileAtIndex(session, session.Index);
        }

        static void RunFullWorldPlaceAllStep(FullWorldGridSession session)
        {
            if (!session.PreparedScene)
                return;

            var placeOffsets = session.PlaceOffsets ?? session.Offsets;
            if (session.Index >= placeOffsets.Length)
            {
                _fullWorldGroundLaidThisBuild = true;
                var placed = CollectAllFullWorldTerrains(session.MainTerrain).Length;
                TrackFullWorldGridProgress(session, $"ground lay complete ({placed} tiles)");
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] ALL GROUND LAID — {placed}/{placeOffsets.Length} terrain slot(s) on grid. " +
                    (session.LayOnlyPass
                        ? "No weld/seams/sculpt until the next pipeline step."
                        : "Starting weld/seams/sculpt next."),
                    forceUnityConsole: true);
                ForceSnapEntireFullWorldGrid(session);
                if (session.LayOnlyPass)
                {
                    session.OnComplete?.Invoke(placed, $"Ground lay complete — {placed} terrain(s).");
                    return;
                }

                QueueFullWorldProceedAfterFlatGridComplete(session);
                return;
            }

            LockFullWorldGridAnchor(session);
            var globalIndex = session.Index;
            var total = placeOffsets.Length;
            var batchSize = ResolveFullWorldGroundLayBatchSize();
            var batchCount = Mathf.Min(batchSize, total - globalIndex);
            var stepEnd = globalIndex + batchCount;
            var batchNumber = globalIndex / batchSize + 1;
            var totalBatches = (total + batchSize - 1) / batchSize;

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] lay ground batch {batchNumber}/{totalBatches} ({stepEnd}/{total})",
                0.84f + 0.04f * (stepEnd / (float)total));
            CaveBuildStepCounter.SetFlatGridPlaceProgress(stepEnd, total);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "FullWorld ground lay",
                $"lay ground batch {batchNumber}/{totalBatches} ({batchCount} tiles)");

            QueueFullWorldLayGroundTile(
                session,
                globalIndex,
                batchCount,
                stepEnd,
                total,
                batchNumber,
                totalBatches);
        }

        static void QueueFullWorldLayGroundTile(
            FullWorldGridSession session,
            int startIndex,
            int batchCount,
            int stepEnd,
            int total,
            int batchNumber,
            int totalBatches)
        {
            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    var placeOffsets = session.PlaceOffsets ?? session.Offsets;
                    var normalizedHeight = SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight;
                    var laidTiles = new List<Terrain>(batchCount);
                    var laidThisStep = 0;

                    for (var i = 0; i < batchCount; i++)
                    {
                        var globalIndex = startIndex + i;
                        var off = placeOffsets[globalIndex];
                        var step = globalIndex + 1;

                        if (!EnsureGridSlotTerrainPlace(session, off, out var tile) || tile == null)
                        {
                            CaveBuildEditorLog.LogSurfaceWarning(
                                $"[Surface] Ground lay — could not resolve tile at ({off.x},{off.y}), skipping.");
                            continue;
                        }

                        ApplyFullWorldGridSlot(session, tile, off);
                        CaveEditorUndo.RecordObject(tile.terrainData, "Flat terrain height");
                        CaveBuildTerrainHeightmapMemory.ApplyUniformFlatGroundLayDefer(tile, normalizedHeight);
                        var isSouthAnnex = off.y <= -1;
                        SurfaceTerrainBiomePreviewAuthor.ApplyPlannedBiomeAlphamap(
                            tile,
                            off,
                            session.Request?.Seed ?? 0,
                            isSouthAnnex);
                        laidTiles.Add(tile);
                        laidThisStep++;

                        if (step == 1 || step == total || step % 24 == 0)
                            CaveBuildLiveSceneFeedback.NotifyTerrainTile(tile, $"lay ground {step}/{total}");
                    }

                    CaveBuildTerrainHeightmapMemory.CommitGroundLayBatch(laidTiles);
                    EnvironmentKitHardwareBudget.OnQueueStepCompletedThrottled();

                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Ground lay batch {batchNumber}/{totalBatches} " +
                        $"({laidThisStep} tiles this step, {stepEnd}/{total} total).",
                        forceUnityConsole: true);

                    session.Index = startIndex + batchCount;
                    ScheduleFullWorldStep(session);
                },
                CaveBuildPipelineDomains.QueueLabel(
                    $"lay ground batch {batchNumber}/{totalBatches} ({batchCount} tiles)"));
        }

        static bool IsFirstHorizonPlaceIndex(Vector2Int[] placeOffsets, int index)
        {
            if (placeOffsets == null || index < 0 || index >= placeOffsets.Length)
                return false;

            var off = placeOffsets[index];
            return IsHorizonRingOffset(off) &&
                   (index == 0 || !IsHorizonRingOffset(placeOffsets[index - 1]));
        }

        static void RunFullWorldPlaceAndSeamStep(FullWorldGridSession session)
        {
            if (!session.PreparedScene)
                return;

            var placeOffsets = session.PlaceOffsets ?? session.Offsets;
            if (session.Index >= placeOffsets.Length)
            {
                QueueFullWorldProceedAfterFlatGridComplete(session);
                return;
            }

            if (IsFirstHorizonPlaceIndex(placeOffsets, session.Index))
            {
                QueueFullWorldHorizonRingBatchPlaceAndSeam(session);
                return;
            }

            LockFullWorldGridAnchor(session);
            var globalIndex = session.Index;
            var off = placeOffsets[globalIndex];
            var step = globalIndex + 1;
            var total = placeOffsets.Length;
            var ringLabel = FullWorldPlaceRingLabel(off);

            if (step == 10 || step == 26)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] FullWorld — starting {ringLabel} (step {step}/{total}).",
                    forceUnityConsole: true);
            }

            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] place+seam {ringLabel} {step}/{total} ({off.x},{off.y})",
                0.84f + 0.04f * (step / (float)total));

            QueueFullWorldFlatGridTile(session, placeOffsets, globalIndex, step, total, off);
        }

        static void QueueFullWorldFlatGridTile(
            FullWorldGridSession session,
            Vector2Int[] placeOffsets,
            int globalIndex,
            int step,
            int total,
            Vector2Int off)
        {
            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    if (!EnsureGridSlotTerrainPlace(session, off, out var tile) || tile == null)
                    {
                        CaveBuildEditorLog.LogSurfaceWarning(
                            $"[Surface] FullWorld place+seam — could not resolve tile at ({off.x},{off.y}), skipping.");
                        session.Index = globalIndex + 1;
                        ScheduleFullWorldStep(session);
                        return;
                    }

                    ApplyFullWorldGridSlot(session, tile, off);
                    CaveBuildStepCounter.SetFlatGridPlaceProgress(step, total);
                    CaveBuildLiveSceneFeedback.NotifyTerrainTile(
                        tile,
                        $"flat grid place {step}/{total}");
                    QueueApplyUniformFlatHeightmap(
                        tile,
                        () =>
                        {
                            void AdvanceFlatGridTile()
                            {
                                if (!IsNineTileGameplayOffset(off) && session.MainTerrain != null)
                                    EnforceWildernessGridLayout(session.MainTerrain);
                                if (PreferSkipFlatGridPerTileSeams &&
                                    IsEndOfChebyshevRingInPlaceOrder(placeOffsets, globalIndex) &&
                                    session.MainTerrain != null)
                                {
                                    RefreshMountainTerrainConnectivity(session.MainTerrain);
                                }

                                session.Index = globalIndex + 1;
                                ScheduleFullWorldStep(session);
                            }

                            if (PreferSkipFlatGridPerTileSeams)
                            {
                                tile.Flush();
                                AdvanceFlatGridTile();
                                return;
                            }

                            CaveBuildActionPacing.ScheduleLight(
                                () =>
                                {
                                    QueueSeamFullWorldTileAtOffset(
                                        session,
                                        tile,
                                        off,
                                        AdvanceFlatGridTile,
                                        flatGridSkipSeamBlend: true);
                                },
                                CaveBuildPipelineDomains.QueueLabel(
                                    $"FullWorld flat grid — seam {step}/{total}"));
                        });
                },
                CaveBuildPipelineDomains.QueueLabel($"FullWorld flat grid — place {step}/{total}"));
        }

        /// <summary>
        /// Stitch a batch in one queue step. Flat 0.42 tiles: neighbor connect only. Seeded tiles: sync edge blend (same math as paced seams).
        /// </summary>
        static void QueueStitchFullWorldFlatGridBatch(
            FullWorldGridSession session,
            Vector2Int[] placeOffsets,
            int startIndex,
            int tileCount,
            bool blendSeededEdges,
            Action onComplete)
        {
            if (session?.MainTerrain == null || placeOffsets == null || tileCount <= 0)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    CaveBuildActionPacing.TouchQueueActivity();
                    var main = session.MainTerrain;
                    var group = BuildMountainWildernessStitchGroup(main);

                    for (var b = 0; b < tileCount; b++)
                    {
                        var idx = startIndex + b;
                        if (idx >= placeOffsets.Length)
                            continue;

                        var off = placeOffsets[idx];
                        if (!TryResolveTerrainAtGridOffset(
                                main,
                                session.TilesRoot,
                                session.WildernessRoot,
                                off,
                                out var tile) ||
                            tile == null)
                            continue;

                        ApplyFullWorldGridSlot(session, tile, off);
                        var step = idx + 1;
                        CaveBuildRunStatusPublisher.PulseSubOperation(
                            "FullWorld flat grid",
                            $"{(blendSeededEdges ? "blend" : "stitch")} {step}/{placeOffsets.Length} {tile.name}");

                        if (blendSeededEdges)
                        {
                            var edges = CollectSeamEdges(tile, main, off, group);
                            for (var e = 0; e < edges.Count; e++)
                                BlendSharedEdgeSync(edges[e]);
                        }
                        else if (main != null)
                        {
                            RefreshMountainTerrainConnectivity(main);
                        }

                        tile.Flush();
                    }

                    if (main != null)
                        RefreshMountainTerrainConnectivity(main);

                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel(
                    blendSeededEdges
                        ? $"FullWorld flat grid — blend batch ({tileCount})"
                        : $"FullWorld flat grid — stitch batch ({tileCount})"));
        }

        static void SnapAllPlacedFullWorldTilesToGrid(FullWorldGridSession session, int throughIndexInclusive)
        {
            if (session?.MainTerrain == null)
                return;

            RefreshFullWorldGridSessionRoots(session);

            var placeOffsets = session.PlaceOffsets ?? session.Offsets;
            if (placeOffsets == null)
                return;

            throughIndexInclusive = Mathf.Min(throughIndexInclusive, placeOffsets.Length - 1);
            for (var i = 0; i <= throughIndexInclusive; i++)
            {
                var off = placeOffsets[i];
                if (!TryForceResolveFullWorldTileAtOffset(session, off, out var tile) ||
                    tile == null)
                    continue;

                ApplyFullWorldGridSlot(session, tile, off);
            }

            EnforceWildernessGridLayout(session.MainTerrain);
            RefreshMountainTerrainConnectivity(session.MainTerrain);
        }

        /// <summary>
        /// Horizon ring batch place+seam (original build path). Work is micro-tasked so the editor stays responsive.
        /// </summary>
        static void QueueFullWorldHorizonRingBatchPlaceAndSeam(FullWorldGridSession session)
        {
            var placeOffsets = session.PlaceOffsets ?? session.Offsets;
            var horizonOffsets = new List<Vector2Int>(HorizonRingTileCount);
            for (var i = session.Index; i < placeOffsets.Length; i++)
            {
                if (IsHorizonRingOffset(placeOffsets[i]))
                    horizonOffsets.Add(placeOffsets[i]);
            }

            if (horizonOffsets.Count == 0)
            {
                session.Index++;
                ScheduleFullWorldStep(session);
                return;
            }

            var gridTotal = placeOffsets.Length;
            var gridStep = session.Index + 1;
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] flat grid {gridStep}/{gridTotal} — horizon ring batch ({horizonOffsets.Count} tiles)…",
                0.84f + 0.04f * (gridStep / (float)Mathf.Max(1, gridTotal)));
            CaveBuildActionPacing.TouchQueueActivity();
            CaveBuildStepCounter.SetFlatGridPlaceProgress(gridStep, gridTotal);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "FullWorld flat grid",
                $"flat grid {gridStep}/{gridTotal} — horizon batch ({horizonOffsets.Count} tiles)");

            var horizonStartIndex = session.Index;
            var cursor = 0;

            void SeedNextTile()
            {
                if (cursor >= horizonOffsets.Count)
                {
                    void StitchHorizonSubBatch(int subStart)
                    {
                        if (subStart >= horizonOffsets.Count)
                        {
                            RefreshMountainTerrainConnectivity(session.MainTerrain);
                            session.Index += horizonOffsets.Count;
                            CaveBuildEditorLog.LogSurface(
                                $"[Surface] Horizon ring batch — placed+seamed {horizonOffsets.Count} tile(s).",
                                forceUnityConsole: true);
                            ScheduleFullWorldStep(session);
                            return;
                        }

                        var subCount = Mathf.Min(
                            FullWorldFlatGridPlaceBatchSize,
                            horizonOffsets.Count - subStart);
                        CaveBuildActionPacing.ScheduleLight(
                            () =>
                            {
                                EnforceWildernessGridLayout(session.MainTerrain);
                                QueueStitchFullWorldFlatGridBatch(
                                    session,
                                    placeOffsets,
                                    horizonStartIndex + subStart,
                                    subCount,
                                    blendSeededEdges: false,
                                    () => StitchHorizonSubBatch(subStart + subCount));
                            },
                            CaveBuildPipelineDomains.QueueLabel(
                                $"FullWorld horizon batch — stitch ({subStart + subCount}/{horizonOffsets.Count})"));
                    }

                    StitchHorizonSubBatch(0);
                    return;
                }

                var hOff = horizonOffsets[cursor];
                cursor++;

                CaveBuildActionPacing.ScheduleHeavy(
                    () =>
                    {
                        CaveBuildActionPacing.TouchQueueActivity();

                        if (!EnsureGridSlotTerrainPlace(session, hOff, out var hTile) || hTile == null)
                        {
                            CaveBuildEditorLog.LogSurfaceWarning(
                                $"[Surface] Horizon batch — skip ({hOff.x},{hOff.y}).");
                            CaveBuildActionPacing.ScheduleLight(SeedNextTile);
                            return;
                        }

                        ApplyFullWorldGridSlot(session, hTile, hOff);
                        QueueApplyUniformFlatHeightmap(
                            hTile,
                            () =>
                            {
                                QueueSeedFullWorldTileEdges(
                                    session,
                                    hTile,
                                    hOff,
                                    () => CaveBuildActionPacing.ScheduleLight(SeedNextTile));
                            });
                    },
                    CaveBuildPipelineDomains.QueueLabel("FullWorld horizon ring — seed tile"));
            }

            SeedNextTile();
        }

        static void QueueSeedFullWorldTileEdges(
            FullWorldGridSession session,
            Terrain tile,
            Vector2Int off,
            Action onComplete)
        {
            if (tile?.terrainData == null || session?.MainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (IsNineTileGameplayOffset(off))
            {
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        SeedFullWorldTileEdges(session, tile, off);
                        onComplete?.Invoke();
                    },
                    CaveBuildPipelineDomains.QueueLabel("FullWorld flat grid — play seed"));
                return;
            }

            QueueSeedWildernessHeightsFromInnerRing(
                tile,
                session.MainTerrain,
                off,
                session.TilesRoot,
                session.WildernessRoot,
                onComplete);
        }

        internal static FullWorldGridSession CreateMinimalFullWorldGridSession(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return null;

            var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
            var session = new FullWorldGridSession
            {
                MainTerrain = mainTerrain,
                Ground = ground,
                PlaceOffsets = BuildFullWorldPlaceOrder(),
                TilesRoot = FindTilesRoot(mainTerrain),
                WildernessRoot = GetOrCreateMountainWildernessRoot(mainTerrain),
                GridAnchorLocked = true,
            };
            SyncFullWorldGridToGroundLevel(session);
            return session;
        }

        internal static bool TryEnsureFlatGridTileAtOffset(
            FullWorldGridSession session,
            Vector2Int off,
            out Terrain tile) =>
            EnsureGridSlotTerrainPlace(session, off, out tile);

        internal static void WeldChebyshevRingBoundary(
            FullWorldGridSession session,
            int ringChebyshev,
            int gridRadius)
        {
            if (session?.MainTerrain == null || ringChebyshev < 1)
                return;

            EnsureFullWorldGridAnchorTransform(session);
            var offsets = BuildChebyshevRingOffsets(ringChebyshev, gridRadius);
            for (var i = 0; i < offsets.Length; i++)
            {
                if (!TryForceResolveFullWorldTileAtOffset(session, offsets[i], out var tile) || tile == null)
                    continue;

                ApplyFullWorldGridSlot(session, tile, offsets[i]);
                WeldFullWorldTileToCardinalNeighbors(session, tile, offsets[i]);
            }

            RefreshMountainTerrainConnectivity(session.MainTerrain);
        }

        /// <summary>Spawn or resolve a grid slot — height fill is queued separately.</summary>
        static bool EnsureGridSlotTerrainPlace(FullWorldGridSession session, Vector2Int off, out Terrain tile)
        {
            tile = null;
            RefreshFullWorldGridSessionRoots(session);
            var mainTerrain = session.MainTerrain;
            var tilesRoot = session.TilesRoot;
            var wildernessRoot = session.WildernessRoot;

            if (off == Vector2Int.zero)
            {
                tile = mainTerrain;
                ApplyFullWorldGridSlot(session, mainTerrain, Vector2Int.zero);
                SurfaceTerrainGridRegistry.ApplyIndex(
                    mainTerrain,
                    SurfaceTerrainGridIndex.PlayMainRing,
                    Vector2Int.zero,
                    SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0));
                return true;
            }

            if (IsNineTileGameplayOffset(off))
            {
                if (!TryFindTileAtOffset(tilesRoot, off, out tile) || tile == null)
                {
                    if (!TrySpawnFlatPlayNeighbor(session, off, out tile))
                        return false;
                    session.SpawnedNew++;
                }

                ApplyFullWorldGridSlot(session, tile, off);
                return true;
            }

            if (!TryFindOuterRingTileAtOffset(wildernessRoot, off, null, out tile) || tile == null ||
                !TileHeightmapResolutionMatches(mainTerrain, tile))
            {
                if (tile != null && TryRepairWildernessTileHeightmapResolution(mainTerrain, tile))
                {
                    ApplyFullWorldGridSlot(session, tile, off);
                    return true;
                }

                if (tile == null)
                {
                    if (!TrySpawnFlatWildernessTile(session, off, out tile))
                        return false;
                    session.SpawnedNew++;
                }
            }

            ApplyFullWorldGridSlot(session, tile, off);
            return true;
        }

        static void SeedFullWorldTileEdges(FullWorldGridSession session, Terrain tile, Vector2Int off)
        {
            if (tile?.terrainData == null || session.MainTerrain == null)
                return;

            if (IsNineTileGameplayOffset(off))
            {
                if (off != Vector2Int.zero)
                    SeedHeightsFromSharedEdges(tile, session.MainTerrain, off, session.MainTerrain);
                return;
            }

            ComputePlayDiskWorldBounds(session.MainTerrain, out var playMinX, out var playMaxX, out var playMinZ, out var playMaxZ);
            var res = tile.terrainData.heightmapResolution;
            var band = Mathf.Max(Mathf.Max(4, res / 12), res / 4);
            var heights = BuildWildernessSeedHeightsArray(
                tile,
                session.MainTerrain,
                off,
                session.TilesRoot,
                session.WildernessRoot,
                band,
                res,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ);
            CaveEditorUndo.RecordObject(tile.terrainData, "Seed wilderness tile from inner ring");
            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(tile, 0, 0, heights, requestDelayLod: false);
            tile.Flush();
        }

        static void QueueSeamFullWorldTileAtOffset(
            FullWorldGridSession session,
            Terrain tile,
            Vector2Int requestedOff,
            Action onComplete,
            bool flatGridSkipSeamBlend = false)
        {
            var mainTerrain = session?.MainTerrain;
            if (mainTerrain == null || tile?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var off = requestedOff;
            if (!TryResolveSurfaceTileGridOffset(mainTerrain, tile, out var resolvedOff))
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[Surface] Seam — could not resolve offset for {tile.name}; using ({requestedOff.x},{requestedOff.y}).");
            }
            else
            {
                off = resolvedOff;
            }

            ApplyFullWorldGridSlot(session, tile, off);

            var anchor = session.GridAnchor ?? FindFullWorldGridAnchor(mainTerrain);
            var expected = anchor != null
                ? anchor.TransformPoint(FullWorldGridLocalPosition(mainTerrain, off))
                : ComputeGridSlotWorldOrigin(mainTerrain, off);
            var mismatch = new Vector2(
                tile.transform.position.x - expected.x,
                tile.transform.position.z - expected.z).magnitude;
            if (mismatch > 1f)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[Surface] Seam — {tile.name} was {mismatch:F1}m off grid; forced snap before stitch.");
                ApplyFullWorldGridSlot(session, tile, off);
            }

            void RunStitch()
            {
                var group = BuildMountainWildernessStitchGroup(mainTerrain);
                QueueStitchTileSeamsPaced(
                    tile,
                    mainTerrain,
                    off,
                    group,
                    () =>
                    {
                        RefreshMountainTerrainConnectivity(mainTerrain);
                        onComplete?.Invoke();
                    },
                    tile.name,
                    flatGridSkipSeamBlend: flatGridSkipSeamBlend);
            }

            RunStitch();
        }

        static void ScheduleTerraformStep(FullWorldGridSession session, int nextIndex)
        {
            if (PreferSequentialFullWorldTerrain)
            {
                CaveBuildActionPacing.QueueWhenIdle(
                    () => QueueDirectionalTerraformTileAtIndex(session, nextIndex),
                    CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — next tile"));
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () => QueueDirectionalTerraformTileAtIndex(session, nextIndex),
                CaveBuildPipelineDomains.QueueLabel("FullWorld terraform step"));
        }

        static void QueueDirectionalTerraformTileAtIndex(FullWorldGridSession session, int index)
        {
            if (session?.MainTerrain == null)
            {
                session?.OnComplete?.Invoke(0, "FullWorld terraform aborted.");
                return;
            }

            if (index >= session.Offsets.Length)
            {
                BeginFullWorldSeamAllPhase(session);
                return;
            }

            session.Index = index;
            TrackFullWorldGridProgress(session, $"terraform queued {index + 1}/{session.Offsets.Length}");
            var off = session.Offsets[index];
            var arm = SurfaceFullWorldDirectionalBuild.ClassifyArm(off);
            var armLabel = SurfaceFullWorldDirectionalBuild.ArmLabel(arm);
            var step = index + 1;
            var total = session.Offsets.Length;

            if (!TryResolveTerrainAtGridOffset(
                    session.MainTerrain,
                    session.TilesRoot,
                    session.WildernessRoot,
                    off,
                    out var tile) ||
                tile == null)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    $"[Surface] Terraform — missing tile at ({off.x},{off.y}), skipping.");
                ScheduleTerraformStep(session, index + 1);
                return;
            }

            SetLiveTerraformProgress(step, total, armLabel);
            SetLiveTerrainFocus(tile);
            CaveBuildStepCounter.SetFlatGridTerraformProgress(step, total);
            PulseLiveTerraformMicro("start", 0, 4, tile, armLabel, step, total);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "FullWorld terraform",
                $"{armLabel} {step}/{total} ({tile.name})");
            CaveBuildLiveSceneFeedback.NotifyTerrainTile(
                tile,
                $"terraform {armLabel} {step}/{total}");

            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                $"[Surface] terraform {armLabel} {step}/{total} ({tile.name})",
                0.88f + 0.08f * (step / (float)total));

            if (off == Vector2Int.zero)
            {
                QueueDirectionalBuildCenterMain(session, index, tile);
                return;
            }

            if (IsNineTileGameplayOffset(off))
            {
                QueueDirectionalBuildPlayTile(session, index, tile, off);
                return;
            }

            QueueDirectionalBuildWildernessTile(session, index, tile, off);
        }

        static void ScheduleDirectionalStep(FullWorldGridSession session, int nextIndex)
        {
            TrackFullWorldGridProgress(session, $"terraform step done → {nextIndex}");
            if (!PipelineContentPreservePolicy.IntegrateOnlyActive &&
                session.Offsets != null &&
                session.Offsets.Length > 0 &&
                nextIndex >= session.Offsets.Length / 2)
            {
                PipelineContentPreservePolicy.MarkMidPipelineIntegrateMode(
                    $"terraform past halfway ({nextIndex}/{session.Offsets.Length})");
            }

            ScheduleTerraformStep(session, nextIndex);
        }

        public static void QueueApplyUniformFlatHeightmap(
            Terrain terrain,
            Action onComplete,
            float normalizedHeight = -1f)
        {
            if (terrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (normalizedHeight < 0f)
                normalizedHeight = SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight;

            CaveEditorUndo.RecordObject(terrain.terrainData, "Flat terrain height");
            CaveBuildMicroTerrainHeightmap.QueueWriteUniformFlat(
                terrain,
                normalizedHeight,
                "FullWorld flat grid",
                onComplete);
        }

        public static void ApplyUniformFlatHeightmap(Terrain terrain, float normalizedHeight = -1f)
        {
            if (terrain?.terrainData == null)
                return;

            if (normalizedHeight < 0f)
                normalizedHeight = SurfaceFullWorldDirectionalBuild.FlatNormalizedHeight;

            var res = terrain.terrainData.heightmapResolution;
            var heights = new float[res, res];
            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                    heights[z, x] = normalizedHeight;
            }

            CaveEditorUndo.RecordObject(terrain.terrainData, "Flat terrain height");
            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(
                terrain,
                0,
                0,
                heights,
                requestDelayLod: false);
            terrain.Flush();
        }

        static void ApplyPlayTileGridSlot(Terrain mainTerrain, Terrain tile, Vector2Int off)
        {
            if (mainTerrain == null || tile == null)
                return;

            if (TryApplyFullWorldGridSlotFromMain(mainTerrain, tile, off))
                return;

            tile.transform.rotation = Quaternion.identity;
            tile.transform.localScale = Vector3.one;
            tile.transform.position = ComputeGridSlotWorldOrigin(mainTerrain, off);
        }

        static bool TrySpawnFlatPlayNeighbor(
            FullWorldGridSession session,
            Vector2Int off,
            out Terrain tile)
        {
            tile = null;
            var mainTerrain = session?.MainTerrain;
            var tilesRoot = session?.TilesRoot;
            if (mainTerrain?.terrainData == null || tilesRoot == null)
                return false;

            var tileData = CreateFreshTileData(mainTerrain.terrainData);
            tileData.name = $"SurfaceTileData_{off.x}_{off.y}";
            var go = Terrain.CreateTerrainGameObject(tileData);
            go.name = $"SurfaceTerrainTile_{off.x}_{off.y}";
            CaveEditorUndo.RegisterCreated(go, "FullWorld flat play tile");
            TryTagAsGround(go);
            go.transform.SetParent(tilesRoot, false);
            tile = go.GetComponent<Terrain>();
            if (tile == null)
                return false;

            ApplyFullWorldGridSlot(session, tile, off);
            SurfaceTerrainGridRegistry.ApplyIndex(
                tile,
                SurfaceTerrainGridIndex.PlayNeighborRing,
                off,
                SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0));
            return true;
        }

        static bool TrySpawnFlatWildernessTile(
            FullWorldGridSession session,
            Vector2Int off,
            out Terrain tile)
        {
            tile = null;
            var mainTerrain = session?.MainTerrain;
            var tilesRoot = session?.TilesRoot;
            var wildernessRoot = session?.WildernessRoot;
            if (mainTerrain?.terrainData == null || wildernessRoot == null)
                return false;

            ResolveWildernessRing(off, out var prefix, out var ringId, out _);
            var isPeak = IsPeakRingOffset(off);
            var isHorizon = IsHorizonRingOffset(off);
            var isOpenWorld = GetOuterRingChebyshevDistance(off) > FullWorldChebyshevRadius;
            var tileData = CreateFullWorldGridTileData(mainTerrain.terrainData);
            tileData.name = isOpenWorld
                ? $"SurfaceOpenWorldData_{off.x}_{off.y}"
                : isHorizon
                    ? $"SurfaceMountainOuterData_horizon_{off.x}_{off.y}"
                    : isPeak
                        ? $"SurfaceMountainOuterData_peak_{off.x}_{off.y}"
                        : $"SurfaceMountainOuterData_foothill_{off.x}_{off.y}";
            var go = Terrain.CreateTerrainGameObject(tileData);
            go.name = $"{prefix}{off.x}_{off.y}";
            CaveEditorUndo.RegisterCreated(go, "FullWorld flat wilderness tile");
            TryTagAsGround(go);
            go.transform.SetParent(wildernessRoot, false);
            tile = go.GetComponent<Terrain>();
            if (tile == null)
                return false;

            ApplyFullWorldGridSlot(session, tile, off);

            var mergeOff = SurfaceTerrainGridRegistry.StepTowardOrigin(off);
            var mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
            if (TryResolveTerrainAtGridOffset(mainTerrain, tilesRoot, wildernessRoot, mergeOff, out var mergeTerrain) &&
                mergeTerrain != null)
            {
                if (mergeTerrain == mainTerrain)
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayMainRing, 0, 0);
                else if (TryParseTileOffset(mergeTerrain.name, out var mo))
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(SurfaceTerrainGridIndex.PlayNeighborRing, mo.x, mo.y);
                else if (TryParseOuterRingTileOffset(mergeTerrain.name, out var fo))
                    mergeId = SurfaceTerrainGridIndex.BuildTileId(ringId, fo.x, fo.y);
            }

            SurfaceTerrainGridRegistry.ApplyIndex(tile, ringId, off, mergeId);
            return true;
        }

        static void QueueDirectionalBuildCenterMain(FullWorldGridSession session, int index, Terrain main)
        {
            ApplyUniformFlatHeightmap(main);
            SurfaceFloridaDemBuildState.MarkAuthoritativeStampCompleted();
            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(main);
            main.Flush();
            var centerXZ = ResolvePlayDiskCenterXZ(session.Ground, main);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Directional center — hometown flat (no sculpt) on SurfaceTerrainMain at " +
                $"({centerXZ.x:F0}, {centerXZ.z:F0}), Y≈{session.Ground?.SurfaceY ?? 0f:F1}m.",
                forceUnityConsole: false);
            ScheduleDirectionalStep(session, index + 1);
        }

        static void QueueDirectionalBuildPlayTile(
            FullWorldGridSession session,
            int index,
            Terrain tile,
            Vector2Int off)
        {
            ApplyUniformFlatHeightmap(tile);
            CaveBuildTerrainHeightmapMemory.AfterTileHeightmapPass(tile);
            tile.Flush();
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Directional play tile — hometown flat (no sculpt) {tile.name} ({off.x},{off.y}).",
                forceUnityConsole: false);

            if (PreferDeferSeamsUntilFullWorldTerraformComplete)
            {
                QueueIncrementalFullWorldTileEdgeWeld(
                    session.MainTerrain,
                    tile,
                    () => ScheduleDirectionalStep(session, index + 1));
                return;
            }

            QueueDirectionalStitchPlayTile(session, index, tile, off);
        }

        static void QueueDirectionalStitchPlayTile(
            FullWorldGridSession session,
            int index,
            Terrain tile,
            Vector2Int off)
        {
            var group = BuildPlayDiskTerrainGroup(session.MainTerrain);
            QueueStitchTileSeamsPaced(
                tile,
                session.MainTerrain,
                off,
                group,
                () =>
                {
                    tile.Flush();
                    ScheduleDirectionalStep(session, index + 1);
                });
        }

        static void QueueDirectionalBuildWildernessTile(
            FullWorldGridSession session,
            int index,
            Terrain tile,
            Vector2Int off)
        {
            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    PulseTerraformTileMicro(session, index, tile, off, 1, 4, "align");
                    RefreshFullWorldGridSessionRoots(session);
                    LockFullWorldGridAnchor(session);
                    ApplyFullWorldGridSlot(session, tile, off);
                    QueueDirectionalBuildWildernessTileSculpt(session, index, tile, off);
                },
                CaveBuildPipelineDomains.QueueLabel("FullWorld terraform — micro align"));
        }

        static void QueueDirectionalBuildWildernessTileSculpt(
            FullWorldGridSession session,
            int index,
            Terrain tile,
            Vector2Int off)
        {
            PulseTerraformTileMicro(session, index, tile, off, 2, 4, "sculpt");

            var ringSession = new AttachOuterRingSession
            {
                MainTerrain = session.MainTerrain,
                Request = session.Request,
                OuterRoot = session.WildernessRoot,
                GameplayRoot = session.TilesRoot,
            };
            QueueCompleteOuterRingTileAfterSeed(ringSession, tile, () =>
            {
                HollowTitanLandmarkAuthor.TrySculptStumpAfterTileTerraform(session.MainTerrain, tile);
                ScheduleDirectionalStep(session, index + 1);
            });
        }

        static void BeginFullWorldSeamAllPhase(FullWorldGridSession session)
        {
            if (session?.MainTerrain == null)
            {
                session?.OnComplete?.Invoke(0, "FullWorld seam pass aborted.");
                return;
            }

            session.Phase = FullWorldGridPhase.SeamAll;
            _liveFullWorldGridPhase = FullWorldGridPhase.SeamAll;
            session.Index = 0;
            var tileCount = session.Offsets?.Length ?? FullWorldTerrainTileCount;
            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                $"[Surface] batch seams — all {tileCount} tiles terraformed",
                0.96f);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] FullWorld terraform complete ({tileCount} tiles) — batch seam/lock pass next.",
                forceUnityConsole: true);
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "FullWorld seams",
                $"batch stitch after {tileCount} tiles");
            QueueFinalizeDirectionalFullWorldBuild(session);
        }

        static void QueueFinalizeDirectionalFullWorldBuild(FullWorldGridSession session)
        {
            ForceSnapEntireFullWorldGrid(session);
            RefreshMountainTerrainConnectivity(session.MainTerrain);

            void FinishDirectional()
            {
                SurfaceFloridaDemBuildState.MarkNineTilePlayDiskPolishCompleted();
                SurfaceFloridaDemBuildState.MarkFullWorldDirectionalBuildCompleted();
                SurfaceTerrainGridRegistry.ReindexFromMain(session.MainTerrain, playDiskLocked: true);

                var surfaceRoot = session.MainTerrain.transform.parent;
                if (CaveBuildAaaSessionPolicy.IsFullAaaRebuild && session.Request != null)
                {
                    BiomeGrassScatterAuthor.EnsureBiomePropsScattered(
                        session.MainTerrain,
                        session.Request,
                        surfaceRoot);
                    WorldPlanV4RuntimeSystemsAuthor.EnsureInScene();
                }

                var placed = CollectAllFullWorldTerrains(session.MainTerrain).Length;
                var foothills = CollectMountainFoothillTiles(session.MainTerrain).Length;
                var peaks = CollectMountainPeakTiles(session.MainTerrain).Length;
                var horizon = CollectMountainHorizonTiles(session.MainTerrain).Length;
                var openWorld = CollectOpenWorldTiles(session.MainTerrain).Length;
                var msg = CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid
                    ? $"Full AAA directional build complete — {placed} terrain(s) " +
                      $"(9 play + {foothills} foothill + {peaks} peak + {horizon} horizon + {openWorld} open world)."
                    : $"FullWorld directional build complete — {FullWorldTerrainTileCount} tiles " +
                      $"(9 play + {foothills} foothill + {peaks} peak + {horizon} horizon), outer rings seamed and locked.";
                CaveBuildEditorLog.LogSurface("[Surface] " + msg, forceUnityConsole: true);
                if (PipelineContentPreservePolicy.IntegrateOnlyActive)
                {
                    var playTiles = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(session.MainTerrain).Count;
                    CaveBuildEditorLog.LogSurface(
                        $"[PipelineIntegrate] Playable merge check — {playTiles}/9 play-disk tiles kept; " +
                        "new sculpt/props layered onto existing world (no blanket delete).",
                        forceUnityConsole: true);
                }

                session.OnComplete?.Invoke(placed, msg);
            }

            void AfterSeamsDressAndFinish()
            {
                var surfaceRoot = session.MainTerrain.transform.parent;
                SurfaceSeamDressingPropAuthor.QueueDressSeamEdges(
                    session.MainTerrain,
                    surfaceRoot,
                    session.Request?.Seed ?? 0,
                    () => QueueStitchNineTilePlayDisk(session.MainTerrain, session.Ground, FinishDirectional));
            }

            void RunSeamPipelineAfterGridWeld()
            {
                if (CaveBuildAaaSessionPolicy.UsesExtendedOpenWorldGrid)
                {
                    if (PreferDeferSeamsUntilFullWorldTerraformComplete)
                    {
                        SurfaceTerrainSeamWelds.QueuePeakFoothillSeamWeld(
                            session.MainTerrain,
                            () => QueueFullWorldOuterRingSeamPipeline(
                                session.MainTerrain,
                                AfterSeamsDressAndFinish));
                        return;
                    }

                    AfterSeamsDressAndFinish();
                    return;
                }

                QueueFullWorldOuterRingSeamPipeline(session.MainTerrain, AfterSeamsDressAndFinish);
            }

            void AfterFoothillResnapAndGridWeld()
            {
                HollowTitanLandmarkAuthor.ResnapToTerrain(session.MainTerrain);
                HollowTitanLandmarkMeatPhases.EnsureLandmarkExteriorVisible();
                WeldFullWorldGridEdgesSync(session);
                RunSeamPipelineAfterGridWeld();
            }

            if (session?.Request != null &&
                session.Request.SurfaceIncludeMountains &&
                session.Request.UseOuterRingMountains)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Final foothill rolling-hills pass (Appalachian relief + outer bowl dip)…",
                    forceUnityConsole: false);
                SurfaceOuterRingMountainsAuthor.QueueApplyFoothillRing(
                    session.MainTerrain,
                    session.Request,
                    AfterFoothillResnapAndGridWeld);
            }
            else
            {
                AfterFoothillResnapAndGridWeld();
            }
        }
    }
}
#endif
