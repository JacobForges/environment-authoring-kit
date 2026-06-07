using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    public struct CaveTubeMeshSettings
    {
        public float RingSpacing;
        public int SidesPerRing;
        public float NoiseAmplitude;
        public float FloorFlatten;
        public float WallNoiseMul;
        public float CeilingNoiseMul;
        public int Seed;
        /// <summary>When true, normals face inward so the tunnel is visible from inside.</summary>
        public bool InteriorView;
        /// <summary>
        /// Vaulted tunnel cross-section: flat floor + vertical walls + arched ceiling.
        /// Matches AK-Saigyouji "three-tiered" style and Bethesda corridor silhouettes.
        /// </summary>
        public bool VerticalWalls;
        /// <summary>Multiplies the vertical (Y) size of the cross-section so caves can be taller without changing path radius.</summary>
        public float HeightMultiplier;

        public static CaveTubeMeshSettings DefaultMobile => DefaultOrganic;

        public static CaveTubeMeshSettings DefaultOrganic => new()
        {
            RingSpacing = 1.65f,
            SidesPerRing = 22,
            NoiseAmplitude = 0.68f,
            FloorFlatten = 0.12f,
            WallNoiseMul = 1.35f,
            CeilingNoiseMul = 1.28f,
            Seed = 1,
            InteriorView = true,
            VerticalWalls = false,
            HeightMultiplier = 1f
        };
    }

    /// <summary>Continuous organic lava-tube mesh — one enclosed surface, no box segments.</summary>
    public static class CaveTubeMeshBuilder
    {
        public static Mesh Build(CaveSplinePath path, CaveTubeMeshSettings settings) =>
            Build(path, null, settings);

        public static Mesh Build(
            CaveSplinePath path,
            IReadOnlyList<CavePathKnot> knots,
            CaveTubeMeshSettings settings)
        {
            if (path == null || path.KnotCount < 2 || path.TotalLength < 0.01f)
                return null;

            settings.SidesPerRing = Mathf.Clamp(settings.SidesPerRing, 12, 28);
            settings.RingSpacing = Mathf.Max(1.4f, settings.RingSpacing);

            var ringCount = Mathf.Max(2, Mathf.CeilToInt(path.TotalLength / settings.RingSpacing) + 1);
            var sides = settings.SidesPerRing;
            // Reserve 2 extra vertices for the start/end cap centers so the tube is sealed.
            var vertCount = ringCount * sides + 2;
            var startCapCenterIdx = ringCount * sides;
            var endCapCenterIdx = ringCount * sides + 1;
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new List<int>((ringCount - 1) * sides * 6 + sides * 6);

            for (var ring = 0; ring < ringCount; ring++)
            {
                var dist = ringCount <= 1 ? 0f : (ring / (float)(ringCount - 1)) * path.TotalLength;
                var sample = path.SampleAtDistance(dist);
                GetRadiiAtDistance(path, knots, dist, sample.RadiusX, sample.RadiusY, out var rx, out var ry);

                for (var side = 0; side < sides; side++)
                {
                    var angle = side / (float)sides * Mathf.PI * 2f;
                    var cos = Mathf.Cos(angle);
                    var sin = Mathf.Sin(angle);

                    Vector3 offset;
                    float noiseMul;

                    if (settings.VerticalWalls)
                    {
                        // Vaulted profile: flat floor / vertical walls / arched ceiling.
                        // sin > ceilingStart  => curved ceiling (ellipse arc)
                        // sin < floorEnd      => flat floor
                        // between             => vertical wall at ±rx, y linearly interpolated
                        const float ceilingStart = 0.62f;
                        const float floorEnd = -0.55f;
                        var floorMul = 1f - settings.FloorFlatten;
                        var heightMul = settings.HeightMultiplier > 0.01f ? settings.HeightMultiplier : 1f;
                        var ryScaled = ry * heightMul;

                        if (sin > ceilingStart)
                        {
                            // Top arch: standard ellipse curve.
                            offset = sample.Right * (cos * rx) + sample.Up * (sin * ryScaled);
                            noiseMul = settings.CeilingNoiseMul;
                        }
                        else if (sin < floorEnd)
                        {
                            // Bottom: flat floor.
                            offset = sample.Right * (cos * rx) + sample.Up * (sin * ryScaled * floorMul);
                            noiseMul = 0.25f;
                        }
                        else
                        {
                            // Vertical wall: snap horizontal to ±rx, lerp y between floor edge and ceiling edge.
                            var sideSign = cos >= 0f ? 1f : -1f;
                            var t = Mathf.InverseLerp(floorEnd, ceilingStart, sin);
                            var yWallBottom = floorEnd * ryScaled * floorMul;
                            var yWallTop = ceilingStart * ryScaled;
                            var y = Mathf.Lerp(yWallBottom, yWallTop, t);
                            offset = sample.Right * (sideSign * rx) + sample.Up * y;
                            noiseMul = settings.WallNoiseMul * 0.55f;
                        }
                    }
                    else
                    {
                        var flat = 1f;
                        if (sin < 0f)
                            flat = 1f - settings.FloorFlatten;

                        offset = sample.Right * (cos * rx) + sample.Up * (sin * ry * flat);
                        noiseMul = sin > 0.35f
                            ? settings.CeilingNoiseMul
                            : (sin < -0.25f ? 0.35f : settings.WallNoiseMul);
                    }

                    var noise = SampleNoise(sample.Position + offset, settings.Seed, settings.NoiseAmplitude * noiseMul);
                    var vertex = sample.Position + offset + sample.Right * noise.x + sample.Up * noise.y + sample.Tangent * noise.z;
                    var normal = (vertex - sample.Position).normalized;

                    var idx = ring * sides + side;
                    vertices[idx] = vertex;
                    normals[idx] = normal;
                    uvs[idx] = new Vector2(dist * 0.035f, side / (float)sides * 4f + ring * 0.0015f);
                }
            }

            for (var ring = 0; ring < ringCount - 1; ring++)
            {
                for (var side = 0; side < sides; side++)
                {
                    var next = (side + 1) % sides;
                    var a = ring * sides + side;
                    var b = ring * sides + next;
                    var c = (ring + 1) * sides + side;
                    var d = (ring + 1) * sides + next;

                    if (settings.InteriorView)
                    {
                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(d);
                        triangles.Add(c);
                    }
                    else
                    {
                        triangles.Add(a);
                        triangles.Add(c);
                        triangles.Add(b);
                        triangles.Add(b);
                        triangles.Add(c);
                        triangles.Add(d);
                    }
                }
            }

            // End caps: seal the tube at both ends so you can never see through to the skybox.
            {
                var startSample = path.SampleAtDistance(0f);
                var endSample = path.SampleAtDistance(path.TotalLength);

                vertices[startCapCenterIdx] = startSample.Position;
                normals[startCapCenterIdx] = settings.InteriorView ? startSample.Tangent : -startSample.Tangent;
                uvs[startCapCenterIdx] = new Vector2(0f, 0.5f);

                vertices[endCapCenterIdx] = endSample.Position;
                normals[endCapCenterIdx] = settings.InteriorView ? -endSample.Tangent : endSample.Tangent;
                uvs[endCapCenterIdx] = new Vector2(path.TotalLength * 0.035f, 0.5f);

                for (var side = 0; side < sides; side++)
                {
                    var next = (side + 1) % sides;
                    var sa = side;
                    var sb = next;
                    var ea = (ringCount - 1) * sides + side;
                    var eb = (ringCount - 1) * sides + next;

                    if (settings.InteriorView)
                    {
                        triangles.Add(startCapCenterIdx);
                        triangles.Add(sa);
                        triangles.Add(sb);

                        triangles.Add(endCapCenterIdx);
                        triangles.Add(eb);
                        triangles.Add(ea);
                    }
                    else
                    {
                        triangles.Add(startCapCenterIdx);
                        triangles.Add(sb);
                        triangles.Add(sa);

                        triangles.Add(endCapCenterIdx);
                        triangles.Add(ea);
                        triangles.Add(eb);
                    }
                }
            }

            if (settings.InteriorView)
            {
                for (var i = 0; i < vertCount - 2; i++)
                    normals[i] = -normals[i];
            }

            var mesh = new Mesh { name = "OrganicCaveTube" };
            if (vertCount > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            // Recalculate smooth normals from the actual triangle topology — our per-vertex
            // analytic normals were creating visible ring banding between adjacent ring loops.
            mesh.RecalculateNormals();
            if (settings.InteriorView)
            {
                var smoothed = mesh.normals;
                for (var i = 0; i < smoothed.Length; i++)
                    smoothed[i] = -smoothed[i];
                mesh.normals = smoothed;
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void GetRadiiAtDistance(
            CaveSplinePath path,
            IReadOnlyList<CavePathKnot> knots,
            float distance,
            float sampleRx,
            float sampleRy,
            out float rx,
            out float ry)
        {
            rx = sampleRx;
            ry = sampleRy;
            if (knots == null || knots.Count < 2)
                return;

            var total = path.TotalLength;
            if (total < 0.01f)
                return;

            var target = Mathf.Clamp(distance, 0f, total);
            for (var i = 0; i < knots.Count - 1; i++)
            {
                var a = knots[i];
                var b = knots[i + 1];
                var segStart = i == 0 ? 0f : ApproxDistanceToKnot(path, knots, i);
                var segEnd = ApproxDistanceToKnot(path, knots, i + 1);
                if (target < segStart || target > segEnd + 0.01f)
                    continue;

                var t = segEnd > segStart ? Mathf.InverseLerp(segStart, segEnd, target) : 0f;
                rx = Mathf.Lerp(a.RadiusX, b.RadiusX, t);
                ry = Mathf.Lerp(a.RadiusY, b.RadiusY, t);
                return;
            }
        }

        static float ApproxDistanceToKnot(CaveSplinePath path, IReadOnlyList<CavePathKnot> knots, int knotIndex)
        {
            var t = knotIndex / (float)(knots.Count - 1);
            return t * path.TotalLength;
        }

        static Vector3 SampleNoise(Vector3 worldPos, int seed, float amplitude)
        {
            var scale = 0.13f;
            var ox = seed * 0.19f;
            var n0 = Mathf.PerlinNoise(worldPos.x * scale + ox, worldPos.z * scale) - 0.5f;
            var n1 = Mathf.PerlinNoise(worldPos.y * scale, worldPos.x * scale + 40f) - 0.5f;
            var n2 = Mathf.PerlinNoise(worldPos.z * scale + 80f, worldPos.y * scale + ox) - 0.5f;
            var n3 = Mathf.PerlinNoise(worldPos.x * scale * 2.1f + 12f, worldPos.z * scale * 2.1f) - 0.5f;
            return new Vector3(n0 + n3 * 0.35f, n1, n2) * (amplitude * 2.2f);
        }
    }
}
