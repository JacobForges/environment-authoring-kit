using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Builds ContentLayout_Authored hierarchy from a brief (editor + runtime).</summary>
    public static class ContentLayoutSceneGraph
    {
        public const string RootName = "ContentLayout_Authored";

        public static Transform EnsureRoot()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null)
                return existing.transform;

            var go = new GameObject(RootName);
            return go.transform;
        }

        public static PlacementStats Build(Transform root, ContentLayoutBriefFile file, bool clearExisting)
        {
            if (root == null || file?.contentPlan == null)
                return default;

            if (clearExisting)
                ClearChildren(root);

            ContentLayoutWorldPlacement.InvalidateCache();
            var catalog = LoadCatalog();

            var plan = file.contentPlan;
            var npcRoot = EnsureChild(root, "NPCs");
            var enemyRoot = EnsureChild(root, "Enemies");
            var propRoot = EnsureChild(root, "Props");

            var stats = new PlacementStats();

            if (plan.npcs != null)
            {
                foreach (var n in plan.npcs)
                {
                    if (n?.worldPosition == null || string.IsNullOrEmpty(n.id))
                        continue;
                    CreateNpcVisual(npcRoot, n, catalog);
                    stats.Npcs++;
                }
            }

            if (plan.enemies != null)
            {
                foreach (var e in plan.enemies)
                {
                    if (e?.worldPosition == null || string.IsNullOrEmpty(e.id))
                        continue;
                    CreateEnemyRoute(enemyRoot, e, catalog);
                    stats.Enemies++;
                }
            }

            if (plan.props != null)
            {
                foreach (var p in plan.props)
                {
                    if (p?.worldPosition == null || string.IsNullOrEmpty(p.id))
                        continue;
                    CreateProp(propRoot, p, catalog);
                    stats.Props++;
                }
            }

            return stats;
        }

        static GameObject CreateNpcVisual(Transform parent, ContentNpcDef n, ContentLayoutVisualCatalog catalog)
        {
            var go = new GameObject(Sanitize(n.id));
            go.transform.SetParent(parent, false);
            var pos = ContentLayoutWorldPlacement.ResolveNpcPosition(n.id, n.worldPosition.ToVector3(), n.rotationY, out var rotY);
            pos = ContentLayoutGroundSnap.SnapWorldPosition(pos, ContentLayoutGroundSnap.NpcFeetClearanceM);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotY, 0f);

            var marker = go.AddComponent<ContentLayoutNpcMarker>();
            marker.SetNpcId(n.id);

            ContentLayoutVisualSpawner.AttachNpcVisual(go.transform, n, catalog);
            ContentLayoutHumanoidFit.RemoveFallbackBody(go.transform);
            ContentLayoutGroundSnap.AlignVisualFeetToGround(go.transform, ContentLayoutGroundSnap.NpcFeetClearanceM);

            var col = go.GetComponent<CapsuleCollider>();
            if (col == null)
                col = go.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            ContentLayoutHumanoidFit.FitCapsuleCollider(go.transform, col);

            var label = CreateWorldLabel(go.transform, n.displayName ?? n.id);
            label.transform.localPosition = Vector3.up * 2.2f;
            return go;
        }

        static void CreateEnemyRoute(Transform parent, ContentEnemyDef e, ContentLayoutVisualCatalog catalog)
        {
            var go = new GameObject(Sanitize(e.id));
            go.transform.SetParent(parent, false);
            var pos = ContentLayoutWorldPlacement.ResolveEnemyPosition(e.id, e.worldPosition.ToVector3());
            pos = ContentLayoutGroundSnap.SnapWorldPosition(pos, ContentLayoutGroundSnap.SpawnerClearanceM);
            go.transform.position = pos;

            var anchor = go.AddComponent<ContentLayoutEnemyAnchor>();
            anchor.SetEnemyId(e.id);

            ContentLayoutVisualSpawner.AttachEnemyVisual(go.transform, e, catalog);

            if (ContentLayoutWorldPlacement.TryResolveEnemyPatrol(e.id, out var patrol))
            {
                for (var i = 0; i < patrol.Length; i++)
                {
                    var wp = new GameObject($"wp_{i}");
                    wp.transform.SetParent(go.transform, false);
                    wp.transform.position = ContentLayoutGroundSnap.SnapWorldPosition(patrol[i], ContentLayoutGroundSnap.SpawnerClearanceM);
                }
            }
            else if (e.patrolWaypoints != null)
            {
                for (var i = 0; i < e.patrolWaypoints.Length; i++)
                {
                    var wp = new GameObject($"wp_{i}");
                    wp.transform.SetParent(go.transform, false);
                    var wpPos = e.patrolWaypoints[i].ToVector3();
                    wp.transform.position = ContentLayoutGroundSnap.SnapWorldPosition(wpPos, ContentLayoutGroundSnap.SpawnerClearanceM);
                }
            }

            CreateWorldLabel(go.transform, e.displayName ?? e.id);
        }

        static void CreateProp(Transform parent, ContentPropDef p, ContentLayoutVisualCatalog catalog)
        {
            var isInvisibleBlocker = (p.id ?? "").StartsWith("blocker_")
                || (p.textureHint ?? "").Contains("invisible");
            GameObject go;
            if (isInvisibleBlocker)
            {
                go = new GameObject(Sanitize(p.id ?? p.label ?? "prop"));
                go.transform.SetParent(parent, false);
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.size = new Vector3(3f, 2.5f, 1f);
            }
            else
            {
                go = new GameObject(Sanitize(p.id ?? p.label ?? "prop"));
                go.transform.SetParent(parent, false);
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(1f, 1.5f, 0.5f);

                if (ContentLayoutVisualSpawner.AttachPropVisual(go.transform, p, catalog) == null)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.transform.SetParent(go.transform, false);
                    cube.transform.localScale = PropScale(p.textureHint);
                    Tint(cube, PropColor(p.textureHint));
                    DestroyCollider(cube.GetComponent<Collider>());
                }
            }

            var pos = ContentLayoutWorldPlacement.ResolvePropPosition(p.id ?? "", p.worldPosition.ToVector3());
            var elevated = pos.y >= 1f || IsElevatedPropHint(p.textureHint);
            pos = elevated
                ? ContentLayoutGroundSnap.SnapWorldPositionWithHeightOffset(pos, ContentLayoutGroundSnap.PropBaseClearanceM, pos.y)
                : ContentLayoutGroundSnap.SnapWorldPosition(pos, ContentLayoutGroundSnap.PropBaseClearanceM);
            go.transform.position = pos;
            if (!elevated)
                ContentLayoutGroundSnap.AlignVisualFeetToGround(go.transform, ContentLayoutGroundSnap.PropBaseClearanceM);

            var anchor = go.AddComponent<ContentLayoutPropAnchor>();
            anchor.SetPropId(p.id ?? go.name);

            if (!isInvisibleBlocker)
                CreateWorldLabel(go.transform, p.label ?? p.id);
        }

        static GameObject CreateWorldLabel(Transform parent, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text ?? "";
            tm.fontSize = 32;
            tm.characterSize = 0.04f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.92f, 0.95f, 1f, 0.95f);
            tm.alignment = TextAlignment.Center;
            return go;
        }

        static bool IsElevatedPropHint(string hint)
        {
            var h = (hint ?? "").ToLowerInvariant();
            return h.Contains("pennant") || h.Contains("banner") || h.Contains("arch");
        }

        static Color PropColor(string hint)
        {
            var h = (hint ?? "").ToLowerInvariant();
            if (h.Contains("slate")) return new Color(0.35f, 0.38f, 0.42f);
            if (h.Contains("cyan") || h.Contains("banner")) return new Color(0.2f, 0.65f, 0.75f);
            if (h.Contains("wood")) return new Color(0.45f, 0.32f, 0.22f);
            return new Color(0.5f, 0.45f, 0.4f);
        }

        static Vector3 PropScale(string hint)
        {
            var h = (hint ?? "").ToLowerInvariant();
            if (h.Contains("banner") || h.Contains("pennant"))
                return new Vector3(0.15f, 1.8f, 0.8f);
            if (h.Contains("arch"))
                return new Vector3(4f, 3f, 0.6f);
            return new Vector3(0.9f, 1.2f, 0.5f);
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            else
                mat.color = c;
            r.sharedMaterial = mat;
            }
        }

        static Transform EnsureChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null)
                return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
                DestroyObject(parent.GetChild(i).gameObject);
        }

        static void DestroyObject(GameObject go)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(go);
            else
#endif
                Object.Destroy(go);
        }

        static void DestroyCollider(Collider col)
        {
            if (col == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Object.DestroyImmediate(col);
            else
#endif
                Object.Destroy(col);
        }

        static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "node";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        static ContentLayoutVisualCatalog LoadCatalog()
        {
            ContentLayoutVisualCatalog.ClearCache();
            var catalog = ContentLayoutVisualCatalog.Load();
#if UNITY_EDITOR
            if (catalog == null)
            {
                catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<ContentLayoutVisualCatalog>(
                    "Assets/EnvironmentKit/Resources/ContentLayoutVisualCatalog.asset");
                catalog?.RebuildMaps();
            }
#endif
            return catalog;
        }

        public struct PlacementStats
        {
            public int Npcs;
            public int Enemies;
            public int Props;
        }
    }
}
