using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Aligns underground spawn to the maze route start in world space (parent-safe).</summary>
    public static class CaveSpawnAlignmentUtility
    {
        public const float SpawnHeightAboveFloor = 1.05f;

        public static CaveMazeLayout TryResolveLayout(Transform caveRoot, WorldGenerationRequest request)
        {
            if (request != null)
            {
                return CaveMazeLayoutGenerator.Generate(
                    request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
            }

            var meta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
            if (meta == null)
                return null;

            return CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
        }

        public static bool AlignSpawnToMazeStart(Transform caveRoot, CaveMazeLayout layout)
        {
            if (caveRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
                return false;

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
                return false;

            var start = layout.SolutionPath[0];
            var floorLocal = layout.GetFloorSurfaceLocal(start.x, start.y);
            var worldPos = caveRoot.TransformPoint(floorLocal + Vector3.up * SpawnHeightAboveFloor);

            var forward = Vector3.forward;
            if (layout.SolutionPath.Count > 1)
            {
                var nextLocal = layout.CellToLocal(layout.SolutionPath[1].x, layout.SolutionPath[1].y);
                var tangent = nextLocal - floorLocal;
                tangent.y = 0f;
                if (tangent.sqrMagnitude > 0.01f)
                    forward = tangent.normalized;
            }

            var worldRot = caveRoot.rotation * Quaternion.LookRotation(forward, Vector3.up);
            CaveEditorUndo.RecordObject(spawn, "Align Cave Spawn To Maze Start");
            spawn.SetPositionAndRotation(worldPos, worldRot);

            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));
            return true;
        }

        public static Vector3? GetMazeStartWorldFloor(Transform caveRoot, CaveMazeLayout layout)
        {
            if (caveRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count == 0)
                return null;

            var start = layout.SolutionPath[0];
            var floorLocal = layout.GetFloorSurfaceLocal(start.x, start.y);
            return caveRoot.TransformPoint(floorLocal);
        }

        /// <summary>Raycast snap spawn onto walkable floor (fixes teleport fall-through).</summary>
        public static bool SnapSpawnToWalkSurface(Transform caveRoot)
        {
            var spawn = caveRoot != null
                ? caveRoot.Find("Entrance/CaveEntrance_SpawnPoint")
                : null;
            if (spawn == null)
                return false;

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                var layout = TryResolveLayout(caveRoot, null);
                if (layout != null)
                    AlignSpawnToMazeStart(caveRoot, layout);
            }

            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));
            Physics.SyncTransforms();
            CaveEditorUndo.RecordObject(spawn, "Snap Cave Spawn To Walk Surface");
            var pad = spawn.Find(CaveSpawnPadUtility.PadName);
            var probe = pad != null ? pad.position + Vector3.up * 0.5f : spawn.position + Vector3.up * 0.5f;
            return PlayerGroundSnap.SnapTransform(spawn, probe);
        }
    }
}
