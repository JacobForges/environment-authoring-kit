using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Editor-placed NPC anchor; gameplay systems read <see cref="npcId"/>.</summary>
    public class ContentLayoutNpcMarker : MonoBehaviour
    {
        [SerializeField] string npcId = "";

        public string NpcId => npcId;

        public void SetNpcId(string id) => npcId = id ?? "";
    }
}
