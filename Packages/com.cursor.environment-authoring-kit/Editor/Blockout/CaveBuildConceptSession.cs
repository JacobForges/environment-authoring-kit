#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Locks the active Hub concept (0–9) for the whole build session — resume, checkpoints, and pipeline
    /// always re-apply the same preset flags (tile plan, enhancements, style id).
    /// </summary>
    public static class CaveBuildConceptSession
    {
        public const string LockRelPath = "Assets/EnvironmentKit/Generated/CaveBuildSessionLockedConcept.json";

        [Serializable]
        sealed class LockDoc
        {
            public int conceptIndex = -1;
            public string styleId;
            public int seed;
            public string lockedUtc;
        }

        static int? _memoryLock;

        /// <summary>True only while a build is actually running — not from stale lock files on disk.</summary>
        public static bool IsBuildSessionLocked() =>
            CaveBuildHubSessionReconcile.IsPacedWorkActive();

        /// <summary>Authoritative concept for this build — checkpoints beat Hub dropdown.</summary>
        public static int ResolveLockedConceptIndex()
        {
            if (CaveBuildAaaSessionPolicy.ActiveRequest?.ConceptLayoutIndex is >= 0 and var active)
                return Mathf.Clamp(active, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);

            if (_memoryLock is >= 0 and var mem)
                return mem;

            if (ReadLockDoc(out var lockDoc) && lockDoc.conceptIndex >= 0)
                return Mathf.Clamp(lockDoc.conceptIndex, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);

            if (CaveBuildPlaytestSessionSnapshot.TryLoad(out var playtest) && playtest.conceptIndex >= 0)
                return Mathf.Clamp(playtest.conceptIndex, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);

            if (CaveBuildPacedStepPersistence.TryLoadSnapshot(out var paced) && paced.conceptIndex >= 0)
                return Mathf.Clamp(paced.conceptIndex, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);

            if (CaveBuildFullWorldGridCheckpoint.TryPeekConceptIndex(out var gridConcept))
                return gridConcept;

            return FullWorldGenerationStylePreset.LoadSelectedIndex();
        }

        public static void LockForBuild(int conceptIndex, int seed = 0, bool syncHubDropdown = true)
        {
            conceptIndex = Mathf.Clamp(conceptIndex, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);
            _memoryLock = conceptIndex;
            if (syncHubDropdown)
                FullWorldGenerationStylePreset.SaveSelectedIndex(conceptIndex);

            var doc = new LockDoc
            {
                conceptIndex = conceptIndex,
                styleId = FullWorldConceptLayoutCatalog.GetStyleId(conceptIndex),
                seed = seed,
                lockedUtc = DateTime.UtcNow.ToString("o"),
            };
            WriteLockDoc(doc);
        }

        public static void ClearLock()
        {
            _memoryLock = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, LockRelPath);
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

        /// <summary>Apply preset flags, bind session policy, refresh step budget, persist lock file.</summary>
        public static void ApplyAndBind(WorldGenerationRequest request, int? conceptOverride = null, bool syncHubDropdown = true)
        {
            if (request == null)
                return;

            var idx = conceptOverride ??
                      (request.ConceptLayoutIndex >= 0
                          ? request.ConceptLayoutIndex
                          : ResolveLockedConceptIndex());
            FullWorldConceptLayoutCatalog.ApplyByIndex(request, idx);
            CaveBuildAaaSessionPolicy.BindActiveRequest(request);
            CaveBuildPipelinePhaseTracker.RefreshProvisionalExtended(request);
            CaveBuildStepCounter.ConfigureForRequest(request);
            FullWorldGenerationStylePreset.WriteActiveStyleFile(request, idx);
            LockForBuild(idx, request.Seed, syncHubDropdown);
        }

        public static bool ValidateTilePlan(int conceptIndex, int tileCount, out string warning)
        {
            warning = null;
            conceptIndex = Mathf.Clamp(conceptIndex, 0, FullWorldConceptLayoutCatalog.ConceptCount - 1);
            var expected = FullWorldConceptLayoutCatalog.ExpectedTerrainTileCount(
                FullWorldConceptLayoutCatalog.CreateHubBoundRequest(0, conceptIndex));
            if (tileCount <= 0 || expected <= 0)
                return true;

            if (tileCount <= expected)
                return true;

            warning =
                $"Concept {conceptIndex} ({FullWorldConceptLayoutCatalog.GetDisplayName(conceptIndex)}) plans ~{expected} tiles " +
                $"but checkpoint/scene has {tileCount} — enforcing preset tile plan (~{expected}).";
            return false;
        }

        static bool ReadLockDoc(out LockDoc doc)
        {
            doc = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, LockRelPath);
            if (!File.Exists(abs))
                return false;

            try
            {
                doc = JsonUtility.FromJson<LockDoc>(File.ReadAllText(abs));
                return doc != null && doc.conceptIndex >= 0;
            }
            catch
            {
                return false;
            }
        }

        static void WriteLockDoc(LockDoc doc)
        {
            if (doc == null)
                return;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var abs = Path.Combine(hub, LockRelPath);
            File.WriteAllText(abs, JsonUtility.ToJson(doc, true) + "\n");
        }
    }
}
#endif
