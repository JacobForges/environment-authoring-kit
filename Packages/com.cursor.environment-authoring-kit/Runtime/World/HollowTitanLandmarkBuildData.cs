using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Authoring metadata on the Hollow Titan root — site XZ, scale, floor plan.</summary>
    public sealed class HollowTitanLandmarkBuildData : MonoBehaviour
    {
        [SerializeField] int buildSeed;
        [SerializeField] int floorCount = 7;
        [SerializeField] float trunkRadius = 58f;
        [SerializeField] float trunkHeight = 172f;
        [SerializeField] float baseClearanceMeters = 0.35f;
        [SerializeField] float floorHeightMeters = 9f;
        [SerializeField] Vector3 siteWorldPosition;
        [SerializeField] float entranceYawDegrees;
        [SerializeField] string anchorTerrainName = string.Empty;
        [SerializeField] bool meatPhasesComplete;

        public int BuildSeed => buildSeed;
        public int FloorCount => floorCount;
        public float TrunkRadius => trunkRadius;
        public float TrunkHeight => trunkHeight;
        public float BaseClearanceMeters => baseClearanceMeters;
        public float FloorHeightMeters => floorHeightMeters;
        public Vector3 SiteWorldPosition => siteWorldPosition;
        public float EntranceYawDegrees => entranceYawDegrees;
        public bool MeatPhasesComplete => meatPhasesComplete;

        public void Configure(
            int seed,
            int floors,
            float radius,
            float height,
            float clearance,
            float floorHeight,
            Vector3 site,
            string terrainName,
            float entranceYaw = float.NaN)
        {
            buildSeed = seed;
            floorCount = Mathf.Max(2, floors);
            trunkRadius = radius;
            trunkHeight = height;
            baseClearanceMeters = clearance;
            floorHeightMeters = floorHeight;
            siteWorldPosition = site;
            anchorTerrainName = terrainName ?? string.Empty;
            meatPhasesComplete = false;
            if (!float.IsNaN(entranceYaw))
                entranceYawDegrees = entranceYaw;
        }

        public void SetEntranceYaw(float yawDegrees) => entranceYawDegrees = yawDegrees;

        public void SetTrunkRadius(float radiusMeters) => trunkRadius = Mathf.Max(4f, radiusMeters);

        public void MarkMeatPhasesComplete() => meatPhasesComplete = true;
    }
}
