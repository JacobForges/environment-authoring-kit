#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Hub-selected FullWorld concept layout (0–9).</summary>
    public enum FullWorldGenerationStyleId
    {
        Style00IdealLabyrinth = 0,
        Style01ClassicRings = 1,
        Style02FloridaKarst = 2,
        Style03AppalachianPeaks = 3,
        Style04CoastalFog = 4,
        Style05TombRaiderVertical = 5,
        Style06SparseTrails = 6,
        Style07WaterLabyrinth = 7,
        Style08PerimeterTrails = 8,
        Style09SpeedMinimal = 9,
    }

    public static class FullWorldGenerationStylePreset
    {
        public const string PrefKey = "EnvironmentKit_FullWorldGenerationStyle";

        public const string ClassicStyleId = "concept_01_classic_rings";
        public const string IdealLayout49StyleId = "concept_00_ideal_labyrinth";

        public const string ActiveStyleRel = CaveBuildAgentContextExporter.Folder + "/ActiveGenerationStyle.json";

        public const string ConceptImagesRootRel = FullWorldConceptLayoutCatalog.ConceptsRootRel;

        public static string ConceptImageRelForIndex(int index) =>
            FullWorldConceptLayoutCatalog.GetConceptImageRel(index);

        public static string[] DisplayNames => FullWorldConceptLayoutCatalog.Definitions.Length > 0
            ? Array.ConvertAll(
                FullWorldConceptLayoutCatalog.Definitions,
                d => d.DisplayName)
            : new[] { "0 — Ideal labyrinth" };

        public static int LoadSelectedIndex()
        {
            var v = EditorPrefs.GetInt(PrefKey, (int)FullWorldGenerationStyleId.Style00IdealLabyrinth);
            return Mathf.Clamp(v, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);
        }

        public static void SaveSelectedIndex(int index) =>
            EditorPrefs.SetInt(
                PrefKey,
                Mathf.Clamp(index, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1));

        public static FullWorldGenerationStyleId LoadSelected() =>
            (FullWorldGenerationStyleId)LoadSelectedIndex();

        public static void SaveSelected(FullWorldGenerationStyleId style) =>
            SaveSelectedIndex((int)style);

        public static string StyleToId(int index) => FullWorldGenerationStyleCatalog.GetStyleId(index);

        public static string StyleToId(FullWorldGenerationStyleId style) =>
            StyleToId((int)style);

        public static void ApplyTo(WorldGenerationRequest request)
        {
            if (request == null)
                return;

            var index = FullWorldConceptLayoutCatalog.ResolveIndexForBuild(request.Seed);
            FullWorldConceptLayoutCatalog.ApplyByIndex(request, index);
            WriteActiveStyleFile(request, index);

            if (request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            Debug.Log(
                $"[CaveBuild] Concept layout {index}: {FullWorldConceptLayoutCatalog.GetDisplayName(index)} " +
                $"({request.GenerationStyleId}). Guide: {ConceptImageRelForIndex(index)}");
        }

        public static void WriteActiveStyleFile(WorldGenerationRequest request, int? styleIndexOverride = null)
        {
            var index = styleIndexOverride ?? request?.ConceptLayoutIndex ?? LoadSelectedIndex();
            index = Mathf.Clamp(index, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var path = Path.Combine(hub, ActiveStyleRel);
            var conceptRel = ConceptImageRelForIndex(index);
            var conceptPath = Path.Combine(hub, conceptRel);
            var displayName = FullWorldConceptLayoutCatalog.GetDisplayName(index).Replace("\"", "'");
            var styleId = FullWorldConceptLayoutCatalog.GetStyleId(index);
            var json =
                "{\n" +
                $"  \"conceptIndex\": {index},\n" +
                $"  \"styleIndex\": {index},\n" +
                $"  \"styleId\": \"{styleId}\",\n" +
                $"  \"displayName\": \"{displayName}\",\n" +
                $"  \"seed\": {request?.Seed ?? 0},\n" +
                $"  \"surfaceScope\": \"{request?.SurfaceScope}\",\n" +
                $"  \"conceptImageRel\": \"{conceptRel}\",\n" +
                $"  \"conceptImagesRootRel\": \"{ConceptImagesRootRel}\",\n" +
                $"  \"conceptImageExists\": {(File.Exists(conceptPath) ? "true" : "false")},\n" +
                $"  \"randomOnBuild\": {(FullWorldConceptLayoutCatalog.RandomOnBuildEnabled ? "true" : "false")},\n" +
                "  \"researchCategories\": [\"fullworld_layout_ideal\", \"fullworld_generation_style\", \"fullworld_do_not\"],\n" +
                "  \"researchDocRel\": \"Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_FULLWORLD_LAYOUT_IDEAL.md\",\n" +
                "  \"similarityRule\": \"Honor buildSeed — similar silhouette family per concept, not a pixel clone.\",\n" +
                $"  \"writtenUtc\": \"{DateTime.UtcNow:o}\"\n" +
                "}\n";
            File.WriteAllText(path, json);
        }

        public static bool IsIdealLayoutActive() => LoadSelectedIndex() == 0;

        public static bool IsIdealLayoutIndex(int index) => index == 0;
    }
}
#endif
