#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Extra labyrinth annex props + dim atmosphere marker.</summary>
    public static class WorldAnnexVisualPass
    {
        public const string RootName = "WorldAnnexVisuals";

        public static void Apply(Terrain mainTerrain, int seed)
        {
            if (mainTerrain == null)
                return;

            var existing = GameObject.Find(RootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(RootName, out existing, "annex visuals"))
                return;

            var root = new GameObject(RootName);
            var rng = new System.Random(seed + 0x414E4E58); // ANNX
            var placed = 0;

            foreach (var tile in SurfaceMountainSouthAnnex.CollectSouthAnnexTiles(mainTerrain))
            {
                if (tile?.terrainData == null)
                    continue;

                for (var i = 0; i < 3; i++)
                {
                    if (!WorldLootSpawnDirector.TrySamplePointPublic(tile, rng, out var world))
                        continue;

                    var id = i switch { 0 => "L07", 1 => "K02", _ => "G-C03" };
                    var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
                    GameObject go;
                    if (prefab != null)
                    {
                        go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                        go.transform.position = world + Vector3.up * 0.2f;
                        go.transform.localScale = Vector3.one * 1.4f;
                    }
                    else
                    {
                        go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        go.transform.SetParent(root.transform, false);
                        go.transform.position = world + Vector3.up * 0.5f;
                        go.transform.localScale = Vector3.one * 1.2f;
                    }

                    go.name = $"Annex_{id}_{placed}";
                    placed++;
                }
            }

            Debug.Log($"[Surface] Annex visual pass — {placed} props.", root);
        }
    }
}
#endif
