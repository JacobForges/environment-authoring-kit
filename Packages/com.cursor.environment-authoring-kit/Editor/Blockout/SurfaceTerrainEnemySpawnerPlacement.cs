#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class SurfaceTerrainEnemySpawnerPlacement
    {
        public const string SpawnerObjectName = "SurfaceTerrainEnemies";

        public static int EnsureOnSurface(Transform surfaceRoot, WorldGenerationRequest request, GameObject enemyPrefab)
        {
            if (surfaceRoot == null)
                return 0;

            var existing = surfaceRoot.Find(SpawnerObjectName);
            GameObject host;
            if (existing != null)
                host = existing.gameObject;
            else
            {
                host = new GameObject(SpawnerObjectName);
                host.transform.SetParent(surfaceRoot, false);
                Undo.RegisterCreatedObjectUndo(host, "Surface enemy spawner");
            }

            var spawner = host.GetComponent<SurfaceTerrainEnemySpawner>();
            if (spawner == null)
                spawner = host.AddComponent<SurfaceTerrainEnemySpawner>();

            spawner.enemyPrefab = enemyPrefab;
            spawner.spawnSeed = request != null ? request.Seed + 8803 : UnityObjectCompat.ReferenceId(host);
            if (request != null)
            {
                spawner.spawnCount = Mathf.Clamp(8 + request.Seed % 8, 8, 18);
                spawner.maxRadiusFromCenter = request.SurfaceExtentMeters > 10f
                    ? request.SurfaceExtentMeters * 0.92f
                    : spawner.maxRadiusFromCenter;
            }

            spawner.spawnOnStart = true;
            EditorUtility.SetDirty(host);
            return spawner.spawnCount;
        }
    }
}
#endif
