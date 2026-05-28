#if UNITY_EDITOR
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Writes terrain sculpt + geo contract for Cursor agents before LiDAR/sculpt runs.</summary>
    public static class SurfaceTerrainSculptPromptBridge
    {
        public const string PhaseId = "terrain_phase_sculpt";
        public const int QueuedStep = 36;
        public const string SculptPromptRel =
            CaveBuildAgentContextExporter.Folder + "/SurfaceTerrainSculptAgentPrompt.md";

        public static void ExportBeforeSculpt(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            int seed,
            Vector3 playCenter,
            float extentMeters)
        {
            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                PhaseId,
                "terrain_integration",
                0,
                QueuedStep,
                seed,
                out _);

            WriteSculptContract(request, ground, seed, playCenter, extentMeters);
        }

        static void WriteSculptContract(
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            int seed,
            Vector3 playCenter,
            float extentMeters)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, SculptPromptRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            var county = "unknown";
            var segmentNote = "no close-up segment";
            if (SurfaceDemGeoreferenceAuthor.TryLoadGeorefForSeed(seed, out var georef, out _))
            {
                county = georef.CountyId ?? county;
                segmentNote = georef.HasCloseUpSegment
                    ? $"close-up {georef.SegmentSizeMeters:F0}m @ {georef.SegmentMetersPerPixel:F2} m/px"
                    : "legacy county UV — run sync-florida-hillshades";
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Surface terrain sculpt — agent contract (read before C# edits)");
            sb.AppendLine();
            var groundName = ground?.Anchor != null ? ground.Anchor.name : "unknown-ground";
            sb.AppendLine("## Ground anchor (Unity scene object/tag)");
            sb.AppendLine($"- **Ground object:** `{groundName}` (tag/named ground selected by `SceneGroundResolver`)");
            sb.AppendLine($"- **Ground anchor XZ:** ({playCenter.x:F1}, {playCenter.z:F1})");
            sb.AppendLine($"- **Surface Y:** {playCenter.y:F3}");
            sb.AppendLine($"- **Extent meters:** {extentMeters:F0}");
            sb.AppendLine($"- **Seed:** {seed}");
            sb.AppendLine($"- **Florida county (research guide):** `{county}` ({segmentNote})");
            sb.AppendLine();
            sb.AppendLine("## Generation order (mandatory)");
            sb.AppendLine("1. **Procedural FBM** is the playable height (`SampleWorldFbm`, `SurfaceTerrainCenteredAuthor`).");
            sb.AppendLine("2. **LiDAR/DEM** is structural bias only (≤28% guide) — never a heightmap photocopy.");
            sb.AppendLine("3. Build from the resolved Ground anchor (`SceneGroundResolver`) — not an arbitrary world-center guide.");
            sb.AppendLine("4. **Never skip the inner playable disk** in `StampGeorefRowRange` — inner preserve caused flat quilted cores.");
            sb.AppendLine("5. After DEM: **3–5 refinement sculpt passes** blend FBM over the guide, then peak normalize.");
            sb.AppendLine("6. Trails/NavMesh/props run only after heightfield is playable (no craters, no parallel shelves).");
            sb.AppendLine();
            sb.AppendLine("## Forbidden (causes user-visible failures)");
            sb.AppendLine("- Flat inner disk + quilted LiDAR ring (uneven ground)");
            sb.AppendLine("- County stretch across entire terrain without world-space FBM");
            sb.AppendLine("- `passIndex` in Perlin UV or additive noise strata per pass");
            sb.AppendLine("- Full-map `SetHeights` in one frame during sculpt");
            sb.AppendLine("- Ignoring the tagged/labeled Ground anchor chosen by `SceneGroundResolver`");
            sb.AppendLine("- Moving cave entrance / layout relative to resolved Ground anchor");
            sb.AppendLine();
            sb.AppendLine("## Files to edit");
            sb.AppendLine("- `SurfaceDemGeoreferenceAuthor.cs` — geo stamp, guide weight ramp");
            sb.AppendLine("- `SurfaceTerrainLidarCreativeGuide.cs` — creative vs guide blend");
            sb.AppendLine("- `SurfaceTerrainCenteredAuthor.cs` — world FBM sculpt passes");
            sb.AppendLine("- `SurfaceTerrainCraterRepair.cs` — bowls / craters after sculpt");
            sb.AppendLine();
            sb.AppendLine("## Grade targets");
            sb.AppendLine("- `heightfield_no_craters` ≥ 90, walkable slope band, peak at Ground Y");
            sb.AppendLine("- Read `SurfaceTerrainBuildLadderReport.json` + `TerrainBuildTailoredAgentPrompt.md` for active rung.");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            CaveBuildEditorLog.LogSurface(
                $"[Surface] Sculpt agent contract → {SculptPromptRel}",
                forceUnityConsole: true);
        }
    }
}
#endif
