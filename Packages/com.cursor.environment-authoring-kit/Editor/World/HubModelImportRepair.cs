#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using EnvironmentAuthoringKit.Editor.Blockout;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Repairs legacy FBX/OBJ import settings and post-processes imported models to fix console warnings.
    /// </summary>
    public static class HubModelImportRepairUtility
    {
        // Unity 6: ModelImporterMaterialLocation.External = 0, InPrefab = 1 (enum inverted vs pre-2022 YAML docs).
        const int MaterialLocationEmbedded = (int)ModelImporterMaterialLocation.InPrefab;
        const int MaterialImportViaDescription = (int)ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        // Environment packs only — no character packs (CC0/FIGHTER) to avoid bulk reimport geometry noise.
        static readonly string[] RepairRoots =
        {
            "Assets/WizardPBR/",
            "Assets/BillemotdonggulLavaTubePack/",
            "Assets/BackRock-NeonCity/",
            "Assets/Pizza&Games/",
        };

        // Stale = legacy External (0) in Unity 6 — NOT InPrefab (1).
        static readonly Regex ExternalMaterialLocation = new(@"materialLocation:\s*0\b", RegexOptions.Multiline);
        static readonly Regex NonDescriptionMaterialImportMode = new(
            @"materialImportMode:\s*(?:0|1)\b",
            RegexOptions.Multiline);
        static readonly Regex ImportNormalsFromFile = new(@"normalImportMode:\s*0\b", RegexOptions.Multiline);
        static readonly Regex AvatarSetupCreateFromModel = new(@"avatarSetup:\s*0\b", RegexOptions.Multiline);
        static readonly Regex AvatarSourceMissing = new(
            @"lastHumanDescriptionAvatarSource:\s*\{instanceID:\s*0\}",
            RegexOptions.Multiline);
        static readonly Regex AutoGenerateAvatarMapping = new(
            @"autoGenerateAvatarMappingIfUnspecified:\s*1\b",
            RegexOptions.Multiline);
        static readonly Regex AnimationWrongMaterialImportMode = new(
            @"materialImportMode:\s*(?:1|2)\b",
            RegexOptions.Multiline);

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Repair Legacy Model Imports", false, 201)]
        public static void RepairFromMenu()
        {
            var result = RepairAll(forceReimport: true);
            EditorUtility.DisplayDialog(
                "Legacy model import repair",
                $"Meta patches: {result.MetaPatched}\nReimports: {result.Reimported}\n\n" +
                "Check the Console for any remaining geometry issues that may need Blender cleanup.",
                "OK");
        }

        /// <summary>Batchmode entry — Unity -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RepairLegacyModelImports</summary>
        public static RepairResult RepairAll(bool forceReimport = false)
        {
            var patchedPaths = new List<string>();
            var metaPatched = PatchAllStaleMeta(patchedPaths);
            var reimported = 0;

            // Never bulk-reimport on startup — only when explicitly requested (menu / batchmode).
            if (forceReimport)
                reimported = ReimportRepairedModels(patchedPaths, forceAll: true);

            Debug.Log(
                $"[HubModelImportRepair] Repair complete — meta patches={metaPatched}, reimports={reimported}.");

            return new RepairResult(metaPatched, reimported);
        }

        public readonly struct RepairResult
        {
            public readonly int MetaPatched;
            public readonly int Reimported;

            public RepairResult(int metaPatched, int reimported)
            {
                MetaPatched = metaPatched;
                Reimported = reimported;
            }
        }

        public static bool IsRepairTarget(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            if (!Path.GetExtension(assetPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var root in RepairRoots)
            {
                if (assetPath.StartsWith(root, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public static bool IsStaticEnvironmentMesh(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            return assetPath.Contains("BillemotdonggulLavaTubePack/", StringComparison.Ordinal) ||
                   assetPath.Contains("BackRock-NeonCity/", StringComparison.Ordinal) ||
                   assetPath.Contains("Pizza&Games/Realistic Rocks/", StringComparison.Ordinal) ||
                   (assetPath.Contains("WizardPBR/Mesh/", StringComparison.Ordinal) &&
                    assetPath.IndexOf("WizardBodyMesh", StringComparison.OrdinalIgnoreCase) < 0);
        }

        public static bool IsAnimationOnlyBundle(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (!Path.GetExtension(assetPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!assetPath.Contains("/Animations/", StringComparison.Ordinal))
                return false;

            return IsRepairTarget(assetPath);
        }

        public static bool IsQuaterniusCharacter(string assetPath) =>
            assetPath.Replace('\\', '/')
                .Contains("quaternius-ultimate-animated-characters", StringComparison.OrdinalIgnoreCase);

        public static bool NeedsBoneWeightFix(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (!Path.GetExtension(assetPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                return false;

            if (assetPath.Contains("CC0Imports/Characters/", StringComparison.Ordinal))
                return true;

            if (assetPath.Contains("WizardPBR/Mesh/WizardBodyMesh.fbx", StringComparison.OrdinalIgnoreCase))
                return true;

            return IsQuaterniusCharacter(assetPath);
        }

        public static bool IsLavaTubeConvexHullMesh(string assetPath, Mesh mesh)
        {
            if (mesh == null)
                return false;

            if (!assetPath.Replace('\\', '/')
                    .Contains("BillemotdonggulLavaTubePack/", StringComparison.Ordinal))
                return false;

            return mesh.name.Contains("ConvexHulls", StringComparison.OrdinalIgnoreCase);
        }

        public static void ApplyPreprocessSettings(string assetPath, ModelImporter importer)
        {
            if (importer == null || !IsRepairTarget(assetPath))
                return;

            ApplyMaterialImporterSettings(importer);
            importer.weldVertices = true;
            importer.meshOptimizationFlags = MeshOptimizationFlags.Everything;

            if (IsStaticEnvironmentMesh(assetPath))
            {
                ApplyStaticEnvironmentImporterSettings(importer);
            }
            else if (IsAnimationOnlyBundle(assetPath))
            {
                ApplyAnimationOnlyImporterSettings(assetPath, importer);
            }
            else if (Cc0ModelImportPostprocessor.ShouldImportAsStaticProp(assetPath))
            {
                Cc0ModelImportPostprocessor.ApplyStaticPropImporterSettings(importer);
                ApplyMaterialImporterSettings(importer);

                if (NeedsBoneWeightFix(assetPath))
                {
                    importer.skinWeights = ModelImporterSkinWeights.Custom;
                    importer.minBoneWeight = 0.001f;
                }
            }
        }

        static void ApplyStaticEnvironmentImporterSettings(ModelImporter importer)
        {
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            importer.importNormals = ModelImporterNormals.Calculate;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
        }

        static void ApplyAnimationOnlyImporterSettings(string assetPath, ModelImporter importer)
        {
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.preserveHierarchy = true;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            importer.meshOptimizationFlags = MeshOptimizationFlags.PolygonOrder;

            var sourceModelPath = ResolveAnimationSourceModelPath(assetPath);
            if (!string.IsNullOrEmpty(sourceModelPath))
            {
                var sourceImporter = AssetImporter.GetAtPath(sourceModelPath) as ModelImporter;
                if (sourceImporter != null)
                    importer.animationType = sourceImporter.animationType;
            }

            var sourceAvatar = ResolveAnimationSourceAvatar(assetPath);
            if (sourceAvatar == null)
                return;

            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = sourceAvatar;
        }

        internal static void ApplyMaterialImporterSettings(ModelImporter importer)
        {
            if (importer == null)
                return;

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
#pragma warning disable CS0618
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
#pragma warning restore CS0618

            using var serialized = new SerializedObject(importer);
            var importMode = serialized.FindProperty("m_MaterialImportMode");
            var location = serialized.FindProperty("m_MaterialLocation");
            var changed = false;

            if (importMode != null && importMode.intValue != MaterialImportViaDescription)
            {
                importMode.intValue = MaterialImportViaDescription;
                changed = true;
            }

            if (location != null && location.intValue != MaterialLocationEmbedded)
            {
                location.intValue = MaterialLocationEmbedded;
                changed = true;
            }

            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool ImporterNeedsRepair(string assetPath, ModelImporter importer)
        {
            if (importer == null)
                return false;

            if (IsAnimationOnlyBundle(assetPath))
                return AnimationImporterNeedsRepair(assetPath, importer);

            if (IsStaticEnvironmentMesh(assetPath))
                return StaticEnvironmentImporterNeedsRepair(assetPath, importer);

            return StandardModelImporterNeedsRepair(importer) ||
                   (NeedsBoneWeightFix(assetPath) &&
                    Cc0ModelImportPostprocessor.ShouldImportAsStaticProp(assetPath) &&
                    importer.skinWeights != ModelImporterSkinWeights.Custom);
        }

        static bool AnimationImporterNeedsRepair(string assetPath, ModelImporter importer)
        {
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                !importer.importAnimation ||
                importer.importVisibility ||
                importer.importBlendShapes ||
                importer.importCameras ||
                importer.importLights ||
                importer.autoGenerateAvatarMappingIfUnspecified)
                return true;

            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther)
                return true;

            var sourceAvatar = ResolveAnimationSourceAvatar(assetPath);
            if (sourceAvatar != null && importer.sourceAvatar != sourceAvatar)
                return true;

            return sourceAvatar != null && importer.sourceAvatar == null;
        }

        static bool StaticEnvironmentImporterNeedsRepair(string assetPath, ModelImporter importer) =>
            StandardModelImporterNeedsRepair(importer) ||
            importer.importAnimation ||
            importer.animationType != ModelImporterAnimationType.None ||
            importer.importNormals != ModelImporterNormals.Calculate;

        static bool StandardModelImporterNeedsRepair(ModelImporter importer)
        {
            using var serialized = new SerializedObject(importer);
            var importMode = serialized.FindProperty("m_MaterialImportMode");
            var location = serialized.FindProperty("m_MaterialLocation");

            if (importMode != null && importMode.intValue != MaterialImportViaDescription)
                return true;

            if (location != null && location.intValue != MaterialLocationEmbedded)
                return true;

            return importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        public static void PostprocessImportedModel(string assetPath, GameObject root)
        {
            if (root == null || !IsRepairTarget(assetPath))
                return;

            if (IsAnimationOnlyBundle(assetPath))
                StripEmbeddedMeshes(root);

            if (IsStaticEnvironmentMesh(assetPath))
                StripConvexHullRenderers(root);

            if (NeedsBoneWeightFix(assetPath))
                FixBoneWeightsInHierarchy(root, assetPath);
        }

        public static void PostprocessImportedMesh(string assetPath, Mesh mesh)
        {
            if (mesh == null || !IsRepairTarget(assetPath))
                return;

            if (IsAnimationOnlyBundle(assetPath))
            {
                DiscardEmbeddedAnimationMesh(mesh);
                return;
            }

            if (IsLavaTubeConvexHullMesh(assetPath, mesh))
                EnsureMeshNormals(mesh);

            if (IsStaticEnvironmentMesh(assetPath))
                EnsureMeshNormalsAndTangents(mesh);

            if (NeedsBoneWeightFix(assetPath))
                FixMeshBoneWeights(mesh);
        }

        static void DiscardEmbeddedAnimationMesh(Mesh mesh)
        {
            if (mesh.vertexCount == 0)
                return;

            mesh.Clear();
        }

        static void EnsureMeshNormals(Mesh mesh)
        {
            var normals = mesh.normals;
            if (normals != null && normals.Length > 0)
                return;

            mesh.RecalculateNormals();
        }

        static void EnsureMeshNormalsAndTangents(Mesh mesh)
        {
            EnsureMeshNormals(mesh);
            var tangents = mesh.tangents;
            if (tangents != null && tangents.Length > 0)
                return;

            mesh.RecalculateTangents();
        }

        static void StripEmbeddedMeshes(GameObject root)
        {
            var toDestroy = new List<UnityEngine.Object>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                toDestroy.Add(smr);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                toDestroy.Add(mf);
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                toDestroy.Add(mr);

            foreach (var component in toDestroy)
                UnityEngine.Object.DestroyImmediate(component);
        }

        static void StripConvexHullRenderers(GameObject root)
        {
            var toDestroy = new List<UnityEngine.Object>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.name.Contains("ConvexHulls", StringComparison.OrdinalIgnoreCase))
                    continue;

                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null)
                    toDestroy.Add(mr);
                toDestroy.Add(mf);
            }

            foreach (var component in toDestroy)
                UnityEngine.Object.DestroyImmediate(component);
        }

        static void FixBoneWeightsInHierarchy(GameObject root, string assetPath)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;

                if (FixMeshBoneWeights(mesh))
                {
                    Debug.Log(
                        $"[HubModelImportRepair] Assigned root bone weight on {mesh.name} ({assetPath}).");
                }
            }
        }

        static bool FixMeshBoneWeights(Mesh mesh)
        {
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0)
                return false;

            var changed = false;
            for (var i = 0; i < weights.Length; i++)
            {
                if (GetTotalWeight(weights[i]) > 0.0001f)
                    continue;

                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                changed = true;
            }

            if (changed)
                mesh.boneWeights = weights;

            return changed;
        }

        static float GetTotalWeight(BoneWeight weight) =>
            weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;

        static string ResolveAnimationSourceModelPath(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (assetPath.Contains("WizardPBR/Animations/", StringComparison.Ordinal))
                return "Assets/WizardPBR/Mesh/WizardBodyMesh.fbx";

            if (assetPath.Contains("CC0Imports/kenney-animated-characters", StringComparison.OrdinalIgnoreCase) &&
                assetPath.Contains("/Animations/", StringComparison.Ordinal))
                return "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Model/characterMedium.fbx";

            return null;
        }

        static Avatar ResolveAnimationSourceAvatar(string assetPath)
        {
            var sourceModelPath = ResolveAnimationSourceModelPath(assetPath);
            if (string.IsNullOrEmpty(sourceModelPath))
                return null;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(sourceModelPath))
            {
                if (asset is Avatar avatar)
                    return avatar;
            }

            return null;
        }

        static string ResolveAnimationSourceAvatarMetaReference(string assetPath)
        {
            var sourceModelPath = ResolveAnimationSourceModelPath(assetPath);
            if (string.IsNullOrEmpty(sourceModelPath))
                return null;

            var hub = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(hub))
                return null;

            var metaPath = Path.Combine(hub, sourceModelPath + ".meta").Replace('\\', '/');
            if (!File.Exists(metaPath))
                return null;

            string guid = null;
            foreach (var line in File.ReadLines(metaPath))
            {
                if (!line.StartsWith("guid:", StringComparison.Ordinal))
                    continue;

                guid = line.Substring("guid: ".Length).Trim();
                break;
            }

            if (string.IsNullOrEmpty(guid))
                return null;

            return "{fileID: 9000000, guid: " + guid + ", type: 3}";
        }

        public static int PatchAllStaleMeta(List<string> patchedAssetPaths = null)
        {
            var hub = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(hub))
                return 0;

            var patched = 0;
            foreach (var root in RepairRoots)
            {
                var diskRoot = Path.Combine(hub, root.Replace('/', Path.DirectorySeparatorChar).TrimEnd('/'));
                if (!Directory.Exists(diskRoot))
                    continue;

                foreach (var metaPath in Directory.EnumerateFiles(diskRoot, "*.meta", SearchOption.AllDirectories))
                {
                    var rel = metaPath.Replace('\\', '/');
                    var assetMetaPath = "Assets" + rel.Substring(Application.dataPath.Length);
                    var assetPath = assetMetaPath.Substring(0, assetMetaPath.Length - ".meta".Length);
                    if (!IsRepairTarget(assetPath))
                        continue;

                    if (!PatchMetaFile(metaPath, assetPath))
                        continue;

                    patched++;
                    patchedAssetPaths?.Add(assetPath);
                }
            }

            return patched;
        }

        static bool PatchMetaFile(string metaPath, string assetPath)
        {
            var text = File.ReadAllText(metaPath);
            string updated;

            if (IsAnimationOnlyBundle(assetPath))
                updated = PatchAnimationOnlyMeta(text, assetPath);
            else
            {
                updated = ExternalMaterialLocation.Replace(text, "materialLocation: 1");
                updated = NonDescriptionMaterialImportMode.Replace(updated, "materialImportMode: 2");

                if (IsStaticEnvironmentMesh(assetPath))
                    updated = ImportNormalsFromFile.Replace(updated, "normalImportMode: 1");
            }

            if (updated == text)
                return false;

            File.WriteAllText(metaPath, updated);
            Debug.Log($"[HubModelImportRepair] Patched meta: {assetPath}");
            return true;
        }

        static string PatchAnimationOnlyMeta(string text, string assetPath)
        {
            var updated = text;
            var avatarReference = ResolveAnimationSourceAvatarMetaReference(assetPath);
            if (!string.IsNullOrEmpty(avatarReference))
            {
                updated = AvatarSetupCreateFromModel.Replace(updated, "avatarSetup: 2");
                updated = AvatarSourceMissing.Replace(
                    updated,
                    "lastHumanDescriptionAvatarSource: " + avatarReference);
            }

            updated = AutoGenerateAvatarMapping.Replace(updated, "autoGenerateAvatarMappingIfUnspecified: 0");
            updated = AnimationWrongMaterialImportMode.Replace(updated, "materialImportMode: 0");

            if (ExternalMaterialLocation.IsMatch(updated))
                updated = ExternalMaterialLocation.Replace(updated, "materialLocation: 1");

            return updated;
        }

        static bool AnimationMetaNeedsRepair(string text)
        {
            if (AvatarSetupCreateFromModel.IsMatch(text) || AvatarSourceMissing.IsMatch(text))
                return true;

            if (AutoGenerateAvatarMapping.IsMatch(text))
                return true;

            if (ExternalMaterialLocation.IsMatch(text))
                return true;

            return AnimationWrongMaterialImportMode.IsMatch(text);
        }

        public static bool HasStaleMeta()
        {
            var hub = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(hub))
                return false;

            foreach (var root in RepairRoots)
            {
                var diskRoot = Path.Combine(hub, root.Replace('/', Path.DirectorySeparatorChar).TrimEnd('/'));
                if (!Directory.Exists(diskRoot))
                    continue;

                foreach (var metaPath in Directory.EnumerateFiles(diskRoot, "*.meta", SearchOption.AllDirectories))
                {
                    var rel = metaPath.Replace('\\', '/');
                    var assetMetaPath = "Assets" + rel.Substring(Application.dataPath.Length);
                    var assetPath = assetMetaPath.Substring(0, assetMetaPath.Length - ".meta".Length);
                    if (!IsRepairTarget(assetPath))
                        continue;

                    var text = File.ReadAllText(metaPath);
                    if (IsAnimationOnlyBundle(assetPath))
                    {
                        if (AnimationMetaNeedsRepair(text))
                            return true;

                        continue;
                    }

                    if (ExternalMaterialLocation.IsMatch(text) ||
                        NonDescriptionMaterialImportMode.IsMatch(text))
                        return true;

                    if (IsStaticEnvironmentMesh(assetPath) && ImportNormalsFromFile.IsMatch(text))
                        return true;
                }
            }

            return false;
        }

        public static int ReimportRepairedModels(IReadOnlyList<string> priorityPaths = null, bool forceAll = false)
        {
            var paths = CollectAllModelPaths();
            if (paths.Count == 0)
                return 0;

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<string>();

            if (priorityPaths != null)
            {
                foreach (var path in priorityPaths)
                {
                    if (string.IsNullOrEmpty(path) || !seen.Add(path))
                        continue;
                    ordered.Add(path);
                }
            }

            foreach (var path in paths)
            {
                if (seen.Add(path))
                    ordered.Add(path);
            }

            var reimported = 0;
            foreach (var path in ordered)
            {
                if (ConfigureAndReimportModel(path, forceAll))
                    reimported++;
            }

            if (reimported > 0)
                AssetDatabase.SaveAssets();

            return reimported;
        }

        static bool ConfigureAndReimportModel(string assetPath, bool force)
        {
            if (!IsRepairTarget(assetPath))
                return false;

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                return false;

            ApplyPreprocessSettings(assetPath, importer);

            if (!force && !ImporterNeedsRepair(assetPath, importer))
                return false;

            importer.SaveAndReimport();
            return true;
        }

        static List<string> CollectAllModelPaths()
        {
            var results = new List<string>();
            foreach (var root in RepairRoots)
            {
                if (!AssetDatabase.IsValidFolder(root.TrimEnd('/')))
                    continue;

                var guids = AssetDatabase.FindAssets("t:Model", new[] { root.TrimEnd('/') });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (IsRepairTarget(path))
                        results.Add(path);
                }
            }

            results.Sort(CompareModelImportOrder);
            return results;
        }

        static int CompareModelImportOrder(string a, string b)
        {
            var aAnim = IsAnimationOnlyBundle(a);
            var bAnim = IsAnimationOnlyBundle(b);
            if (aAnim != bAnim)
                return aAnim ? 1 : -1;

            return string.Compare(a, b, StringComparison.Ordinal);
        }

        internal static void RunDeferredStartupRepair()
        {
            // Disabled — bulk SaveAndReimport on startup floods the console with third-party mesh warnings.
        }
    }

    sealed class HubModelImportRepairPostprocessor : AssetPostprocessor
    {
        const uint PostprocessorVersion = 7;

        public override uint GetVersion() => PostprocessorVersion;

        void OnPreprocessModel()
        {
            if (assetImporter is ModelImporter importer)
                HubModelImportRepairUtility.ApplyPreprocessSettings(assetPath, importer);
        }

        void OnPostprocessModel(GameObject root) =>
            HubModelImportRepairUtility.PostprocessImportedModel(assetPath, root);

        void OnPostprocessMesh(Mesh mesh) =>
            HubModelImportRepairUtility.PostprocessImportedMesh(assetPath, mesh);
    }
}
#endif
