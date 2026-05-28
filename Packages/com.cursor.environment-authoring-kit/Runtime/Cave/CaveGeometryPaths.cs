using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Hierarchy paths for grid-aligned adventure cave geometry (Y-up, XZ floor).</summary>
    public static class CaveGeometryPaths
    {
        /// <summary>Scene root for generated underground cave (legacy name: LavaTubeCaveSystem).</summary>
        public const string CaveSystemRootName = "UndergroundCaveSystem";
        public const string LegacyCaveSystemRootName = "LavaTubeCaveSystem";

        public static Transform FindCaveSystemRoot()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var under = FindNamedCaveRootInHierarchy(grid.transform);
                if (under != null)
                    return under;
            }

            var env = Object.FindAnyObjectByType<EnvironmentRoot>();
            if (env != null)
            {
                var underEnv = FindNamedCaveRootInHierarchy(env.transform);
                if (underEnv != null)
                    return underEnv;
            }

            var direct = GameObject.Find(CaveSystemRootName);
            if (direct != null)
                return direct.transform;

            direct = GameObject.Find(LegacyCaveSystemRootName);
            if (direct != null)
                return direct.transform;

            foreach (var meta in Object.FindObjectsByType<CaveBuildMetadata>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (meta != null)
                    return meta.transform;
            }

            return null;
        }

        static Transform FindNamedCaveRootInHierarchy(Transform root)
        {
            if (root == null)
                return null;

            if (root.name == CaveSystemRootName || root.name == LegacyCaveSystemRootName)
                return root;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root)
                    continue;
                var n = t.name;
                if (n == CaveSystemRootName || n == LegacyCaveSystemRootName)
                    return t;
            }

            return null;
        }

        public const string GeometryRoot = "CaveGeometry";
        public const string BlockTunnel = "BlockTunnel";
        public const string AdventureShell = "AdventureShell";
        public const string PathPlatforms = "PathPlatforms";
        public const string RouteTerrainFloorName = "RouteTerrainFloor";
        public const string RouteTerrainCeilingName = "RouteTerrainCeiling";
        public const string RouteTerrainFloor = RouteTerrainFloorName;
        public const string RouteTerrainCeiling = RouteTerrainCeilingName;

        /// <summary>How far below the Ground surface the cave root is placed.</summary>
        public const float UndergroundDepthMeters = 8f;

        public static Transform FindBlockTunnel(Transform caveRoot)
        {
            if (caveRoot == null)
                return null;

            var geometry = caveRoot.Find(GeometryRoot);
            if (geometry != null)
            {
                var underGeometry = geometry.Find(BlockTunnel);
                if (underGeometry != null)
                    return underGeometry;
            }

            return caveRoot.Find(BlockTunnel);
        }

        public static bool IsAdventureCave(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var geometry = caveRoot.Find(GeometryRoot);
            if (geometry == null)
                return false;

            return geometry.Find(AdventureShell) != null ||
                   geometry.Find(BlockTunnel) != null ||
                   geometry.Find(PathPlatforms) != null;
        }
    }
}
