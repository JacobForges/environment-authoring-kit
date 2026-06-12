#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Scene + JSON checkpoint after every paced Hub step — all presets, all tile plans.
    /// </summary>
    public static class CaveBuildPacedStepPersistence
    {
        public const string RelPath = "Assets/EnvironmentKit/Generated/CaveBuildPacedStepCheckpoint.json";
        public const string PrefEnabled = "EnvironmentKit_PacedStepSceneSave";
        public const string PrefSceneSaveInterval = "EnvironmentKit_PacedStepSceneSaveInterval";
        public const string PrefJsonSaveInterval = "EnvironmentKit_PacedStepJsonSaveInterval";
        public const int DefaultSceneSaveInterval = 100;
        public const int DefaultJsonSaveInterval = 100;
        const long MinFreeDiskBytes = 512L * 1024 * 1024;

        static bool _diskFullLogged;
        static int _lastJsonStep = -1;

        [Serializable]
        public sealed class PacedStepSnapshot
        {
            public string capturedUtc;
            public int pacedStep;
            public string label;
            public string scenePath;
            public bool sceneSaved;
            public string sceneSaveDetail;
            public int conceptIndex = -1;
            public string generationStyleId;
            public int seed;
            public int terrainCount;
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefEnabled, true);
            set => EditorPrefs.SetBool(PrefEnabled, value);
        }

        /// <summary>0 = scene saves only at pipeline milestones / playtest break (fastest).</summary>
        public static int SceneSaveIntervalSteps
        {
            get => EditorPrefs.GetInt(PrefSceneSaveInterval, DefaultSceneSaveInterval);
            set => EditorPrefs.SetInt(PrefSceneSaveInterval, Mathf.Max(0, value));
        }

        /// <summary>JSON resume metadata interval — independent of Unity scene saves.</summary>
        public static int JsonSaveIntervalSteps
        {
            get => EditorPrefs.GetInt(PrefJsonSaveInterval, DefaultJsonSaveInterval);
            set => EditorPrefs.SetInt(PrefJsonSaveInterval, Mathf.Max(1, value));
        }

        public static bool ShouldSaveSceneForPacedStep(int step, bool force)
        {
            if (!Enabled || step <= 0)
                return false;
            if (force)
                return true;
            if (step <= 2)
                return true;

            var interval = SceneSaveIntervalSteps;
            return interval > 0 && step % interval == 0;
        }

        public static bool ShouldWriteGridCheckpointForPacedStep(int step, bool force)
        {
            if (!Enabled || step <= 0)
                return false;
            if (force || step <= 2)
                return true;

            var interval = SceneSaveIntervalSteps;
            if (interval <= 0)
                return step % 25 == 0;

            return step % Mathf.Max(1, interval) == 0;
        }

        public static bool ShouldWriteJsonForPacedStep(int step, bool force)
        {
            if (!Enabled || step <= 0)
                return false;
            if (force || step <= 2)
                return true;

            return step % JsonSaveIntervalSteps == 0;
        }

        public static bool TryLoadSnapshot(out PacedStepSnapshot snapshot)
        {
            snapshot = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, RelPath);
            if (!File.Exists(abs))
                return false;

            try
            {
                snapshot = JsonUtility.FromJson<PacedStepSnapshot>(File.ReadAllText(abs));
                return snapshot != null && snapshot.pacedStep > 0;
            }
            catch
            {
                return false;
            }
        }

        public static void OnPacedStepCompleted(int step, string label)
        {
            if (!Enabled || step <= 0)
                return;

            if (!EnvironmentKitSceneSafeguards.IsKitBuildSessionActive)
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            WriteCheckpoint(step, label ?? string.Empty, forceSceneSave: false);
        }

        /// <summary>Playtest break / pause — always write when terrain exists (session may be idle after Play Mode).</summary>
        public static void WritePlaytestBreakCheckpoint()
        {
            if (!Enabled)
                return;

            if (SurfaceTerrainTileExpansion.FindMainTerrainInScene() == null)
                return;

            var step = CaveBuildStepCounter.HasSession ? CaveBuildStepCounter.Current : 0;
            var label = "playtest break";
            if (step <= 0 && TryLoadSnapshot(out var prior))
            {
                step = prior.pacedStep;
                label = string.IsNullOrEmpty(prior.label) ? label : prior.label;
            }

            if (step <= 0)
                step = 1;

            WriteCheckpoint(step, label, forceSceneSave: true);
        }

        static bool HasMinimumFreeDiskSpace()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, RelPath);
            return EnvironmentKitDataRoot.HasMinimumFreeSpaceForWrites(abs, MinFreeDiskBytes);
        }

        static void WriteCheckpoint(int step, string label, bool forceSceneSave)
        {
            if (!HasMinimumFreeDiskSpace())
            {
                NotifyDiskFullOnce();
                return;
            }

            EnvironmentKitSceneSafeguards.EnsureActiveSceneHasSavePath();

            var request = CaveBuildAaaSessionPolicy.ActiveRequest;
            var doc = new PacedStepSnapshot
            {
                capturedUtc = DateTime.UtcNow.ToString("o"),
                pacedStep = step,
                label = label ?? string.Empty,
                scenePath = SceneManager.GetActiveScene().path,
                conceptIndex = request?.ConceptLayoutIndex ?? CaveBuildConceptSession.ResolveLockedConceptIndex(),
                generationStyleId = request?.GenerationStyleId ?? string.Empty,
                seed = request?.Seed ?? 0,
                terrainCount = UnityEngine.Object.FindObjectsByType<Terrain>().Length,
            };

            if (doc.seed == 0 && TryLoadSnapshot(out var priorSeed) && priorSeed.seed != 0)
                doc.seed = priorSeed.seed;

            var saveScene = ShouldSaveSceneForPacedStep(step, forceSceneSave);
            var writeGrid = saveScene || ShouldWriteGridCheckpointForPacedStep(step, forceSceneSave);
            var writeJson = forceSceneSave || saveScene || writeGrid || ShouldWriteJsonForPacedStep(step, forceSceneSave);

            if (saveScene)
            {
                doc.sceneSaved = EnvironmentKitSceneSafeguards.SaveBuildCheckpointSnapshot(
                    forceSceneSave ? "playtest break" : $"paced step {step}",
                    out doc.sceneSaveDetail,
                    flushAssets: forceSceneSave || step <= 2 || step % SceneSaveIntervalSteps == 0);
            }

            if (writeGrid)
            {
                CaveBuildFullWorldGridCheckpoint.SaveActiveSession(
                    forceSceneSave ? "playtest break" : $"paced step {step}");
            }

            if (writeJson)
            {
                WriteDoc(doc);
                _lastJsonStep = step;
                Debug.Log(
                    $"[CaveBuild] Paced checkpoint step {step}" +
                    (doc.sceneSaved ? $" — {doc.sceneSaveDetail}" : " — metadata only (scene save deferred)"));
            }
        }

        public static void ClearCheckpoint()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, RelPath);
            if (File.Exists(abs))
            {
                try
                {
                    File.Delete(abs);
                }
                catch
                {
                    // advisory
                }
            }
        }

        static void WriteDoc(PacedStepSnapshot doc)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var abs = Path.Combine(hub, RelPath);
            if (!EnvironmentKitDataRoot.HasMinimumFreeSpaceForWrites(abs, MinFreeDiskBytes))
            {
                NotifyDiskFullOnce();
                return;
            }

            if (!EnvironmentKitDataRoot.TryWriteAllText(abs, JsonUtility.ToJson(doc, true) + "\n", "paced checkpoint"))
                NotifyDiskFullOnce();
        }

        static void NotifyDiskFullOnce(string detail = null)
        {
            if (_diskFullLogged)
                return;

            _diskFullLogged = true;
            Enabled = false;
            EnvironmentKitDataRoot.NotifyDiskPressureOnce(
                EnvironmentKitDataRoot.ResolveProjectGeneratedRoot(),
                "paced checkpoints paused",
                detail);
        }
    }
}
#endif
