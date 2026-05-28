using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    [CreateAssetMenu(fileName = "TerrainDressingPreset", menuName = "Environment Kit/Terrain Dressing Preset")]
    public sealed class TerrainDressingPreset : ScriptableObject
    {
        public Color groundTint = new(0.35f, 0.55f, 0.28f);
        public Color secondaryTint = new(0.45f, 0.42f, 0.35f);
        public float detailDensity = 0.4f;
        public Texture2D heightmapStamp;
        public float heightmapStrength = 0.35f;
    }
}
