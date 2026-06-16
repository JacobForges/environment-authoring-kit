using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>CC0 prefab picks for content-layout NPCs, enemies, and props (agent + runtime).</summary>
    [CreateAssetMenu(fileName = "ContentLayoutVisualCatalog", menuName = "Environment Kit/Content Layout Visual Catalog")]
    public class ContentLayoutVisualCatalog : ScriptableObject
    {
        [Serializable]
        public class VisualEntry
        {
            public string id = "";
            public GameObject prefab;
            [Tooltip("Uniform scale applied after instantiate.")]
            public float scale = 1f;
            public float feetClearanceM = 0.08f;
        }

        [Header("NPCs (id → prefab)")]
        public List<VisualEntry> npcs = new();

        [Header("Enemy spawners (id → marker prefab, optional)")]
        public List<VisualEntry> enemies = new();

        [Header("Props (id → prefab)")]
        public List<VisualEntry> props = new();

        [Header("Fallback props by textureHint substring")]
        public List<TextureHintEntry> propHints = new();

        [Serializable]
        public class TextureHintEntry
        {
            public string hintContains = "wood";
            public GameObject prefab;
            public float scale = 1f;
        }

        Dictionary<string, VisualEntry> _npcMap;
        Dictionary<string, VisualEntry> _enemyMap;
        Dictionary<string, VisualEntry> _propMap;

        void OnEnable() => RebuildMaps();

        public void RebuildMaps()
        {
            _npcMap = BuildMap(npcs);
            _enemyMap = BuildMap(enemies);
            _propMap = BuildMap(props);
        }

        static Dictionary<string, VisualEntry> BuildMap(List<VisualEntry> list)
        {
            var map = new Dictionary<string, VisualEntry>(StringComparer.OrdinalIgnoreCase);
            if (list == null)
                return map;
            foreach (var e in list)
            {
                if (e == null || string.IsNullOrEmpty(e.id))
                    continue;
                map[e.id] = e;
            }
            return map;
        }

        public bool TryNpc(string id, out VisualEntry entry) => Try(_npcMap, id, out entry);
        public bool TryEnemy(string id, out VisualEntry entry) => Try(_enemyMap, id, out entry);
        public bool TryProp(string id, out VisualEntry entry) => Try(_propMap, id, out entry);

        public GameObject ResolveProp(string propId, string textureHint)
        {
            if (TryProp(propId, out var e) && e.prefab != null)
                return e.prefab;
            var hint = (textureHint ?? "").ToLowerInvariant();
            foreach (var h in propHints)
            {
                if (h?.prefab == null || string.IsNullOrEmpty(h.hintContains))
                    continue;
                if (hint.Contains(h.hintContains.ToLowerInvariant()))
                    return h.prefab;
            }
            return null;
        }

        static bool Try(Dictionary<string, VisualEntry> map, string id, out VisualEntry entry)
        {
            entry = null;
            if (map == null || string.IsNullOrEmpty(id))
                return false;
            return map.TryGetValue(id, out entry) && entry?.prefab != null;
        }

        static ContentLayoutVisualCatalog _cached;

        public static void ClearCache() => _cached = null;

        public static ContentLayoutVisualCatalog Load()
        {
            if (_cached != null)
                return _cached;
            _cached = Resources.Load<ContentLayoutVisualCatalog>("ContentLayoutVisualCatalog");
            _cached?.RebuildMaps();
            return _cached;
        }
    }
}
