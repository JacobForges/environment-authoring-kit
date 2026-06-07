#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Detects flat default heightmaps, quilted ring stamps, and other unusable land before props / cave meat.
    /// </summary>
    public static class SurfaceDefaultTerrainDetector
    {
        public const string StatusRel = CaveBuildAgentContextExporter.Folder + "/SurfaceDefaultTerrainStatus.json";

        [Serializable]
        public class Status
        {
            public bool unusableLand;
            public int score;
            public int tilesSampled;
            public int flatPlateauTiles;
            public int lowVarianceTiles;
            public int expectedTiles;
            public int placedTiles;
            public string gradingTask;
            public string[] issues;
            public string note;
        }

        const float FlatNormSpanThreshold = 0.018f;
        const float LowVarianceNormThreshold = 0.035f;

        public static Status Evaluate(SceneGroundInfo ground, WorldGenerationRequest request)
        {
            var status = new Status
            {
                gradingTask = CaveBuildGradingProfile.ResolveGradingTask(request),
                expectedTiles = CaveBuildGradingProfile.ExpectedSurfaceTileCount(request),
                score = 100,
            };

            if (ground?.Terrain == null)
            {
                status.score = 35;
                status.unusableLand = true;
                status.issues = new[] { "No terrain on Ground — cannot ship default procedural land." };
                return status;
            }

            var terrains = SurfaceTerrainPlayRegion.CollectSurfaceTerrains(ground.Terrain);
            status.placedTiles = terrains.Count;

            var minRequired = CaveBuildGradingProfile.MinRequiredSurfaceTiles(request);
            if (terrains.Count < minRequired)
            {
                status.score = Mathf.Max(30, 40 + terrains.Count);
                status.unusableLand = true;
                status.issues = new[]
                {
                    $"Only {terrains.Count}/{status.expectedTiles} surface tiles (need ≥{minRequired}).",
                };
                Write(status);
                return status;
            }

            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = SurfaceTerrainPlayRegion.ResolveRepairExtentMeters(ground.Terrain, center, request);
            var issues = new List<string>();

            foreach (var terrain in terrains)
            {
                if (terrain?.terrainData == null)
                    continue;

                if (!SamplePlayBandVariance(terrain, center, extent, out var minH, out var maxH, out var samples))
                    continue;

                status.tilesSampled++;
                var span = maxH - minH;
                if (span < FlatNormSpanThreshold)
                {
                    status.flatPlateauTiles++;
                    if (issues.Count < 6)
                        issues.Add($"Flat default plateau on `{terrain.name}` (norm span {span:F4}).");
                }
                else if (span < LowVarianceNormThreshold)
                {
                    status.lowVarianceTiles++;
                    if (issues.Count < 6)
                        issues.Add($"Low relief on `{terrain.name}` (norm span {span:F4}) — likely unplayable.");
                }
            }

            if (status.flatPlateauTiles > 0)
                status.score = Mathf.Min(status.score, 55);
            if (status.lowVarianceTiles >= Mathf.Max(2, status.tilesSampled / 4))
                status.score = Mathf.Min(status.score, 62);
            if (status.flatPlateauTiles >= 2)
                status.score = Mathf.Min(status.score, 48);

            status.unusableLand = status.score < SurfaceTerrainBuildLadder.StagePassScore;
            status.issues = issues.Count > 0 ? issues.ToArray() : Array.Empty<string>();
            status.note = status.unusableLand
                ? "Geo meat loop must fix heightfield/slopes/LiDAR sculpt — no default flat quilt."
                : "Surface height variance acceptable for props meat loop.";
            Write(status);
            return status;
        }

        static bool SamplePlayBandVariance(
            Terrain terrain,
            Vector3 center,
            float extentMeters,
            out float minNorm,
            out float maxNorm,
            out int samples)
        {
            minNorm = 1f;
            maxNorm = 0f;
            samples = 0;
            var td = terrain.terrainData;
            if (td == null)
                return false;

            var res = td.heightmapResolution;
            var size = td.size;
            var origin = terrain.transform.position;
            var half = extentMeters * 0.5f;

            for (var z = 0; z < res; z += Mathf.Max(1, res / 24))
            {
                for (var x = 0; x < res; x += Mathf.Max(1, res / 24))
                {
                    var wx = origin.x + (x / (float)(res - 1)) * size.x;
                    var wz = origin.z + (z / (float)(res - 1)) * size.z;
                    if (Mathf.Abs(wx - center.x) > half || Mathf.Abs(wz - center.z) > half)
                        continue;

                    var h = td.GetHeight(x, z) / Mathf.Max(size.y, 0.01f);
                    minNorm = Mathf.Min(minNorm, h);
                    maxNorm = Mathf.Max(maxNorm, h);
                    samples++;
                }
            }

            return samples >= 16;
        }

        public static void Write(Status status)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, StatusRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, JsonUtility.ToJson(status, true));
        }

        /// <summary>Queue crater repair + LiDAR touch on play band when land reads as default flat.</summary>
        public static void QueueRemediateDefaultLand(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action<bool, string> onComplete)
        {
            if (ground?.Terrain == null)
            {
                onComplete?.Invoke(false, "no terrain");
                return;
            }

            var center = ground.HasAnchor
                ? ground.Anchor.position
                : new Vector3(ground.Bounds.center.x, ground.SurfaceY, ground.Bounds.center.z);
            var extent = SurfaceTerrainPlayRegion.ResolveRepairExtentMeters(ground.Terrain, center, request);

            SurfaceTerrainLadderFixer.QueueFixCraters(
                ground,
                center,
                extent,
                (_, craterMsg) =>
                {
                    if (CaveBuildWorkflowCoordinator.TryConsumeMeatSurfaceTerrainPass())
                    {
                        var seed = request?.Seed ?? 0;
                        var ok = SurfaceTerrainRefinement.TryLidarRefineAndSmooth(
                            ground.Terrain,
                            center,
                            extent,
                            seed + 31,
                            out var lidarMsg);
                        onComplete?.Invoke(ok, craterMsg + " " + lidarMsg);
                        return;
                    }

                    onComplete?.Invoke(true, craterMsg);
                });
        }
    }
}
#endif
