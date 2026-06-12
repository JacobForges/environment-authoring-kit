#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Morph/sculpt variants of kit characters for planner concept cards (NPC, enemy, player).
    /// Output: Assets/EnvironmentKit/Generated/PlannerCharacters/Prefabs/**
    /// </summary>
    public static class CaveBuildPlannerGeneratedCharacters
    {
        public const string Root = "Assets/EnvironmentKit/Generated/PlannerCharacters";
        public const string PrefabRoot = Root + "/Prefabs";
        public const string CardSculptRequestRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.request.json";
        public const string CardSculptDoneRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-sculpt.done.json";

        [Serializable]
        public sealed class AiColor
        {
            public float r = -1f;
            public float g = -1f;
            public float b = -1f;
        }

        [Serializable]
        public sealed class AiCharacterSculptSpec
        {
            public float heightScale = 1f;
            public float buildScale = 1f;
            public float shoulderScale = 1f;
            public float headScale = 1f;
            public float limbScale = 1f;
            public AiColor skinTint;
            public AiColor clothTint;
            public string styleNotes;
        }

        [Serializable]
        public sealed class CardSculptRequest
        {
            public string cardId;
            public string sourcePrefabPath;
            public string label;
            public string sculptHint;
            public string cardType;
            public int seed;
            public int regenIndex;
            public AiCharacterSculptSpec aiSpec;
        }

        public static bool TrySculptForCard(
            CardSculptRequest req,
            out string prefabRel,
            out string message)
        {
            prefabRel = null;
            message = null;
            if (req == null || string.IsNullOrWhiteSpace(req.sourcePrefabPath))
            {
                message = "Missing source character prefab.";
                return false;
            }

            EnsureFolders();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(req.sourcePrefabPath);
            if (source == null)
            {
                message = $"Could not load source prefab: {req.sourcePrefabPath}";
                return false;
            }

            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                message = "Failed to instantiate character prefab.";
                return false;
            }

            try
            {
                var spec = req.aiSpec ?? new AiCharacterSculptSpec();
                var seed = unchecked(req.seed + Mathf.Max(0, req.regenIndex) * 7919);
                ApplyMorph(instance, spec, req.sculptHint, req.cardType, seed);

                var slug = Slug(req.cardId, req.label);
                var subPath = $"{(req.cardType ?? "npc").ToLowerInvariant()}/{slug}.prefab";
                prefabRel = $"{PrefabRoot}/{subPath}";
                var abs = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), prefabRel);
                var dir = Path.GetDirectoryName(abs);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabRel);
                if (saved == null)
                {
                    message = "Failed to save sculpted character prefab.";
                    return false;
                }

                CaveBuildPlannerKitCatalogExporter.ExportThumbnailForPrefab(prefabRel);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                var hint = string.IsNullOrWhiteSpace(req.sculptHint) ? "" : $" ({req.sculptHint.Trim()})";
                if (!string.IsNullOrWhiteSpace(spec.styleNotes))
                    message = $"Sculpted {req.label}{hint} — {spec.styleNotes.Trim()}";
                else
                    message = $"Sculpted {req.label}{hint} via AI character morph.";
                return File.Exists(abs);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void ApplyMorph(
            GameObject root,
            AiCharacterSculptSpec spec,
            string sculptHint,
            string cardType,
            int seed)
        {
            var height = Mathf.Clamp(spec.heightScale > 0.01f ? spec.heightScale : 1f, 0.72f, 1.35f);
            var build = Mathf.Clamp(spec.buildScale > 0.01f ? spec.buildScale : 1f, 0.75f, 1.4f);
            var shoulders = Mathf.Clamp(spec.shoulderScale > 0.01f ? spec.shoulderScale : 1f, 0.8f, 1.35f);
            var head = Mathf.Clamp(spec.headScale > 0.01f ? spec.headScale : 1f, 0.82f, 1.22f);
            var limb = Mathf.Clamp(spec.limbScale > 0.01f ? spec.limbScale : 1f, 0.85f, 1.2f);

            var rng = new System.Random(seed);
            var yaw = (float)(rng.NextDouble() * 14.0 - 7.0);
            root.transform.localRotation *= Quaternion.Euler(0f, yaw, 0f);
            root.transform.localScale = new Vector3(build, height, build);

            ResolveColor(spec.skinTint, 0.82f, 0.66f, 0.54f, out var skinR, out var skinG, out var skinB);
            ResolveColor(spec.clothTint, 0.35f, 0.42f, 0.55f, out var clothR, out var clothG, out var clothB);

            var hint = (sculptHint ?? string.Empty).ToLowerInvariant();
            if (hint.Contains("tall"))
                root.transform.localScale = new Vector3(build * 0.95f, height * 1.08f, build * 0.95f);
            if (hint.Contains("stocky") || hint.Contains("bulky") || hint.Contains("muscular"))
                root.transform.localScale = new Vector3(build * 1.12f, height * 0.96f, build * 1.12f);
            if (hint.Contains("slim") || hint.Contains("lean"))
                root.transform.localScale = new Vector3(build * 0.88f, height * 1.02f, build * 0.88f);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("head") || n.Contains("skull") || n.Contains("face"))
                    t.localScale = Vector3.Scale(t.localScale, new Vector3(head, head, head));
                else if (n.Contains("shoulder") || n.Contains("chest") || n.Contains("spine"))
                    t.localScale = Vector3.Scale(t.localScale, new Vector3(shoulders, 1f, shoulders));
                else if (n.Contains("arm") || n.Contains("leg") || n.Contains("hand") || n.Contains("foot"))
                    t.localScale = Vector3.Scale(t.localScale, new Vector3(limb, limb, limb));
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var n = r.name.ToLowerInvariant();
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null)
                        continue;
                    var inst = UnityEngine.Object.Instantiate(mat);
                    var isSkin = n.Contains("skin") || n.Contains("face") || n.Contains("head") || n.Contains("body");
                    var tint = isSkin ? new Color(skinR, skinG, skinB) : new Color(clothR, clothG, clothB);
                    if (inst.HasProperty("_BaseColor"))
                        inst.SetColor("_BaseColor", tint);
                    else if (inst.HasProperty("_Color"))
                        inst.SetColor("_Color", tint);
                    mats[i] = inst;
                }

                r.sharedMaterials = mats;
            }

            if (string.Equals(cardType, "enemy", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null || !mat.HasProperty("_BaseColor"))
                            continue;
                        var c = mat.GetColor("_BaseColor");
                        mat.SetColor("_BaseColor", Color.Lerp(c, new Color(0.55f, 0.2f, 0.2f), 0.12f));
                    }
                }
            }
        }

        static void ResolveColor(AiColor c, float dr, float dg, float db, out float r, out float g, out float b)
        {
            if (c != null && c.r >= 0f && c.g >= 0f && c.b >= 0f)
            {
                r = Mathf.Clamp01(c.r);
                g = Mathf.Clamp01(c.g);
                b = Mathf.Clamp01(c.b);
                return;
            }

            r = dr;
            g = dg;
            b = db;
        }

        static string Slug(string cardId, string label)
        {
            var raw = string.IsNullOrWhiteSpace(label) ? cardId : label;
            while (raw.StartsWith("Gen_", StringComparison.Ordinal))
                raw = raw.Substring(4);
            var slug = System.Text.RegularExpressions.Regex.Replace(raw, @"[^a-zA-Z0-9]+", "_")
                .Trim('_');
            if (slug.Length > 48)
                slug = slug.Substring(0, 48);
            return string.IsNullOrEmpty(slug) ? "Gen_Character" : $"Gen_{slug}";
        }

        static void EnsureFolders()
        {
            foreach (var rel in new[] { Root, PrefabRoot })
            {
                var abs = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), rel);
                if (!Directory.Exists(abs))
                    Directory.CreateDirectory(abs);
            }
        }
    }
}
#endif
