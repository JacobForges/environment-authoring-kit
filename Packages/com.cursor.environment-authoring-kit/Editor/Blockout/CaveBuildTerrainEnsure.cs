#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEngine;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Ensures Unity Terrain for cave/surface pipeline (ignores Environment Kit "never create terrain" prefs).
    /// </summary>
    public static class CaveBuildTerrainEnsure
    {
        public static Terrain TryEnsure(
            SceneGroundInfo ground,
            Transform caveRoot,
            int seed,
            out string message)
        {
            message = null;
            var existing = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            if (existing == null)
                existing = ActiveSceneUtility.FindInActiveScene<Terrain>();
            if (existing != null && existing.name.StartsWith("SurfaceTerrainTile_", StringComparison.Ordinal))
            {
                message =
                    $"Ignoring orphan play tile '{existing.name}' as main — run Full AAA Rebuild or purge play disk.";
                existing = SurfaceTerrainTileExpansion.FindMainTerrainInScene();
            }

            if (existing != null)
            {
                if (ground != null)
                {
                    ground.Terrain = existing;
                    if (ground.Anchor == null ||
                        SceneGroundResolver.IsModularKitBlockoutGrid(ground.Anchor) ||
                        ground.Anchor.GetComponent<Terrain>() == null)
                        ground.Anchor = existing.transform;
                }

                SurfaceTerrainTileExpansion.EnsureMainTerrainIdentity(existing);
                return existing;
            }

            if (ground == null || !ground.HasAnchor)
            {
                message = "Cannot create terrain — tag walkable floor as Ground or assign anchor in Environment Kit.";
                return null;
            }

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            CaveTerrainIntegrationUtility.EnsureSceneTerrain(
                envRoot.transform,
                ground,
                caveRoot,
                seed,
                out message);

            existing = ActiveSceneUtility.FindInActiveScene<Terrain>();
            if (existing != null)
            {
                SurfaceTerrainTileExpansion.EnsureMainTerrainIdentity(existing);
                if (ground != null)
                {
                    ground.Terrain = existing;
                    ground.Anchor = existing.transform;
                    SceneGroundResolver.RefreshSurface(ground);
                }

                return existing;
            }

            if (string.IsNullOrEmpty(message))
            {
                message =
                    "Terrain creation failed. Check Environment Kit window or add a Terrain manually under the environment root.";
            }

            return null;
        }
    }
}
#endif
