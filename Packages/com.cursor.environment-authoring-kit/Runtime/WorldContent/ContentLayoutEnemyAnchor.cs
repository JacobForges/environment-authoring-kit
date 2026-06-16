using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Editor/runtime anchor for a brief enemy spawner route.</summary>
    public class ContentLayoutEnemyAnchor : MonoBehaviour
    {
        [SerializeField] string enemyId = "";

        public string EnemyId => enemyId;

        public void SetEnemyId(string id) => enemyId = id ?? "";
    }
}
