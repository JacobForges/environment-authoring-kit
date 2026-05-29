#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

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
            var offsets = NeighborOffsets(score, fullWorld, request.SurfaceTileLayoutVariant, request.Seed);
            var added = new List<Terrain>();
            var entries = new List<GameplayTileEntry>();

            RemoveStaleGameplayTiles(tilesRoot);

            var tileCap = ResolveTileCap(fullWorld);
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
            public Action OnComplete;
        }

        enum SeedHeightsStep
        {
            Prepare,
            UploadRows,
            Done,
        }

        static int SeedRowChunk(int res) =>
            res >= 1025 ? 8 : res >= 513 ? 32 : 32;

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

            var offsets = NeighborOffsets(score, fullWorld, request.SurfaceTileLayoutVariant, request.Seed);
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
                TileCap = ResolveTileCap(fullWorld),
                OnComplete = onComplete,
            };

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Neighbor tiles queued — up to {session.TileCap} slot(s), one tile + paced seam seed per slot.",
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

            var createdThisFrame = false;
            while (session.OffsetIndex < session.Offsets.Length &&
                   session.Count < session.TileCap &&
                   !createdThisFrame)
            {
                var off = session.Offsets[session.OffsetIndex++];
                if (TryFindTileAtOffset(session.TilesRoot, off, out _))
                    continue;

                var pos = session.MainOrigin + new Vector3(
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

                session.WaitingForSeed = true;
                createdThisFrame = true;
                var tileNum = session.Count + 1;
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Created {go.name} — seeding seams ({tileNum}/{session.TileCap})…",
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
                    () =>
                    {
                        session.WaitingForSeed = false;
                        session.Added.Add(tile);
                        session.Count++;
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] {go.name} seam seed done ({session.Count}/{session.TileCap}).",
                            forceUnityConsole: true);
                        CaveBuildActionPacing.ScheduleNextEditorFrame(
                            () => RunAttachTilesFrame(session));
                    });
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
            {
                RefreshTerrainConnectivity(session.MainTerrain, session.Added);
                QueueStitchNeighborSeamsOnly(session.MainTerrain, null);
            }

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

        static void EnsureMainTerrainIdentity(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return;

            if (!mainTerrain.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal) &&
                mainTerrain.name != MainTerrainName)
            {
                mainTerrain.name = MainTerrainName;
                CaveEditorUndo.RecordObject(mainTerrain.gameObject, "Name main surface terrain");
            }

            TryTagAsGround(mainTerrain.gameObject);
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

        static bool TryFindTileAtOffset(Transform tilesRoot, Vector2Int off, out Terrain tile)
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
        public static int StitchAllNeighborSeamsSync(Terrain mainTerrain)
        {
            if (mainTerrain == null)
                return 0;

            var tiles = CollectGameplayTiles(mainTerrain);
            if (tiles.Length == 0)
                return 0;

            var group = tiles;
            var stitched = 0;
            foreach (var tile in tiles)
            {
                if (tile == null || !TryParseTileOffset(tile.name, out var off))
                    continue;

                StitchTileSeams(tile, mainTerrain, off, group);
                tile.Flush();
                stitched++;
            }

            var all = new List<Terrain>(tiles.Length + 1) { mainTerrain };
            foreach (var t in tiles)
                all.Add(t);
            RefreshTerrainConnectivity(mainTerrain, all);
            return stitched;
        }

        public static void QueueStitchNeighborSeamsOnly(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    var count = StitchAllNeighborSeamsSync(mainTerrain);
                    if (count > 0)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Neighbor seam stitch only ({count} tile(s)) — no per-tile DEM/smooth.",
                            forceUnityConsole: true);
                    }

                    EnvironmentKitHardwareBudget.OnQueueStepCompleted();
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel("neighbor seam stitch"));
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

            var tiles = CollectGameplayTiles(mainTerrain);
            if (tiles.Length == 0)
                return 0;

            if (SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild)
                return StitchAllNeighborSeamsSync(mainTerrain);

            var playCenter = SurfaceTerrainPlayRegion.ResolveUnifiedPlayCenter(ground, mainTerrain);
            var extent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                mainTerrain,
                playCenter,
                Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f));
            var stamped = 0;

            foreach (var tile in tiles)
            {
                if (tile == null || !TryParseTileOffset(tile.name, out var off))
                    continue;

                if (SurfaceDemGeoreferenceAuthor.ApplyGeoreferencedStamp(
                        tile,
                        playCenter,
                        extent,
                        request.Seed,
                        out _))
                    stamped++;

                StitchTileSeams(tile, mainTerrain, off, CollectGameplayTiles(mainTerrain));
                SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                    tile,
                    playCenter,
                    extent,
                    strength: 0.26f);
                SurfaceTerrainRefinement.SmoothTerrainFootprintUniform(tile, 0.1f);
                tile.Flush();
            }

            var all = new List<Terrain>(tiles.Length + 1) { mainTerrain };
            foreach (var t in tiles)
                all.Add(t);
            RefreshTerrainConnectivity(mainTerrain, all);
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

            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    EditorUtility.DisplayProgressBar(
                        "Environment Kit",
                        "[Surface] Unified Florida DEM + seams on all neighbor tiles…",
                        0.88f);

                    var stamped = ApplyUnifiedSurfaceWorldPolishSync(mainTerrain, ground, request);
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Unified neighbor world — Florida DEM {stamped}/{tiles.Length} tile(s), seams + footprint smooth.",
                        forceUnityConsole: true);
                    EditorUtility.ClearProgressBar();
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel("unified neighbor Florida DEM"));
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
                var all = new List<Terrain>(tiles.Count + 1) { mainTerrain };
                for (var i = 0; i < tiles.Count; i++)
                    all.Add(tiles[i].Tile);
                RefreshTerrainConnectivity(mainTerrain, all);
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Neighbor LiDAR complete ({tiles.Count} tile(s)).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var entry = tiles[index];
            if (entry.Tile == null)
            {
                QueueLidarTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete);
                return;
            }

            var tileNum = index + 1;
            CaveBuildActionPacing.ScheduleHeavyChain(
                () =>
                {
                    ApplyTileLidarAndStitch(mainTerrain, ground, request, entry);
                    if (entry.Tile != null)
                        entry.Tile.Flush();
                    QueueLidarTileAtIndex(mainTerrain, ground, request, tiles, index + 1, onComplete);
                },
                CaveBuildPipelineDomains.SurfaceQueueLabel(
                    $"neighbor tile {entry.Tile.name} LiDAR {tileNum}/{tiles.Count}"));
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
                request.SurfaceIncludeMountains,
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
            GameplayTileEntry entry)
        {
            if (entry.Tile == null)
                return;

            var tileCenter = ground != null && ground.HasAnchor
                ? ground.Anchor.position
                : TileCenterWorld(mainTerrain);

            var extent = Mathf.Clamp(request.SurfaceExtentMeters, 80f, 512f);
            // Main tile already has paced DEM — edge-seeded neighbors only need seam stitch (avoids N× full stamps).
            var group = CollectGameplayTiles(mainTerrain);
            StitchTileSeams(entry.Tile, mainTerrain, entry.Offset, group);
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
                request.SurfaceIncludeMountains,
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
                td.terrainLayers = layers;
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
                if (child != null &&
                    child.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
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
            Undo.RecordObject(tile.terrainData, "Seed neighbor tile from seams");
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
            ScheduleSeedStep(session, SeedHeightsStep.Prepare);
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
                case SeedHeightsStep.Prepare:
                    session.Main?.terrainData?.SyncHeightmap();
                    session.Heights = BuildSeedHeightsArray(
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
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                    {
                        session.Tile?.Flush();
                        session.OnComplete?.Invoke();
                    });
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

            session.Tile.terrainData.SetHeights(0, yStart, slice);
        }

        static float[,] BuildSeedHeightsArray(
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

            CopyTouchingEdge(main, heights, off, band);

            var neighbors = CollectGameplayTiles(connectivityMain);
            foreach (var other in neighbors)
            {
                if (other == null || other == tile || !TryParseTileOffset(other.name, out var otherOff))
                    continue;

                var delta = otherOff - off;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                    continue;

                CopyTouchingEdge(other, heights, delta, band);
            }

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
            var heights = BuildSeedHeightsArray(tile, main, off, connectivityMain, band, res);
            Undo.RecordObject(tile.terrainData, "Seed neighbor tile from seams");
            tile.terrainData.SetHeights(0, 0, heights);
            tile.Flush();
        }

        static void CopyTouchingEdge(Terrain source, float[,] dest, Vector2Int destOffsetFromSource, int band)
        {
            if (source?.terrainData == null)
                return;

            var res = dest.GetLength(0);
            band = Mathf.Clamp(band, 1, res / 2);
            var data = source.terrainData;

            if (destOffsetFromSource.x == 1 && destOffsetFromSource.y == 0)
            {
                var srcBand = data.GetHeights(res - band, 0, band, res);
                for (var z = 0; z < res; z++)
                for (var b = 0; b < band; b++)
                    dest[z, b] = srcBand[z, band - 1 - b];
            }
            else if (destOffsetFromSource.x == -1 && destOffsetFromSource.y == 0)
            {
                var srcBand = data.GetHeights(0, 0, band, res);
                for (var z = 0; z < res; z++)
                for (var b = 0; b < band; b++)
                    dest[z, res - 1 - b] = srcBand[z, b];
            }
            else if (destOffsetFromSource.x == 0 && destOffsetFromSource.y == 1)
            {
                var srcBand = data.GetHeights(0, res - band, res, band);
                for (var x = 0; x < res; x++)
                for (var b = 0; b < band; b++)
                    dest[b, x] = srcBand[band - 1 - b, x];
            }
            else if (destOffsetFromSource.x == 0 && destOffsetFromSource.y == -1)
            {
                var srcBand = data.GetHeights(0, 0, res, band);
                for (var x = 0; x < res; x++)
                for (var b = 0; b < band; b++)
                    dest[res - 1 - b, x] = srcBand[b, x];
            }
        }

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
            if (tile?.terrainData == null)
                return;

            var seam = Mathf.Max(2, tile.terrainData.heightmapResolution / 40);
            if (main != null)
                BlendSharedEdge(tile, main, off, seam);

            if (group == null)
                return;

            foreach (var other in group)
            {
                if (other == null || other == tile || !TryParseTileOffset(other.name, out var otherOff))
                    continue;

                var delta = otherOff - off;
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                    continue;

                BlendSharedEdge(tile, other, delta, seam);
            }
        }

        static void BlendSharedEdge(Terrain tile, Terrain neighbor, Vector2Int tileOffsetFromNeighbor, int seam)
        {
            if (tile?.terrainData == null || neighbor?.terrainData == null)
                return;

            var res = tile.terrainData.heightmapResolution;
            if (neighbor.terrainData.heightmapResolution != res)
                return;

            seam = Mathf.Clamp(seam, 1, res / 8);
            var changed = false;

            if (tileOffsetFromNeighbor.x == 1 && tileOffsetFromNeighbor.y == 0)
            {
                var tileBand = tile.terrainData.GetHeights(0, 0, seam, res);
                var neighborBand = neighbor.terrainData.GetHeights(res - seam, 0, seam, res);
                for (var z = 0; z < res; z++)
                for (var b = 0; b < seam; b++)
                {
                    tileBand[z, b] = Mathf.Lerp(tileBand[z, b], neighborBand[z, seam - 1 - b], 0.5f);
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(tile.terrainData, "Stitch neighbor terrain seam");
                    tile.terrainData.SetHeights(0, 0, tileBand);
                }

                return;
            }

            if (tileOffsetFromNeighbor.x == -1 && tileOffsetFromNeighbor.y == 0)
            {
                var tileBand = tile.terrainData.GetHeights(res - seam, 0, seam, res);
                var neighborBand = neighbor.terrainData.GetHeights(0, 0, seam, res);
                for (var z = 0; z < res; z++)
                for (var b = 0; b < seam; b++)
                {
                    tileBand[z, b] = Mathf.Lerp(tileBand[z, b], neighborBand[z, b], 0.5f);
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(tile.terrainData, "Stitch neighbor terrain seam");
                    tile.terrainData.SetHeights(res - seam, 0, tileBand);
                }

                return;
            }

            if (tileOffsetFromNeighbor.x == 0 && tileOffsetFromNeighbor.y == 1)
            {
                var tileBand = tile.terrainData.GetHeights(0, 0, res, seam);
                var neighborBand = neighbor.terrainData.GetHeights(0, res - seam, res, seam);
                for (var x = 0; x < res; x++)
                for (var b = 0; b < seam; b++)
                {
                    tileBand[b, x] = Mathf.Lerp(tileBand[b, x], neighborBand[seam - 1 - b, x], 0.5f);
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(tile.terrainData, "Stitch neighbor terrain seam");
                    tile.terrainData.SetHeights(0, 0, tileBand);
                }

                return;
            }

            if (tileOffsetFromNeighbor.x == 0 && tileOffsetFromNeighbor.y == -1)
            {
                var tileBand = tile.terrainData.GetHeights(0, res - seam, res, seam);
                var neighborBand = neighbor.terrainData.GetHeights(0, 0, res, seam);
                for (var x = 0; x < res; x++)
                for (var b = 0; b < seam; b++)
                {
                    tileBand[b, x] = Mathf.Lerp(tileBand[b, x], neighborBand[b, x], 0.5f);
                    changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(tile.terrainData, "Stitch neighbor terrain seam");
                    tile.terrainData.SetHeights(0, res - seam, tileBand);
                }
            }
        }

        static int TileSculptSeed(int baseSeed, Vector2Int off) =>
            baseSeed + off.x * 10_007 + off.y * 79_919;

        static int TileDemSeed(int baseSeed, Vector2Int off) =>
            baseSeed + off.x * 131 + off.y * 719;

        static bool TryParseTileOffset(string name, out Vector2Int off)
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

        static int ResolveTileCap(bool fullWorld)
        {
            var budgetCap = EnvironmentKitHardwareBudget.Active.MaxExtraTerrainTiles;
            return Mathf.Min(MaxExtraTiles, budgetCap);
        }

        static Vector2Int[] NeighborOffsets(float score, bool fullWorld, int layoutVariant, int seed)
        {
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
                terrain.SetNeighbors(left, top, right, bottom);
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

                CaveEditorUndo.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>
        /// Non–FullWorld only: block when another terrain's interior overlaps this slot (edge touch with main is OK).
        /// </summary>
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
    }
}
#endif
