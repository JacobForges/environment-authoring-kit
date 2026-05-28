namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>URLs for ladder context — full manifest in CaveBuildResearch.json.</summary>
    public static class CaveBuildResearchSources
    {
        public const int MinResearchYear = 2025;

        public static readonly string[] All = CaveBuildResearchExporter.AllUrlsFromSeed();

        public const string WorkflowHint =
            "Mandatory: ResearchCache is pulled every build (reuse existing PNGs/DEM hillshades; HTTP-fetch only missing). Read entries + images on disk before web search. Florida aquifer/LiDAR: cave structure only — not water table/bathy. Attribution: RESEARCH_DATA_ATTRIBUTION.md. 2025–2026 proven only.";

        public const string DataAttributionDoc =
            "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md";
    }
}
