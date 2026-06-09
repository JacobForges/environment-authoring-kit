#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Captures in-memory build position before Play Mode so Continue can resume without restarting ground lay.
    /// </summary>
    public static class CaveBuildPlaytestSessionSnapshot
    {
        public const string RelPath = "Assets/EnvironmentKit/Generated/CaveBuildPlaytestSessionSnapshot.json";

        [Serializable]
        public sealed class SnapshotDoc
        {
            public string capturedUtc;
            public string reason;
            public int pacedStep;
            public string pacedLabel;
            public int conceptIndex = -1;
            public int seed;
            public int terrainCount;
            public bool buildInProgress;
            public bool startupActive;
            public bool gridPipelineActive;
            public int researchSubStep = -1;
        }

        public static void Capture(string reason)
        {
            var request = CaveBuildAaaSessionPolicy.ActiveRequest;
            var doc = new SnapshotDoc
            {
                capturedUtc = DateTime.UtcNow.ToString("o"),
                reason = reason ?? string.Empty,
                pacedStep = CaveBuildStepCounter.HasSession ? CaveBuildStepCounter.Current : 0,
                pacedLabel = CaveBuildRunStatusPublisher.SubOperationDetail ?? string.Empty,
                conceptIndex = request?.ConceptLayoutIndex ?? CaveBuildConceptSession.ResolveLockedConceptIndex(),
                seed = request?.Seed ?? 0,
                terrainCount = UnityEngine.Object.FindObjectsByType<Terrain>().Length,
                buildInProgress = LavaTubeCaveBuilder.IsBuildInProgress,
                startupActive = CaveBuildStartupCoordinator.IsActive,
                gridPipelineActive = CaveBuildSurfaceCompletionGate.IsFullWorldGridPipelineActive,
                researchSubStep = CaveBuildPrePlacementResearch.LastActiveSubStep,
            };

            if (doc.seed == 0 && CaveBuildPacedStepPersistence.TryLoadSnapshot(out var paced) && paced.seed != 0)
            {
                doc.seed = paced.seed;
                if (doc.pacedStep <= 0)
                    doc.pacedStep = paced.pacedStep;
                if (string.IsNullOrEmpty(doc.pacedLabel))
                    doc.pacedLabel = paced.label;
                if (doc.conceptIndex < 0)
                    doc.conceptIndex = paced.conceptIndex;
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var abs = Path.Combine(hub, RelPath);
            File.WriteAllText(abs, JsonUtility.ToJson(doc, true) + "\n");
        }

        public static void Clear()
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

        public static bool TryLoad(out SnapshotDoc doc)
        {
            doc = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, RelPath);
            if (!File.Exists(abs))
                return false;

            try
            {
                doc = JsonUtility.FromJson<SnapshotDoc>(File.ReadAllText(abs));
                return doc != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
