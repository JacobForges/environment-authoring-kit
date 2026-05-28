#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Tracks whether cave + surface are playable (polish/NPC dialogue left for humans).
    /// </summary>
    public static class PlayableWorldGate
    {
        public const string StatusRel = "Assets/EnvironmentKit/Generated/PlayableWorldStatus.json";

        [Serializable]
        public class PlayableWorldStatus
        {
            public bool meetsPlayableWorld;
            public int meatPass;
            public bool hasSurfaceRoot;
            public bool hasTerrain;
            public bool hasVegetation;
            public bool hasSurfaceNavMesh;
            public bool mouthGrounded;
            public int qualityScore;
            public string letterGrade;
            public string[] blockers;
            public string note;
        }

        public static bool EvaluateAndWrite(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            int pass,
            out string message)
        {
            var status = new PlayableWorldStatus { meatPass = pass };

            var env = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceWorldPaths.RootName) : null;
            status.hasSurfaceRoot = surface != null;
            status.hasTerrain = ground?.Terrain != null;
            var veg = surface != null ? surface.Find(SurfaceIntelligentPropPlacer.VegetationLayerName) : null;
            status.hasVegetation = veg != null && veg.childCount > 0;
            status.mouthGrounded = CaveBuildWorkflowCoordinator.IsGroundPlacementLocked;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildQualityReport report = null;
            CaveBuildQualityReportLoader.TryLoad(hub, out report);
            if (report != null)
            {
                status.qualityScore = report.OverallScore;
                status.letterGrade = report.LetterGrade;
            }

            var blockers = new System.Collections.Generic.List<string>();
            if (!status.hasSurfaceRoot)
                blockers.Add("Missing GeneratedSurfaceWorld — run FullWorld surface pipeline.");
            if (!status.hasTerrain)
                blockers.Add("No terrain on Ground.");
            if (!status.hasVegetation)
                blockers.Add("No surface vegetation layer — run surface prop passes.");
            if (!status.mouthGrounded)
                blockers.Add("Cave mouth not grounded to terrain.");
            if (report != null && report.OverallScore < 85)
                blockers.Add($"Quality below playable threshold ({report.OverallScore}/100).");

            status.blockers = blockers.ToArray();
            status.meetsPlayableWorld =
                blockers.Count == 0 &&
                report != null &&
                report.OverallScore >= 85;

            status.note = status.meetsPlayableWorld
                ? "Playable world: cave + surface + props; polish and NPC interaction remain for humans."
                : "Continue meat loop / autonomous fixes until blockers clear.";

            var path = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), StatusRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, JsonUtility.ToJson(status, true));

            message = status.meetsPlayableWorld
                ? $"PLAYABLE WORLD gate met (score {status.qualityScore}, grade {status.letterGrade})."
                : $"Playable world NOT met — {blockers.Count} blocker(s). See {StatusRel}.";
            CaveBuildPipelineLog.Info(message, "Surface-Meat");
            return status.meetsPlayableWorld;
        }
    }
}
#endif
