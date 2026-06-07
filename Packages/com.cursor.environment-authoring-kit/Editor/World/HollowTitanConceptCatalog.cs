#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Twelve Hollow Titan meat-phase concept guides (0–11) plus master overview image.
    /// Prompt text mirrors <c>hollow-titan-concept-prompts.ts</c>.
    /// </summary>
    public static class HollowTitanConceptCatalog
    {
        public const int PhaseCount = 12;
        public const string ConceptsRootRel =
            "Assets/EnvironmentKit/ResearchCache/images/hollow-titan-concepts";
        public const string MasterConceptImageRel = ConceptsRootRel + "/master/concept.png";
        public const string PhasePromptManifestRel =
            CaveBuildAgentContextExporter.Folder + "/HollowTitanPhasePromptManifest.json";

        public static string GetPhaseConceptImageRel(int phaseIndex) =>
            $"{ConceptsRootRel}/{Mathf.Clamp(phaseIndex, 0, PhaseCount - 1):D2}/concept.png";

        public static string GetPhaseId(int phaseIndex) =>
            phaseIndex >= 0 && phaseIndex < PhaseCount
                ? Definitions[phaseIndex].Id
                : Definitions[0].Id;

        public static string GetPhaseLabel(int phaseIndex) =>
            phaseIndex >= 0 && phaseIndex < PhaseCount
                ? Definitions[phaseIndex].Label
                : HollowTitanLandmarkMeatPhases.PhaseLabels[0];

        public static string GetPromptCore(int phaseIndex) =>
            phaseIndex >= 0 && phaseIndex < PhaseCount
                ? Definitions[phaseIndex].PromptCore
                : Definitions[0].PromptCore;

        public static string FormatPhasePromptBlock(int phaseIndex)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var idx = Mathf.Clamp(phaseIndex, 0, PhaseCount - 1);
            var def = Definitions[idx];
            var sb = new StringBuilder(1024);
            sb.AppendLine("## Hollow Titan phase guide (planning — open before editing)");
            sb.AppendLine();
            sb.AppendLine($"**Master concept:** `{hub}/{MasterConceptImageRel}`");
            sb.AppendLine($"**Phase concept:** `{hub}/{GetPhaseConceptImageRel(idx)}`");
            sb.AppendLine();
            sb.AppendLine(ScaleBlock);
            sb.AppendLine();
            sb.AppendLine($"### {def.Label} (phase {idx})");
            sb.AppendLine();
            sb.AppendLine(def.PromptCore);
            sb.AppendLine();
            sb.AppendLine("**Builder notes:**");
            foreach (var note in def.BuilderNotes)
                sb.AppendLine($"- {note}");
            return sb.ToString();
        }

        public static void LogPhasePrompt(HollowTitanLandmarkMeatPhases.Phase phase)
        {
            var idx = (int)phase;
            if (idx < 0 || idx >= PhaseCount)
                return;

            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Phase prompt ({idx}): {GetPromptCore(idx)}",
                forceUnityConsole: false);
            CaveBuildEditorLog.LogSurface(
                $"[HollowTitan] Concept guide → {GetPhaseConceptImageRel(idx)} (master: {MasterConceptImageRel})",
                forceUnityConsole: false);
        }

        public static bool TryRevealMasterConcept()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, MasterConceptImageRel);
            if (!File.Exists(path))
                return false;
            UnityEditor.EditorUtility.RevealInFinder(path);
            return true;
        }

        public static bool TryRevealPhaseGuide(int phaseIndex)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var rel = GetPhaseConceptImageRel(phaseIndex);
            var path = Path.Combine(hub, rel);
            if (!File.Exists(path))
                return TryRevealMasterConcept();

            UnityEditor.EditorUtility.RevealInFinder(path);
            return true;
        }

        public static bool ExportPhasePromptManifest(out string message)
        {
            message = string.Empty;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var scriptRel =
                "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/generate-hollow-titan-phase-prompt-manifest.ts";
            var scriptPath = Path.Combine(hub, scriptRel);
            if (!File.Exists(scriptPath))
            {
                message = $"Manifest script missing: {scriptPath}";
                return WriteManifestInline(hub, out message);
            }

            return CaveBuildPhasePromptBridge.ExportHollowTitanPhasePromptManifest(out message);
        }

        static bool WriteManifestInline(string hub, out string message)
        {
            message = string.Empty;
            try
            {
                CaveBuildAgentContextExporter.EnsureFolderPublic();
                var path = Path.Combine(hub, PhasePromptManifestRel);
                var sb = new StringBuilder(4096);
                sb.AppendLine("{");
                sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
                sb.AppendLine("  \"landmarkId\": \"HollowTitanLandmark\",");
                sb.AppendLine($"  \"phaseCount\": {PhaseCount},");
                sb.AppendLine($"  \"masterConceptImageRel\": \"{MasterConceptImageRel}\",");
                sb.AppendLine("  \"phases\": [");
                for (var i = 0; i < PhaseCount; i++)
                {
                    var def = Definitions[i];
                    var comma = i < PhaseCount - 1 ? "," : string.Empty;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"index\": {i},");
                    sb.AppendLine($"      \"id\": \"{def.Id}\",");
                    sb.AppendLine($"      \"label\": \"{def.Label}\",");
                    sb.AppendLine($"      \"promptCore\": \"{EscapeJson(def.PromptCore)}\",");
                    sb.AppendLine($"      \"conceptImageRel\": \"{GetPhaseConceptImageRel(i)}\"");
                    sb.AppendLine($"    }}{comma}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(path, sb.ToString());
                message = $"Wrote {PhasePromptManifestRel} (inline fallback).";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? string.Empty;

        public const string ScaleBlock =
            "Hollow Titan landmark: one massive mystical medieval hollow dead tree per FullWorld seed. " +
            "Authoritative tree generation guide: master/concept.png (AI master option-02-ai). " +
            "Surface terrain tile only — never cave mouths, labyrinth benches, or trail corridors. " +
            "Massive scale: trunk radius ~30 m, height ~82 m, 4 interior floors @ ~6.5 m each. " +
            "Landmark-only legendary spawns — never world scatter tables. Ground snap after sculpt.";

        public static readonly PhaseDefinition[] Definitions =
        {
            new(
                "hollow_titan_site_pick",
                "Hollow Titan — site pick",
                "Pick one random eligible surface peak tile in Florida karst terrain. Exclude cave mouths, labyrinth annex, and trail corridors.",
                new[]
                {
                    "HollowTitanLandmarkSitePicker — peak ring tiles only.",
                    "Destroy prior root; write HollowTitanLandmarkBuildData + floor plan.",
                }),
            new(
                "hollow_titan_base_snap",
                "Hollow Titan — base snap",
                "Snap tree base to terrain SampleHeight at stored site XZ with small clearance above ground.",
                new[]
                {
                    "HollowTitanLandmarkTerrainSnap.SnapRootBaseToGround.",
                    "Re-snap after terrain sculpt via ResnapAfterTerrainSculpt.",
                }),
            new(
                "hollow_titan_trunk_shell",
                "Hollow Titan — trunk shell",
                "Exterior hollow stump bowl — terraformed terrain tiles: raised bleached-bark rim + depressed crater interior. No log/branch CC0 props.",
                new[]
                {
                    "HollowTitanExteriorTerraform.SculptHollowStumpBowl — multi-tile heightmap sculpt after surface terraform.",
                    "Collision shell BarkOuter cylinder; interior floors/stairs unchanged.",
                    "Ensure BossStagePortal child at entrance approach.",
                }),
            new(
                "hollow_titan_hollow_carve",
                "Hollow Titan — hollow carve",
                "Carve interior void: inner cylinder ~62% trunk radius, ~88% height.",
                new[]
                {
                    "Interior/HollowVolume — renderer off, collider removed.",
                }),
            new(
                "hollow_titan_floor_plates",
                "Hollow Titan — floor plates",
                "Four stacked ring platforms inside the hollow trunk at planned floor Y.",
                new[]
                {
                    "Interior/Floors — cylinder plates from FloorPlan.Floors.",
                }),
            new(
                "hollow_titan_stair_spiral",
                "Hollow Titan — stair spiral",
                "Spiral ramp of wooden steps hugging the inner bark wall between all four floors.",
                new[]
                {
                    "Interior/Stairs — cube steps from FloorPlan.Stairs.",
                }),
            new(
                "hollow_titan_dead_branch_scatter",
                "Hollow Titan — dead branches",
                "No exterior limb/branch dressing — terraformed stump silhouette only.",
                new[]
                {
                    "Exterior/DeadBranches cleared; prop scatter removed.",
                }),
            new(
                "hollow_titan_entrance_framing",
                "Hollow Titan — entrance frame",
                "Simple cube arch + opening at stump base entrance — no log prefabs.",
                new[]
                {
                    "Exterior/Entrance — cube arch oriented along FloorPlan.EntranceForward.",
                }),
            new(
                "hollow_titan_per_floor_spawn_markers",
                "Hollow Titan — floor spawn markers",
                "Landmark-only enemy spawn markers on each floor ring.",
                new[]
                {
                    "Spawns/Enemies + HollowTitanLandmarkSpawner on root.",
                }),
            new(
                "hollow_titan_lighting_fog_mood",
                "Hollow Titan — lighting & fog",
                "Warm amber point lights at each floor center; soft interior fog volume.",
                new[]
                {
                    "Mood/Lights + Mood/FogVolume markers.",
                }),
            new(
                "hollow_titan_landmark_loot_table",
                "Hollow Titan — landmark loot table",
                "Landmark loot markers per floor; write spawn manifest JSON.",
                new[]
                {
                    "Spawns/Loot + HollowTitanLandmarkSpawnManifest.Write.",
                }),
            new(
                "hollow_titan_enemy_patrol_nodes",
                "Hollow Titan — enemy patrol nodes",
                "Patrol node ring on each floor — completes landmark encounter loop.",
                new[]
                {
                    "Spawns/Patrol — landmark scope only.",
                }),
        };

        public readonly struct PhaseDefinition
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string PromptCore;
            public readonly string[] BuilderNotes;

            public PhaseDefinition(string id, string label, string promptCore, string[] builderNotes)
            {
                Id = id;
                Label = label;
                PromptCore = promptCore;
                BuilderNotes = builderNotes;
            }
        }
    }
}
#endif
