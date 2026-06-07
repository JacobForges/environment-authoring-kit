#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Light prop markers on surface tiles (AAA / Standard density).</summary>
    public static class WorldTilePropScatter
    {
        public const string RootName = "WorldTileProps";

        static readonly string[] PropItemIds = { "M01", "M02", "M03", "PL01", "PL02", "PL03" };

        public static void Scatter(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null)
                return;
            if (request.ContentTier == WorldBuildContentTier.Light)
                return;

            var existing = GameObject.Find(RootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(RootName, out existing, "world tile props"))
                return;

            var root = new GameObject(RootName);
            var rng = new System.Random(request.Seed + 0x50524F50); // PROP
            var density = request.ContentTier == WorldBuildContentTier.Aaa ? 0.35f : 0.18f;
            var placed = 0;

            foreach (var tile in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (tile?.terrainData == null)
                    continue;

                if (rng.NextDouble() > density)
                    continue;

                if (!WorldLootSpawnDirector.TrySamplePointPublic(tile, rng, out var world))
                    continue;

                var id = PropItemIds[rng.Next(PropItemIds.Length)];
                var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
                GameObject go;
                if (prefab != null)
                {
                    go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    go.transform.position = world + Vector3.up * 0.15f;
                    go.transform.localScale = Vector3.one * (0.35f + (float)rng.NextDouble() * 0.25f);
                    go.transform.rotation = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = world + Vector3.up * 0.3f;
                    go.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);
                }

                go.name = $"Prop_{id}_{placed:D3}";
                placed++;
            }

            Debug.Log($"[Surface] Tile props — {placed} placed.", root);
        }
    }
}
#endif
