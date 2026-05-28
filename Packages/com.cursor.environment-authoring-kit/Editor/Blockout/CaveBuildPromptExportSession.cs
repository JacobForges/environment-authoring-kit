#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Avoids duplicate tsx prompt exports in the same build (validate gate + research sub-steps).
    /// Uses on-disk artifacts first — session timers alone caused validate 7/13 to block on 120s tsx.
    /// </summary>
    static class CaveBuildPromptExportSession
    {
        const double FreshSeconds = 600;
        const double ManifestFreshSeconds = 1800;

        static int _seed;
        static double _completedAt;
        static double _manifestCompletedAt;

        public static void MarkFresh(int seed)
        {
            _seed = seed;
            _completedAt = EditorApplication.timeSinceStartup;
        }

        public static void MarkManifestFresh()
        {
            _manifestCompletedAt = EditorApplication.timeSinceStartup;
        }

        public static bool IsFresh(int seed) =>
            seed == _seed && EditorApplication.timeSinceStartup - _completedAt < FreshSeconds;

        public static bool IsManifestFresh() =>
            _manifestCompletedAt > 0 &&
            EditorApplication.timeSinceStartup - _manifestCompletedAt < ManifestFreshSeconds;

        public static bool ShouldSkipManifestRebuild(int seed) =>
            IsManifestFresh() || IsFresh(seed) || HasValidateResearchPromptArtifactsOnDisk();

        static string GeneratedFolder(string hub = null) =>
            Path.Combine(
                hub ?? CaveBuildCursorSettings.ResolveHubRoot(),
                CaveBuildAgentContextExporter.Folder);

        static bool FileOk(string path) =>
            File.Exists(path) && new FileInfo(path).Length > 32;

        /// <summary>All MD/JSON prompt outputs needed to skip blocking tsx during cave validate.</summary>
        public static bool HasValidateResearchPromptArtifactsOnDisk(string hub = null)
        {
            var gen = GeneratedFolder(hub);
            return FileOk(Path.Combine(gen, "CaveBuildGeneratedJsonManifest.json")) &&
                   FileOk(Path.Combine(gen, "CaveBuildUnifiedAgentPrompt.md")) &&
                   FileOk(Path.Combine(gen, "CaveBuildResearchAgentPrompt.md")) &&
                   FileOk(Path.Combine(gen, "CaveBuildResearchActionPlan.json")) &&
                   FileOk(Path.Combine(gen, "CaveBuildActivePhasePrompt.md"));
        }

        static bool ForcePromptExport(string[] extraEnvs = null)
        {
            if (string.Equals(
                    System.Environment.GetEnvironmentVariable("CAVE_FORCE_PROMPT_EXPORT"),
                    "1",
                    System.StringComparison.Ordinal))
                return true;

            if (extraEnvs == null)
                return false;

            foreach (var e in extraEnvs)
            {
                if (e != null && e.StartsWith("CAVE_FORCE_PROMPT_EXPORT=1", System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>Maps tsx script to required output file(s) under Generated/.</summary>
        public static bool ShouldSkipPromptTsxDuringValidate(
            string scriptName,
            string hub = null,
            string[] extraEnvs = null)
        {
            if (ForcePromptExport(extraEnvs))
                return false;

            if (!LavaTubeCaveBuildPipeline.IsPhasedBuildActive &&
                !CaveBuildStartupCoordinator.IsActive &&
                !CaveBuildActionPacing.IsBusy)
                return false;

            var gen = GeneratedFolder(hub);
            switch (scriptName)
            {
                case "export-rung-prompt.ts":
                    return FileOk(Path.Combine(gen, "CaveBuildTailoredAgentPrompt.md")) &&
                           FileOk(Path.Combine(gen, "CaveBuildActiveRungPrompt.md"));
                case "export-terrain-rung-prompt.ts":
                    return FileOk(Path.Combine(gen, "TerrainBuildTailoredAgentPrompt.md")) &&
                           FileOk(Path.Combine(gen, "SurfaceTerrainActiveRungPrompt.md"));
                case "generate-meat-pass-plan.ts":
                    return false;
                case "generate-unified-agent-prompt.ts":
                    return FileOk(Path.Combine(gen, "CaveBuildGeneratedJsonManifest.json")) &&
                           FileOk(Path.Combine(gen, "CaveBuildUnifiedAgentPrompt.md"));
                case "generate-research-agent-prompt.ts":
                    return FileOk(Path.Combine(gen, "CaveBuildResearchAgentPrompt.md"));
                case "generate-research-action-plan.ts":
                    return FileOk(Path.Combine(gen, "CaveBuildResearchActionPlan.json"));
                case "generate-phase-prompts.ts":
                    return FileOk(Path.Combine(gen, "CaveBuildActivePhasePrompt.md"));
                default:
                    return HasValidateResearchPromptArtifactsOnDisk(hub);
            }
        }
    }
}
#endif
