using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Before cave generation: export context, then Cursor research → compile → readiness ladder (mirrors post-build).</summary>
    public static class CaveBuildPreBuildWorkflow
    {
        /// <returns>True if Cursor pre-build agent was started.</returns>
        public static bool Begin(
            CaveBuildPreBuildReport report,
            SceneGroundInfo ground,
            WorldGenerationRequest request = null)
        {
            if (report == null)
                return false;

            CaveBuildPreBuildWorkflowExporter.WriteInitial(report);
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildResearchExporter.WriteMinimal(hub, report);
            CaveBuildCompileGate.ExportDiagnostics(hub);
            CaveBuildAgentMemoryExporter.SyncToDisk();

            UnityEngine.Debug.Log(
                "[CaveBuild] Pre-build workflow: research → plan → compile_gate → readiness_ladder. " +
                "See Assets/EnvironmentKit/Generated/CaveBuildPreBuildWorkflowContext.json");

            if (CaveBuildCursorAgentBridge.TryBeginPreBuildWorkflow(report, ground, request))
                return true;

            UnityEngine.Debug.LogWarning(
                "[CaveBuild] Pre-build Cursor workflow did not start — check API key and Console.");
            return false;
        }
    }
}
