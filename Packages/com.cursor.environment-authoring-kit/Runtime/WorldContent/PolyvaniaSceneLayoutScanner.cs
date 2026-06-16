using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.WorldContent
{
    /// <summary>Measures PolyvaniaTownMap layout from the open scene (hub, arms, wild, bounds).</summary>
    public static class PolyvaniaSceneLayoutScanner
    {
        public const float WildOutsideMapM = 18f;
        public const float RoadArmInsetM = 6f;
        public const float PlazaNpcRingM = 7f;

        public struct ScanResult
        {
            public bool foundMap;
            public Vector3 hubCenter;
            public float hubRadiusM;
            public Bounds mapBounds;
            public Vector3 playerSpawn;
            public Vector3? dtaCourseCenter;
            public Vector3 townCenter;
            public Vector3 eastTrail;
            public Vector3 caveGate;
            public Vector3 battleArena;
            public Vector3 wildEast;
            public Vector3 wildWest;
            public List<SidewalkSample> plazaSidewalks;
        }

        public struct SidewalkSample
        {
            public string id;
            public Vector3 position;
            public float rotationY;
        }

        public static ScanResult Scan()
        {
            var result = new ScanResult { plazaSidewalks = new List<SidewalkSample>(8) };

            var spawn = GameObject.Find("PlayerSpawnPoint");
            result.playerSpawn = spawn != null ? spawn.transform.position : Vector3.zero;

            var map = GameObject.Find(PolyvaniaSocialHubResolver.MapObjectName);
            if (map == null)
                return result;

            result.foundMap = true;
            result.mapBounds = ComputeMapBounds(map.transform);

            if (!PolyvaniaSocialHubResolver.TryResolveFourWayCenter(out result.hubCenter, out result.hubRadiusM))
                result.hubCenter = result.mapBounds.center;

            if (ContentLayoutGroundSnap.TrySampleContentGroundY(result.hubCenter, out var hubY))
                result.hubCenter.y = hubY;

            result.townCenter = result.hubCenter;
            result.eastTrail = SnapPoint(ArmPoint(result.hubCenter, result.mapBounds, +1, 0, RoadArmInsetM));
            result.caveGate = SnapPoint(ArmPoint(result.hubCenter, result.mapBounds, 0, +1, RoadArmInsetM));
            var westArm = SnapPoint(ArmPoint(result.hubCenter, result.mapBounds, -1, 0, RoadArmInsetM));

            var dta = GameObject.Find("DTA_FloatingCourse");
            if (dta != null)
            {
                result.dtaCourseCenter = BoundsCenterOnGround(dta);
                result.battleArena = result.dtaCourseCenter.Value;
            }
            else
            {
                result.battleArena = westArm;
            }

            result.wildEast = SnapPoint(new Vector3(
                result.mapBounds.max.x + WildOutsideMapM,
                result.hubCenter.y,
                result.hubCenter.z));
            result.wildWest = SnapPoint(new Vector3(
                result.mapBounds.min.x - WildOutsideMapM,
                result.hubCenter.y,
                result.hubCenter.z - 8f));

            BuildPlazaSidewalks(ref result);
            return result;
        }

        static void BuildPlazaSidewalks(ref ScanResult result)
        {
            var hub = result.hubCenter;
            TryAddSidewalk(result.plazaSidewalks, "west", SnapPoint(hub + new Vector3(-PlazaNpcRingM, 0f, -2f)), 90f);
            TryAddSidewalk(result.plazaSidewalks, "east", SnapPoint(hub + new Vector3(PlazaNpcRingM, 0f, 2f)), 270f);
            TryAddSidewalk(result.plazaSidewalks, "south", SnapPoint(hub + new Vector3(3f, 0f, -PlazaNpcRingM)), 180f);
            TryAddSidewalk(result.plazaSidewalks, "north", SnapPoint(hub + new Vector3(-2f, 0f, PlazaNpcRingM)), 180f);
            TryAddSidewalk(result.plazaSidewalks, "north_east", SnapPoint(hub + new Vector3(PlazaNpcRingM - 1f, 0f, 5f)), 225f);
            TryAddSidewalk(result.plazaSidewalks, "north_west", SnapPoint(hub + new Vector3(-PlazaNpcRingM + 1f, 0f, 5f)), 90f);
        }

        static void TryAddSidewalk(List<SidewalkSample> list, string id, Vector3 pos, float rotY)
        {
            if (!IsLikelyTownWalkable(pos, list))
                return;
            list.Add(new SidewalkSample { id = id, position = pos, rotationY = rotY });
        }

        static bool IsLikelyTownWalkable(Vector3 pos, List<SidewalkSample> existing)
        {
            if (!ContentLayoutGroundSnap.TrySampleContentGroundY(pos, out _))
                return false;

            foreach (var s in existing)
            {
                if (Vector3.Distance(s.position, pos) < 4f)
                    return false;
            }

            return true;
        }

        static Bounds ComputeMapBounds(Transform map)
        {
            var renderers = map.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return new Bounds(map.position, Vector3.one * 40f);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        static Vector3 ArmPoint(Vector3 hub, Bounds map, int xSign, int zSign, float inset)
        {
            if (xSign > 0)
                return new Vector3(map.max.x - inset, hub.y, hub.z);
            if (xSign < 0)
                return new Vector3(map.min.x + inset, hub.y, hub.z);
            if (zSign > 0)
                return new Vector3(hub.x, hub.y, map.max.z - inset);
            return new Vector3(hub.x, hub.y, map.min.z + inset);
        }

        static Vector3 BoundsCenterOnGround(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
                return go.transform.position;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    bounds.Encapsulate(renderers[i].bounds);
            }

            var p = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return SnapPoint(p);
        }

        static Vector3 SnapPoint(Vector3 world)
        {
            if (ContentLayoutGroundSnap.TrySampleContentGroundY(world, out var y))
                return new Vector3(world.x, y, world.z);
            return world;
        }
    }
}
