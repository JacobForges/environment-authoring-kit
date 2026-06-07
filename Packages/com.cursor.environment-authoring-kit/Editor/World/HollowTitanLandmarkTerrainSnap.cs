#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Ground snap for Hollow Titan — resolves owning terrain tile and samples height at XZ.
    /// </summary>
    public static class HollowTitanLandmarkTerrainSnap
    {
        public static float ResolveBaseClearance()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            return Mathf.Max(0.05f, settings.hollowTitanBaseClearanceMeters);
        }

        /// <summary>World Y at XZ via <see cref="Terrain.SampleHeight"/> on the owning tile.</summary>
        public static bool TrySampleGroundY(
            Terrain mainTerrain,
            float worldX,
            float worldZ,
            out float worldY,
            out Terrain tile)
        {
            worldY = 0f;
            tile = mainTerrain;
            if (mainTerrain == null)
                return false;

            if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, worldX, worldZ, out var atTile) &&
                atTile != null)
                tile = atTile;

            if (tile?.terrainData == null)
                return false;

            worldY = tile.SampleHeight(new Vector3(worldX, 0f, worldZ)) + tile.transform.position.y;
            return true;
        }

        /// <summary>Seat landmark root base on terrain + configurable clearance.</summary>
        public static bool SnapRootBaseToGround(Terrain mainTerrain, Transform root, float clearanceMeters)
        {
            if (mainTerrain == null || root == null)
                return false;

            var pos = root.position;
            if (!TrySampleGroundY(mainTerrain, pos.x, pos.z, out var groundY, out _))
                return false;

            pos.y = groundY + clearanceMeters;
            root.position = pos;
            return true;
        }

        /// <summary>Re-seat using stored site XZ from build data.</summary>
        public static bool ResnapFromBuildData(Terrain mainTerrain, HollowTitanLandmarkBuildData data)
        {
            if (mainTerrain == null || data == null)
                return false;

            var site = data.SiteWorldPosition;
            if (!TrySampleGroundY(mainTerrain, site.x, site.z, out var groundY, out _))
                return false;

            var root = data.transform;
            var clearance = data.BaseClearanceMeters;
            root.position = new Vector3(site.x, groundY + clearance, site.z);
            return true;
        }
    }
}
#endif
