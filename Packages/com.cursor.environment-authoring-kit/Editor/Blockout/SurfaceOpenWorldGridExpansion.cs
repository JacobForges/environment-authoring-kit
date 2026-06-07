#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Incremental Chebyshev ring expansion toward ~289 tiles (radius 8 → 17×17 = 289).
    /// Grid math: tile count = (2r+1)² — r=8 gives 17×17=289 (user target ~284).
    /// Only places/welds the next ring; prior rings stay untouched.
    /// </summary>
    public static class SurfaceOpenWorldGridExpansion
    {
        public const int TargetTileCount = 289;
        public const int MaxChebyshevRadius = 8;
        public const string ManifestRel = CaveBuildAgentContextExporter.Folder + "/OpenWorldGridManifest.json";

        [Serializable]
        public class Manifest
        {
            public string generatedUtc;
            public int targetTileCount = TargetTileCount;
            public int targetChebyshevRadius = MaxChebyshevRadius;
            public int builtChebyshevRadius = SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
            public int placedTileCount;
            public string note =
                "FullWorld default (UseExtendedOpenWorldGrid) places all rings 0–8 (~289 tiles, 17×17). " +
                "Incremental menu expansion adds one ring at a time; rings 0–4 are the 81-tile mountain core.";
        }

        public static int ChebyshevRadiusForTileCount(int tileCount)
        {
            if (tileCount <= 1)
                return 0;

            for (var r = 0; r <= MaxChebyshevRadius; r++)
            {
                var side = 2 * r + 1;
                if (side * side >= tileCount)
                    return r;
            }

            return MaxChebyshevRadius;
        }

        public static int GetBuiltChebyshevRadius() => LoadManifest().builtChebyshevRadius;

        public static int GetTargetChebyshevRadius() =>
            Mathf.Min(MaxChebyshevRadius, ChebyshevRadiusForTileCount(TargetTileCount));

        public static Manifest LoadManifest()
        {
            var path = ManifestRel;
            if (!File.Exists(path))
            {
                return new Manifest
                {
                    builtChebyshevRadius = SurfaceTerrainTileExpansion.FullWorldChebyshevRadius,
                    targetChebyshevRadius = GetTargetChebyshevRadius(),
                    targetTileCount = TargetTileCount,
                    placedTileCount = SurfaceTerrainTileExpansion.FullWorldTerrainTileCount,
                };
            }

            try
            {
                var json = File.ReadAllText(path);
                var manifest = JsonUtility.FromJson<Manifest>(json);
                if (manifest == null)
                    return new Manifest();
                manifest.targetChebyshevRadius = Mathf.Min(
                    MaxChebyshevRadius,
                    manifest.targetChebyshevRadius > 0
                        ? manifest.targetChebyshevRadius
                        : GetTargetChebyshevRadius());
                manifest.builtChebyshevRadius = Mathf.Clamp(
                    manifest.builtChebyshevRadius,
                    0,
                    manifest.targetChebyshevRadius);
                return manifest;
            }
            catch
            {
                return new Manifest();
            }
        }

        public static void WriteManifest(Manifest manifest)
        {
            if (manifest == null)
                return;

            manifest.generatedUtc = DateTime.UtcNow.ToString("o");
            manifest.placedTileCount = CountPlacedTiles(manifest.builtChebyshevRadius);
            var dir = Path.GetDirectoryName(ManifestRel);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ManifestRel, JsonUtility.ToJson(manifest, true));
            AssetDatabase.Refresh();
        }

        static int CountPlacedTiles(int builtRadius)
        {
            var count = 0;
            for (var r = 0; r <= builtRadius; r++)
                count += SurfaceTerrainTileExpansion.BuildChebyshevRingOffsets(r, builtRadius).Length;
            return count;
        }

        public static Vector2Int[] BuildIncrementalRingOffsets(int ringChebyshev) =>
            SurfaceTerrainTileExpansion.BuildChebyshevRingOffsets(ringChebyshev, MaxChebyshevRadius);

        /// <summary>FullWorld default slot count (~289) — center + play disk, then rings 2–8.</summary>
        public static int ExtendedBuildTileSlotCount => BuildAaaExtendedPlaceOrder().Length;

        /// <summary>Center + play disk (9 tiles), then rings 2–8 flat (~289 tiles) for FullWorld / Full AAA Rebuild.</summary>
        public static Vector2Int[] BuildAaaExtendedPlaceOrder()
        {
            var list = new List<Vector2Int> { Vector2Int.zero };
            list.AddRange(SurfaceTerrainTileExpansion.BuildChebyshevRingOffsets(1, MaxChebyshevRadius));
            for (var ring = 2; ring <= MaxChebyshevRadius; ring++)
                list.AddRange(SurfaceTerrainTileExpansion.BuildChebyshevRingOffsets(ring, MaxChebyshevRadius));

            return list.ToArray();
        }

        /// <summary>Queue place (flat 0.42) + boundary weld for the next Chebyshev ring only.</summary>
        public static void QueueExpandOpenWorldNextRing(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var manifest = LoadManifest();
            var targetRadius = manifest.targetChebyshevRadius > 0
                ? manifest.targetChebyshevRadius
                : GetTargetChebyshevRadius();
            if (manifest.builtChebyshevRadius >= targetRadius)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Open world — already at ring {manifest.builtChebyshevRadius} " +
                    $"(target {targetRadius}, ~{manifest.placedTileCount} tiles).",
                    forceUnityConsole: true);
                onComplete?.Invoke();
                return;
            }

            var nextRing = manifest.builtChebyshevRadius + 1;
            var ringOffsets = BuildIncrementalRingOffsets(nextRing);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Open world — expanding ring {nextRing}/{targetRadius} ({ringOffsets.Length} new tile(s), " +
                $"target ~{TargetTileCount} tiles).",
                forceUnityConsole: true);

            SurfaceTerrainTileExpansion.SnapFullWorldTerrainGridNow(mainTerrain);

            var session = SurfaceTerrainTileExpansion.CreateMinimalFullWorldGridSession(mainTerrain);
            if (session == null)
            {
                onComplete?.Invoke();
                return;
            }

            SurfaceTerrainTileExpansion.EnsureFullWorldGridAnchorTransform(session);
            var index = 0;

            void PlaceNext()
            {
                if (index >= ringOffsets.Length)
                {
                    CaveBuildActionPacing.ScheduleHeavy(
                        () =>
                        {
                            SurfaceTerrainTileExpansion.WeldChebyshevRingBoundary(
                                session,
                                nextRing,
                                MaxChebyshevRadius);
                            manifest.builtChebyshevRadius = nextRing;
                            manifest.targetChebyshevRadius = targetRadius;
                            WriteManifest(manifest);
                            SurfaceTerrainGridRegistry.ReindexFromMain(mainTerrain, playDiskLocked: false);
                            CaveBuildEditorLog.LogSurface(
                                $"[Surface] Open world ring {nextRing} complete — {manifest.placedTileCount} tile(s) in grid.",
                                forceUnityConsole: true);
                            onComplete?.Invoke();
                        },
                        CaveBuildPipelineDomains.QueueLabel($"open world ring {nextRing} — weld"));
                    return;
                }

                var off = ringOffsets[index++];
                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        if (!SurfaceTerrainTileExpansion.TryEnsureFlatGridTileAtOffset(session, off, out var tile) ||
                            tile == null)
                        {
                            PlaceNext();
                            return;
                        }

                        SurfaceTerrainTileExpansion.ApplyFullWorldGridSlot(session, tile, off);
                        SurfaceTerrainTileExpansion.QueueApplyUniformFlatHeightmap(
                            tile,
                            () =>
                            {
                                tile.Flush();
                                PlaceNext();
                            });
                    },
                    CaveBuildPipelineDomains.QueueLabel(
                        $"open world ring {nextRing} — place {index}/{ringOffsets.Length}"));
            }

            PlaceNext();
        }
    }
}
#endif
