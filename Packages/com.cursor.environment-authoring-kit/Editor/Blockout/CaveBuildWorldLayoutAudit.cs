#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Layout reports: terrain seams, prop floaters, cave overlap, mouth delta, milestone gates.
    /// </summary>
    public static class CaveBuildWorldLayoutAudit
    {
        public const string ReportRel = CaveBuildAgentContextExporter.Folder + "/WorldLayoutAudit.json";

        public const string MilestonePlayDisk = "layout_audit_play_disk";
        public const string MilestoneFoothillRing = "layout_audit_foothill_ring";
        public const string MilestoneWilderness = "layout_audit_wilderness";

        public const float SeamFailMeters = 0.35f;
        public const float SeamWarnMeters = 0.15f;
        public const float PropFloaterFailMeters = 1.0f;
        public const float PropFloaterWarnMeters = 0.35f;
        public const float CaveOverlapFailMeters = 2.0f;
        public const float CaveOverlapWarnMeters = 0.85f;
        public const float MouthDeltaFailMeters = 1.5f;
        public const float MouthDeltaWarnMeters = 0.5f;
        public const float PositionWarnMeters = 1.5f;
        public const float PositionFailMeters = 3f;

        public static AuditReport LastReport { get; private set; }

        [Serializable]
        public class SeamEdgeRecord
        {
            public string tileA;
            public string tileB;
            public float gapMeters;
        }

        [Serializable]
        public class AuditReport
        {
            public string generatedUtc;
            public string scene;
            public string auditMilestone;
            public int seed;
            public int terrainTileCount;
            public float maxSeamGapMeters;
            public int seamEdgeCount;
            public int propFloaterCount;
            public int propSampleCount;
            public float caveOverlapMeters;
            public float mouthDeltaYMeters;
            public bool vegetationContractPass;
            public bool layoutAcceptable;
            public bool playDiskGridValid;
            public bool blocksCaveQueue;
            public bool blocksSurfaceContinue;
            public bool blocksMountainRing;
            public float playToWildernessMaxSeamGapMeters;
            public int playToWildernessSeamEdgeCount;
            public float wildernessMaxPositionMismatchMeters;
            public int playToWildernessMissingAdjacencyCount;
            public string terrainFingerprint;
            public string suggestedFix = CaveBuildLayoutAuditMilestones.FixNone;
            public string[] failingTileIds = Array.Empty<string>();
            public SeamEdgeRecord[] failingSeamEdges = Array.Empty<SeamEdgeRecord>();
            public string terrainGridManifestRel = SurfaceTerrainGridRegistry.ManifestRel;
            public string[] issues = Array.Empty<string>();
        }

        public static AuditReport Run(SceneGroundInfo ground, WorldGenerationRequest request = null) =>
            Run(ground, request, null, null);

        public static AuditReport Run(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            string milestone,
            string propsCategory)
        {
            var report = new AuditReport
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                auditMilestone = milestone ?? "full",
                seed = request?.Seed ?? 0,
                terrainTileCount = 0,
            };

            var issues = new List<string>();
            if (!ground.HasAnchor)
            {
                issues.Add("No Ground anchor — run Cave Build → Prepare Project For First Build.");
                report.issues = issues.ToArray();
                report.layoutAcceptable = false;
                report.blocksCaveQueue = true;
                report.blocksSurfaceContinue = true;
                report.blocksMountainRing = true;
                report.suggestedFix = CaveBuildLayoutAuditMilestones.FixNone;
                FinalizeReport(report, ground, request, issues);
                return report;
            }

            var mainTerrain = ground.Terrain ?? UnityEngine.Object.FindAnyObjectByType<Terrain>();
            var playTerrains = mainTerrain != null
                ? SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain)
                : new List<Terrain>();

            report.terrainTileCount = playTerrains.Count;
            report.terrainFingerprint = mainTerrain != null
                ? CaveBuildTerrainFingerprint.ComputePlayDiskFingerprint(mainTerrain)
                : "none";

            if (mainTerrain != null)
            {
                report.playDiskGridValid = SurfaceTerrainTileExpansion.ValidatePlayDiskGridLayout(
                    mainTerrain,
                    out var gridMsg);
                if (!report.playDiskGridValid && !string.IsNullOrEmpty(gridMsg))
                    issues.Add("Play disk grid: " + gridMsg);
            }
            else
                report.playDiskGridValid = false;

            var auditTerrains = BuildTerrainSetForMilestone(mainTerrain, playTerrains, request, milestone);
            var failingEdges = new List<SeamEdgeRecord>();
            MeasureSeamsDetailed(auditTerrains, failingEdges, out report.maxSeamGapMeters, out report.seamEdgeCount, issues);
            report.failingSeamEdges = failingEdges
                .Where(e => e.gapMeters > SeamWarnMeters)
                .ToArray();

            if (mainTerrain != null && request != null && request.UseOuterRingMountains &&
                milestone != MilestonePlayDisk)
            {
                var playPlusWild = new List<Terrain>(playTerrains);
                playPlusWild.AddRange(SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain));
                if (milestone == MilestoneWilderness)
                    playPlusWild.AddRange(SurfaceTerrainTileExpansion.CollectMountainPeakTiles(mainTerrain));

                MeasureSeamsDetailed(
                    playPlusWild,
                    failingEdges,
                    out report.playToWildernessMaxSeamGapMeters,
                    out report.playToWildernessSeamEdgeCount,
                    issues);
                MeasureWildernessGridAlignment(
                    mainTerrain,
                    playTerrains,
                    issues,
                    out report.wildernessMaxPositionMismatchMeters,
                    out report.playToWildernessMissingAdjacencyCount);
                if (report.playToWildernessMaxSeamGapMeters > SeamWarnMeters)
                {
                    issues.Add(
                        $"Play↔wilderness seam {report.playToWildernessMaxSeamGapMeters:F2}m (warn>{SeamWarnMeters}m).");
                }
            }

            if (milestone == null || milestone.StartsWith(CaveBuildLayoutAuditMilestones.MilestonePropsPrefix, StringComparison.Ordinal))
                MeasurePropFloaters(playTerrains, out report.propFloaterCount, out report.propSampleCount, issues);
            else if (milestone != MilestonePlayDisk && milestone != MilestoneFoothillRing)
                MeasurePropFloaters(playTerrains, out report.propFloaterCount, out report.propSampleCount, issues);

            if (milestone == null || milestone == MilestoneWilderness)
            {
                var caveRoot = FindCaveRoot();
                if (caveRoot != null)
                {
                    var openings = new List<Vector3>();
                    var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
                    if (mouth.sqrMagnitude > 0.01f)
                        openings.Add(mouth);
                    report.caveOverlapMeters = CaveGroundPlacementUtility.MeasureMaxCaveProtrusionAboveHeightmap(
                        caveRoot, ground, openings, out _);
                    if (report.caveOverlapMeters > CaveOverlapWarnMeters)
                    {
                        issues.Add(
                            $"Cave mesh protrusion {report.caveOverlapMeters:F2}m above heightmap (warn>{CaveOverlapWarnMeters}m).");
                    }

                    report.mouthDeltaYMeters = Mathf.Abs(
                        CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(caveRoot, ground));
                    if (report.mouthDeltaYMeters > MouthDeltaWarnMeters)
                    {
                        issues.Add($"Mouth surface error {report.mouthDeltaYMeters:F2}m (warn>{MouthDeltaWarnMeters}m).");
                    }
                }
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var manifestPath = Path.Combine(hub, SurfaceTerrainGridRegistry.ManifestRel);
            if (!File.Exists(manifestPath) && request?.SurfaceScope != SurfaceBuildScope.CaveOnly &&
                milestone != null && milestone.StartsWith("layout_audit_props", StringComparison.Ordinal) == false)
            {
                issues.Add($"Missing {SurfaceTerrainGridRegistry.ManifestRel} — run surface tile expansion.");
            }

            var surfaceRoot = GameObject.Find("GeneratedSurfaceWorld");
            var veg = surfaceRoot != null ? surfaceRoot.transform.Find("Vegetation") : null;
            report.vegetationContractPass =
                mainTerrain != null &&
                veg != null &&
                SurfaceTerrainPropPlacementRegion.IsNineTileVegetationSufficient(veg, mainTerrain);

            if (!report.vegetationContractPass && request?.SurfaceScope != SurfaceBuildScope.CaveOnly &&
                (milestone == null || milestone.StartsWith(CaveBuildLayoutAuditMilestones.MilestonePropsPrefix, StringComparison.Ordinal)))
            {
                issues.Add("Nine-tile vegetation contract not met (≥42 instances per tile).");
            }

            report.failingTileIds = CollectFailingTileIds(report.failingSeamEdges);
            report.layoutAcceptable = EvaluateAcceptable(report, milestone);
            report.blocksSurfaceContinue =
                report.maxSeamGapMeters > SeamFailMeters ||
                (milestone == MilestonePlayDisk && !report.playDiskGridValid);
            report.blocksMountainRing =
                report.playToWildernessMaxSeamGapMeters > SeamFailMeters ||
                report.wildernessMaxPositionMismatchMeters > PositionFailMeters ||
                report.playToWildernessMissingAdjacencyCount > 0 ||
                (milestone == MilestoneFoothillRing && report.playToWildernessMaxSeamGapMeters > SeamWarnMeters);
            report.blocksCaveQueue =
                report.maxSeamGapMeters > SeamFailMeters ||
                report.playToWildernessMaxSeamGapMeters > SeamFailMeters ||
                report.propFloaterCount > 12 ||
                report.caveOverlapMeters > CaveOverlapFailMeters ||
                report.mouthDeltaYMeters > MouthDeltaFailMeters;

            report.suggestedFix = InferSuggestedFix(report, milestone, propsCategory);
            FinalizeReport(report, ground, request, issues);
            return report;
        }

        public static bool TryResolveGridOffset(Terrain mainTerrain, Terrain tile, out Vector2Int off) =>
            TryResolveSurfaceTileGridOffset(mainTerrain, tile, out off);

        static bool TryResolveSurfaceTileGridOffset(Terrain mainTerrain, Terrain tile, out Vector2Int off)
        {
            off = default;
            if (tile == null)
                return false;

            if (SurfaceTerrainTileExpansion.TryParseMountainWildernessOffset(tile.name, out off))
                return true;
            if (SurfaceTerrainTileExpansion.TryParsePlayDiskGridOffset(mainTerrain, tile, out off))
                return true;
            return SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out off);
        }

        static List<Terrain> BuildTerrainSetForMilestone(
            Terrain mainTerrain,
            List<Terrain> playTerrains,
            WorldGenerationRequest request,
            string milestone)
        {
            if (mainTerrain == null)
                return playTerrains;

            switch (milestone)
            {
                case MilestoneFoothillRing:
                {
                    var list = new List<Terrain>(playTerrains);
                    list.AddRange(SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain));
                    return list;
                }
                case MilestoneWilderness:
                {
                    var list = new List<Terrain>(playTerrains);
                    list.AddRange(SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain));
                    return list;
                }
                case MilestonePlayDisk:
                default:
                    return playTerrains;
            }
        }

        static bool EvaluateAcceptable(AuditReport report, string milestone)
        {
            if (milestone != null && milestone.StartsWith(CaveBuildLayoutAuditMilestones.MilestonePropsPrefix, StringComparison.Ordinal))
            {
                return report.propFloaterCount <= 4;
            }

            if (milestone == MilestonePlayDisk)
            {
                return report.maxSeamGapMeters <= SeamWarnMeters && report.playDiskGridValid;
            }

            if (milestone == MilestoneFoothillRing)
            {
                return report.playToWildernessMaxSeamGapMeters <= SeamWarnMeters &&
                       report.maxSeamGapMeters <= SeamWarnMeters &&
                       report.wildernessMaxPositionMismatchMeters <= PositionWarnMeters &&
                       report.playToWildernessMissingAdjacencyCount == 0;
            }

            return report.maxSeamGapMeters <= SeamWarnMeters &&
                   report.propFloaterCount == 0 &&
                   report.caveOverlapMeters <= CaveOverlapWarnMeters &&
                   report.mouthDeltaYMeters <= MouthDeltaWarnMeters &&
                   report.playToWildernessMaxSeamGapMeters <= SeamWarnMeters;
        }

        static string InferSuggestedFix(AuditReport report, string milestone, string propsCategory)
        {
            if (milestone != null && milestone.StartsWith(CaveBuildLayoutAuditMilestones.MilestonePropsPrefix, StringComparison.Ordinal))
            {
                return report.propFloaterCount > 0
                    ? CaveBuildLayoutAuditMilestones.FixPropSnapCategory
                    : CaveBuildLayoutAuditMilestones.FixNone;
            }

            if (report.failingSeamEdges != null && report.failingSeamEdges.Length > 0 &&
                report.maxSeamGapMeters > SeamWarnMeters)
            {
                if (milestone == MilestoneFoothillRing && report.playToWildernessMaxSeamGapMeters > SeamWarnMeters)
                    return CaveBuildLayoutAuditMilestones.FixRestitchPlayPerimeter;

                if (milestone == MilestoneWilderness)
                    return CaveBuildLayoutAuditMilestones.FixRestitchWildernessTiles;

                return CaveBuildLayoutAuditMilestones.FixRestitchEdge;
            }

            if (milestone == MilestoneFoothillRing &&
                (report.wildernessMaxPositionMismatchMeters > PositionWarnMeters ||
                 report.playToWildernessMissingAdjacencyCount > 0))
                return CaveBuildLayoutAuditMilestones.FixEnforceWildernessGrid;

            if (milestone == MilestoneFoothillRing && report.playToWildernessMaxSeamGapMeters > SeamWarnMeters)
                return CaveBuildLayoutAuditMilestones.FixLockFoothillInner;

            return CaveBuildLayoutAuditMilestones.FixNone;
        }

        static string[] CollectFailingTileIds(SeamEdgeRecord[] edges)
        {
            if (edges == null || edges.Length == 0)
                return Array.Empty<string>();

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in edges)
            {
                if (!string.IsNullOrEmpty(e.tileA))
                    set.Add(e.tileA);
                if (!string.IsNullOrEmpty(e.tileB))
                    set.Add(e.tileB);
            }

            return set.ToArray();
        }

        static void FinalizeReport(
            AuditReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            List<string> issues)
        {
            report.issues = issues.ToArray();
            LastReport = report;
            Write(report);
        }

        static Transform FindCaveRoot()
        {
            var env = GameObject.Find("EnvironmentRoot");
            if (env != null)
            {
                var cave = env.transform.Find("UndergroundCaveSystem");
                if (cave != null)
                    return cave;
            }

            return GameObject.Find("UndergroundCaveSystem")?.transform;
        }

        static void MeasureSeamsDetailed(
            IReadOnlyList<Terrain> terrains,
            List<SeamEdgeRecord> failingEdges,
            out float maxGap,
            out int edgeCount,
            List<string> issues)
        {
            maxGap = 0f;
            edgeCount = 0;
            if (terrains == null || terrains.Count < 2)
                return;

            const int samples = 8;
            for (var i = 0; i < terrains.Count; i++)
            {
                var a = terrains[i];
                if (a?.terrainData == null)
                    continue;

                var aPos = a.transform.position;
                var aSize = a.terrainData.size;

                for (var j = i + 1; j < terrains.Count; j++)
                {
                    var b = terrains[j];
                    if (b?.terrainData == null)
                        continue;

                    var bPos = b.transform.position;
                    var bSize = b.terrainData.size;
                    var gap = MeasureSharedEdgeGap(a, aPos, aSize, b, bPos, bSize, samples);
                    if (gap < 0f)
                        continue;

                    edgeCount++;
                    maxGap = Mathf.Max(maxGap, gap);
                    if (gap > SeamWarnMeters)
                    {
                        failingEdges.Add(new SeamEdgeRecord
                        {
                            tileA = a.name,
                            tileB = b.name,
                            gapMeters = gap,
                        });
                        issues.Add($"Terrain seam {a.name} ↔ {b.name}: max height delta {gap:F3}m.");
                    }
                }
            }
        }

        static float MeasureSharedEdgeGap(
            Terrain a,
            Vector3 aPos,
            Vector3 aSize,
            Terrain b,
            Vector3 bPos,
            Vector3 bSize,
            int samples)
        {
            const float eps = 1.5f;
            var maxGap = 0f;
            var found = false;

            bool TryEdge(bool aIsWest, float ax, float azStart, float azEnd)
            {
                for (var s = 0; s <= samples; s++)
                {
                    var t = s / (float)samples;
                    var az = Mathf.Lerp(azStart, azEnd, t);
                    var worldA = new Vector3(ax, 0f, az);
                    var worldB = worldA;
                    if (aIsWest)
                        worldB.x += aSize.x;
                    else
                        worldB.x -= bSize.x;

                    var ha = a.SampleHeight(worldA - aPos) + aPos.y;
                    var hb = b.SampleHeight(worldB - bPos) + bPos.y;
                    maxGap = Mathf.Max(maxGap, Mathf.Abs(ha - hb));
                }

                return true;
            }

            if (Mathf.Abs((aPos.x + aSize.x) - bPos.x) < eps &&
                Mathf.Abs(aPos.z - bPos.z) < eps)
            {
                found = true;
                TryEdge(true, aPos.x + aSize.x, aPos.z, aPos.z + aSize.z);
            }
            else if (Mathf.Abs((bPos.x + bSize.x) - aPos.x) < eps &&
                     Mathf.Abs(aPos.z - bPos.z) < eps)
            {
                found = true;
                TryEdge(false, aPos.x, aPos.z, aPos.z + aSize.z);
            }
            else if (Mathf.Abs((aPos.z + aSize.z) - bPos.z) < eps &&
                     Mathf.Abs(aPos.x - bPos.x) < eps)
            {
                found = true;
                var ax = aPos.x;
                for (var s = 0; s <= samples; s++)
                {
                    var t = s / (float)samples;
                    var axSample = Mathf.Lerp(ax, ax + aSize.x, t);
                    var worldA = new Vector3(axSample, 0f, aPos.z + aSize.z);
                    var worldB = new Vector3(axSample, 0f, bPos.z);
                    var ha = a.SampleHeight(worldA - aPos) + aPos.y;
                    var hb = b.SampleHeight(worldB - bPos) + bPos.y;
                    maxGap = Mathf.Max(maxGap, Mathf.Abs(ha - hb));
                }
            }
            else if (Mathf.Abs((bPos.z + bSize.z) - aPos.z) < eps &&
                     Mathf.Abs(aPos.x - bPos.x) < eps)
            {
                found = true;
                var ax = aPos.x;
                for (var s = 0; s <= samples; s++)
                {
                    var t = s / (float)samples;
                    var axSample = Mathf.Lerp(ax, ax + aSize.x, t);
                    var worldA = new Vector3(axSample, 0f, aPos.z);
                    var worldB = new Vector3(axSample, 0f, bPos.z + bSize.z);
                    var ha = a.SampleHeight(worldA - aPos) + aPos.y;
                    var hb = b.SampleHeight(worldB - bPos) + bPos.y;
                    maxGap = Mathf.Max(maxGap, Mathf.Abs(ha - hb));
                }
            }

            return found ? maxGap : -1f;
        }

        static void MeasureWildernessGridAlignment(
            Terrain mainTerrain,
            IReadOnlyList<Terrain> playTerrains,
            List<string> issues,
            out float maxPositionMismatch,
            out int missingPlayAdjacency)
        {
            maxPositionMismatch = 0f;
            missingPlayAdjacency = 0;
            if (mainTerrain?.terrainData == null)
                return;

            var tileSize = mainTerrain.terrainData.size;
            var mainOrigin = mainTerrain.transform.position;
            const float posWarn = PositionWarnMeters;

            foreach (var tile in SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain))
            {
                if (tile?.terrainData == null ||
                    !SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(tile.name, out var off))
                    continue;

                var expected = SurfaceTerrainGridRegistry.ExpectedOrigin(mainTerrain, off);
                var mismatch = new Vector2(
                    tile.transform.position.x - expected.x,
                    tile.transform.position.z - expected.z).magnitude;
                maxPositionMismatch = Mathf.Max(maxPositionMismatch, mismatch);
                if (mismatch > posWarn)
                {
                    issues.Add(
                        $"Wilderness tile {tile.name} misaligned by {mismatch:F1}m (expected grid slot {off.x},{off.y}).");
                }
            }

            var foothills = SurfaceTerrainTileExpansion.CollectMountainFoothillTiles(mainTerrain);
            if (foothills.Length == 0 || playTerrains == null)
                return;

            const int samples = 4;
            foreach (var play in playTerrains)
            {
                if (play?.terrainData == null || !TryResolveGridOffset(mainTerrain, play, out var playOff))
                    continue;

                if (Mathf.Max(Mathf.Abs(playOff.x), Mathf.Abs(playOff.y)) > 1)
                    continue;

                foreach (var foothill in foothills)
                {
                    if (foothill?.terrainData == null ||
                        !SurfaceTerrainTileExpansion.TryParseOuterRingTileOffset(foothill.name, out var fhOff))
                        continue;

                    var delta = fhOff - playOff;
                    if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                        continue;

                    var gap = MeasureSharedEdgeGap(
                        play,
                        play.transform.position,
                        tileSize,
                        foothill,
                        foothill.transform.position,
                        tileSize,
                        samples);
                    if (gap >= 0f)
                        continue;

                    missingPlayAdjacency++;
                    issues.Add(
                        $"Play↔foothill gap — {play.name} and {foothill.name} not edge-adjacent " +
                        $"(grid {playOff.x},{playOff.y}↔{fhOff.x},{fhOff.y}).");
                }
            }
        }

        static void MeasurePropFloaters(
            IReadOnlyList<Terrain> terrains,
            out int floaterCount,
            out int sampleCount,
            List<string> issues)
        {
            floaterCount = 0;
            sampleCount = 0;
            var surfaceRoot = GameObject.Find("GeneratedSurfaceWorld");
            var veg = surfaceRoot != null ? surfaceRoot.transform.Find("Vegetation") : null;
            if (veg == null)
                return;

            var mainTerrain = terrains.Count > 0 ? terrains[0] : null;
            if (mainTerrain == null)
                return;

            foreach (Transform child in veg)
            {
                if (child == null)
                    continue;

                sampleCount++;
                if (!SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(
                        mainTerrain,
                        child.position.x,
                        child.position.z,
                        out var terrain) ||
                    terrain == null)
                    continue;

                var expectedY = terrain.SampleHeight(child.position) + terrain.transform.position.y;
                var delta = child.position.y - expectedY;
                if (delta > PropFloaterWarnMeters || delta < -PropFloaterWarnMeters)
                {
                    floaterCount++;
                    if (floaterCount <= 8)
                    {
                        issues.Add(
                            $"Prop floater '{child.name}' ΔY={delta:F2}m at ({child.position.x:F0},{child.position.z:F0}).");
                    }
                }
            }

            if (floaterCount > 8)
                issues.Add($"… and {floaterCount - 8} more prop floaters.");
        }

        public static void Write(AuditReport report)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ReportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"generatedUtc\": \"{report.generatedUtc}\",");
            sb.AppendLine($"  \"scene\": \"{Escape(report.scene)}\",");
            sb.AppendLine($"  \"auditMilestone\": \"{Escape(report.auditMilestone)}\",");
            sb.AppendLine($"  \"seed\": {report.seed},");
            sb.AppendLine($"  \"terrainTileCount\": {report.terrainTileCount},");
            sb.AppendLine($"  \"maxSeamGapMeters\": {report.maxSeamGapMeters:F4},");
            sb.AppendLine($"  \"seamEdgeCount\": {report.seamEdgeCount},");
            sb.AppendLine($"  \"playToWildernessMaxSeamGapMeters\": {report.playToWildernessMaxSeamGapMeters:F4},");
            sb.AppendLine($"  \"playToWildernessSeamEdgeCount\": {report.playToWildernessSeamEdgeCount},");
            sb.AppendLine($"  \"wildernessMaxPositionMismatchMeters\": {report.wildernessMaxPositionMismatchMeters:F4},");
            sb.AppendLine($"  \"playToWildernessMissingAdjacencyCount\": {report.playToWildernessMissingAdjacencyCount},");
            sb.AppendLine($"  \"terrainFingerprint\": \"{Escape(report.terrainFingerprint)}\",");
            sb.AppendLine($"  \"suggestedFix\": \"{Escape(report.suggestedFix)}\",");
            sb.AppendLine($"  \"terrainGridManifestRel\": \"{Escape(report.terrainGridManifestRel)}\",");
            sb.AppendLine($"  \"propFloaterCount\": {report.propFloaterCount},");
            sb.AppendLine($"  \"propSampleCount\": {report.propSampleCount},");
            sb.AppendLine($"  \"caveOverlapMeters\": {report.caveOverlapMeters:F4},");
            sb.AppendLine($"  \"mouthDeltaYMeters\": {report.mouthDeltaYMeters:F4},");
            sb.AppendLine($"  \"vegetationContractPass\": {(report.vegetationContractPass ? "true" : "false")},");
            sb.AppendLine($"  \"layoutAcceptable\": {(report.layoutAcceptable ? "true" : "false")},");
            sb.AppendLine($"  \"blocksSurfaceContinue\": {(report.blocksSurfaceContinue ? "true" : "false")},");
            sb.AppendLine($"  \"blocksMountainRing\": {(report.blocksMountainRing ? "true" : "false")},");
            sb.AppendLine($"  \"blocksCaveQueue\": {(report.blocksCaveQueue ? "true" : "false")},");
            WriteStringArray(sb, "failingTileIds", report.failingTileIds, 2);
            WriteSeamEdges(sb, report.failingSeamEdges);
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < report.issues.Length; i++)
            {
                var comma = i < report.issues.Length - 1 ? "," : "";
                sb.AppendLine($"    \"{Escape(report.issues[i])}\"{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString());
            Debug.Log(
                $"[CaveBuild] WorldLayoutAudit [{report.auditMilestone}] — acceptable={report.layoutAcceptable} " +
                $"fix={report.suggestedFix} seam={report.maxSeamGapMeters:F3}m → {ReportRel}");
        }

        static void WriteStringArray(StringBuilder sb, string key, string[] values, int indent)
        {
            var pad = new string(' ', indent);
            sb.AppendLine($"{pad}\"{key}\": [");
            for (var i = 0; i < values.Length; i++)
            {
                var comma = i < values.Length - 1 ? "," : "";
                sb.AppendLine($"{pad}  \"{Escape(values[i])}\"{comma}");
            }

            sb.AppendLine($"{pad}],");
        }

        static void WriteSeamEdges(StringBuilder sb, SeamEdgeRecord[] edges)
        {
            sb.AppendLine("  \"failingSeamEdges\": [");
            for (var i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                var comma = i < edges.Length - 1 ? "," : "";
                sb.AppendLine("    {");
                sb.AppendLine($"      \"tileA\": \"{Escape(e.tileA)}\",");
                sb.AppendLine($"      \"tileB\": \"{Escape(e.tileB)}\",");
                sb.AppendLine($"      \"gapMeters\": {e.gapMeters:F4}");
                sb.AppendLine($"    }}{comma}");
            }

            sb.AppendLine("  ],");
        }

        static string Escape(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
