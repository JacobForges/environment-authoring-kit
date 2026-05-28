using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Repairs cave renderers in the open scene: removes magenta blockout tunnel caps,
    /// rebinds pack URP materials, and tiles textures on stretched floor/ceiling modules.
    /// </summary>
    public static class CaveSceneMaterialRepair
    {
        const float ModuleSpan = 5f;

        static readonly string[] BlockoutPrimitiveNames =
        {
            "TunnelCeiling", "TunnelFloor", "TunnelWallL", "TunnelWallR"
        };

        static readonly Regex CeilingIdPattern = new(
            @"(?:SM_)?Ceiling(\d{1,2}[A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Fix Cave Pink Materials")]
        public static void FixActiveSceneFromMenu()
        {
            var caveRoot = FindCaveRoot();
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Fix Cave Materials",
                    "No LavaTubeCaveSystem found. Build the cave first, or open the scene that contains it.",
                    "OK");
                return;
            }

            var report = RepairCaveRoot(caveRoot);
            EditorUtility.DisplayDialog(
                "Fix Cave Materials",
                $"Removed {report.BlockoutRemoved} blockout caps.\n" +
                $"Removed {report.CollidersRemoved} ceiling colliders.\n" +
                $"Fixed {report.RenderersFixed} renderer(s).\n" +
                $"Tiled {report.TilingApplied} stretched module(s).",
                "OK");
        }

        public static CaveMaterialRepairReport RepairCaveRoot(Transform caveRoot)
        {
            var report = new CaveMaterialRepairReport();
            if (caveRoot == null)
                return report;

            LavaTubeMaterialUpgrader.EnsurePackMaterialsUpgraded();

            report.BlockoutRemoved = RemoveBlockoutTunnelPrimitives(caveRoot);
            report.CollidersRemoved = RemoveBlockingCeilingColliders(caveRoot);

            var urpLit = Shader.Find(LavaTubeMaterialUpgrader.UrpLitShaderName);
            if (urpLit == null)
            {
                Debug.LogError("[CaveSceneMaterialRepair] URP Lit shader not found.");
                return report;
            }

            foreach (var renderer in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                if (RepairRenderer(renderer, urpLit, report))
                    EditorUtility.SetDirty(renderer);
            }

            AssetDatabase.SaveAssets();
            EnvironmentSceneUtility.MarkSceneDirty();
            return report;
        }

        /// <summary>Apply pack materials + UV tiling when a tunnel module is placed.</summary>
        public static void ApplyModuleMaterials(GameObject instance, Vector3 localScale)
        {
            if (instance == null)
                return;

            var urpLit = Shader.Find(LavaTubeMaterialUpgrader.UrpLitShaderName);
            if (urpLit == null)
                return;

            var report = new CaveMaterialRepairReport();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                if (RepairRenderer(renderer, urpLit, report))
                    EditorUtility.SetDirty(renderer);
            }
        }

        public static int RemoveBlockingCeilingColliders(Transform caveRoot)
        {
            var removed = 0;
            foreach (var col in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col == null)
                    continue;

                var n = col.gameObject.name;
                if (!n.Contains("Ceiling") && !n.Contains("Cap_"))
                    continue;

                if (col is MeshCollider or BoxCollider or CapsuleCollider)
                {
                    CaveEditorUndo.DestroyImmediate(col);
                    removed++;
                }
            }

            return removed;
        }

        public static int RemoveBlockoutTunnelPrimitives(Transform caveRoot)
        {
            var removed = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == caveRoot)
                    continue;

                foreach (var blockoutName in BlockoutPrimitiveNames)
                {
                    if (t.name != blockoutName)
                        continue;

                    CaveEditorUndo.DestroyImmediate(t.gameObject);
                    removed++;
                    break;
                }
            }

            return removed;
        }

        static bool RepairRenderer(Renderer renderer, Shader urpLit, CaveMaterialRepairReport report)
        {
            var mats = renderer.sharedMaterials;
            var changed = false;

            for (var i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                var resolved = ResolveMaterial(renderer, mat, urpLit);
                if (resolved == null)
                    continue;

                if (resolved != mat)
                {
                    mats[i] = resolved;
                    changed = true;
                    report.RenderersFixed++;
                }

                if (ShouldApplyWorldTiling(renderer))
                {
                    mats[i] = CreateTiledInstance(resolved, renderer.transform.lossyScale);
                    changed = true;
                    report.TilingApplied++;
                }
            }

            if (changed)
                renderer.sharedMaterials = mats;

            return changed;
        }

        static Material ResolveMaterial(Renderer renderer, Material mat, Shader urpLit)
        {
            if (mat != null)
            {
                var path = AssetDatabase.GetAssetPath(mat);
                if (!string.IsNullOrEmpty(path) &&
                    (LavaTubePrefabCatalog.IsUnderModuleAssetRoots(path) ||
                     path.StartsWith(LavaTubeMaterialUpgrader.PackRoot, System.StringComparison.Ordinal)))
                {
                    if (LavaTubeMaterialUpgrader.IsBrokenMaterial(mat))
                        LavaTubeMaterialUpgrader.UpgradeMaterial(mat, urpLit);
                    return mat;
                }

                if (!LavaTubeMaterialUpgrader.IsBrokenMaterial(mat) &&
                    !IsUntexturedSceneMaterial(mat))
                    return mat;
            }

            var fromName = LoadPackMaterialFromObjectName(renderer.gameObject.name);
            if (fromName != null)
                return fromName;

            var parent = renderer.transform.parent;
            if (parent != null)
            {
                fromName = LoadPackMaterialFromObjectName(parent.name);
                if (fromName != null)
                    return fromName;
            }

            if (mat != null && LavaTubeMaterialUpgrader.IsBrokenMaterial(mat))
            {
                LavaTubeMaterialUpgrader.UpgradeMaterial(mat, urpLit);
                if (!IsUntexturedSceneMaterial(mat))
                    return mat;
            }

            return LoadFallbackCaveMaterial(urpLit);
        }

        static bool IsUntexturedSceneMaterial(Material mat)
        {
            if (mat == null)
                return true;

            var path = AssetDatabase.GetAssetPath(mat);
            if (!string.IsNullOrEmpty(path))
                return false;

            if (mat.shader == null)
                return true;

            if (!mat.shader.name.Contains("Universal"))
                return true;

            return mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null;
        }

        static Material LoadPackMaterialFromObjectName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
                return null;

            var id = NormalizeSurfaceId(ExtractSurfaceId(objectName));
            if (string.IsNullOrEmpty(id))
                return null;

            foreach (var root in LavaTubePrefabCatalog.GetModuleAssetRoots())
            {
                var candidates = new[]
                {
                    $"{root}/Material/MI_{id}.mat",
                    $"{root}/Materials/MI_{id}.mat",
                    $"{root}/Mesh/Materials/MI_{id}.mat",
                    $"{root}/Material/M_{id}.mat",
                    $"{root}/Materials/M_{id}.mat",
                    $"{root}/Mesh/Materials/M_{id}.mat",
                    $"{LavaTubeMaterialUpgrader.PackRoot}/Material/MI_{id}.mat",
                    $"{LavaTubeMaterialUpgrader.PackRoot}/Mesh/Materials/MI_{id}.mat",
                };

                foreach (var path in candidates)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat != null)
                        return mat;
                }
            }

            return null;
        }

        static string NormalizeSurfaceId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return id;

            if (id.StartsWith("Ceiling6") && !id.StartsWith("Ceiling06"))
                return "Ceiling0" + id.Substring("Ceiling".Length);

            return id;
        }

        static string ExtractSurfaceId(string objectName)
        {
            var ceiling = CeilingIdPattern.Match(objectName);
            if (ceiling.Success)
                return "Ceiling" + ceiling.Groups[1].Value;

            if (objectName.Contains("Floor"))
            {
                var m = Regex.Match(objectName, @"Floor(?:0)?(\d{1,2}[A-Z])", RegexOptions.IgnoreCase);
                if (m.Success)
                    return "Floor" + m.Groups[1].Value;
            }

            if (objectName.Contains("Wall"))
            {
                var m = Regex.Match(objectName, @"Wall(?:0)?(\d{1,2}[A-Z])", RegexOptions.IgnoreCase);
                if (m.Success)
                    return "Wall" + m.Groups[1].Value;
            }

            return null;
        }

        static Material LoadFallbackCaveMaterial(Shader urpLit)
        {
            var fallbackPaths = new[]
            {
                $"{LavaTubeMaterialUpgrader.PackRoot}/Material/MI_Ceiling01A.mat",
                $"{LavaTubeMaterialUpgrader.PackRoot}/Mesh/Materials/MI_Ceiling01A.mat",
                $"{LavaTubeMaterialUpgrader.PackRoot}/Material/MI_Wall06A.mat"
            };

            foreach (var path in fallbackPaths)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                    continue;
                LavaTubeMaterialUpgrader.UpgradeMaterial(mat, urpLit);
                return mat;
            }

            return null;
        }

        static bool ShouldApplyWorldTiling(Renderer renderer)
        {
            var n = renderer.gameObject.name;
            if (n.StartsWith("Tunnel") || n.StartsWith("Chamber"))
                return false;

            return n.Contains("Ceiling") || n.Contains("Floor");
        }

        static Material CreateTiledInstance(Material source, Vector3 lossyScale)
        {
            var along = Mathf.Max(1f, lossyScale.z / ModuleSpan);
            var across = Mathf.Max(1f, lossyScale.x / ModuleSpan);
            var tile = new Vector2(along * 2f, across * 2f);

            var inst = new Material(source) { name = source.name + "_Tiled" };
            if (inst.HasProperty("_BaseMap"))
            {
                inst.SetTextureScale("_BaseMap", tile);
                inst.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            if (inst.HasProperty("_BumpMap") && inst.GetTexture("_BumpMap") != null)
            {
                inst.SetTextureScale("_BumpMap", tile);
                inst.SetTextureOffset("_BumpMap", Vector2.zero);
            }

            if (inst.HasProperty("_OcclusionMap") && inst.GetTexture("_OcclusionMap") != null)
            {
                inst.SetTextureScale("_OcclusionMap", tile);
                inst.SetTextureOffset("_OcclusionMap", Vector2.zero);
            }

            return inst;
        }

        static Transform FindCaveRoot()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var underGrid = grid.transform.Find("LavaTubeCaveSystem");
                if (underGrid != null)
                    return underGrid;
            }

            var direct = GameObject.Find("LavaTubeCaveSystem");
            return direct != null ? direct.transform : null;
        }

        public sealed class CaveMaterialRepairReport
        {
            public int BlockoutRemoved;
            public int CollidersRemoved;
            public int RenderersFixed;
            public int TilingApplied;
        }
    }
}
