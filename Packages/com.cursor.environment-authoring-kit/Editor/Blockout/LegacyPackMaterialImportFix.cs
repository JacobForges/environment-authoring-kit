#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.World;
using UnityEditor;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Thin preprocess hook — delegates to HubModelImportRepairUtility for legacy pack material settings.
    /// Postprocess fixes run in HubModelImportRepairPostprocessor.
    /// </summary>
    sealed class LegacyPackMaterialImportFix : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetImporter is ModelImporter importer)
                HubModelImportRepairUtility.ApplyPreprocessSettings(assetPath, importer);
        }
    }
}
#endif
