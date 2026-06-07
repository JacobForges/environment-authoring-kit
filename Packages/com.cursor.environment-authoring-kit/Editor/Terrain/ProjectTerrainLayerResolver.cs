#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    /// <summary>Reuses terrain layer assets already in the project instead of flat color tints.</summary>
    public static class ProjectTerrainLayerResolver
    {
        static TerrainLayer[] _cached;

        public static TerrainLayer[] TryResolveTerrainLayers(int maxLayers = 4)
        {
            if (_cached != null && _cached.Length > 0)
                return _cached;

            var scored = new List<(TerrainLayer layer, int score)>();
            foreach (var guid in AssetDatabase.FindAssets("t:TerrainLayer"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path.Contains("/Editor/", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null || layer.diffuseTexture == null)
                    continue;

                var blob = (path + " " + layer.name).ToLowerInvariant();
                var score = 10;
                if (blob.Contains("grass") || blob.Contains("meadow") || blob.Contains("ground"))
                    score += 40;
                if (blob.Contains("dirt") || blob.Contains("soil") || blob.Contains("sand"))
                    score += 25;
                if (blob.Contains("rock") || blob.Contains("cliff") || blob.Contains("stone"))
                    score += 15;
                if (blob.Contains("forest") || blob.Contains("terrain"))
                    score += 10;

                scored.Add((layer, score));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var take = Mathf.Min(maxLayers, scored.Count);
            if (take == 0)
                return null;

            _cached = new TerrainLayer[take];
            for (var i = 0; i < take; i++)
                _cached[i] = scored[i].layer;

            return _cached;
        }

        /// <summary>Rock/cliff layers for peak tiles — prefers project rock textures over grass/sand.</summary>
        public static TerrainLayer[] TryResolveMountainRockLayers(int maxLayers = 3)
        {
            var scored = new List<(TerrainLayer layer, int score)>();
            foreach (var guid in AssetDatabase.FindAssets("t:TerrainLayer"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path.Contains("/Editor/", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null || layer.diffuseTexture == null)
                    continue;

                var blob = (path + " " + layer.name).ToLowerInvariant();
                var score = 5;
                if (blob.Contains("rock") || blob.Contains("cliff") || blob.Contains("stone") || blob.Contains("granite"))
                    score += 80;
                if (blob.Contains("rubble") || blob.Contains("mountain") || blob.Contains("boulder"))
                    score += 45;
                if (blob.Contains("grass") || blob.Contains("meadow") || blob.Contains("sand"))
                    score -= 25;

                scored.Add((layer, score));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var take = Mathf.Min(maxLayers, scored.Count);
            if (take == 0)
                return null;

            var layers = new TerrainLayer[take];
            for (var i = 0; i < take; i++)
                layers[i] = scored[i].layer;

            return layers;
        }

        public static void ClearCache() => _cached = null;
    }
}
#endif
