using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Builds overlapping, spline-aligned tunnel rings so floor / walls / ceiling read as one enclosed tube.
    /// </summary>
    public static class CaveSeamlessTunnelBuilder
    {
        const float FloorModuleSpan = 5f;
        const float RingStep = 3.6f;
        const float StepAdvance = 3.05f;
        const float SpanOverlap = 1.24f;
        const float WallSliceSpacing = 2.8f;

        public struct TunnelDimensions
        {
            public float Width;
            public float Height;
            public float NavClearance;
        }

        public static int BuildAlongSpline(
            Transform tunnelRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplinePath spline,
            TunnelDimensions dims,
            IReadOnlyList<Vector3> chamberCenters,
            float chamberRadius,
            bool placeLights)
        {
            if (spline == null || spline.TotalLength < 0.5f)
                return 0;

            var count = 0;
            var ringIndex = 0;
            for (var dist = 0f; dist < spline.TotalLength; dist += StepAdvance)
            {
                var distB = Mathf.Min(dist + RingStep, spline.TotalLength);
                var a = spline.SampleAtDistance(dist);
                var b = spline.SampleAtDistance(distB);

                if (IsInsideChamber(a.Position, chamberCenters, chamberRadius) &&
                    IsInsideChamber(b.Position, chamberCenters, chamberRadius))
                    continue;

                count += BuildEnclosedSpan(
                    tunnelRoot,
                    catalog,
                    rng,
                    a,
                    b,
                    $"Ring_{ringIndex:D3}",
                    placeLights && ringIndex % 2 == 0,
                    dims);
                ringIndex++;
            }

            return count;
        }

        public static int BridgeEntranceToPath(
            Transform tunnelRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 entranceEnd,
            CaveSplineSample pathStart,
            TunnelDimensions dims)
        {
            return BuildEnclosedSpan(
                tunnelRoot,
                catalog,
                rng,
                new CaveSplineSample
                {
                    Position = entranceEnd,
                    Tangent = pathStart.Tangent,
                    Right = pathStart.Right,
                    Up = pathStart.Up,
                    RadiusX = pathStart.RadiusX,
                    RadiusY = pathStart.RadiusY
                },
                pathStart,
                "Entrance_Bridge",
                true,
                dims);
        }

        static bool IsInsideChamber(Vector3 localPos, IReadOnlyList<Vector3> centers, float radius)
        {
            if (centers == null || radius <= 0f)
                return false;

            var r2 = radius * radius;
            foreach (var c in centers)
            {
                if ((c - localPos).sqrMagnitude < r2)
                    return true;
            }

            return false;
        }

        static int BuildEnclosedSpan(
            Transform parent,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            CaveSplineSample from,
            CaveSplineSample to,
            string label,
            bool placeLight,
            TunnelDimensions dims)
        {
            var delta = to.Position - from.Position;
            var length = delta.magnitude;
            if (length < 0.05f)
                return 0;

            var forward = delta.sqrMagnitude > 0.0001f ? delta.normalized : to.Tangent.normalized;
            var mid = (from.Position + to.Position) * 0.5f;
            var up = Vector3.Slerp(from.Up, to.Up, 0.5f).normalized;
            if (up.sqrMagnitude < 0.01f)
                up = Vector3.up;

            var moduleRoot = new GameObject($"Tunnel_{label}");
            CaveEditorUndo.RegisterCreated(moduleRoot, "Seamless Tunnel Ring");
            moduleRoot.transform.SetParent(parent, false);
            moduleRoot.transform.localPosition = mid;
            moduleRoot.transform.localRotation = Quaternion.LookRotation(forward, up);

            var module = moduleRoot.transform;
            var count = 0;
            var along = (length / FloorModuleSpan) * SpanOverlap;
            var floorScale = new Vector3(dims.Width / FloorModuleSpan, 1.05f, Mathf.Max(along, 0.65f));
            var halfW = dims.Width * 0.5f;

            count += CavePrefabScatter.PlaceModule(module, catalog.Pick(catalog.Floors, rng), Vector3.zero,
                Quaternion.identity, floorScale, "Floor", false) ? 1 : 0;
            count += CavePrefabScatter.PlaceModule(module, catalog.Pick(catalog.Ceilings, rng),
                new Vector3(0f, dims.Height, 0f), Quaternion.identity,
                Vector3.Scale(floorScale, new Vector3(1.02f, 1f, 1.04f)), "Ceiling", false) ? 1 : 0;
            count += CavePrefabScatter.PlaceModule(module, catalog.Pick(catalog.Ceilings, rng),
                new Vector3(0f, dims.Height + 0.9f, 0f), Quaternion.identity,
                Vector3.Scale(floorScale, new Vector3(1.18f, 0.9f, 1.16f)), "Ceiling_Seal", false) ? 1 : 0;

            count += PlaceDenseWalls(module, catalog, rng, halfW, dims.Height, length * SpanOverlap);

            if (catalog.Rockfalls.Count > 0 && rng.NextDouble() > 0.62)
            {
                count += CavePrefabScatter.PlaceModule(module, catalog.Pick(catalog.Rockfalls, rng),
                    new Vector3(-halfW * 0.82f, dims.Height * 0.35f, 0f),
                    Quaternion.Euler(0f, 25f, 8f), Vector3.one * 0.9f, "Wall_Infill_L", false) ? 1 : 0;
                count += CavePrefabScatter.PlaceModule(module, catalog.Pick(catalog.Rockfalls, rng),
                    new Vector3(halfW * 0.82f, dims.Height * 0.35f, 0f),
                    Quaternion.Euler(0f, -25f, -8f), Vector3.one * 0.9f, "Wall_Infill_R", false) ? 1 : 0;
            }

            if (placeLight)
                PlaceTunnelLight(module, dims.Height);

            EnsureNavClearance(module, length * SpanOverlap, dims);
            return count;
        }

        static int PlaceDenseWalls(
            Transform module,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            float halfWidth,
            float wallHeight,
            float segmentLength)
        {
            var count = 0;
            var slices = Mathf.Max(1, Mathf.CeilToInt(segmentLength / WallSliceSpacing));
            var scaleY = Mathf.Max(0.95f, wallHeight / 3.85f);
            var wallScale = new Vector3(1.08f, scaleY, 1.06f);
            var halfLen = segmentLength * 0.5f;

            for (var i = 0; i < slices; i++)
            {
                var t = (i + 0.5f) / slices;
                var z = Mathf.Lerp(-halfLen, halfLen, t);

                CaveVerticalWallPlacer.PlaceWallSlice(module, catalog, rng,
                    new Vector3(-halfWidth, wallHeight * 0.5f, z),
                    Quaternion.LookRotation(Vector3.right, Vector3.up), wallScale, ref count);
                CaveVerticalWallPlacer.PlaceWallSlice(module, catalog, rng,
                    new Vector3(halfWidth, wallHeight * 0.5f, z),
                    Quaternion.LookRotation(Vector3.left, Vector3.up), wallScale, ref count);
            }

            var capScale = new Vector3(halfWidth * 2.05f / FloorModuleSpan, scaleY, 1.08f);
            CaveVerticalWallPlacer.PlaceWallSlice(module, catalog, rng,
                new Vector3(0f, wallHeight * 0.5f, -halfLen),
                Quaternion.LookRotation(Vector3.forward, Vector3.up), capScale, ref count);
            CaveVerticalWallPlacer.PlaceWallSlice(module, catalog, rng,
                new Vector3(0f, wallHeight * 0.5f, halfLen),
                Quaternion.LookRotation(Vector3.back, Vector3.up), capScale, ref count);

            return count;
        }

        static void PlaceTunnelLight(Transform module, float tunnelHeight)
        {
            var lightGo = new GameObject("TunnelLight");
            CaveEditorUndo.RegisterCreated(lightGo, "Tunnel Light");
            lightGo.transform.SetParent(module, false);
            lightGo.transform.localPosition = new Vector3(0f, tunnelHeight * 0.82f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = RingStep * 1.65f;
            light.intensity = 0.52f;
            light.color = new Color(1f, 0.78f, 0.48f);
            CaveLightingSettings.ApplyCaveLight(light);
        }

        static void EnsureNavClearance(Transform moduleRoot, float length, TunnelDimensions dims)
        {
            var col = moduleRoot.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, dims.NavClearance * 0.5f, 0f);
            col.size = new Vector3(length * 0.94f, dims.NavClearance, dims.Width * 0.76f);
            col.gameObject.name = moduleRoot.name + "_NavClearance";
        }
    }
}
