#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Resolves floor/rock materials from whatever modular cave or environment packs exist in the open project
    /// (not a single hard-coded store folder).
    /// </summary>
    public static class ProjectCaveMaterialResolver
    {
        public enum MaterialRole
        {
            Floor,
            Wall,
            Rock,
        }

        static readonly Dictionary<MaterialRole, Material> Cached = new();

        public static Material Resolve(MaterialRole role, LavaTubePrefabCatalog catalog = null)
        {
            if (Cached.TryGetValue(role, out var hit) && hit != null && HasUsableTexture(hit))
                return hit;

            catalog ??= LavaTubePrefabCatalog.Load();
            LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();

            var urpLit = Shader.Find(LavaTubeMaterialUpgrader.UrpLitShaderName);
            var fromPrefab = ResolveFromCatalogPrefabs(catalog, role, urpLit);
            if (fromPrefab != null)
            {
                Cached[role] = fromPrefab;
                return fromPrefab;
            }

            var fromScan = ScanProjectMaterials(role, urpLit);
            if (fromScan != null)
            {
                Cached[role] = fromScan;
                return fromScan;
            }

            return null;
        }

        public static Material EnsureUsable(Material mat, MaterialRole role, LavaTubePrefabCatalog catalog = null)
        {
            if (mat != null && HasUsableTexture(mat) && !LavaTubeMaterialUpgrader.IsBrokenMaterial(mat))
                return mat;

            var resolved = Resolve(role, catalog);
            return resolved ?? mat;
        }

        public static void ClearCache() => Cached.Clear();

        public static int UpgradeRenderableMaterialsOnPrefab(GameObject prefab)
        {
            if (prefab == null)
                return 0;

            var urpLit = Shader.Find(LavaTubeMaterialUpgrader.UrpLitShaderName);
            if (urpLit == null)
                return 0;

            var upgraded = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                var mats = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null)
                        continue;

                    var path = AssetDatabase.GetAssetPath(mat);
                    if (!string.IsNullOrEmpty(path) && UpgradeIfNeeded(mat, urpLit))
                    {
                        changed = true;
                        upgraded++;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = mats;
            }

            return upgraded;
        }

        static bool UpgradeIfNeeded(Material mat, Shader urpLit)
        {
            if (!LavaTubeMaterialUpgrader.IsBrokenMaterial(mat) && HasUsableTexture(mat))
                return false;

            return LavaTubeMaterialUpgrader.UpgradeMaterial(mat, urpLit);
        }

        static Material ResolveFromCatalogPrefabs(LavaTubePrefabCatalog catalog, MaterialRole role, Shader urpLit)
        {
            if (catalog == null)
                return null;

            var lists = role switch
            {
                MaterialRole.Floor => new[] { catalog.Floors },
                MaterialRole.Wall => new[] { catalog.Walls, catalog.Ceilings },
                _ => new[] { catalog.Walls, catalog.Rockfalls, catalog.Floors },
            };

            Material best = null;
            var bestScore = int.MinValue;
            foreach (var list in lists)
            {
                foreach (var prefab in list)
                {
                    var mat = ExtractBestMaterial(prefab, role, urpLit, out var score);
                    if (mat == null || score <= bestScore)
                        continue;
                    best = mat;
                    bestScore = score;
                }
            }

            return best;
        }

        static Material ExtractBestMaterial(
            GameObject prefab,
            MaterialRole role,
            Shader urpLit,
            out int score)
        {
            score = int.MinValue;
            if (prefab == null)
                return null;

            Material best = null;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null)
                        continue;

                    var s = ScoreMaterial(mat, role);
                    if (s <= score)
                        continue;

                    var path = AssetDatabase.GetAssetPath(mat);
                    if (!string.IsNullOrEmpty(path))
                        UpgradeIfNeeded(mat, urpLit);

                    if (!HasUsableTexture(mat) && LavaTubeMaterialUpgrader.IsBrokenMaterial(mat))
                        continue;

                    score = s;
                    best = mat;
                }
            }

            return best;
        }

        static Material ScanProjectMaterials(MaterialRole role, Shader urpLit)
        {
            Material best = null;
            var bestScore = int.MinValue;

            foreach (var root in LavaTubePrefabCatalog.GetModuleAssetRoots())
            {
                if (!AssetDatabase.IsValidFolder(root))
                    continue;

                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (ShouldSkipMaterialPath(path))
                        continue;

                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                        continue;

                    var score = ScoreMaterial(mat, role, path);
                    if (score <= bestScore)
                        continue;

                    UpgradeIfNeeded(mat, urpLit);
                    if (!HasUsableTexture(mat) && LavaTubeMaterialUpgrader.IsBrokenMaterial(mat))
                        continue;

                    best = mat;
                    bestScore = score;
                }
            }

            return best;
        }

        static bool ShouldSkipMaterialPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            var p = path.Replace('\\', '/').ToLowerInvariant();
            return p.Contains("/editor/") ||
                   p.Contains("/textmesh pro/") ||
                   p.Contains("/plugins/") ||
                   p.Contains("/packages/") ||
                   p.Contains("ui/") ||
                   p.Contains("sprite") ||
                   p.Contains("particle") ||
                   p.Contains("skybox") ||
                   p.Contains("water") && !p.Contains("floor");
        }

        static int ScoreMaterial(Material mat, MaterialRole role, string path = null)
        {
            if (mat == null)
                return int.MinValue;

            var blob = ((path ?? AssetDatabase.GetAssetPath(mat)) + " " + mat.name).ToLowerInvariant();
            var score = HasUsableTexture(mat) ? 40 : 0;
            if (!LavaTubeMaterialUpgrader.IsBrokenMaterial(mat))
                score += 20;

            switch (role)
            {
                case MaterialRole.Floor:
                    if (blob.Contains("floor") || blob.Contains("ground") || blob.Contains("walk") ||
                        blob.Contains("path") || blob.Contains("pavement") || blob.Contains("dirt"))
                        score += 50;
                    if (blob.Contains("wall") || blob.Contains("ceiling"))
                        score -= 30;
                    break;
                case MaterialRole.Wall:
                    if (blob.Contains("wall") || blob.Contains("cliff") || blob.Contains("rock"))
                        score += 45;
                    if (blob.Contains("floor"))
                        score -= 15;
                    break;
                case MaterialRole.Rock:
                    if (blob.Contains("rock") || blob.Contains("stone") || blob.Contains("wall") ||
                        blob.Contains("cliff") || blob.Contains("cave"))
                        score += 45;
                    break;
            }

            return score;
        }

        public static bool HasUsableTexture(Material mat)
        {
            if (mat == null)
                return false;

            if (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null)
                return true;
            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null)
                return true;

            return false;
        }
    }
}
#endif
