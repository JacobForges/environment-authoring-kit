#if UNITY_EDITOR
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Mandatory research sync before any geometry, terrain, or prop placement in the queued pipeline.
    /// </summary>
    public static partial class CaveBuildPrePlacementResearch
    {
        public const string GateRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildPrePlacementResearchGate.json";

        [System.Serializable]
        public class GateFile
        {
            public bool passed;
            public string completedUtc;
            public string message;
            public bool additiveSurface;
            public int seed;
        }

        public static bool IsGatePassedForSeed(int seed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GateRel);
            if (!File.Exists(path))
                return false;
            try
            {
                var gate = JsonUtility.FromJson<GateFile>(File.ReadAllText(path));
                return gate.passed && gate.seed == seed;
            }
            catch
            {
                return false;
            }
        }

        public static void WriteGateAfterActivePrompt(
            WorldGenerationRequest request,
            bool additiveSurface,
            ref string accumulatedMessage,
            out string message)
        {
            var seed = request?.Seed ?? 0;
            message =
                $"Research ready. {accumulatedMessage} | " +
                "Additive build on existing land — no radial overwrite of center disk.";
            WriteGate(true, message, additiveSurface, seed);
            CaveBuildRunStatusPublisher.SetResearchPhase(
                message,
                CaveBuildRunStatusPublisher.ResearchGateState.Passed);
            Debug.Log("[CaveBuild] Pre-placement research gate PASSED — placement may begin.");
        }

        public static bool RunBeforeAnyPlacement(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            bool additiveSurface,
            out string message)
        {
            message = string.Empty;
            var seed = request?.Seed ?? 0;
            if (IsGatePassedForSeed(seed))
            {
                message = "Pre-placement research already completed for this seed.";
                return true;
            }
            CaveBuildRunStatusPublisher.SetResearchPhase(
                "Syncing ResearchCache + Florida hillshades + execution brief…",
                CaveBuildRunStatusPublisher.ResearchGateState.InProgress);

            if (!CaveBuildResearchCacheBridge.SyncFullResearchPull("terrain_integration", out var syncMsg))
            {
                if (!CaveBuildResearchCacheBridge.HasUsableLocalResearchCache())
                {
                    message = "Pre-placement research sync failed: " + syncMsg;
                    WriteGate(false, message, additiveSurface, seed);
                    CaveBuildRunStatusPublisher.SetResearchPhase(
                        message,
                        CaveBuildRunStatusPublisher.ResearchGateState.Failed);
                    return false;
                }

                syncMsg += " | Using existing ResearchCache on disk (offline/degraded mode).";
                Debug.LogWarning("[CaveBuild] Pre-placement research: " + syncMsg);
            }

            CaveBuildUnifiedPromptBridge.RefreshForPhase(
                "research",
                "research",
                -1,
                0,
                seed,
                out var promptsMsg);

            message =
                $"Research ready. {syncMsg} | {promptsMsg} | " +
                "Additive build on existing land — no radial overwrite of center disk.";
            WriteGate(true, message, additiveSurface, seed);
            CaveBuildRunStatusPublisher.SetResearchPhase(
                message,
                CaveBuildRunStatusPublisher.ResearchGateState.Passed);
            Debug.Log("[CaveBuild] Pre-placement research gate PASSED — placement may begin.");
            return true;
        }

        static void WriteGate(bool passed, string message, bool additive, int seed)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, GateRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var gate = new GateFile
            {
                passed = passed,
                completedUtc = System.DateTime.UtcNow.ToString("o"),
                message = message,
                additiveSurface = additive,
                seed = seed,
            };
            File.WriteAllText(path, JsonUtility.ToJson(gate, true));
        }
    }
}
#endif
