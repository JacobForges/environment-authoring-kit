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
    using FullWorldGridPhase = SurfaceTerrainTileExpansion.FullWorldGridPhase;
    using FullWorldGridSession = SurfaceTerrainTileExpansion.FullWorldGridSession;

    /// <summary>
    /// Resume FullWorld place/terraform/seam chain after crash or memory pause (phase + index on disk).
    /// Also saves the Unity scene + terrain assets at each checkpoint so you can stop and resume later.
    /// </summary>
    public static class CaveBuildFullWorldGridCheckpoint
    {
        public const string RelPath = "Assets/EnvironmentKit/Generated/CaveBuildFullWorldGridCheckpoint.json";

        /// <summary>Match memory-guard unload cadence — scene + assets saved on the same rhythm.</summary>
        const int SceneSaveEveryTerraformSteps = 6;
        const int SceneSaveEveryTerraformSteps16Gb = 48;
        const int SceneSaveEveryPlaceAllSteps = 48;
        const int SceneSaveEveryPlaceAllSteps16Gb = 96;
        const double MinSceneSaveIntervalSeconds = 90.0;
        const double MinSceneSaveIntervalSeconds16Gb = 600.0;

        static int _checkpointJsonTick;

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
            public int conceptIndex = -1;
            public string phase;
            public int index;
            public int tileCount;
            public bool layOnlyPass;
            public bool groundLaid;
            public string reason;
        }

        static FullWorldGridSession _activeSession;
        static double _lastSceneSaveAt;

        public static bool TryPeekConceptIndex(out int conceptIndex)
        {
            conceptIndex = -1;
            if (!TryLoadCheckpoint(out var doc))
                return false;

            if (doc.conceptIndex >= 0)
            {
                conceptIndex = Mathf.Clamp(doc.conceptIndex, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);
                return true;
            }

            return false;
        }

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

        internal static void BindActiveSession(FullWorldGridSession session) => _activeSession = session;

        public static void SaveActiveSession(string reason)
        {
            if (_activeSession?.MainTerrain != null)
            {
                Save(_activeSession, reason);
                return;
            }

            var main = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (main == null)
                return;

            var terrainCount = UnityEngine.Object.FindObjectsByType<Terrain>().Length;
            if (!CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive &&
                terrainCount < 9 &&
                CaveBuildStepCounter.HasSession &&
                CaveBuildStepCounter.Current > 0)
                return;

            var session = SurfaceTerrainTileExpansion.CreateMinimalFullWorldGridSession(main);
            if (session == null)
                return;

            session.Request = FullWorldConceptLayoutCatalog.CreateHubBoundRequest();
            session.Phase = FullWorldGridPhase.PlaceAll;
            session.Index = Mathf.Max(0, UnityEngine.Object.FindObjectsByType<Terrain>().Length - 1);
            BindActiveSession(session);
            Save(session, reason);
        }

        internal static bool IsSceneMilestoneReason(string reason)
        {
            var r = reason ?? string.Empty;
            return r.IndexOf("CC0 import complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   r.IndexOf("post-terrain tiles complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   r.IndexOf("Hollow Titan meat complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   r.IndexOf("Hollow Titan stump complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   r.IndexOf("Hollow Titan stump phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   r.IndexOf("Hollow Titan meat phase", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsFullAssetFlushMilestone(string reason)
        {
            if (!IsSceneMilestoneReason(reason))
                return false;

            var r = reason ?? string.Empty;
            if (r.IndexOf("meat phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("stump phase", StringComparison.OrdinalIgnoreCase) >= 0)
                return !CaveBuildLateBuildPerformance.ShouldSkipIntermediateTitanMilestoneSave();

            return true;
        }

        internal static bool ShouldSaveSceneForMilestone(string reason)
        {
            if (!IsSceneMilestoneReason(reason))
                return false;

            var r = reason ?? string.Empty;
            if (r.IndexOf("meat phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("stump phase", StringComparison.OrdinalIgnoreCase) >= 0)
                return !CaveBuildLateBuildPerformance.ShouldSkipIntermediateTitanMilestoneSave();

            return true;
        }

        /// <summary>Scene + terrain assets at CC0 / Hollow Titan milestones (not just grid index JSON).</summary>
        public static void SaveActiveSceneMilestone(string reason)
        {
            if (_activeSession?.MainTerrain == null)
                return;

            Save(_activeSession, reason);
        }

        internal static bool ShouldWriteProgressCheckpoint(FullWorldGridSession session, string reason)
        {
            if (IsSceneMilestoneReason(reason))
                return true;

            var r = reason ?? string.Empty;
            if (TryParsePacedStep(r, out var pacedStep))
                return CaveBuildPacedStepPersistence.ShouldWriteGridCheckpointForPacedStep(pacedStep, force: false);

            if (r.IndexOf("terraform begin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("ground lay complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("memory pressure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("pipeline start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("playtest break", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("resume ", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (session?.Phase != FullWorldGridPhase.Terraform)
                return true;

            if (!PreferJsonOnlyCheckpointDuringBuild())
            {
                return session.Index <= 0 ||
                       session.Index % ResolveTerraformSceneSaveInterval() == 0;
            }

            _checkpointJsonTick++;
            if (session.Index > 0 && session.Index % SceneSaveEveryTerraformSteps16Gb == 0)
                return true;

            return _checkpointJsonTick % 16 == 0;
        }

        internal static void Save(FullWorldGridSession session, string reason)
        {
            if (session?.MainTerrain == null)
                return;

            if (CaveBuildMemoryGuard.IsCc0ImportPhaseActive)
                return;

            var offsets = session.PlaceOffsets ?? session.Offsets;
            var doc = new CheckpointDoc
            {
                capturedUtc = DateTime.UtcNow.ToString("o"),
                scenePath = SceneManager.GetActiveScene().path,
                mainTerrainName = session.MainTerrain.name,
                seed = session.Request?.Seed ?? 0,
                conceptIndex = session.Request?.ConceptLayoutIndex ??
                               FullWorldGenerationStylePreset.LoadSelectedIndex(),
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
                ShouldSaveSceneForMilestone(reason) &&
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

        static bool PreferJsonOnlyCheckpointDuringBuild()
        {
            if (!CaveBuildEditorResponsiveness.IsLongBuildActive)
                return false;

            return CaveBuildMemoryGuard.SystemRamGb() is > 0f and <= 17f;
        }

        static int ResolveTerraformSceneSaveInterval() =>
            PreferJsonOnlyCheckpointDuringBuild()
                ? SceneSaveEveryTerraformSteps16Gb
                : SceneSaveEveryTerraformSteps;

        static bool ShouldSaveSceneAtCheckpoint(FullWorldGridSession session, string reason)
        {
            if (IsSceneMilestoneReason(reason))
                return true;

            var r = reason ?? string.Empty;
            if (TryParsePacedStep(r, out var pacedStep))
                return CaveBuildPacedStepPersistence.ShouldSaveSceneForPacedStep(pacedStep, force: false);

            if (r.IndexOf("memory pressure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("pipeline complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("pipeline start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("post-lay resume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("ground lay complete", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("terraform begin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("playtest break", StringComparison.OrdinalIgnoreCase) >= 0 ||
                r.IndexOf("resume ", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (PreferJsonOnlyCheckpointDuringBuild())
            {
                if (session?.Phase == FullWorldGridPhase.Terraform && session.Offsets != null)
                {
                    var total = session.Offsets.Length;
                    if (session.Index >= total - 1)
                        return true;

                    var interval = ResolveTerraformSceneSaveInterval();
                    return interval > 0 && session.Index > 0 && session.Index % interval == 0;
                }

                return false;
            }

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
                var interval = PreferJsonOnlyCheckpointDuringBuild()
                    ? SceneSaveEveryPlaceAllSteps16Gb
                    : SceneSaveEveryPlaceAllSteps;
                if (interval > 0 && session.Index > 0 && session.Index % interval == 0)
                    return true;
            }

            var now = EditorApplication.timeSinceStartup;
            var minInterval = PreferJsonOnlyCheckpointDuringBuild()
                ? MinSceneSaveIntervalSeconds16Gb
                : MinSceneSaveIntervalSeconds;
            if (PreferJsonOnlyCheckpointDuringBuild() && session.Phase == FullWorldGridPhase.Terraform)
                return false;

            return _lastSceneSaveAt <= 0 || now - _lastSceneSaveAt >= minInterval;
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
                out detail,
                flushAssets: IsFullAssetFlushMilestone(reason));
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
            CaveBuildPauseController.ClearPauseFlagOnly();

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

            ResumeBoundSession(session, doc);
        }

        internal static bool TryResumeGridCheckpointSilent()
        {
            if (!HasResumable || !TryLoadCheckpoint(out var doc))
                return false;

            CaveBuildMemoryGuard.ClearMemoryPause();
            if (!TryOpenCheckpointScene(doc))
                return false;

            if (!TryResumeSession(doc, out var session, out _))
                return false;

            ResumeBoundSession(session, doc);
            return true;
        }

        static void ResumeBoundSession(FullWorldGridSession session, CheckpointDoc doc)
        {
            _activeSession = session;
            BindActiveSession(session);
            CaveBuildStepCounter.BeginSession();
            if (doc.seed > 0 && session.Request != null)
                session.Request.Seed = doc.seed;

            if (CaveBuildSessionConfig.HasFinalizedActive)
            {
                CaveBuildSessionConfig.ApplyToRequest(session.Request);
                CaveBuildSessionConfig.ApplyToEditorSettings();
            }
            else
            {
                var conceptIndex = doc.conceptIndex >= 0
                    ? doc.conceptIndex
                    : FullWorldGenerationStylePreset.LoadSelectedIndex();
                FullWorldConceptLayoutCatalog.ApplySessionBinding(session.Request, conceptIndex);

                if (!CaveBuildConceptSession.ValidateTilePlan(conceptIndex, doc.tileCount, out var tileWarning))
                    CaveBuildEditorLog.LogSurfaceWarning("[Checkpoint] " + tileWarning);
            }

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
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>())
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
            var conceptIndex = doc.conceptIndex >= 0
                ? doc.conceptIndex
                : (CaveBuildPlaytestSessionSnapshot.TryLoad(out var playtest) && playtest.conceptIndex >= 0
                    ? playtest.conceptIndex
                    : FullWorldGenerationStylePreset.LoadSelectedIndex());
            var request = FullWorldConceptLayoutCatalog.CreateHubBoundRequest(doc.seed, conceptIndex);

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

        static bool TryParsePacedStep(string reason, out int step)
        {
            step = 0;
            if (string.IsNullOrEmpty(reason))
                return false;

            var marker = "paced step";
            var idx = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            var tail = reason.Substring(idx + marker.Length).Trim();
            var end = tail.IndexOfAny(new[] { ' ', '—', '-', '|', ',' });
            if (end > 0)
                tail = tail.Substring(0, end);

            return int.TryParse(tail, out step) && step > 0;
        }
    }
}
#endif
