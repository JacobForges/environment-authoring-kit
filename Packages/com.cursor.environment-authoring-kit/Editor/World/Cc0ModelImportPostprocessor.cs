#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// CC0 props, Quaternius OBJ/FBX packs, and RPG item meshes must not import as Humanoid (LeftFoot rig spam).
    /// Only rigged slots under CC0Imports/Characters/ may use Humanoid when bones are present.
    /// </summary>
    sealed class Cc0ModelImportPostprocessor : AssetPostprocessor
    {
        const string Cc0Root = "Assets/EnvironmentKit/CC0Imports";
        const string StartupDonePrefsKey = "EnvironmentKit.Cc0PropRigFix.StartupDone.v3";

        static Cc0ModelImportPostprocessor() { }

        static void RunDeferredStartupFix()
        {
            // No startup bulk reimport — causes third-party mesh warning floods on every editor load.
            if (!AssetDatabase.IsValidFolder(Cc0Root))
                return;

            EditorPrefs.SetBool(StartupDonePrefsKey, true);
        }

        void OnPreprocessModel()
        {
            if (assetImporter is not ModelImporter importer)
                return;

            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(Cc0Root, StringComparison.Ordinal))
                return;

            if (ShouldImportAsStaticProp(path))
                ApplyStaticPropImporterSettings(importer);
        }

        void OnPostprocessMesh(Mesh mesh)
        {
            if (mesh == null)
                return;

            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(Cc0Root, StringComparison.Ordinal))
                return;

            if (!path.Contains("/Characters/", StringComparison.Ordinal))
                return;

            FixUnweightedVertices(mesh);
        }

        static void FixUnweightedVertices(Mesh mesh)
        {
            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0)
                return;

            var changed = false;
            for (var i = 0; i < weights.Length; i++)
            {
                var w = weights[i];
                var total = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                if (total > 0.0001f)
                    continue;

                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                changed = true;
            }

            if (changed)
                mesh.boneWeights = weights;
        }

        internal static void ApplyStaticPropImporterSettings(ModelImporter importer)
        {
            if (importer == null)
                return;

            importer.animationType = ModelImporterAnimationType.None;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = false;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
        }

        internal static bool ShouldImportAsStaticProp(string assetPath)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (!assetPath.StartsWith(Cc0Root, StringComparison.Ordinal))
                return false;

            if (assetPath.Contains("/Characters/", StringComparison.Ordinal))
                return false;

            if (assetPath.Contains("/Prefabs/", StringComparison.Ordinal) ||
                assetPath.Contains("/Resources/", StringComparison.Ordinal) ||
                assetPath.Contains("/Textures/", StringComparison.Ordinal) ||
                assetPath.Contains("/Audio/", StringComparison.Ordinal))
                return false;

            if (assetPath.Contains("/Items/", StringComparison.Ordinal))
                return true;

            if (assetPath.IndexOf("/rpg-items", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (assetPath.IndexOf("kenney-animated-characters", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var ext = Path.GetExtension(assetPath);
            if (ext.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".3ds", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }

    /// <summary>Reimport pass for assets that already have Humanoid in .meta.</summary>
    public static class Cc0PropRigFixUtility
    {
        static readonly Regex HumanoidAnimType = new(@"animationType:\s*2\b", RegexOptions.Multiline);
        static readonly Regex StaleHumanoidMeta = new(
            @"animationType:\s*[12]\b|importAnimation:\s*1\b|avatarSetup:\s*1\b|autoGenerateAvatarMappingIfUnspecified:\s*1\b",
            RegexOptions.Multiline);

        [MenuItem("Window/Environment Kit/World/Fix CC0 Prop Rigs (stop LeftFoot errors)")]
        public static void FixFromMenu()
        {
            var patchedPaths = new List<string>();
            int meta;
            int reimport;
            using (Cc0ImportWarningSuppressor.Begin())
            {
                meta = PatchStaleMetaFiles(patchedPaths);
                reimport = ReimportAllStaticProps(
                    log: true,
                    onlyPaths: patchedPaths.Count > 0 ? patchedPaths : null,
                    onlyIfImporterDirty: patchedPaths.Count == 0);
            }

            Debug.Log($"[CC0] Manual rig fix — meta patches={meta}, reimports={reimport}.");
        }

        public static int FixAll(bool log = false)
        {
            var patchedPaths = new List<string>();
            var meta = PatchStaleMetaFiles(patchedPaths);
            if (meta == 0)
                return 0;

            using (Cc0ImportWarningSuppressor.Begin())
            {
                return meta + ReimportAllStaticProps(log: log, onlyPaths: patchedPaths);
            }
        }

        /// <summary>Fast read-only scan — true when any static-prop .meta still has Humanoid rig lines.</summary>
        public static bool HasStaleStaticPropMeta()
        {
            var hub = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(hub))
                return false;

            var cc0 = Path.Combine(hub, Cc0ContentImportUtility.Cc0Root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(cc0))
                return false;

            foreach (var metaPath in Directory.EnumerateFiles(cc0, "*.meta", SearchOption.AllDirectories))
            {
                var rel = metaPath.Replace('\\', '/');
                var assetPath = "Assets" + rel.Substring(Application.dataPath.Length);
                if (!Cc0ModelImportPostprocessor.ShouldImportAsStaticProp(assetPath))
                    continue;

                var text = File.ReadAllText(metaPath);
                if (StaleHumanoidMeta.IsMatch(text))
                    return true;
            }

            return false;
        }

        internal static bool MetaLooksCorrectForStaticProp(string assetPath)
        {
            var hub = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(hub))
                return false;

            var metaPath = Path.Combine(
                hub,
                assetPath.Replace('/', Path.DirectorySeparatorChar) + ".meta");
            if (!File.Exists(metaPath))
                return false;

            var text = File.ReadAllText(metaPath);
            if (StaleHumanoidMeta.IsMatch(text))
                return false;

            if (!text.Contains("animationType: 0", StringComparison.Ordinal))
                return false;

            if (!text.Contains("globalScale: 0.35", StringComparison.Ordinal))
                return false;

            return text.Contains("materialImportMode: 2", StringComparison.Ordinal);
        }

        /// <summary>Rewrites stale Humanoid lines in .meta before Unity reimports (fixes console spam immediately).</summary>
        public static int PatchStaleMetaFiles(List<string> patchedAssetPaths = null)
        {
            var hub = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(hub))
                return 0;

            var cc0 = Path.Combine(hub, Cc0ContentImportUtility.Cc0Root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(cc0))
                return 0;

            var patched = 0;
            foreach (var metaPath in Directory.EnumerateFiles(cc0, "*.meta", SearchOption.AllDirectories))
            {
                var rel = metaPath.Replace('\\', '/');
                var assetPath = "Assets" + rel.Substring(Application.dataPath.Length);
                if (!Cc0ModelImportPostprocessor.ShouldImportAsStaticProp(assetPath))
                    continue;

                var text = File.ReadAllText(metaPath);
                if (!text.Contains("animationType: 2", StringComparison.Ordinal))
                    continue;

                var updated = HumanoidAnimType.Replace(text, "animationType: 0");
                updated = updated.Replace(
                    "avatarSetup: 1",
                    "avatarSetup: 0",
                    StringComparison.Ordinal);
                updated = updated.Replace(
                    "autoGenerateAvatarMappingIfUnspecified: 1",
                    "autoGenerateAvatarMappingIfUnspecified: 0",
                    StringComparison.Ordinal);

                if (updated == text)
                    continue;

                File.WriteAllText(metaPath, updated);
                patched++;
                patchedAssetPaths?.Add(assetPath);
            }

            return patched;
        }

        public static int ReimportAllStaticProps(
            bool log = false,
            IReadOnlyList<string> onlyPaths = null,
            bool onlyIfImporterDirty = false)
        {
            if (!AssetDatabase.IsValidFolder(Cc0ContentImportUtility.Cc0Root))
                return 0;

            IEnumerable<string> paths;
            if (onlyPaths != null && onlyPaths.Count > 0)
            {
                paths = onlyPaths;
            }
            else
            {
                var guids = AssetDatabase.FindAssets("t:Model", new[] { Cc0ContentImportUtility.Cc0Root });
                var all = new List<string>(guids.Length);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path) || !Cc0ModelImportPostprocessor.ShouldImportAsStaticProp(path))
                        continue;
                    all.Add(path);
                }

                paths = all;
            }

            var changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path) || !Cc0ModelImportPostprocessor.ShouldImportAsStaticProp(path))
                        continue;

                    if (onlyIfImporterDirty && MetaLooksCorrectForStaticProp(path))
                        continue;

                    if (Cc0ContentImportUtility.ConfigurePropFbx(path))
                        changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (changed > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            if (log)
            {
                Debug.Log(
                    changed > 0
                        ? $"[CC0] Reimported {changed} static model(s) as None (no Humanoid avatar)."
                        : "[CC0] Static models already correct — clear Console after refresh.");
            }

            return changed;
        }
    }
}
#endif
