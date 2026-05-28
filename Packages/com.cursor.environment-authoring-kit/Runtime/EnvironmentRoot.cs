using UnityEngine;

namespace EnvironmentAuthoringKit
{
    /// <summary>
    /// Parent marker for all kit-generated environment content.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnvironmentRoot : MonoBehaviour
    {
        public const string DefaultName = "EnvironmentRoot";

        [SerializeField] string generatedDescription;
        [SerializeField] int generationSeed;

        public string GeneratedDescription => generatedDescription;
        public int GenerationSeed => generationSeed;

        public void SetGenerationInfo(string description, int seed)
        {
            generatedDescription = description ?? string.Empty;
            generationSeed = seed;
        }
    }
}
