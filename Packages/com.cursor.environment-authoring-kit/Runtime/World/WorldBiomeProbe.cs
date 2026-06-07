using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Resolves current surface biome from WorldBiome markers or Chebyshev ring.</summary>
    public static class WorldBiomeProbe
    {
        static System.Collections.Generic.List<(Vector3 pos, WorldSurfaceBiomeId id)> _markers;
        static float _cacheTime;

        public static WorldSurfaceBiomeId ResolveAt(Vector3 worldPos, Terrain fallbackTerrain = null)
        {
            RefreshCacheIfNeeded();
            var best = float.MaxValue;
            var biome = WorldSurfaceBiomeId.PlayKarst;
            if (_markers != null)
            {
                foreach (var (pos, id) in _markers)
                {
                    var dist = Vector3.SqrMagnitude(pos - worldPos);
                    if (dist >= best)
                        continue;
                    best = dist;
                    biome = id;
                }
            }

            if (best < float.MaxValue)
                return biome;

            if (fallbackTerrain != null &&
                SurfaceWorldBiomeMath.TryBiomeFromTerrain(fallbackTerrain, worldPos, out var fromTerrain))
                return fromTerrain;

            return WorldSurfaceBiomeId.PlayKarst;
        }

        static void RefreshCacheIfNeeded()
        {
            if (_markers != null && Time.unscaledTime - _cacheTime < 8f)
                return;

            _markers ??= new System.Collections.Generic.List<(Vector3, WorldSurfaceBiomeId)>();
            _markers.Clear();
            _cacheTime = Time.unscaledTime;

            var roots = Object.FindObjectsByType<Transform>();
            foreach (var t in roots)
            {
                if (t == null || !t.name.StartsWith(SurfaceWorldBiomeMarker.MarkerPrefix))
                    continue;
                if (!SurfaceWorldBiomeMarker.TryParseBiome(t.name, out var id))
                    continue;
                _markers.Add((t.position, id));
            }
        }
    }

    /// <summary>Runtime marker created by editor biome author (name prefix only).</summary>
    public static class SurfaceWorldBiomeMarker
    {
        public const string MarkerPrefix = "WorldBiome_";

        public static bool TryParseBiome(string objectName, out WorldSurfaceBiomeId biome)
        {
            biome = WorldSurfaceBiomeId.PlayKarst;
            if (string.IsNullOrEmpty(objectName) || !objectName.StartsWith(MarkerPrefix))
                return false;

            var rest = objectName.Substring(MarkerPrefix.Length);
            var underscore = rest.IndexOf('_');
            if (underscore <= 0)
                return false;

            var token = rest.Substring(0, underscore);
            return System.Enum.TryParse(token, out biome);
        }
    }

    static class SurfaceWorldBiomeMath
    {
        public static bool TryBiomeFromTerrain(Terrain terrain, Vector3 worldPos, out WorldSurfaceBiomeId biome)
        {
            biome = WorldSurfaceBiomeId.PlayKarst;
            if (terrain?.terrainData == null)
                return false;

            var local = worldPos - terrain.transform.position;
            var size = terrain.terrainData.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                return false;

            // Without tile name we default play karst — markers preferred.
            return true;
        }
    }
}
