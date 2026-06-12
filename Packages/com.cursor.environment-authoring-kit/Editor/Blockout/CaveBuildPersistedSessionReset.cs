#if UNITY_EDITOR
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Clears on-disk resume/checkpoint state when the operator starts Build Complete Cave
    /// so stale JSON never collides with a fresh run.
    /// </summary>
    public static class CaveBuildPersistedSessionReset
    {
        public static void ClearForNewBuild(string reason = "new build session")
        {
            CaveBuildFullWorldGridCheckpoint.Clear(reason);
            CaveBuildPacedStepPersistence.ClearCheckpoint();
            CaveBuildFreshBuildSession.ClearIncrementalArtifacts();
            CaveBuildStepCounter.EndSession();
            CaveBuildMemoryGuard.ClearMemoryPause();
            CaveBuildPauseController.ClearOnNewBuildSession();
            SurfaceTerrainTileExpansion.ResetHollowTitanTerrainGateForNewBuild();

            var sceneName = SceneManager.GetActiveScene().name;
            CaveBuildSessionScratchReset.ClearAll(reason, sceneName);

            CaveBuildEditorLog.LogSurface(
                $"[Build] Cleared persisted resume/checkpoint state ({reason}).",
                forceUnityConsole: false);
        }
    }
}
#endif
