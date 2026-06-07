using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Runtime lookup for manifest item definitions.</summary>
    public static class WorldItemCatalog
    {
        const string ResourcePath = "WorldItemCatalog";

        static WorldItemCatalogAsset _asset;
        static Dictionary<string, WorldItemDefinitionEntry> _byId;
        static bool _warnedMissing;

        public static bool TryGet(string id, out WorldItemDefinitionEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            EnsureLoaded();
            return _byId != null && _byId.TryGetValue(id.Trim(), out entry) && entry != null;
        }

        public static WorldItemDefinitionEntry GetOrDefault(string id)
        {
            if (TryGet(id, out var entry))
                return entry;

            return new WorldItemDefinitionEntry
            {
                id = id ?? string.Empty,
                displayName = id ?? "Unknown",
                stackMax = 99,
            };
        }

        static void EnsureLoaded()
        {
            if (_byId != null)
                return;

            _byId = new Dictionary<string, WorldItemDefinitionEntry>(StringComparer.Ordinal);
            _asset = Resources.Load<WorldItemCatalogAsset>(ResourcePath);
            if (_asset?.entries == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning(
                        "[WorldItemCatalog] Missing Resources/WorldItemCatalog — run " +
                        "Window/Environment Kit/World/Build Item Catalog From Manifest.");
                }

                return;
            }

            foreach (var e in _asset.entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.id))
                    continue;
                _byId[e.id] = e;
            }
        }

        public static void ReloadForEditor(WorldItemCatalogAsset asset)
        {
            _asset = asset;
            _byId = null;
            _warnedMissing = false;
            if (asset != null)
                EnsureLoaded();
        }
    }
}
