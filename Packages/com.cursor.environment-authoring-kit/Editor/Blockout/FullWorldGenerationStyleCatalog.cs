#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Ten numbered FullWorld concept layouts (0–9) — delegates to <see cref="FullWorldConceptLayoutCatalog"/>.</summary>
    public static class FullWorldGenerationStyleCatalog
    {
        public const int StyleCount = FullWorldConceptLayoutCatalog.ConceptCount;

        public static string GetDisplayName(int index) =>
            FullWorldConceptLayoutCatalog.GetDisplayName(index);

        public static string GetStyleId(int index) =>
            FullWorldConceptLayoutCatalog.GetStyleId(index);

        public static void ApplyByIndex(WorldGenerationRequest request, int index) =>
            FullWorldConceptLayoutCatalog.ApplyByIndex(request, index);
    }
}
#endif
