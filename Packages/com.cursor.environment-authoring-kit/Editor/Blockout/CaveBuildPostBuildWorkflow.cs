using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>First step after cave generation: export context, then Cursor research → compile → ladder.</summary>
    public static class CaveBuildPostBuildWorkflow
    {
        public static void BeginAfterCaveGeneration(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground)
        {
            if (report == null)
                return;

            CaveBuildWorkflowExporter.WriteInitial(report, caveRoot);
            var activeRung = CaveBuildPromptLadder.PickActiveRung(report, caveRoot, ground);
            CaveBuildResearchExporter.Write(report, activeRung, report.LadderGradePasses);
            CaveBuildAgentContextExporter.Export(report, caveRoot, -1, ground);

            UnityEngine.Debug.Log(
                "[CaveBuild] Post-build workflow: research → compile_gate → ladder. " +
                "See Assets/EnvironmentKit/Generated/CaveBuildWorkflowContext.json");

            if (!CaveBuildCursorAgentBridge.TryBeginPostBuildWorkflow(report, caveRoot, ground))
            {
                UnityEngine.Debug.LogWarning(
                    "[CaveBuild] Post-build Cursor workflow did not start — check API key and Console.");
            }
        }
    }
}
