#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Full AAA Rebuild / Full AAA Rebuild + Recording session flags (not normal Build Complete Cave).
    /// </summary>
    public static class CaveBuildAaaSessionPolicy
    {
        static WorldGenerationRequest _activeRequest;

        public static bool IsFullAaaRebuild { get; private set; }
        public static bool IsRecording { get; private set; }

        /// <summary>Flat ~289-tile grid after 9 play tiles, random boss tree, biome sculpt, one grid weld.</summary>
        public static bool UsesExtendedOpenWorldGrid =>
            IsFullAaaRebuild ||
            (_activeRequest?.SurfaceScope == SurfaceBuildScope.FullWorld &&
             _activeRequest.UseExtendedOpenWorldGrid);

        /// <summary>Always refresh generate-phase-prompts + research bundle per active pipeline phase.</summary>
        public static bool ForcePerPhasePromptExport =>
            IsFullAaaRebuild;

        /// <summary>Prefer additive surface updates — repair grid/content before replace.</summary>
        public static bool ForceNonAdditiveSurface =>
            false;

        public static void BindActiveRequest(WorldGenerationRequest request) =>
            _activeRequest = request;

        public static void BeginFullAaaRebuild(bool recording)
        {
            IsFullAaaRebuild = true;
            IsRecording = recording;
            CaveBuildEditorQueueSafeguard.ResetForNewBuildSession();
            CaveBuildSurfaceTerrainLock.ResetForNewBuildSession();
            CaveBuildPromptExportSession.ClearSkipCacheForAaaRebuild();
        }

        public static void EndSession()
        {
            IsFullAaaRebuild = false;
            IsRecording = false;
            _activeRequest = null;
        }
    }
}
#endif
