using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    public class ContentLayoutPropAnchor : MonoBehaviour
    {
        [SerializeField] string propId = "";

        public string PropId => propId;

        public void SetPropId(string id) => propId = id ?? "";
    }
}
