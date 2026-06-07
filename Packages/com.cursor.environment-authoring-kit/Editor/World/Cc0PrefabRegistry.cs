#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    public sealed class Cc0PrefabRegistry : ScriptableObject
    {
        public List<Cc0PrefabEntry> entries = new();
    }

    [System.Serializable]
    public struct Cc0PrefabEntry
    {
        public string slot;
        public GameObject prefab;
    }
}
#endif
