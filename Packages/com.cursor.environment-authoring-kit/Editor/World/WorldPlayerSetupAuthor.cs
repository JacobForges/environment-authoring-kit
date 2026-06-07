#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Plan v4 — ensure Player tag + <see cref="PlayerPersistence"/> on the play character.</summary>
    public static class WorldPlayerSetupAuthor
    {
        public const string PlayerTag = WorldPlayerSetupRuntime.PlayerTag;

        [MenuItem("Window/Environment Kit/World/Wire Player Persistence (active scene)")]
        public static void WireFromMenu() => WireActiveScenePlayer();

        /// <summary>Editor menu + edit-mode wiring; runtime play uses <see cref="WorldPlayerSetupRuntime"/>.</summary>
        public static bool WireActiveScenePlayer()
        {
            var ok = WorldPlayerSetupRuntime.WireScenePlayer();
            if (ok)
                WorldPlanV4RuntimeSystemsAuthor.EnsureInScene();

            var player = WorldPlayerSetupRuntime.ResolvePlayerRoot();
            if (player != null && !Application.isPlaying)
                EditorUtility.SetDirty(player);

            var mainTerrain = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (mainTerrain != null)
            {
                var ground = SceneGroundResolver.ResolveForFullWorld(mainTerrain.transform);
                LavaTubeCaveBuildPipeline.EnsurePlayDiskCenterSpawn(ground);
            }

            return ok;
        }
    }
}
#endif
