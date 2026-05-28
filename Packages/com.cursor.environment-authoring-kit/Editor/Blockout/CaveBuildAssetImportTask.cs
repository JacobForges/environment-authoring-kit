#if UNITY_EDITOR
namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>What to import from disk — never bulk-import all of Generated (JSON/MD spam).</summary>
    public enum CaveBuildAssetImportTask
    {
        /// <summary>Metadata write only — no AssetDatabase.ImportAsset.</summary>
        None = 0,
        MaterialsPack,
        StructurePrefabs,
        ScatterProps,
        ExportedPrefabs,
    }
}
#endif
