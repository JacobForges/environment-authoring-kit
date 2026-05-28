using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Collects surface trail/road waypoints then cave mouth for play-mode bot walk order.</summary>
    public static class SurfacePlaytestRouteCollector
    {
        public const string SurfaceRootName = "GeneratedSurfaceWorld";
        public const string TrailsName = "Trails";
        public const string RoadsName = "Roads";
        public const string CaveOpeningsName = "CaveOpenings";

        public static List<Vector3> CollectWorldWaypoints(Transform caveRoot)
        {
            var list = new List<Vector3>();
            var env = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            var surface = env != null ? env.transform.Find(SurfaceRootName) : null;
            if (surface != null)
            {
                var target = caveRoot != null ? ResolveMouthWorld(caveRoot) : Vector3.zero;
                var trails = surface.Find(TrailsName);
                var primary = SelectPrimaryTrail(trails, target);
                if (primary != null)
                    AppendWaypoints(primary, list);
                else
                    AppendWaypoints(trails, list);

                var openings = surface.Find(CaveOpeningsName);
                if (openings != null && openings.childCount > 0)
                {
                    var openingPos = openings.GetChild(0).position;
                    if (list.Count == 0 || Vector3.Distance(list[list.Count - 1], openingPos) > 3f)
                        list.Add(openingPos);
                }
            }

            if (caveRoot != null)
            {
                var mouth = ResolveMouthWorld(caveRoot);
                if (list.Count == 0 || Vector3.Distance(list[list.Count - 1], mouth) > 4f)
                    list.Add(mouth);
            }

            return list;
        }

        static Transform SelectPrimaryTrail(Transform trailsRoot, Vector3 routeTarget)
        {
            if (trailsRoot == null || trailsRoot.childCount == 0)
                return null;

            Transform best = null;
            var bestDist = float.MaxValue;
            foreach (Transform trail in trailsRoot)
            {
                if (trail == null)
                    continue;
                Vector3? end = null;
                foreach (Transform child in trail)
                {
                    if (child != null && child.name.StartsWith("Waypoint_"))
                        end = child.position;
                }

                if (!end.HasValue)
                    continue;
                var endDist = Vector3.Distance(end.Value, routeTarget);
                if (endDist < bestDist)
                {
                    bestDist = endDist;
                    best = trail;
                }
            }

            return best ?? trailsRoot.GetChild(0);
        }

        static void AppendWaypoints(Transform trailOrRoot, List<Vector3> list)
        {
            if (trailOrRoot == null)
                return;

            if (trailOrRoot.name.StartsWith("Trail_"))
            {
                foreach (Transform child in trailOrRoot)
                {
                    if (child != null && child.name.StartsWith("Waypoint_"))
                        list.Add(child.position);
                }

                return;
            }

            foreach (Transform trail in trailOrRoot)
            {
                if (trail == null)
                    continue;
                foreach (Transform child in trail)
                {
                    if (child != null && child.name.StartsWith("Waypoint_"))
                        list.Add(child.position);
                }
            }
        }

        static Vector3 ResolveMouthWorld(Transform caveRoot)
        {
            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                return entrance.position;
            return caveRoot.position;
        }
    }
}
