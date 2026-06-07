using System;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Scatter
{
    static class ScatterPlacer
    {
        public static GameObject Place(
            ScatterProfile profile,
            ScatterProfile.ScatterEntry entry,
            GameObject prefab,
            Vector3 position,
            Vector3 normal,
            Transform parent,
            System.Random rng,
            string undoLabel)
        {
            GameObject instance;
            if (prefab != null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else if (profile.usePlaceholderPrimitives)
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                instance.name = "ScatterPlaceholder";
                var col = instance.GetComponent<Collider>();
                if (col != null)
                    UnityEngine.Object.DestroyImmediate(col);
            }
            else
            {
                return null;
            }

            Undo.RegisterCreatedObjectUndo(instance, undoLabel);
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;

            var scale = entry != null
                ? (float)(entry.scaleRange.x + rng.NextDouble() * (entry.scaleRange.y - entry.scaleRange.x))
                : (float)(0.8 + rng.NextDouble() * 0.4);

            var align = entry == null || entry.alignToSurfaceNormal;
            if (align)
            {
                instance.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
                if (entry == null || entry.randomYRotation)
                    instance.transform.Rotate(Vector3.up, (float)rng.NextDouble() * 360f, Space.Self);
            }
            else if (entry == null || entry.randomYRotation)
            {
                instance.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            }

            instance.transform.localScale = Vector3.one * scale;
            return instance;
        }

        public static void ScatterOverTerrain(
            ScatterProfile profile,
            UnityEngine.Terrain terrain,
            Transform parent,
            int seed,
            float densityMultiplier = 1f)
        {
            if (profile == null || terrain == null)
                return;

            var rng = new System.Random(seed);
            var data = terrain.terrainData;
            var size = data.size;
            var spacing = Mathf.Max(1.5f, 1f / Mathf.Max(0.001f, profile.densityPerSquareMeter * densityMultiplier));
            var scatterRoot = EnvironmentSceneUtility.GetOrCreateChild(parent, "Scatter");

            for (var x = spacing * 0.5f; x < size.x; x += spacing)
            {
                for (var z = spacing * 0.5f; z < size.z; z += spacing)
                {
                    var jitterX = (float)(rng.NextDouble() - 0.5) * spacing;
                    var jitterZ = (float)(rng.NextDouble() - 0.5) * spacing;
                    var worldX = terrain.transform.position.x + x + jitterX;
                    var worldZ = terrain.transform.position.z + z + jitterZ;
                    var normX = (worldX - terrain.transform.position.x) / size.x;
                    var normZ = (worldZ - terrain.transform.position.z) / size.z;
                    if (normX < 0f || normX > 1f || normZ < 0f || normZ > 1f)
                        continue;

                    var height = data.GetInterpolatedHeight(normX, normZ);
                    var normal = data.GetInterpolatedNormal(normX, normZ);
                    var pos = new Vector3(worldX, terrain.transform.position.y + height, worldZ);

                    if (!ScatterUtility.SlopeOk(normal, profile) || !ScatterUtility.HeightOk(pos, profile))
                        continue;

                    if (rng.NextDouble() > 0.65)
                        continue;

                    var prefab = profile.PickWeighted(rng);
                    profile.TryGetEntryForPrefab(prefab, out var entry);
                    Place(profile, entry, prefab, pos, normal, scatterRoot, rng, "Scatter Generate");
                }
            }
        }
    }
}
