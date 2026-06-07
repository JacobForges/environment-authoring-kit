#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Saves the finished generation as a prefab only after the full pipeline + Cursor workflow are idle.
    /// </summary>
    public static class CaveBuildGenerationPrefabExporter
    {
        public const string PrefabFolderRel = "Assets/EnvironmentKit/Generated/Prefabs";
        public const string ManifestRel = "Assets/EnvironmentKit/Generated/CaveBuildGenerationPrefabManifest.json";

        [Serializable]
        public class PrefabManifestEntry
        {
            public string sceneName;
            public int seed;
            public string prefabAssetPath;
            public string surfaceLandPrefabPath;
            public string portalPrefabPaths;
            public string exportedUtc;
            public int qualityScore;
            public string letterGrade;
            public bool meetsShip;
            public bool meetsPlayableWorld;
            public string finishReason;
        }

        [Serializable]
        class ManifestFile
        {
            public PrefabManifestEntry latest;
            public PrefabManifestEntry[] history;
        }

        static string _lastExportKey;
        static double _lastExportAt;

        /// <summary>True when build queue, meat loop, Cursor agent, and autonomous loop are not running.</summary>
        public static bool IsCursorWorkflowFinished =>
            !CaveBuildAutonomousOrchestrator.IsRunning &&
            !CaveBuildCursorAgentBridge.IsAgentRunning &&
            !EditorApplication.isCompiling;

        public static bool TryExportWhenPipelineFinished(
            string sceneName,
            int seed,
            CaveBuildQualityReport quality,
            string finishReason,
            out string prefabPath,
            out string message)
        {
            prefabPath = null;
            message = null;

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.exportGenerationPrefabWhenFinished)
            {
                message = "Prefab export disabled in Cave Build Cursor settings.";
                return false;
            }

            if (!IsCursorWorkflowFinished)
            {
                message = "Deferred — Cursor workflow still active (agent or autonomous loop).";
                return false;
            }

            var exportKey = $"{sceneName}_{seed}";
            if (exportKey == _lastExportKey &&
                EditorApplication.timeSinceStartup - _lastExportAt < 4.0)
            {
                message = "Already exported this generation moments ago.";
                return false;
            }

            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentRoot>();
            if (envRoot == null)
            {
                message = "No EnvironmentRoot in scene — cannot export prefab.";
                return false;
            }

            var hasCave = CaveRouteProbeRunner.FindCaveRoot() != null;
            var hasSurface = envRoot.transform.Find(SurfaceWorldPaths.RootName) != null;
            if (!hasCave && !hasSurface)
            {
                message = "Nothing to export — no cave system or GeneratedSurfaceWorld.";
                return false;
            }

            EnsurePrefabFolder();
            var safeScene = SanitizeFileToken(sceneName);
            prefabPath = $"{PrefabFolderRel}/WorldGen_{safeScene}_seed{seed}.prefab";

            var description =
                $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — scene {sceneName}, seed {seed}.";
            envRoot.SetGenerationInfo(description, seed);
            EditorUtility.SetDirty(envRoot);

            GameObject saved;
            try
            {
                AssetDatabase.StartAssetEditing();
                saved = PrefabUtility.SaveAsPrefabAsset(envRoot.gameObject, prefabPath);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (saved == null)
            {
                message = "PrefabUtility.SaveAsPrefabAsset failed.";
                return false;
            }

            ExportSurfaceLandAndPortals(envRoot.transform, seed, out var surfaceLandPath, out var portalPaths);

            AssetDatabase.SaveAssets();
            EnvironmentKitScopedAssetRefresh.ImportAssetPathsNow(prefabPath, surfaceLandPath);
            if (!string.IsNullOrEmpty(portalPaths))
            {
                foreach (var portalPath in portalPaths.Split(';'))
                    EnvironmentKitScopedAssetRefresh.ImportAssetPathsNow(portalPath.Trim());
            }

            _lastExportKey = exportKey;
            _lastExportAt = EditorApplication.timeSinceStartup;

            var meetsPlayable = TryReadPlayableWorld(out var playable);
            WriteManifest(
                sceneName,
                seed,
                prefabPath,
                surfaceLandPath,
                portalPaths,
                quality,
                meetsPlayable,
                finishReason);

            EnvironmentKitScopedAssetRefresh.ImportExportedPrefabsNow();

            message = $"Saved generation prefab: {prefabPath}";
            if (!string.IsNullOrEmpty(surfaceLandPath))
                message += $"\nSurface land: {surfaceLandPath}";
            if (!string.IsNullOrEmpty(portalPaths))
                message += $"\nPortals: {portalPaths}";
            CaveBuildPipelineLog.Info(message, "PrefabExport");
            return true;
        }

        static bool TryReadPlayableWorld(out bool meets)
        {
            meets = false;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, PlayableWorldGate.StatusRel);
            if (!File.Exists(path))
                return false;
            try
            {
                var status = JsonUtility.FromJson<PlayableWorldGate.PlayableWorldStatus>(File.ReadAllText(path));
                meets = status.meetsPlayableWorld;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void ExportSurfaceLandAndPortals(
            Transform envRoot,
            int seed,
            out string surfaceLandPath,
            out string portalPathsCsv)
        {
            surfaceLandPath = null;
            portalPathsCsv = null;
            if (envRoot == null)
                return;

            var surface = envRoot.Find(SurfaceWorldPaths.RootName);
            if (surface != null)
            {
                surfaceLandPath = $"{PrefabFolderRel}/SurfaceLand_seed{seed}.prefab";
                PrefabUtility.SaveAsPrefabAsset(surface.gameObject, surfaceLandPath);
            }

            var portalList = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "PortalFive", "MainScene_CavePortal" })
            {
                var portal = GameObject.Find(name);
                if (portal == null)
                    continue;
                var path = $"{PrefabFolderRel}/Portal_{SanitizeFileToken(name)}_seed{seed}.prefab";
                if (PrefabUtility.SaveAsPrefabAsset(portal, path) != null)
                    portalList.Add(path);
            }

            if (portalList.Count > 0)
                portalPathsCsv = string.Join(";", portalList);
        }

        static void WriteManifest(
            string sceneName,
            int seed,
            string prefabPath,
            string surfaceLandPath,
            string portalPathsCsv,
            CaveBuildQualityReport quality,
            bool meetsPlayable,
            string finishReason)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ManifestRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);

            ManifestFile file = null;
            if (File.Exists(path))
            {
                try
                {
                    file = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(path));
                }
                catch
                {
                    file = null;
                }
            }

            file ??= new ManifestFile();
            var entry = new PrefabManifestEntry
            {
                sceneName = sceneName,
                seed = seed,
                prefabAssetPath = prefabPath,
                surfaceLandPrefabPath = surfaceLandPath ?? string.Empty,
                portalPrefabPaths = portalPathsCsv ?? string.Empty,
                exportedUtc = DateTime.UtcNow.ToString("o"),
                qualityScore = quality?.OverallScore ?? 0,
                letterGrade = quality?.LetterGrade ?? "—",
                meetsShip = quality != null && quality.MeetsShipTarget,
                meetsPlayableWorld = meetsPlayable,
                finishReason = finishReason ?? "pipeline_complete",
            };
            file.latest = entry;

            var hist = new System.Collections.Generic.List<PrefabManifestEntry>();
            if (file.history != null)
                hist.AddRange(file.history);
            hist.Add(entry);
            if (hist.Count > 24)
                hist.RemoveRange(0, hist.Count - 24);
            file.history = hist.ToArray();

            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }

        static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Generated"))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
            if (!AssetDatabase.IsValidFolder(PrefabFolderRel))
                AssetDatabase.CreateFolder("Assets/EnvironmentKit/Generated", "Prefabs");
        }

        static string SanitizeFileToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Scene";
            var sb = new StringBuilder();
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
                else if (c == ' ' || c == '-')
                    sb.Append('_');
            }

            var s = sb.ToString();
            return s.Length > 0 ? s : "Scene";
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Open Last Generation Prefab")]
        public static void OpenLastPrefabMenu()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ManifestRel);
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog(
                    "Generation Prefab",
                    "No prefab manifest yet. Finish a full build (after Cursor autonomous loop completes).",
                    "OK");
                return;
            }

            try
            {
                var file = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(path));
                var asset = file?.latest?.prefabAssetPath;
                if (string.IsNullOrEmpty(asset))
                {
                    EditorUtility.DisplayDialog("Generation Prefab", "Manifest has no latest entry.", "OK");
                    return;
                }

                var obj = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                if (obj == null)
                {
                    EditorUtility.DisplayDialog("Generation Prefab", "Missing asset: " + asset, "OK");
                    return;
                }

                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Generation Prefab", ex.Message, "OK");
            }
        }
    }
}
#endif
