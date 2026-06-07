using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Records which project asset path was used when this cave piece was placed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CavePrefabSource : MonoBehaviour
    {
        [SerializeField] string assetPath;

        public string AssetPath => assetPath;

        public void SetAssetPath(string path)
        {
            assetPath = path ?? string.Empty;
            name = string.IsNullOrEmpty(assetPath)
                ? name
                : $"{System.IO.Path.GetFileNameWithoutExtension(assetPath)} [asset_reference: {assetPath}]";
        }
    }
}
