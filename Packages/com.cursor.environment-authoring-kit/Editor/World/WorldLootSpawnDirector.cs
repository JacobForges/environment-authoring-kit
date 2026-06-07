#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Plan v4 — biome-scoped pickups with CC0 item meshes when available.</summary>
    public static class WorldLootSpawnDirector
    {
        public const string RootName = "WorldLootPickups";

        public static void Scatter(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null)
                return;

            var existing = GameObject.Find(RootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(RootName, out existing, "world loot scatter"))
                return;

            var root = new GameObject(RootName);
            var density = request.ContentTier switch
            {
                WorldBuildContentTier.Aaa => 1f,
                WorldBuildContentTier.Standard => 0.45f,
                _ => 0.25f,
            };

            var rng = new System.Random(request.Seed + 0x4C4F4F54); // "LOOT"
            var placed = 0;
            foreach (var tile in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (tile?.terrainData == null ||
                    !SurfaceTerrainTileExpansion.TryParseTileOffset(tile.name, out var off))
                    continue;

                if (rng.NextDouble() > density)
                    continue;

                if (!TrySamplePoint(tile, rng, out var world))
                    continue;

                var defId = PickDefinitionForBiome(
                    SurfaceWorldBiomeAuthor.ResolveBiomeForTileOffset(off, off.y <= -1),
                    rng);
                var pickup = CreatePickup(defId, world);
                pickup.transform.SetParent(root.transform, false);
                pickup.AddComponent<WorldItemPickup>().Configure(
                    defId,
                    defId,
                    SurfaceWorldBiomeAuthor.ResolveBiomeForTileOffset(off, off.y <= -1));
                placed++;
            }

            Debug.Log($"[Surface] World loot scatter — {placed} pickup(s), tier {request.ContentTier}.", root);
        }

        static GameObject CreatePickup(string defId, Vector3 world)
        {
            var itemPrefab = Cc0ContentImportUtility.LoadItemPrefab(defId);
            if (itemPrefab != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab);
                go.name = $"Pickup_{defId}";
                go.transform.position = world + Vector3.up * 0.35f;
                go.transform.localScale = Vector3.one * 0.5f;
                EnsureTriggerCollider(go);
                return go;
            }

            var pickup = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pickup.name = $"Pickup_{defId}";
            pickup.transform.position = world + Vector3.up * 0.5f;
            pickup.transform.localScale = Vector3.one * 0.6f;
            var col = pickup.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
            return pickup;
        }

        static void EnsureTriggerCollider(GameObject go)
        {
            var col = go.GetComponentInChildren<Collider>();
            if (col == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one * 0.8f;
                box.center = Vector3.up * 0.25f;
            }
            else
            {
                col.isTrigger = true;
            }
        }

        static string PickDefinitionForBiome(WorldSurfaceBiomeId biome, System.Random rng)
        {
            return biome switch
            {
                WorldSurfaceBiomeId.PlayKarst => rng.NextDouble() < 0.5 ? "C01" : "P01",
                WorldSurfaceBiomeId.FoothillGreen => rng.NextDouble() < 0.5 ? "C02" : "G-T01",
                WorldSurfaceBiomeId.PeakStone => rng.NextDouble() < 0.5 ? "C03" : "G-G01",
                WorldSurfaceBiomeId.HorizonMist => "G-C05",
                WorldSurfaceBiomeId.AnnexLabyrinth => "K01",
                _ => "C01",
            };
        }

        public static bool TrySamplePointPublic(Terrain tile, System.Random rng, out Vector3 world) =>
            TrySamplePoint(tile, rng, out world);

        static bool TrySamplePoint(Terrain tile, System.Random rng, out Vector3 world)
        {
            world = default;
            var td = tile.terrainData;
            if (td == null)
                return false;

            for (var i = 0; i < 8; i++)
            {
                var lx = (float)rng.NextDouble() * td.size.x;
                var lz = (float)rng.NextDouble() * td.size.z;
                var steep = td.GetSteepness(lx / td.size.x, lz / td.size.z);
                if (steep > 32f)
                    continue;

                var nx = lx / td.size.x;
                var nz = lz / td.size.z;
                var h = td.GetInterpolatedHeight(nx, nz);
                world = tile.transform.position + new Vector3(lx, h, lz);
                return true;
            }

            return false;
        }
    }
}
#endif
