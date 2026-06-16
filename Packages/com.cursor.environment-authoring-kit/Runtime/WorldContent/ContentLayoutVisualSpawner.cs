using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    public static class ContentLayoutVisualSpawner
    {
        public static GameObject AttachNpcVisual(Transform anchor, ContentNpcDef def, ContentLayoutVisualCatalog catalog)
        {
            catalog ??= ContentLayoutVisualCatalog.Load();
            if (catalog == null || def == null || !catalog.TryNpc(def.id, out var entry))
                return CreateFallbackCapsule(anchor, RoleColor(def.role));

            return AttachPrefab(anchor, entry.prefab, entry.scale, entry.feetClearanceM);
        }

        public static GameObject AttachEnemyVisual(Transform anchor, ContentEnemyDef def, ContentLayoutVisualCatalog catalog)
        {
            catalog ??= ContentLayoutVisualCatalog.Load();
            if (catalog == null || def == null || !catalog.TryEnemy(def.id, out var entry))
                return null;

            return AttachPrefab(anchor, entry.prefab, entry.scale, entry.feetClearanceM);
        }

        public static GameObject AttachPropVisual(Transform anchor, ContentPropDef def, ContentLayoutVisualCatalog catalog)
        {
            catalog ??= ContentLayoutVisualCatalog.Load();
            if (catalog == null || def == null)
                return null;

            var prefab = catalog.ResolveProp(def.id, def.textureHint);
            if (prefab == null)
                return null;

            var scale = 1f;
            if (catalog.TryProp(def.id, out var entry))
                scale = entry.scale;

            return AttachPrefab(anchor, prefab, scale, ContentLayoutGroundSnap.PropBaseClearanceM);
        }

        static GameObject AttachPrefab(Transform anchor, GameObject prefab, float scale, float clearanceM)
        {
            if (anchor == null || prefab == null)
                return null;

            var instance = Object.Instantiate(prefab, anchor);
            instance.name = "Visual";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            ContentLayoutVisualMaterialFix.FixVisualMaterials(instance);
            EnvironmentAuthoringKit.World.Cc0RuntimePrefabResolver.PrepareSpawnedInstance(instance);

            var isProp = anchor.GetComponent<ContentLayoutPropAnchor>() != null;
            if (isProp)
                ContentLayoutVisualScaleFit.FitPropHeight(instance.transform, scale);
            else
                ContentLayoutVisualScaleFit.FitHumanoidHeight(instance.transform, scale);

            StripPhysicsFromVisual(instance);
            return instance;
        }

        static void StripPhysicsFromVisual(GameObject visual)
        {
            foreach (var col in visual.GetComponentsInChildren<Collider>())
            {
                if (col != null)
                    DestroyComponent(col);
            }
            foreach (var rb in visual.GetComponentsInChildren<Rigidbody>())
            {
                if (rb != null)
                    DestroyComponent(rb);
            }
        }

        static GameObject CreateFallbackCapsule(Transform anchor, Color color)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(anchor, false);
            body.transform.localPosition = Vector3.up;
            body.transform.localScale = new Vector3(0.9f, 1f, 0.9f);
            Tint(body, color);
            DestroyComponent(body.GetComponent<Collider>());
            return body;
        }

        static void DestroyComponent(Object obj)
        {
            if (obj == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }

        static Color RoleColor(string role)
        {
            switch ((role ?? "").ToLowerInvariant())
            {
                case "quest_giver": return new Color(0.35f, 0.55f, 0.95f);
                case "trainer": return new Color(0.95f, 0.75f, 0.25f);
                case "ambient": return new Color(0.55f, 0.75f, 0.55f);
                default: return new Color(0.65f, 0.65f, 0.7f);
            }
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null)
                return;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            else
                mat.color = c;
            r.sharedMaterial = mat;
        }
    }
}
