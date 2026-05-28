using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    internal static class CavePrefabScatter
    {
        public static void PlaceRandomProp(
            Transform propsRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos,
            float scaleMul = 1f)
        {
            var roll = rng.NextDouble();
            GameObject prefab;
            if (roll < 0.32 && catalog.Mushrooms.Count > 0)
                prefab = catalog.Pick(catalog.Mushrooms, rng);
            else if (roll < 0.52 && catalog.Crystals.Count > 0)
                prefab = catalog.Pick(catalog.Crystals, rng);
            else if (roll < 0.68 && catalog.GlowProps.Count > 0)
                prefab = catalog.Pick(catalog.GlowProps, rng);
            else if (roll < 0.82 && catalog.Artifacts.Count > 0)
                prefab = catalog.Pick(catalog.Artifacts, rng);
            else if (catalog.MossProps.Count > 0)
                prefab = catalog.Pick(catalog.MossProps, rng);
            else if (catalog.Rockfalls.Count > 0)
                prefab = catalog.Pick(catalog.Rockfalls, rng);
            else
                return;

            var scale = Vector3.one * (0.55f + (float)rng.NextDouble() * 0.35f) * scaleMul;
            PlaceModule(propsRoot, prefab, localPos,
                Quaternion.Euler(0f, (float)(rng.NextDouble() * 360), 0f), scale, prefab.name, false);
        }

        public static void PlaceMinableRock(
            Transform parent,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos)
        {
            PlaceModule(parent, catalog.Pick(catalog.Rockfalls, rng), localPos,
                Quaternion.Euler(0f, (float)(rng.NextDouble() * 360), 0f),
                Vector3.one, "SM_Rockfall", true);
        }

        public static bool PlaceModule(
            Transform parent,
            GameObject prefab,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 scale,
            string label,
            bool minable)
        {
            if (prefab == null)
                return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (instance == null)
                return false;

            // Surface vegetation passes can place hundreds/thousands of modules in one run.
            // Registering each one in Undo can overflow Unity's undo stack.
            var suppressUndo = !string.IsNullOrEmpty(label) &&
                               label.StartsWith("Surface_", System.StringComparison.Ordinal);
            if (!suppressUndo)
                CaveEditorUndo.RegisterCreated(instance, "Cave Piece");
            instance.name = $"{prefab.name} [{label}]";
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = localRot;
            instance.transform.localScale = scale;
            CaveSceneMaterialRepair.ApplyModuleMaterials(instance, scale);
            CaveBuildLiveSceneFeedback.NotifyPlaced(instance, label);

            if (minable)
            {
                if (instance.GetComponent<MinableRock>() == null)
                    instance.AddComponent<MinableRock>();
                instance.tag = CaveTags.Minable;
            }
            else
            {
                // Decorative cave modules should not keep physics colliders; invisible collision volumes
                // hurt playability and performance.
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null)
                        Object.DestroyImmediate(col);
                }
            }

            return true;
        }
    }
}
