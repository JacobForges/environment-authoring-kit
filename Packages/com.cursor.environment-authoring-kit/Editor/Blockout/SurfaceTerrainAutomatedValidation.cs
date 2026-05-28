#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Above-ground validation only — surface route + walk-in entrance.
    /// Runs from the terrain pipeline (not the cave queued pipeline).
    /// </summary>
    public static class SurfaceTerrainAutomatedValidation
    {
        public const int StepCount = 2;

        public static readonly string[] StepLabels =
        {
            "Surface route bot (trails → cave mouth)",
            "Surface walk-in entrance",
        };

        public static void RunAll(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (caveRoot == null || !CaveGeometryPaths.IsAdventureCave(caveRoot))
                return;

            for (var i = 0; i < StepCount; i++)
                RunStep(i, caveRoot, ground, request);
        }

        public static void RunStep(
            int step,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            if (caveRoot == null)
                return;

            switch (step)
            {
                case 0:
                    RunSurfaceRouteProbe(caveRoot);
                    break;
                case 1:
                    CaveBuildAutomatedValidation.RunSurfaceEntranceCheckPublic(
                        caveRoot, ground, request);
                    break;
            }
        }

        static void RunSurfaceRouteProbe(Transform caveRoot)
        {
            var report = SurfaceRouteProbeRunner.Run(caveRoot);
            SurfaceRouteProbeRunner.Export(report);
            if (report.Passed)
            {
                CaveBuildEditorLog.LogSurface("[Terrain] Surface route bot: PASS (trails → mouth).");
                return;
            }

            CaveBuildEditorLog.LogSurfaceWarning(
                $"[Terrain] Surface route bot: {report.Issues.Count} issue(s) — see {SurfaceRouteProbeRunner.ReportPath}");
        }
    }
}
#endif
