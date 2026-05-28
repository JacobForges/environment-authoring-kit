#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class CaveBuildEnhancementRunner
    {
        public const string RunLogRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildEnhancementRunLog.json";

        [Serializable]
        class RunLog
        {
            public string generatedUtc;
            public int seed;
            public string profile;
            public Entry[] entries = Array.Empty<Entry>();
        }

        [Serializable]
        class Entry
        {
            public string phaseId;
            public string hook;
            public bool executed;
            public string note;
        }

        static readonly List<Entry> SessionLog = new();
        static WorldGenerationRequest _activeRequest;

        public static void BeginSession(WorldGenerationRequest request, string profile = "reliable_full_world")
        {
            SessionLog.Clear();
            _activeRequest = request;
            CaveBuildPipelineLog.Info($"Enhancement session started — profile `{profile}`.", "Enhancement");
        }

        public static void RunHook(CaveBuildEnhancementCatalog.Hook hook, Transform caveRoot = null)
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var enabled = settings.enableEnhancementPhases;
            if (_activeRequest != null)
                enabled &= _activeRequest.RunEnhancementPhases;
            if (!enabled)
                return;

            var profile = CaveBuildReliableFullWorldPreset.ProfileId;
            foreach (var phase in CaveBuildEnhancementCatalog.All)
            {
                if (phase.hook != hook)
                    continue;
                if (!IsEnabled(phase, profile))
                    continue;

                var note = ExecutePhase(phase, caveRoot);
                SessionLog.Add(new Entry
                {
                    phaseId = phase.id,
                    hook = hook.ToString(),
                    executed = true,
                    note = note,
                });
                CaveBuildPipelineLog.Info($"Enhancement `{phase.id}`: {note}", "Enhancement");
            }

            FlushLog();
        }

        static bool IsEnabled(CaveBuildEnhancementCatalog.PhaseDef phase, string profile)
        {
            if (profile == CaveBuildReliableFullWorldPreset.ProfileId)
                return phase.reliableFullWorldDefault;
            return phase.reliableFullWorldDefault;
        }

        static string ExecutePhase(CaveBuildEnhancementCatalog.PhaseDef phase, Transform caveRoot)
        {
            var request = _activeRequest;
            var rng = request != null ? new System.Random(request.Seed + phase.id.GetHashCode()) : null;

            switch (phase.id)
            {
                case "creative_maze_flavor_seed":
                case "creative_visual_style_roll":
                case "creative_chamber_scale_jitter":
                case "creative_opening_sector_bias":
                case "creative_path_yaw_variance":
                case "creative_prop_emphasis_karst":
                    if (request != null && rng != null)
                        CaveBuildEnhancementCreativePasses.ApplyRequestRolls(request, rng);
                    return "request rolls applied";

                case "creative_entrance_key_light":
                case "creative_biolum_accent":
                case "creative_finish_beacon":
                case "creative_torch_warmth":
                    if (caveRoot != null && request != null)
                        CaveBuildEnhancementCreativePasses.ApplyAfterShell(caveRoot, request);
                    return caveRoot != null ? "shell creative lighting" : "skipped (no cave root)";

                case "creative_post_color_mood":
                    if (caveRoot != null && request != null)
                        CaveBuildEnhancementCreativePasses.ApplyPostColorMood(caveRoot, request);
                    return "fog/color mood";

                case "quality_dem_supersample_128":
                    SurfaceDemGeoreferenceAuthor.SetSupersampleTargetDim(128);
                    return "DEM target 128";

                case "quality_dem_supersample_256":
                    SurfaceDemGeoreferenceAuthor.SetSupersampleTargetDim(256);
                    return "DEM target 256";

                case "quality_grade_after_geo":
                    if (caveRoot != null && request != null)
                    {
                        var ground = SceneGroundResolver.Resolve();
                        CaveBuildQualitySystem.Grade(
                            caveRoot,
                            ground,
                            request,
                            null,
                            invokeCursorAgent: false);
                        return "quality graded";
                    }

                    return "skipped";

                case "quality_completion_contract":
                    return "deferred to PostPipeline";

                case "quality_preflight_checklist":
                    return "see CaveBuildPreflightReport.md";

                default:
                    return "registered (hook OK)";
            }
        }

        static void FlushLog()
        {
            if (SessionLog.Count == 0)
                return;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, RunLogRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var log = new RunLog
            {
                generatedUtc = DateTime.UtcNow.ToString("o"),
                seed = _activeRequest?.Seed ?? 0,
                profile = CaveBuildReliableFullWorldPreset.ProfileId,
                entries = SessionLog.ToArray(),
            };
            File.WriteAllText(path, JsonUtility.ToJson(log, true), Encoding.UTF8);
        }

        public static void ExportCatalogJson()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildAgentContextExporter.Folder, "CaveBuildEnhancementPhases.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"phaseCount\": {CaveBuildEnhancementCatalog.PhaseCount},");
            sb.AppendLine("  \"phases\": [");
            for (var i = 0; i < CaveBuildEnhancementCatalog.All.Length; i++)
            {
                var p = CaveBuildEnhancementCatalog.All[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{p.id}\",");
                sb.AppendLine($"      \"title\": \"{Escape(p.title)}\",");
                sb.AppendLine($"      \"category\": \"{p.category}\",");
                sb.AppendLine($"      \"hook\": \"{p.hook}\",");
                sb.AppendLine($"      \"reliableFullWorldDefault\": {(p.reliableFullWorldDefault ? "true" : "false")},");
                sb.AppendLine($"      \"costWeight\": {p.costWeight}");
                sb.Append(i < CaveBuildEnhancementCatalog.All.Length - 1 ? "    }," : "    }");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
#endif
