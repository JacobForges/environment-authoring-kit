#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Master + per-preset (0–9) biome/prop layout concept guides. Prompt blocks mirror
    /// <c>biome-layout-concept-prompts.ts</c>.
    /// </summary>
    public static class BiomeLayoutConceptCatalog
    {
        public const int PresetCount = FullWorldConceptLayoutCatalog.ConceptCount;
        public const string ConceptsRootRel =
            "Assets/EnvironmentKit/ResearchCache/images/biome-layout-concepts";
        public const string MasterConceptImageRel = ConceptsRootRel + "/master/concept.png";
        public const string PhasePromptManifestRel =
            CaveBuildAgentContextExporter.Folder + "/BiomeLayoutPhasePromptManifest.json";

        public const string ScaleBlock =
            "FullWorld biome layout: 289 tiles (17×17 Chebyshev), play disk center (9 tiles), " +
            "72 mixed transition ring (cheb 2–4), 208 preset wedges (cheb 5–8). " +
            "Prop transitions use feather bands (~1.75 tiles) — mix adjacent biome prop pools by distance, " +
            "not terrain alphamap fade. Mystical Florida karst aesthetic.";

        public const string FeatherRulesBlock =
            "Feather rules: center of biome = 100% that biome's prop catalog. " +
            "Within ~1.75 Chebyshev tiles of a zone boundary, weighted mix of both adjacent biomes' trees/grass/rocks. " +
            "Play karst meadow → foothill green → peak stone → horizon mist → preset wedge. " +
            "Water/coastal presets (4, 7) favor blue plants + sparse palms. Paced queue placement respects hardware budget.";

        public static string GetPresetConceptImageRel(int presetIndex) =>
            $"{ConceptsRootRel}/{Mathf.Clamp(presetIndex, 0, PresetCount - 1):D2}/concept.png";

        public static string GetFullWorldConceptImageRel(int presetIndex) =>
            FullWorldConceptLayoutCatalog.GetConceptImageRel(presetIndex);

        public static string GetPresetId(int presetIndex) =>
            presetIndex >= 0 && presetIndex < PresetCount
                ? Definitions[presetIndex].Id
                : Definitions[0].Id;

        public static string GetPresetLabel(int presetIndex) =>
            presetIndex >= 0 && presetIndex < PresetCount
                ? Definitions[presetIndex].Label
                : Definitions[0].Label;

        public static string GetDominantBiomes(int presetIndex) =>
            presetIndex >= 0 && presetIndex < PresetCount
                ? Definitions[presetIndex].DominantBiomes
                : Definitions[0].DominantBiomes;

        public static string GetPropSetSummary(int presetIndex) =>
            presetIndex >= 0 && presetIndex < PresetCount
                ? Definitions[presetIndex].PropSetSummary
                : Definitions[0].PropSetSummary;

        public static string FormatPresetPromptBlock(int presetIndex, WorldGenerationRequest request = null)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var idx = Mathf.Clamp(presetIndex, 0, PresetCount - 1);
            var def = Definitions[idx];
            var sb = new StringBuilder(2048);
            sb.AppendLine("## Biome layout guide (planning — open before prop pass)");
            sb.AppendLine();
            sb.AppendLine($"**Master biome concept:** `{hub}/{MasterConceptImageRel}`");
            sb.AppendLine($"**Preset biome guide:** `{hub}/{GetPresetConceptImageRel(idx)}`");
            sb.AppendLine($"**FullWorld concept:** `{hub}/{GetFullWorldConceptImageRel(idx)}`");
            sb.AppendLine();
            sb.AppendLine(ScaleBlock);
            sb.AppendLine();
            sb.AppendLine(FeatherRulesBlock);
            sb.AppendLine();
            sb.AppendLine($"### Preset {idx} — {def.Label}");
            sb.AppendLine();
            sb.AppendLine($"**Dominant biomes:** {def.DominantBiomes}");
            sb.AppendLine($"**Prop sets:** {def.PropSetSummary}");
            sb.AppendLine();
            sb.AppendLine(def.PromptCore);
            sb.AppendLine();
            sb.AppendLine("**Ring layout:**");
            foreach (var ring in def.RingLayout)
                sb.AppendLine($"- {ring}");
            sb.AppendLine();
            sb.AppendLine("**Builder notes:**");
            foreach (var note in def.BuilderNotes)
                sb.AppendLine($"- {note}");

            if (request != null)
            {
                sb.AppendLine();
                sb.AppendLine($"**Active build:** seed {request.Seed}, concept index {request.ConceptLayoutIndex}, " +
                              $"style `{request.GenerationStyleId}`, water={request.SurfaceIncludeWater}.");
            }

            return sb.ToString();
        }

        public static void LogPresetPrompt(WorldGenerationRequest request)
        {
            if (request == null)
                return;

            var idx = Mathf.Clamp(request.ConceptLayoutIndex, 0, PresetCount - 1);
            CaveBuildEditorLog.LogSurface(
                $"[BiomeLayout] Preset {idx} ({GetPresetId(idx)}): {GetDominantBiomes(idx)}",
                forceUnityConsole: false);
            CaveBuildEditorLog.LogSurface(
                $"[BiomeLayout] Master → {MasterConceptImageRel}, preset → {GetPresetConceptImageRel(idx)}, " +
                $"fullworld → {GetFullWorldConceptImageRel(idx)}",
                forceUnityConsole: false);
            CaveBuildEditorLog.LogSurface(
                $"[BiomeLayout] Feather: {FeatherRulesBlock}",
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

        public static bool TryRevealPresetGuide(int presetIndex)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var rel = GetPresetConceptImageRel(presetIndex);
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
                "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/generate-biome-layout-phase-prompt-manifest.ts";
            var scriptPath = Path.Combine(hub, scriptRel);
            if (!File.Exists(scriptPath))
                return WriteManifestInline(hub, out message);

            return CaveBuildPhasePromptBridge.ExportBiomeLayoutPhasePromptManifest(out message);
        }

        static bool WriteManifestInline(string hub, out string message)
        {
            message = string.Empty;
            try
            {
                CaveBuildAgentContextExporter.EnsureFolderPublic();
                var path = Path.Combine(hub, PhasePromptManifestRel);
                var sb = new StringBuilder(8192);
                sb.AppendLine("{");
                sb.AppendLine($"  \"generatedUtc\": \"{DateTime.UtcNow:o}\",");
                sb.AppendLine($"  \"presetCount\": {PresetCount},");
                sb.AppendLine($"  \"masterConceptImageRel\": \"{MasterConceptImageRel}\",");
                sb.AppendLine($"  \"scaleBlock\": \"{EscapeJson(ScaleBlock)}\",");
                sb.AppendLine($"  \"featherRulesBlock\": \"{EscapeJson(FeatherRulesBlock)}\",");
                sb.AppendLine("  \"presets\": [");
                for (var i = 0; i < PresetCount; i++)
                {
                    var def = Definitions[i];
                    var comma = i < PresetCount - 1 ? "," : string.Empty;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"index\": {i},");
                    sb.AppendLine($"      \"id\": \"{def.Id}\",");
                    sb.AppendLine($"      \"label\": \"{def.Label}\",");
                    sb.AppendLine($"      \"dominantBiomes\": \"{EscapeJson(def.DominantBiomes)}\",");
                    sb.AppendLine($"      \"propSetSummary\": \"{EscapeJson(def.PropSetSummary)}\",");
                    sb.AppendLine($"      \"promptCore\": \"{EscapeJson(def.PromptCore)}\",");
                    sb.AppendLine($"      \"conceptImageRel\": \"{GetPresetConceptImageRel(i)}\",");
                    sb.AppendLine($"      \"fullWorldConceptImageRel\": \"{GetFullWorldConceptImageRel(i)}\"");
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

        public readonly struct PresetDefinition
        {
            public readonly string Id;
            public readonly string Label;
            public readonly string DominantBiomes;
            public readonly string PropSetSummary;
            public readonly string PromptCore;
            public readonly string[] RingLayout;
            public readonly string[] BuilderNotes;

            public PresetDefinition(
                string id,
                string label,
                string dominantBiomes,
                string propSetSummary,
                string promptCore,
                string[] ringLayout,
                string[] builderNotes)
            {
                Id = id;
                Label = label;
                DominantBiomes = dominantBiomes;
                PropSetSummary = propSetSummary;
                PromptCore = promptCore;
                RingLayout = ringLayout;
                BuilderNotes = builderNotes;
            }
        }

        public static readonly PresetDefinition[] Definitions =
        {
            new(
                "biome_layout_00_ideal_labyrinth",
                "Ideal labyrinth — karst play + foothill/peak rings",
                "PlayKarst (center), FoothillGreen (cheb 2–4), PeakStone (5–7), MixedTransition (8–9), ConceptPreset00 wedge",
                "Karst meadow poplar/beech + fern ground cover; foothill green pine/oak; peak blue spruce + granite rocks",
                "Center play disk reads as open karst meadow with scattered poplar and beech. South labyrinth annex gets fern/mushroom understory only — no tall trees on bench tiles. Mixed ring feathers play→foothill→peak toward preset wedge.",
                new[]
                {
                    "Cheb 0–1: PlayKarst — open karst meadow, sparse trail-readable trees",
                    "Cheb 2–4: FoothillGreen feather from play — denser green pine/oak, meadow grass patches",
                    "Cheb 5–7: PeakStone — blue spruce/pine, exposed granite boulders, sparse understory",
                    "Cheb 8–9: MixedTransition feather toward outer preset wedge",
                    "Cheb 10+: ConceptPreset00 wedge — ideal labyrinth foothill emphasis",
                },
                new[]
                {
                    "Reference master concept + preset 00 fullworld-concepts/concept.png.",
                    "BiomePropCatalog + BiomePropFeatherResolver at each placement slot.",
                    "SurfaceIntelligentPropPlacer paced queue — DefaultPropsPerEditorChunk.",
                }),
            new(
                "biome_layout_01_classic_rings",
                "Classic open rings — minimal maze",
                "PlayKarst, FoothillGreen, PeakStone, HorizonMist, ConceptPreset01 wedge",
                "Even green rings; classic concentric prop density falloff",
                "Minimal labyrinth — play disk stays open meadow. Concentric rings read as foothill→peak→horizon with smooth prop feather bands. Outer wedge sparse wilderness.",
                new[]
                {
                    "Cheb 0–1: PlayKarst meadow",
                    "Cheb 2–5: FoothillGreen ring",
                    "Cheb 6–8: PeakStone + HorizonMist feather",
                    "Cheb 10+: ConceptPreset01 open wilderness",
                },
                new[] { "No south labyrinth prop override.", "Wider feather bands at cheb 4–6 for visible ring transitions." }),
            new(
                "biome_layout_02_florida_karst",
                "Florida karst — flat play + water",
                "PlayKarst, coastal water band, ConceptPreset02 wedge",
                "Flat meadow + lotus/reed/alocasia near water; minimal peak props",
                "Flat karst play disk with meadow grass and low beech. Water tiles (when SurfaceIncludeWater) get blue coastal plants — lotus, lily, sparse palms. No mountain peak props on play disk.",
                new[]
                {
                    "Cheb 0–1: PlayKarst flat meadow",
                    "Cheb 2–9: MixedTransition with water-plant bias near basins",
                    "Cheb 10+: ConceptPreset02 Florida karst wedge",
                },
                new[] { "PropEmphasis: florida_karst.", "Water/coastal pool uses blue LPMagicalForest plants." }),
            new(
                "biome_layout_03_appalachian_peaks",
                "Appalachian tall peaks",
                "PeakStone dominant, FoothillGreen base, PlayKarst meadow center",
                "Heavy peak spruce/pine + granite rocks; thin play meadow",
                "Tall peak biome dominates mixed ring — dense blue spruce, exposed boulder scatter, sparse grass above cheb 4. Play disk stays readable karst meadow but smaller tree count.",
                new[]
                {
                    "Cheb 0–1: PlayKarst (thin meadow)",
                    "Cheb 2–4: FoothillGreen base",
                    "Cheb 5–9: PeakStone dominant with rock scatter",
                    "Cheb 10+: ConceptPreset03 Appalachian wedge",
                },
                new[] { "PropEmphasis: appalachian_ridge.", "Rocks category prioritized on cheb 5+ tiles." }),
            new(
                "biome_layout_04_coastal_fog",
                "Coastal fog low mountains",
                "HorizonMist, coastal water, ConceptPreset04 wedge",
                "Blue mist plants, sparse trees, reed/lotus near water, low fog-readable silhouettes",
                "Foggy coastal read — blue-toned trees and bushes, sparse tall silhouettes, water-edge reeds. Mixed ring feathers horizon mist props inward. Play disk stays open with low ground cover.",
                new[]
                {
                    "Cheb 0–1: PlayKarst open",
                    "Cheb 2–9: HorizonMist + coastal feather",
                    "Cheb 10+: ConceptPreset04 coastal fog wedge",
                },
                new[] { "Weather Foggy — prop silhouettes stay low.", "SurfaceIncludeWater enables coastal plant pools." }),
            new(
                "biome_layout_05_tomb_raider_vertical",
                "Tomb Raider vertical cave + maze",
                "PlayKarst, AnnexLabyrinth south, PeakStone approach",
                "Labyrinth fern/mushroom; peak pines at cave mouth approaches; clear trail corridors",
                "South labyrinth annex uses fern/mushroom ground cover only. Peak stone pines frame vertical cave approaches. Play disk meadow with deliberate clearings at trail nodes.",
                new[]
                {
                    "Cheb 0–1: PlayKarst with trail clearings",
                    "South annex: AnnexLabyrinth fern/mushroom",
                    "Cheb 5–9: PeakStone at mouth approaches",
                    "Cheb 10+: ConceptPreset05 wedge",
                },
                new[] { "Exclude props within cave mouth clearance radius.", "UseTombRaiderLabyrinthCadence geometry." }),
            new(
                "biome_layout_06_sparse_trails",
                "Sparse jumps + trail network",
                "PlayKarst, sparse FoothillGreen, ConceptPreset06 wedge",
                "Sparse trees — trail-corridor readable; jump nodes bare",
                "Intentionally sparse prop density — trees clustered off-trail. Jump puzzle nodes stay bare rock/grass only. Mixed ring light foothill scatter.",
                new[]
                {
                    "Cheb 0–1: PlayKarst sparse meadow",
                    "Cheb 2–9: Light FoothillGreen scatter",
                    "Cheb 10+: ConceptPreset06 sparse wilderness",
                },
                new[] { "TrimToSparse on preset 06 catalog.", "Trail corridor half-width prop exclusion." }),
            new(
                "biome_layout_07_water_labyrinth",
                "Water basins + foothill maze",
                "PlayKarst, water/coastal, FoothillGreen, ConceptPreset07 wedge",
                "Water basin lotus/reeds; foothill maze green pine; labyrinth fern understory",
                "Water basins with blue coastal plants. Foothill maze green pine/oak on mixed ring. South labyrinth fern understory. Feather water→foothill at basin edges.",
                new[]
                {
                    "Cheb 0–1: PlayKarst",
                    "Water basins: coastal plant pools",
                    "Cheb 2–9: FoothillGreen + water feather",
                    "Cheb 10+: ConceptPreset07 water labyrinth wedge",
                },
                new[] { "SurfaceIncludeWater required.", "Feather water/coastal ↔ foothill at basin rim." }),
            new(
                "biome_layout_08_perimeter_trails",
                "Perimeter trail ring",
                "PlayKarst center, FoothillGreen perimeter band, ConceptPreset08 wedge",
                "Perimeter trail ring: bush/ground cover along ring; open play center",
                "Open play center with meadow grass. Perimeter trail ring (cheb 2–3) gets bush and ground cover accent — visual ring guide. Outer mixed ring standard foothill→peak feather.",
                new[]
                {
                    "Cheb 0–1: PlayKarst open center",
                    "Cheb 2–3: Perimeter trail bush/ground cover band",
                    "Cheb 4–9: Standard mixed ring feather",
                    "Cheb 10+: ConceptPreset08 wedge",
                },
                new[] { "SurfaceIncludeTrails — props follow ring polyline.", "Do not block trail readability." }),
            new(
                "biome_layout_09_speed_minimal",
                "Speed minimal — 81-tile core",
                "PlayKarst only (81-tile core)",
                "Minimal props — meadow grass only on play disk",
                "Speed build — props limited to play disk 3×3 only. Meadow grass and minimal ground cover. No mixed ring or preset wedge prop passes.",
                new[]
                {
                    "Cheb 0–1 only: PlayKarst minimal meadow",
                    "No props beyond play disk when UseExtendedOpenWorldGrid=false",
                },
                new[] { "UseExtendedOpenWorldGrid=false skips outer prop placement.", "Lowest hardware budget." }),
        };
    }
}
#endif
