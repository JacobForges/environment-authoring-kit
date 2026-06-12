#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Loads <see cref="PlannerBriefRelPath"/> — the creative contract the planner finalized.
    /// Unity builders read this; chat text alone is not authoritative.
    /// </summary>
    public static class CaveBuildPlannerLayoutBridge
    {
        public const string PlannerBriefRelPath =
            "Assets/EnvironmentKit/Generated/CaveBuildPlannerBrief.json";

        [Serializable]
        public sealed class BriefFile
        {
            public string conceptImageRel;
            public string conceptDensityImageRel;
            public CaveBuildPlannerApprovedAssets.ApprovedCard[] approvedConceptCards;
            public BriefPayload brief;
        }

        [Serializable]
        public sealed class BriefPayload
        {
            public string title;
            public string summary;
            public LayoutPlan layoutPlan;
        }

        [Serializable]
        public sealed class LayoutPlan
        {
            public string gridNote;
            public PlayDisk playDisk;
            public TechnicalSpecs technicalSpecs;
            public Marker[] markers;
            public TrailLink[] trails;
            public IslandMeta[] islands;
        }

        [Serializable]
        public sealed class TrailLink
        {
            public string from;
            public string to;
            public string label;
        }

        [Serializable]
        public sealed class IslandMeta
        {
            public string dir;
            public string label;
            public int enemies;
            public int npcs;
            public int props;
            public int collectibles;
        }

        public static string LoadedConceptImageRel { get; private set; }
        public static string LoadedConceptDensityImageRel { get; private set; }

        [Serializable]
        public sealed class PlayDisk
        {
            public bool labyrinth;
            public string labyrinthNote;
        }

        [Serializable]
        public sealed class TechnicalSpecs
        {
            public float plateauHeightM = 35f;
            public float platformHeightM = 12f;
            public float platformHopM = 4f;
            public float hubGapM = 28f;
            public string fallRespawn = "center_spawn";
            public float jumpHeightM = 2f;
        }

        [Serializable]
        public sealed class Marker
        {
            public string kind;
            public string zone;
            public int row = 1;
            public int col = 1;
            public string dir;
            public int slot;
            public string label;
            public string markerSlotKey;
            public string plannerCardId;
        }

        public static bool TryLoadBriefFile(out BriefFile file, out string message)
        {
            file = null;
            message = null;
            LoadedConceptImageRel = null;
            LoadedConceptDensityImageRel = null;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, PlannerBriefRelPath);
            if (!File.Exists(abs))
            {
                message = $"Missing {PlannerBriefRelPath} — finalize a planner session first.";
                return false;
            }

            try
            {
                file = JsonUtility.FromJson<BriefFile>(File.ReadAllText(abs));
                LoadedConceptImageRel = file?.conceptImageRel;
                LoadedConceptDensityImageRel = file?.conceptDensityImageRel;
                return file != null;
            }
            catch (Exception ex)
            {
                message = "Failed to parse planner brief file: " + ex.Message;
                return false;
            }
        }

        public static bool TryLoad(out BriefPayload brief, out string message)
        {
            brief = null;
            message = null;

            if (!CaveBuildSessionConfig.HasFinalizedActive)
            {
                message = "No finalized planner session.";
                return false;
            }

            if (!TryLoadBriefFile(out var doc, out message))
                return false;

            try
            {
                brief = doc?.brief;
                if (brief?.layoutPlan?.markers == null || brief.layoutPlan.markers.Length == 0)
                {
                    message = "Planner brief has no layoutPlan.markers.";
                    return false;
                }

                message = $"Loaded \"{brief.title ?? CaveBuildSessionConfig.Active?.label}\" — " +
                          $"{brief.layoutPlan.markers.Length} layout markers.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Failed to parse planner brief: " + ex.Message;
                return false;
            }
        }

        /// <summary>Play-disk row/col (0–2, north=row0) → grid offset (x west→east, y south→north).</summary>
        public static Vector2Int PlayRowColToOffset(int row, int col) =>
            new(Mathf.Clamp(col, 0, 2) - 1, 1 - Mathf.Clamp(row, 0, 2));

        public static bool IsCornerCell(int row, int col) =>
            (row == 0 || row == 2) && (col == 0 || col == 2);

        public static bool IsHubCell(int row, int col) => !IsCornerCell(row, col);

        public static TechnicalSpecs ResolveSpecs(LayoutPlan plan)
        {
            var specs = plan?.technicalSpecs ?? new TechnicalSpecs();
            var note = plan?.gridNote ?? string.Empty;

            if (specs.plateauHeightM <= 0.1f)
                specs.plateauHeightM = ParseNoteMeters(note, @"\+(\d+(?:\.\d+)?)\s*u.*plateau", 35f);
            if (specs.platformHeightM <= 0.1f)
                specs.platformHeightM = ParseNoteMeters(note, @"platforms?\s+at\s+\+(\d+(?:\.\d+)?)\s*u", 12f);
            if (specs.hubGapM <= 0.1f)
                specs.hubGapM = ParseNoteMeters(note, @"(\d+(?:\.\d+)?)\s*m\s+gap", 28f);
            if (specs.platformHopM <= 0.1f)
                specs.platformHopM = 4f;

            return specs;
        }

        static float ParseNoteMeters(string note, string pattern, float fallback)
        {
            if (string.IsNullOrEmpty(note))
                return fallback;

            var match = Regex.Match(note, pattern, RegexOptions.IgnoreCase);
            return match.Success && float.TryParse(match.Groups[1].Value, out var v) ? v : fallback;
        }

        public static bool IsPlatformMarker(Marker m) =>
            m != null &&
            string.Equals(m.kind, "prop", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(m.label) &&
            m.label.StartsWith("plat", StringComparison.OrdinalIgnoreCase);

        public static bool IsMazePlateauMarker(Marker m) =>
            m != null &&
            !string.IsNullOrEmpty(m.label) &&
            m.label.IndexOf("maze", StringComparison.OrdinalIgnoreCase) >= 0 &&
            m.label.IndexOf("plateau", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsScatterHubMarker(Marker m) =>
            m != null &&
            string.Equals(m.kind, "prop", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(m.label) &&
            (m.label.StartsWith("tree", StringComparison.OrdinalIgnoreCase) ||
             m.label.StartsWith("grass", StringComparison.OrdinalIgnoreCase) ||
             m.label.StartsWith("rock", StringComparison.OrdinalIgnoreCase));

        public static bool IsHubDressingMarker(Marker m) =>
            m != null &&
            string.Equals(m.kind, "prop", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(m.label) &&
            m.label.StartsWith("hub-prop", StringComparison.OrdinalIgnoreCase);

        public static bool IsSwitchMarker(Marker m) =>
            m != null &&
            string.Equals(m.kind, "prop", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(m.label) &&
            m.label.StartsWith("switch-", StringComparison.OrdinalIgnoreCase);

        public static bool IsIslandJunctionMarker(Marker m) =>
            m != null &&
            string.Equals(m.kind, "prop", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.zone, "island", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(m.label) &&
            m.label.StartsWith("junction-", StringComparison.OrdinalIgnoreCase);

        public static Vector2Int CardinalDirToOffset(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return Vector2Int.zero;

            return dir.ToUpperInvariant() switch
            {
                "N" => new Vector2Int(0, 2),
                "E" => new Vector2Int(2, 0),
                "S" => new Vector2Int(0, -2),
                "W" => new Vector2Int(-2, 0),
                _ => Vector2Int.zero,
            };
        }

        public static bool TryResolveConceptImagePath(out string absolutePath, out string relativePath)
        {
            absolutePath = null;
            relativePath = LoadedConceptImageRel;

            if (!string.IsNullOrEmpty(relativePath))
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var abs = Path.Combine(hub, relativePath);
                if (File.Exists(abs))
                {
                    absolutePath = abs;
                    return true;
                }
            }

            var root = CaveBuildCursorSettings.ResolveHubRoot();
            var fallbacks = new[]
            {
                "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/planner/concept.png",
                "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept.png",
            };

            return TryResolveImageUnderHub(root, fallbacks, out absolutePath, out relativePath);
        }

        public static bool TryResolveConceptDensityImagePath(out string absolutePath, out string relativePath)
        {
            absolutePath = null;
            relativePath = LoadedConceptDensityImageRel;

            if (!string.IsNullOrEmpty(relativePath))
            {
                var hub = CaveBuildCursorSettings.ResolveHubRoot();
                var abs = Path.Combine(hub, relativePath);
                if (File.Exists(abs))
                {
                    absolutePath = abs;
                    return true;
                }
            }

            var root = CaveBuildCursorSettings.ResolveHubRoot();
            var fallbacks = new[]
            {
                "Assets/EnvironmentKit/ResearchCache/images/fullworld-concepts/planner/concept-density.png",
                "Assets/EnvironmentKit/ResearchCache/images/concepts/phases/_pending-review/planner-session/concept-density.png",
            };

            return TryResolveImageUnderHub(root, fallbacks, out absolutePath, out relativePath);
        }

        static bool TryResolveImageUnderHub(
            string root,
            string[] fallbacks,
            out string absolutePath,
            out string relativePath)
        {
            absolutePath = null;
            relativePath = null;

            foreach (var rel in fallbacks)
            {
                var abs = Path.Combine(root, rel);
                if (!File.Exists(abs))
                    continue;

                absolutePath = abs;
                relativePath = rel;
                return true;
            }

            return false;
        }
    }
}
#endif
