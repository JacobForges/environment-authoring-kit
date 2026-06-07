using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Single authority for MainScene portal → underground maze-start spawn (not surface mouth).
    /// </summary>
    public static class CaveSpawnTeleportAuthority
    {
        public static Transform ApplyMainAreaTeleportSpawn(
            Transform caveRoot,
            WorldGenerationRequest request = null)
        {
            if (caveRoot == null)
                return null;

            var layout = CaveSpawnAlignmentUtility.TryResolveLayout(caveRoot, request);
            Transform spawn = null;
            if (layout != null && layout.SolutionPath != null && layout.SolutionPath.Count > 0)
            {
                CaveSpawnAlignmentUtility.AlignSpawnToMazeStart(caveRoot, layout);
                spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
                if (spawn != null)
                {
                    var marker = spawn.GetComponent<CaveEntranceSpawnPoint>();
                    if (marker == null)
                        marker = CaveEditorUndo.GetOrAddComponent<CaveEntranceSpawnPoint>(spawn.gameObject);
                    marker.positionOffset = Vector3.zero;
                    marker.applyRotation = true;
                    marker.snapPlayerOnStart = false;
                    marker.teleportFromMainAreaUsesMazeStart = true;
                    CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));
                }
            }
            else
            {
                var entrance = caveRoot.Find("Entrance");
                var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
                if (entrance != null && authoring != null && authoring.Knots != null && authoring.Knots.Count >= 2)
                {
                    var spline = new CaveSplinePath();
                    spline.SetKnots(authoring.Knots);
                    spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(
                        caveRoot, entrance, spline, keepAtSurfaceMouth: false, layout);
                }
            }

            spawn ??= caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
                return null;

            CavePortalSpawnLinker.LinkScenePortals(caveRoot, spawn);
            Debug.Log(
                $"[CaveSpawn] Main-area teleport → {spawn.name} world {spawn.position} " +
                $"(maze route, portal linked).");
            return spawn;
        }
    }
}
