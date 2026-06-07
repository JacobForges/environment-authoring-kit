#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Resume FullWorld place/terraform/seam chain after crash or memory pause (phase + index on disk).
    /// Also saves the Unity scene + terrain assets at each checkpoint so you can stop and resume later.
    /// </summary>
    public static class CaveBuildFullWorldGridCheckpoint
    {
        public const string RelPath = "Assets/EnvironmentKit/Generated/CaveBuildFullWorldGridCheckpoint.json";

        /// <summary>Match memory-guard unload cadence — scene + assets saved on the same rhythm.</summary>
        const int SceneSaveEveryTerraformSteps = 6;
        const double MinSceneSaveIntervalSeconds = 90.0;

        [Serializable]
        sealed class CheckpointDoc
        {
            public string capturedUtc;
            public string scenePath;
            public string sceneSavedUtc;
            public bool sceneSaved;
            public string sceneSaveDetail;
            public string mainTerrainName;
            public int seed;
            public string phase;
            public int index;
            public int tileCount;
            public bool layOnlyPass;
            public bool groundLaid;
            public string reason;
        }

        static FullWorldGridSession _activeSession;
        static double _lastSceneSaveAt;

        public static bool HasResumable
        {
            get
            {
                var path = AbsolutePath();
                if (!File.Exists(path))
                    return false;

                try
                {
                    var doc = JsonUtility.FromJson<CheckpointDoc>(File.ReadAllText(path));
                    return doc != null &&
                           !string.IsNullOrEmpty(doc.scenePath) &&
                           doc.index >= 0 &&
                           doc.tileCount > 0 &&
                           Enum.TryParse(doc.phase, out FullWorldGridPhase _);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void BindActiveSession(FullWorldGridSession session) => _activeSession = session;

        public static void SaveActiveSession(string reason)
        {
            if (_activeSession?.MainTerrain == null)
                return;

            Save(_activeSession, reason);
        }

        public static void Save(FullWorldGridSession session, string reason)
        {
            if (session?.MainTerrain == null)
                return;

            var offsets = session.PlaceOffsets ?? session.Offsets;
            var doc = new CheckpointDoc
            {
                capturedUtc = DateTime.UtcNow.ToString("o"),
                scenePath = SceneManager.GetActiveScene().path,
                mainTerrainName = session.MainTerrain.name,
                seed = session.Request?.Seed ?? 0,
                phase = session.Phase.ToString(),
                index = session.Index,
                tileCount = offsets?.Length ?? 0,
                layOnlyPass = session.LayOnlyPass,
                groundLaid = SurfaceTerrainTileExpansion.FullWorldGroundTilesLaidThisBuild,
                reason = reason ?? string.Empty,
            };

            var abs = AbsolutePath();
            Directory.CreateDirectory(Path.GetDirectoryName(abs) ?? CaveBuildAgentContextExporter.Folder);
            File.WriteAllText(abs, JsonUtility.ToJson(doc, true) + "\n");

            if (ShouldSaveSceneAtCheckpoint(session, reason) &&
                TrySaveCheckpointScene(doc, reason, out var saveDetail))
            {
                doc.sceneSavedUtc = DateTime.UtcNow.ToString("o");
                doc.sceneSaved = true;
                doc.sceneSaveDetail = saveDetail;
                File.WriteAllText(abs, JsonUtility.ToJson(doc, true) + "\n");
            }

            CaveBuildEditorLog.LogSurface(
                $"[Checkpoint] FullWorld {doc.phase} index {doc.index}/{doc.tileCount} — {reason}" +
                (doc.sceneSaved ? $" | scene saved ({doc.sceneSaveDetail})" : string.Empty),
                forceUnityConsole: doc.sceneSaved);
        }

        static bool ShouldSaveSceneAtCheckpoint(FullWorldGridSession session, string reason)
        {
            var r = reason ?? string.Empty;
            if (r.IndexOf("memory pressure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("pipeline start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("post-lay resume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("ground lay complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("terraform begin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("resume ", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (session == null)
                return true;

            if (session.Phase == FullWorldGridPhase.Terraform && session.Offsets != null)
            {
                var total = session.Offsets.Length;
                if (session.Index >= total - 1)
                    return true;
                if (session.Index % SceneSaveEveryTerraformSteps == 0)
                    return true;
            }
            else if (session.Phase == FullWorldGridPhase.PlaceAll)
            {
                var total = (session.PlaceOffsets ?? session.Offsets)?.Length ?? 0;
                if (total > 0 && session.Index >= total - 1)
                    return true;
                if (session.Index % 24 == 0)
                    return true;
            }

            var now = EditorApplication.timeSinceStartup;
            return _lastSceneSaveAt <= 0 || now - _lastSceneSaveAt >= MinSceneSaveIntervalSeconds;
        }

        static bool TrySaveCheckpointScene(CheckpointDoc doc, string reason, out string detail)
        {
            detail = string.Empty;
            if (string.IsNullOrEmpty(doc.scenePath))
            {
                detail = "active scene has no saved path — save MainScene.unity once to enable checkpoint scene writes";
                CaveBuildEditorLog.LogSurfaceWarning($"[Checkpoint] {detail}", forceUnityConsole: true);
                return false;
            }

            var ok = EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                $"FullWorld {doc.phase} {doc.index}/{doc.tileCount} — {reason}",
                out detail);
            if (ok)
                _lastSceneSaveAt = EditorApplication.timeSinceStartup;
            return ok;
        }

        public static void Clear(string reason = "complete")
        {
            if (reason.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0 &&
                _activeSession?.MainTerrain != null)
            {
                var offsets = _activeSession.PlaceOffsets ?? _activeSession.Offsets;
                var doc = new CheckpointDoc
                {
                    scenePath = SceneManager.GetActiveScene().path,
                    phase = _activeSession.Phase.ToString(),
                    index = _activeSession.Index,
                    tileCount = offsets?.Length ?? 0,
                };
                TrySaveCheckpointScene(doc, "pipeline complete", out _);
            }

            _activeSession = null;
            _lastSceneSaveAt = 0;
            var abs = AbsolutePath();
            if (File.Exists(abs))
            {
                File.Delete(abs);
                CaveBuildEditorLog.LogSurface($"[Checkpoint] Cleared ({reason}).", forceUnityConsole: false);
            }
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Resume FullWorld Grid From Checkpoint", false, 3)]
        public static void ResumeFromCheckpointMenu()
        {
            CaveBuildMemoryGuard.ClearMemoryPause();
            CaveBuildPauseController.Continue();

            if (!TryLoadCheckpoint(out var doc))
            {
                EditorUtility.DisplayDialog(
                    "FullWorld checkpoint",
                    "No resumable checkpoint found.\n\n" +
                    $"Expected: {RelPath}",
                    "OK");
                return;
            }

            if (doc.sceneSaved && !string.IsNullOrEmpty(doc.scenePath))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Checkpoint] Last scene save: {doc.sceneSavedUtc} — {doc.sceneSaveDetail}",
                    forceUnityConsole: false);
            }

            if (!TryOpenCheckpointScene(doc))
                return;

            if (!TryResumeSession(doc, out var session, out var error))
            {
                EditorUtility.DisplayDialog("FullWorld checkpoint", error, "OK");
                return;
            }

            _activeSession = session;
            BindActiveSession(session);
            CaveBuildStepCounter.BeginSession();
            CaveBuildStepCounter.ConfigureForBuild(SurfaceBuildScope.FullWorld, doc.tileCount > 81);

            CaveBuildEditorLog.LogSurface(
                $"[Checkpoint] Resuming {doc.phase} at index {doc.index}/{doc.tileCount} — {doc.reason}",
                forceUnityConsole: true);

            SurfaceTerrainTileExpansion.ResumeFullWorldGridFromCheckpoint(session);
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Clear FullWorld Grid Checkpoint", false, 4)]
        public static void ClearCheckpointMenu()
        {
            Clear("user cleared");
            EditorUtility.DisplayDialog("FullWorld checkpoint", "Checkpoint cleared.", "OK");
        }

        static bool TryLoadCheckpoint(out CheckpointDoc doc)
        {
            doc = null;
            var abs = AbsolutePath();
            if (!File.Exists(abs))
                return false;

            try
            {
                doc = JsonUtility.FromJson<CheckpointDoc>(File.ReadAllText(abs));
                return doc != null;
            }
            catch
            {
                return false;
            }
        }

        static bool TryOpenCheckpointScene(CheckpointDoc doc)
        {
            if (string.IsNullOrEmpty(doc.scenePath))
                return false;

            if (SceneManager.GetActiveScene().path != doc.scenePath)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(doc.scenePath) == null)
                {
                    EditorUtility.DisplayDialog(
                        "FullWorld checkpoint",
                        $"Scene missing: {doc.scenePath}",
                        "OK");
                    return false;
                }

                if (!EnvironmentKitSceneSafeguards.TryOpenSceneByPath(doc.scenePath))
                {
                    EditorUtility.DisplayDialog(
                        "FullWorld checkpoint",
                        $"Could not open scene: {doc.scenePath}",
                        "OK");
                    return false;
                }
            }

            return true;
        }

        static bool TryResumeSession(CheckpointDoc doc, out FullWorldGridSession session, out string error)
        {
            session = null;
            error = string.Empty;

            Terrain main = null;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (t != null && t.name == doc.mainTerrainName)
                {
                    main = t;
                    break;
                }
            }

            main ??= GameObject.Find("SurfaceTerrainMain")?.GetComponent<Terrain>();
            if (main == null)
            {
                error = "Main terrain not found in scene — rebuild or open the scene that was checkpointed.";
                return false;
            }

            var ground = SceneGroundResolver.ResolveForFullWorld(main.transform);
            var request = WorldGenerationRequest.LoadOrDefault();
            if (doc.seed > 0)
                request.Seed = doc.seed;
            request.EnsureFullWorldSurfaceContract();

            if (!Enum.TryParse(doc.phase, out FullWorldGridPhase phase))
            {
                error = $"Unknown checkpoint phase: {doc.phase}";
                return false;
            }

            session = SurfaceTerrainTileExpansion.CreateResumeFullWorldGridSession(
                main,
                ground,
                request,
                phase,
                doc.index,
                doc.layOnlyPass,
                doc.groundLaid,
                (count, msg) =>
                {
                    Clear("pipeline complete");
                    CaveBuildStepCounter.EndSession();
                    CaveBuildEditorLog.LogSurface($"[Checkpoint] Pipeline finished: {msg}", forceUnityConsole: true);
                });

            if (session == null)
            {
                error = "Could not reconstruct FullWorld session.";
                return false;
            }

            return true;
        }

        static string AbsolutePath() =>
            Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty,
                RelPath);
    }
}
#endif
