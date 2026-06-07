#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Scatters L01–L10 landmark proxies (CC0 item meshes) across biomes.</summary>
    public static class WorldLandmarkSpawnDirector
    {
        public const string RootName = "WorldLandmarks";

        static readonly (string id, WorldSurfaceBiomeId biome, float angle, float radius, float scale)[] Placements =
        {
            ("L01", WorldSurfaceBiomeId.PeakStone, 0f, 40f, 3.5f),
            ("L02", WorldSurfaceBiomeId.PlayKarst, 90f, 35f, 2.2f),
            ("L03", WorldSurfaceBiomeId.FoothillGreen, 200f, 75f, 2.8f),
            ("L04", WorldSurfaceBiomeId.FoothillGreen, 320f, 90f, 2.4f),
            ("L05", WorldSurfaceBiomeId.HorizonMist, 45f, 140f, 2f),
            ("L06", WorldSurfaceBiomeId.PeakStone, 160f, 110f, 2.5f),
            ("L07", WorldSurfaceBiomeId.AnnexLabyrinth, 270f, 55f, 2.2f),
            ("L08", WorldSurfaceBiomeId.PlayKarst, 300f, 22f, 1.8f),
            ("L09", WorldSurfaceBiomeId.HorizonMist, 210f, 155f, 2f),
            ("L10", WorldSurfaceBiomeId.AnnexLabyrinth, 120f, 48f, 1.6f),
        };

        public static void Scatter(Terrain mainTerrain, WorldGenerationRequest request)
        {
            if (mainTerrain == null || request == null)
                return;
            if (request.ContentTier == WorldBuildContentTier.Light)
                return;

            var existing = GameObject.Find(RootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(RootName, out existing, "world landmarks scatter"))
                return;

            var root = new GameObject(RootName);
            if (!WorldNpcLayoutAuthor.TryPlayCenterPublic(mainTerrain, out var center))
                center = mainTerrain.transform.position + mainTerrain.terrainData.size * 0.5f;

            foreach (var p in Placements)
            {
                var rad = p.angle * Mathf.Deg2Rad;
                var pos = center + new Vector3(Mathf.Cos(rad) * p.radius, 0f, Mathf.Sin(rad) * p.radius);
                pos = SnapToTerrain(mainTerrain, pos);

                var prefab = Cc0ContentImportUtility.LoadItemPrefab(p.id);
                GameObject go;
                if (prefab != null)
                {
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    go.name = $"Landmark_{p.id}";
                    go.transform.position = pos + Vector3.up * 0.2f;
                    go.transform.localScale = Vector3.one * p.scale;
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Landmark_{p.id}_Proxy";
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = pos + Vector3.up * p.scale * 0.5f;
                    go.transform.localScale = Vector3.one * p.scale;
                }

                var marker = go.GetComponent<WorldLandmarkMarker>();
                if (marker == null)
                    marker = go.AddComponent<WorldLandmarkMarker>();
                marker.Configure(p.id, p.biome);
            }

            UpgradeHollowTree(mainTerrain, request.Seed);
            Debug.Log($"[Surface] Landmarks placed — {Placements.Length} slots.", root);
        }

        static void UpgradeHollowTree(Terrain mainTerrain, int seed)
        {
            var hollow = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            if (hollow == null)
                return;

            if (hollow.GetComponent<HollowTitanLandmarkBuildData>() != null &&
                hollow.transform.Find("Interior/Floors") != null)
            {
                var builtPortal = hollow.transform.Find("BossStagePortal");
                if (builtPortal != null)
                {
                    if (builtPortal.GetComponent<WorldBossStagePortal>() == null)
                        builtPortal.gameObject.AddComponent<WorldBossStagePortal>();
                    WorldProjectTagSetup.TrySetTag(builtPortal.gameObject, HollowTitanLandmarkAuthor.PortalTag);
                }

                HollowTitanLandmarkTerrainSnap.SnapRootBaseToGround(
                    mainTerrain,
                    hollow.transform,
                    HollowTitanLandmarkTerrainSnap.ResolveBaseClearance());
                return;
            }

            var proxy = hollow.transform.Find("HollowTitan_Proxy");
            if (proxy != null)
                Object.DestroyImmediate(proxy.gameObject);

            var l01 = Cc0ContentImportUtility.LoadItemPrefab("L01");
            if (l01 != null)
            {
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(l01, hollow.transform);
                tree.name = "HollowTitan_L01";
                tree.transform.localPosition = Vector3.zero;
                tree.transform.localScale = Vector3.one * 4f;
            }

            var portal = hollow.transform.Find("BossStagePortal");
            if (portal != null)
            {
                if (portal.GetComponent<WorldBossStagePortal>() == null)
                    portal.gameObject.AddComponent<WorldBossStagePortal>();
                WorldProjectTagSetup.TrySetTag(portal.gameObject, HollowTitanLandmarkAuthor.PortalTag);
            }
        }

        static Vector3 SnapToTerrain(Terrain terrain, Vector3 world)
        {
            if (terrain?.terrainData == null)
                return world;

            var local = world - terrain.transform.position;
            var td = terrain.terrainData;
            local.x = Mathf.Clamp(local.x, 0f, td.size.x);
            local.z = Mathf.Clamp(local.z, 0f, td.size.z);
            var h = td.GetInterpolatedHeight(local.x / td.size.x, local.z / td.size.z);
            return terrain.transform.position + new Vector3(local.x, h, local.z);
        }
    }
}
#endif
