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
        static bool _provisionalFullWorldExtended;

        public static bool IsFullAaaRebuild { get; private set; }
        public static bool IsRecording { get; private set; }

        /// <summary>True from Hub queue until request bind — Build Complete Cave defaults to ~289 tiles.</summary>
        public static void MarkProvisionalFullWorldExtended(bool active) =>
            _provisionalFullWorldExtended = active;

        /// <summary>Flat ~289-tile grid after 9 play tiles, random boss tree, biome sculpt, one grid weld.</summary>
        public static bool UsesExtendedOpenWorldGrid
        {
            get
            {
                if (IsFullAaaRebuild)
                    return true;

                if (_activeRequest?.SurfaceScope == SurfaceBuildScope.FullWorld)
                    return _activeRequest.UseExtendedOpenWorldGrid;

                if (_activeRequest == null)
                {
                    var estimate = FullWorldConceptLayoutCatalog.CreateHubBoundRequest(
                        0,
                        CaveBuildConceptSession.ResolveLockedConceptIndex());
                    return estimate.UseExtendedOpenWorldGrid;
                }

                return _provisionalFullWorldExtended;
            }
        }

        /// <summary>Always refresh generate-phase-prompts + research bundle per active pipeline phase.</summary>
        public static bool ForcePerPhasePromptExport =>
            IsFullAaaRebuild;

        /// <summary>Prefer additive surface updates — repair grid/content before replace.</summary>
        public static bool ForceNonAdditiveSurface =>
            false;

        public static WorldGenerationRequest ActiveRequest => _activeRequest;

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
            _provisionalFullWorldExtended = false;
        }
    }
}
#endif
