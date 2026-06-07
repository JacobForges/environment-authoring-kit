using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    public enum HollowTitanSpawnKind
    {
        Enemy = 0,
        Loot = 1,
        Patrol = 2,
    }

    /// <summary>Landmark-scoped spawn marker — never read by world-wide spawn directors.</summary>
    public sealed class HollowTitanLandmarkSpawnPoint : MonoBehaviour
    {
        [SerializeField] HollowTitanSpawnKind kind = HollowTitanSpawnKind.Enemy;
        [SerializeField] int floorIndex;
        [SerializeField] string definitionId = string.Empty;
        [SerializeField] int quantity = 1;

        public HollowTitanSpawnKind Kind => kind;
        public int FloorIndex => floorIndex;
        public string DefinitionId => definitionId;
        public int Quantity => quantity;

        public void Configure(HollowTitanSpawnKind spawnKind, int floor, string defId, int qty = 1)
        {
            kind = spawnKind;
            floorIndex = floor;
            definitionId = defId ?? string.Empty;
            quantity = Mathf.Max(1, qty);
        }
    }
}
