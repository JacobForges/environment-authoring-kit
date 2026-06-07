#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.World;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Surface labyrinth on the southern two rows of the 3×3 play disk; trails continue to south peak cave mouths.
    /// Mirrors underground <see cref="CaveMazeGenFlavor.WalkwayLabyrinthCavern"/> cadence for FullWorld mountains.
    /// </summary>
    public sealed class SurfaceMountainLabyrinthLayout
    {
        public bool[,] Passage;
        public int Width;
        public int Height;
        public float CellSizeMeters = 7.5f;
        public Vector3 WorldOrigin;
        public List<Vector2Int> SolutionPath = new();
        public Vector2Int PlayEntranceCell = new(-1, -1);
        public Vector2Int MazeCenterCell = new(-1, -1);
        public Vector3 PlayEntranceWorld;
        public Vector3 MazeCenterWorld;
        public Vector3 TitanExitWorld;
        public Vector2Int TitanExitCell = new(-1, -1);

        public bool IsPassage(int gx, int gz) =>
            gx >= 0 && gz >= 0 && gx < Width && gz < Height && Passage[gx, gz];

        public Vector3 CellToWorld(int gx, int gz)
        {
            var local = new Vector3(gx * CellSizeMeters, 0f, gz * CellSizeMeters);
            return WorldOrigin + local;
        }

        /// <summary>World-space AABB of the labyrinth grid (meters, Y ignored).</summary>
        public void GetWorldBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = WorldOrigin.x;
            minZ = WorldOrigin.z;
            maxX = WorldOrigin.x + Width * CellSizeMeters;
            maxZ = WorldOrigin.z + Height * CellSizeMeters;
        }

        /// <summary>
        /// Build a Tomb-Raider-style labyrinth on the play disk's southern two rows (6 tiles).
        /// </summary>
        public static SurfaceMountainLabyrinthLayout Generate(
            int seed,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            float foothillMinX,
            float foothillMaxX,
            float foothillMinZ,
            float foothillMaxZ,
            Terrain mainTerrain = null)
        {
            var rng = new System.Random(seed + 44017);
            var tileSizeX = mainTerrain?.terrainData?.size.x ?? 512f;
            var tileSizeZ = mainTerrain?.terrainData?.size.z ?? 512f;

            var layout = new SurfaceMountainLabyrinthLayout
            {
                CellSizeMeters = 7.5f + (float)rng.NextDouble() * 1.5f,
                Passage = null,
            };

            float annexMinX;
            float annexMaxX;
            float annexMinZ;
            float annexMaxZ;
            if (mainTerrain != null)
            {
                SurfaceMountainPlayLabyrinthRegion.ComputePlayLabyrinthWorldBounds(
                    mainTerrain,
                    out annexMinX,
                    out annexMaxX,
                    out annexMinZ,
                    out annexMaxZ);
            }
            else
            {
                var playCx = (playMinX + playMaxX) * 0.5f;
                var playCz = (playMinZ + playMaxZ) * 0.5f;
                annexMinX = playCx - tileSizeX * 1.05f;
                annexMaxX = playCx + tileSizeX * 1.05f;
                annexMinZ = playCz - tileSizeZ * 1.05f;
                annexMaxZ = playCz + tileSizeZ * 0.05f;
            }

            const float edgePad = 14f;
            var usableW = annexMaxX - annexMinX - edgePad * 2f;
            var usableH = annexMaxZ - annexMinZ - edgePad * 2f;
            var gridW = Mathf.FloorToInt(usableW / layout.CellSizeMeters);
            var gridH = Mathf.FloorToInt(usableH / layout.CellSizeMeters);
            if ((gridW & 1) == 0)
                gridW++;
            if ((gridH & 1) == 0)
                gridH++;
            gridW = Mathf.Clamp(gridW, 31, 180);
            gridH = Mathf.Clamp(gridH, 29, 120);

            layout.Width = gridW;
            layout.Height = gridH;
            layout.Passage = new bool[gridW, gridH];
            layout.WorldOrigin = new Vector3(annexMinX + edgePad, 0f, annexMinZ + edgePad);

            // South end of play labyrinth (toward peak mouths via trails); north connects to upper play row.
            layout.MazeCenterCell = new Vector2Int(gridW / 2, Mathf.Max(3, gridH - 4 - rng.Next(0, 3)));
            layout.PlayEntranceCell = PickPlayEntranceCell(layout, playMinX, playMaxX, playMinZ, playMaxZ, rng);
            layout.StartCell = layout.PlayEntranceCell;

            CarveWalkwayToAnnex(layout, playMinX, playMaxX, playMinZ, playMaxZ, rng);
            CarveProperMaze(layout, layout.MazeCenterCell, rng);
            if (CaveBuildAaaSessionPolicy.IsFullAaaRebuild)
            {
                CarveTwoColumnBranches(layout, rng);
                WidenPassages(layout, 2 + rng.Next(0, 2));
            }
            else
            {
                WidenPassages(layout, 1 + rng.Next(0, 2));
            }

            layout.PlayEntranceWorld = layout.CellToWorld(layout.PlayEntranceCell.x, layout.PlayEntranceCell.y);
            layout.MazeCenterWorld = layout.CellToWorld(layout.MazeCenterCell.x, layout.MazeCenterCell.y);
            CarveTitanExitTowardTarget(layout, mainTerrain, seed, rng);
            layout.SolutionPath = BuildSolutionPath(layout, rng);
            return layout;
        }

        /// <summary>Legacy overload — prefers <see cref="Generate"/> with terrain for north span.</summary>
        public static SurfaceMountainLabyrinthLayout Generate(
            int seed,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            float foothillMinX,
            float foothillMaxX,
            float foothillMinZ,
            float foothillMaxZ) =>
            Generate(
                seed,
                playMinX,
                playMaxX,
                playMinZ,
                playMaxZ,
                foothillMinX,
                foothillMaxX,
                foothillMinZ,
                foothillMaxZ,
                null);

        static void WidenPassages(SurfaceMountainLabyrinthLayout layout, int iterations)
        {
            if (layout?.Passage == null || iterations <= 0)
                return;

            for (var pass = 0; pass < iterations; pass++)
            {
                var next = (bool[,])layout.Passage.Clone();
                for (var z = 0; z < layout.Height; z++)
                {
                    for (var x = 0; x < layout.Width; x++)
                    {
                        if (!layout.Passage[x, z])
                            continue;

                        for (var dz = -1; dz <= 1; dz++)
                        {
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dz == 0)
                                    continue;
                                var nx = x + dx;
                                var nz = z + dz;
                                if (nx < 0 || nz < 0 || nx >= layout.Width || nz >= layout.Height)
                                    continue;
                                next[nx, nz] = true;
                            }
                        }
                    }
                }

                layout.Passage = next;
            }
        }

        Vector2Int StartCell;

        static Vector2Int PickPlayEntranceCell(
            SurfaceMountainLabyrinthLayout layout,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            System.Random rng)
        {
            var best = new Vector2Int(layout.Width / 2, 2);
            var bestD = float.MaxValue;
            var playCx = (playMinX + playMaxX) * 0.5f;
            var playCz = (playMinZ + playMaxZ) * 0.5f;
            var zMin = 2;
            var zMax = Mathf.Min(layout.Height - 3, 10);

            for (var z = zMin; z <= zMax; z++)
            {
                for (var x = 1; x < layout.Width - 1; x++)
                {
                    var w = layout.CellToWorld(x, z);
                    var d = (w.x - playCx) * (w.x - playCx) + (w.z - playCz) * (w.z - playCz);
                    if (d >= bestD)
                        continue;
                    bestD = d;
                    best = new Vector2Int(x, z);
                }
            }

            layout.Passage[best.x, best.y] = true;
            return best;
        }

        static void CarveWalkwayToAnnex(
            SurfaceMountainLabyrinthLayout layout,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ,
            System.Random rng)
        {
            var cursor = layout.PlayEntranceCell;
            layout.SolutionPath.Add(cursor);
            var playCx = (playMinX + playMaxX) * 0.5f;
            var steps = 6 + rng.Next(0, 5);
            for (var i = 0; i < steps; i++)
            {
                var dx = cursor.x < layout.MazeCenterCell.x ? 1 : cursor.x > layout.MazeCenterCell.x ? -1 : 0;
                var dz = cursor.y > layout.MazeCenterCell.y ? -1 : cursor.y < layout.MazeCenterCell.y ? 1 : 0;
                if (rng.NextDouble() < 0.45f && dx != 0)
                    cursor = new Vector2Int(Mathf.Clamp(cursor.x + dx, 0, layout.Width - 1), cursor.y);
                else if (dz != 0)
                    cursor = new Vector2Int(cursor.x, Mathf.Clamp(cursor.y + dz, 0, layout.Height - 1));
                else
                    cursor = new Vector2Int(cursor.x, Mathf.Clamp(cursor.y - 1, 0, layout.Height - 1));

                layout.Passage[cursor.x, cursor.y] = true;
                if (!layout.SolutionPath.Contains(cursor))
                    layout.SolutionPath.Add(cursor);
            }

            // Extend connector from play disk south edge into foothill row (fields).
            var entranceWorld = layout.CellToWorld(layout.PlayEntranceCell.x, layout.PlayEntranceCell.y);
            var edgeZ = playMinZ + 6f;
            var edgeX = Mathf.Clamp(entranceWorld.x, playMinX + 24f, playMaxX - 24f);
            var edgeCell = layout.WorldToCell(new Vector3(edgeX, 0f, edgeZ));
            edgeCell.x = Mathf.Clamp(edgeCell.x, 1, layout.Width - 2);
            edgeCell.y = Mathf.Clamp(edgeCell.y, 2, Mathf.Min(12, layout.Height - 3));
            layout.Passage[edgeCell.x, edgeCell.y] = true;
            layout.Passage[layout.PlayEntranceCell.x, layout.PlayEntranceCell.y] = true;

            var walk = edgeCell;
            while (walk.y > layout.PlayEntranceCell.y)
            {
                layout.Passage[walk.x, walk.y] = true;
                if (!layout.SolutionPath.Contains(walk))
                    layout.SolutionPath.Add(walk);
                walk = new Vector2Int(walk.x, walk.y - 1);
            }

            layout.StartCell = cursor;
        }

        /// <summary>West / east maze columns from north entrance toward south peak mouths.</summary>
        static void CarveTwoColumnBranches(SurfaceMountainLabyrinthLayout layout, System.Random rng)
        {
            var westX = Mathf.Max(2, layout.Width / 4);
            var eastX = Mathf.Min(layout.Width - 3, layout.Width * 3 / 4);
            var southZ = Mathf.Max(layout.MazeCenterCell.y, layout.Height - 8);
            for (var z = layout.PlayEntranceCell.y; z <= southZ; z++)
            {
                layout.Passage[westX, z] = true;
                layout.Passage[eastX, z] = true;
                if (rng.NextDouble() < 0.32)
                {
                    var wOff = rng.NextDouble() < 0.5 ? -1 : 1;
                    var eOff = rng.NextDouble() < 0.5 ? -1 : 1;
                    layout.Passage[Mathf.Clamp(westX + wOff, 1, layout.Width - 2), z] = true;
                    layout.Passage[Mathf.Clamp(eastX + eOff, 1, layout.Width - 2), z] = true;
                }
            }
        }

        public Vector2Int WorldToCell(Vector3 world)
        {
            var local = world - WorldOrigin;
            return new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(local.x / CellSizeMeters), 0, Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(local.z / CellSizeMeters), 0, Height - 1));
        }

        static void CarveProperMaze(SurfaceMountainLabyrinthLayout layout, Vector2Int annexCenter, System.Random rng)
        {
            var visited = new bool[layout.Width, layout.Height];
            var stack = new Stack<Vector2Int>();
            var start = layout.StartCell;
            visited[start.x, start.y] = true;
            layout.Passage[start.x, start.y] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var current = stack.Peek();
                var neighbors = new List<Vector2Int>();

                foreach (var step in new[]
                         {
                             new Vector2Int(2, 0), new Vector2Int(-2, 0), new Vector2Int(0, 2), new Vector2Int(0, -2),
                         })
                {
                    var nx = current.x + step.x;
                    var nz = current.y + step.y;
                    if (nx < 1 || nz < 1 || nx >= layout.Width - 1 || nz >= layout.Height - 1)
                        continue;
                    if (visited[nx, nz])
                        continue;
                    neighbors.Add(new Vector2Int(nx, nz));
                }

                if (neighbors.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                var next = neighbors[rng.Next(neighbors.Count)];
                var between = new Vector2Int((current.x + next.x) / 2, (current.y + next.y) / 2);
                layout.Passage[between.x, between.y] = true;
                layout.Passage[next.x, next.y] = true;
                visited[next.x, next.y] = true;
                stack.Push(next);
            }

            layout.Passage[annexCenter.x, annexCenter.y] = true;
        }

        static List<Vector2Int> BuildSolutionPath(SurfaceMountainLabyrinthLayout layout, System.Random rng)
        {
            var path = new List<Vector2Int>(layout.SolutionPath);
            var end = layout.MazeCenterCell;
            var bfs = FindPath(layout, layout.PlayEntranceCell, end);
            if (bfs.Count > 1)
            {
                for (var i = 1; i < bfs.Count; i++)
                {
                    if (!path.Contains(bfs[i]))
                        path.Add(bfs[i]);
                }
            }

            return path;
        }

        static List<Vector2Int> FindPath(SurfaceMountainLabyrinthLayout layout, Vector2Int from, Vector2Int to)
        {
            var q = new Queue<Vector2Int>();
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            q.Enqueue(from);
            prev[from] = from;

            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c == to)
                    break;

                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= layout.Width || n.y >= layout.Height)
                        continue;
                    if (!layout.Passage[n.x, n.y] || prev.ContainsKey(n))
                        continue;
                    prev[n] = c;
                    q.Enqueue(n);
                }
            }

            var result = new List<Vector2Int>();
            if (!prev.ContainsKey(to))
                return result;

            var walk = to;
            while (walk != from)
            {
                result.Add(walk);
                walk = prev[walk];
            }

            result.Reverse();
            result.Insert(0, from);
            return result;
        }

        /// <summary>
        /// Production carve paths — solution spine + play connector + south deep annex (no per-cell edge grid).
        /// </summary>
        public List<Vector3[]> BuildCarvePolylines(
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            var list = new List<Vector3[]>(8);
            var connector = BuildPlayConnectorPolyline(playMinX, playMaxX, playMinZ, playMaxZ);
            if (connector != null && connector.Length >= 2)
                list.Add(SmoothPolylineWithSwitchbacks(connector, 8f, 7));

            if (SolutionPath.Count >= 2)
            {
                var spine = new Vector3[SolutionPath.Count];
                for (var i = 0; i < SolutionPath.Count; i++)
                {
                    var c = SolutionPath[i];
                    spine[i] = CellToWorld(c.x, c.y);
                }

                list.Add(SmoothPolylineWithSwitchbacks(spine, 6f, 6));
            }

            AddMazeBranchSpines(list, playMinX, playMaxX, playMinZ, playMaxZ);
            AddTitanExitSpine(list);
            return list;
        }

        void AddTitanExitSpine(List<Vector3[]> list)
        {
            if (TitanExitCell.x < 0 || Passage == null)
                return;

            var path = FindPath(this, MazeCenterCell, TitanExitCell);
            if (path.Count < 2)
                path = FindPath(this, PlayEntranceCell, TitanExitCell);
            if (path.Count < 2)
                return;

            var pts = new Vector3[path.Count];
            for (var i = 0; i < path.Count; i++)
                pts[i] = CellToWorld(path[i].x, path[i].y);
            list.Add(SmoothPolylineWithSwitchbacks(pts, 7f, 7));
        }

        static void CarveTitanExitTowardTarget(
            SurfaceMountainLabyrinthLayout layout,
            Terrain mainTerrain,
            int seed,
            System.Random rng)
        {
            if (!HollowTitanLandmarkSitePlanner.TryResolveLabyrinthTitanTarget(mainTerrain, seed, out var target))
            {
                layout.TitanExitWorld = layout.MazeCenterWorld;
                return;
            }

            layout.TitanExitWorld = target;
            var local = target - layout.WorldOrigin;
            var gx = Mathf.Clamp(Mathf.RoundToInt(local.x / layout.CellSizeMeters), 2, layout.Width - 3);
            var gz = Mathf.Clamp(Mathf.RoundToInt(local.z / layout.CellSizeMeters), 2, layout.Height - 3);

            var best = new Vector2Int(gx, gz);
            var bestD = float.MaxValue;
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var x = rng.Next(2, layout.Width - 2);
                var z = rng.Next(Mathf.Max(2, layout.Height / 3), layout.Height - 2);
                var w = layout.CellToWorld(x, z);
                var d = (w - target).sqrMagnitude;
                if (d >= bestD)
                    continue;
                bestD = d;
                best = new Vector2Int(x, z);
            }

            layout.TitanExitCell = best;
            layout.Passage[best.x, best.y] = true;

            var cursor = layout.MazeCenterCell;
            var guard = 0;
            while (cursor != best && guard++ < layout.Width + layout.Height)
            {
                layout.Passage[cursor.x, cursor.y] = true;
                var dx = best.x - cursor.x;
                var dz = best.y - cursor.y;
                if (Mathf.Abs(dx) >= Mathf.Abs(dz))
                    cursor = new Vector2Int(cursor.x + (dx > 0 ? 1 : -1), cursor.y);
                else
                    cursor = new Vector2Int(cursor.x, cursor.y + (dz > 0 ? 1 : -1));
                cursor = new Vector2Int(
                    Mathf.Clamp(cursor.x, 0, layout.Width - 1),
                    Mathf.Clamp(cursor.y, 0, layout.Height - 1));
            }

            layout.Passage[best.x, best.y] = true;
            if (CaveBuildAaaSessionPolicy.IsFullAaaRebuild)
                AddMouthApproachSpinesForTitan(layout, best);
        }

        static void AddMouthApproachSpinesForTitan(SurfaceMountainLabyrinthLayout layout, Vector2Int exitCell)
        {
            var zPath = exitCell.y;
            for (var x = layout.MazeCenterCell.x; x != exitCell.x; x += exitCell.x > layout.MazeCenterCell.x ? 1 : -1)
                layout.Passage[Mathf.Clamp(x, 0, layout.Width - 1), zPath] = true;
        }

        /// <summary>Seed-random annex branches from DFS passages — no hub arcs or fixed west/east columns.</summary>
        void AddMazeBranchSpines(
            List<Vector3[]> list,
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            if (Passage == null || Width < 4 || Height < 4)
                return;

            var rng = new System.Random(
                PlayEntranceCell.x * 92821 + PlayEntranceCell.y * 68917 + MazeCenterCell.x * 131 + Width * Height);
            var branchCount = CaveBuildAaaSessionPolicy.IsFullAaaRebuild
                ? Mathf.Clamp(4 + rng.Next(0, 3), 4, 7)
                : Mathf.Clamp(2 + rng.Next(0, 3), 2, 5);
            var used = new HashSet<Vector2Int> { PlayEntranceCell, MazeCenterCell };

            for (var b = 0; b < branchCount && list.Count < 6; b++)
            {
                var start = PickRandomPassageCell(this, rng, used);
                if (start.x < 0)
                    break;

                used.Add(start);
                var path = FindPath(this, PlayEntranceCell, start);
                if (path.Count < 3)
                    path = FindPath(this, start, MazeCenterCell);
                if (path.Count < 2)
                    continue;

                var pts = new Vector3[path.Count];
                for (var i = 0; i < path.Count; i++)
                    pts[i] = CellToWorld(path[i].x, path[i].y);
                list.Add(SmoothPolylineWithSwitchbacks(pts, 5f + (float)rng.NextDouble() * 2f, 5));
            }
        }

        static Vector2Int PickRandomPassageCell(
            SurfaceMountainLabyrinthLayout layout,
            System.Random rng,
            HashSet<Vector2Int> used)
        {
            for (var attempt = 0; attempt < 48; attempt++)
            {
                var x = rng.Next(2, layout.Width - 2);
                var z = rng.Next(2, layout.Height - 2);
                if (!layout.IsPassage(x, z))
                    continue;
                var c = new Vector2Int(x, z);
                if (!used.Contains(c))
                    return c;
            }

            return new Vector2Int(-1, -1);
        }

        void AddHubSwitchbackArcs(List<Vector3[]> list)
        {
            if (Width < 8 || Height < 8)
                return;

            var hub = CellToWorld(MazeCenterCell.x, MazeCenterCell.y);
            var westX = Mathf.Clamp(Mathf.RoundToInt(Width * 0.28f), 2, Width - 3);
            var eastX = Mathf.Clamp(Mathf.RoundToInt(Width * 0.72f), 2, Width - 3);
            var hubZ = MazeCenterCell.y;
            var west = CellToWorld(westX, hubZ);
            var east = CellToWorld(eastX, hubZ);
            var entrance = CellToWorld(PlayEntranceCell.x, PlayEntranceCell.y);

            list.Add(BuildQuadraticArc(west, hub, lateralMeters: 14f, segments: 20));
            list.Add(BuildQuadraticArc(hub, east, lateralMeters: -14f, segments: 20));
            list.Add(BuildQuadraticArc(entrance, hub, lateralMeters: 11f, segments: 22));

            var southHub = CellToWorld(MazeCenterCell.x, Mathf.Min(Height - 3, MazeCenterCell.y + 4));
            list.Add(BuildQuadraticArc(hub, southHub, lateralMeters: -9f, segments: 16));
        }

        static Vector3[] BuildQuadraticArc(Vector3 from, Vector3 to, float lateralMeters, int segments)
        {
            segments = Mathf.Max(6, segments);
            var mid = (from + to) * 0.5f;
            var dir = to - from;
            dir.y = 0f;
            var len = dir.magnitude;
            if (len < 0.5f)
                return new[] { from, to };

            dir /= len;
            var perp = Vector3.Cross(Vector3.up, dir).normalized;
            var control = mid + perp * lateralMeters;
            var pts = new Vector3[segments + 1];
            for (var i = 0; i <= segments; i++)
            {
                var t = i / (float)segments;
                pts[i] = QuadraticBezier(from, control, to, t);
            }

            return pts;
        }

        static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            var u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        static Vector3[] SmoothPolylineWithSwitchbacks(Vector3[] input, float lateralMeters, int segmentsPerEdge)
        {
            if (input == null || input.Length < 2)
                return input;

            segmentsPerEdge = Mathf.Clamp(segmentsPerEdge, 3, 24);
            var output = new List<Vector3>(input.Length * segmentsPerEdge);
            output.Add(input[0]);

            for (var i = 0; i < input.Length - 1; i++)
            {
                var a = input[i];
                var c = input[i + 1];
                var mid = (a + c) * 0.5f;
                var dir = c - a;
                dir.y = 0f;
                var len = dir.magnitude;
                if (len < 0.25f)
                    continue;

                dir /= len;
                var perp = Vector3.Cross(Vector3.up, dir).normalized;
                var sign = (i & 1) == 0 ? 1f : -1f;
                var control = mid + perp * lateralMeters * sign;

                for (var s = 1; s <= segmentsPerEdge; s++)
                {
                    var t = s / (float)segmentsPerEdge;
                    output.Add(QuadraticBezier(a, control, c, t));
                }
            }

            return output.ToArray();
        }

        /// <summary>Wide bench spines only — west/east columns + branches to three south peak mouths (no grid raster).</summary>
        void AddColumnAndMouthSpines(List<Vector3[]> list)
        {
            var west = BuildColumnPolyline(0.28f);
            if (west != null && west.Length >= 2)
                list.Add(SmoothPolylineWithSwitchbacks(west, 5f, 5));

            var east = BuildColumnPolyline(0.72f);
            if (east != null && east.Length >= 2)
                list.Add(SmoothPolylineWithSwitchbacks(east, 5f, 5));

            AddMouthApproachSpines(list);
        }

        Vector3[] BuildColumnPolyline(float columnU01)
        {
            var x = Mathf.Clamp(Mathf.RoundToInt(Width * columnU01), 2, Width - 3);
            var z0 = Mathf.Max(2, PlayEntranceCell.y);
            var z1 = Mathf.Min(Height - 3, Height - 2);
            if (z1 - z0 < 4)
                return null;

            var count = z1 - z0 + 1;
            var pts = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var z = z0 + i;
                Passage[x, z] = true;
                pts[i] = CellToWorld(x, z);
            }

            return pts;
        }

        void AddMouthApproachSpines(List<Vector3[]> list)
        {
            var zMouth = Height - 3;
            var targets = new[]
            {
                Mathf.Clamp(Width / 4, 2, Width - 3),
                Mathf.Clamp(Width / 2, 2, Width - 3),
                Mathf.Clamp(Width * 3 / 4, 2, Width - 3),
            };

            foreach (var tx in targets)
            {
                var from = new Vector2Int(tx, Mathf.Max(PlayEntranceCell.y + 2, 4));
                var path = FindPath(this, from, new Vector2Int(tx, zMouth));
                if (path.Count < 2)
                    continue;

                var pts = new Vector3[path.Count];
                for (var i = 0; i < path.Count; i++)
                {
                    var c = path[i];
                    Passage[c.x, c.y] = true;
                    pts[i] = CellToWorld(c.x, c.y);
                }

                list.Add(SmoothPolylineWithSwitchbacks(pts, 4.5f, 5));
            }
        }

        /// <summary>DO NOT use for heightmap carve — rasterizes every passage cell (star/checkerboard artifact).</summary>
        public List<Vector3[]> BuildAnnexPassageRunPolylines()
        {
            var list = new List<Vector3[]>(Height + Width);
            if (Passage == null)
                return list;

            for (var z = 0; z < Height; z++)
                AppendRunsForRow(list, z, horizontal: true);

            for (var x = 0; x < Width; x++)
                AppendRunsForRow(list, x, horizontal: false);

            return list;
        }

        void AppendRunsForRow(List<Vector3[]> list, int fixedIndex, bool horizontal)
        {
            var runStart = -1;
            var limit = horizontal ? Width : Height;
            for (var i = 0; i <= limit; i++)
            {
                var isPass = i < limit && Passage[horizontal ? i : fixedIndex, horizontal ? fixedIndex : i];
                if (isPass && runStart < 0)
                    runStart = i;

                if ((!isPass || i == limit) && runStart >= 0)
                {
                    var runEnd = i - 1;
                    if (runEnd - runStart >= 0)
                    {
                        var count = runEnd - runStart + 1;
                        if (count >= 2)
                        {
                            var pts = new Vector3[count];
                            for (var j = 0; j < count; j++)
                            {
                                var idx = runStart + j;
                                pts[j] = horizontal
                                    ? CellToWorld(idx, fixedIndex)
                                    : CellToWorld(fixedIndex, idx);
                            }

                            list.Add(pts);
                        }
                    }

                    runStart = -1;
                }
            }
        }

        Vector3[] BuildSouthDeepAnnexPolyline()
        {
            var cx = Mathf.Clamp(MazeCenterCell.x, 1, Width - 2);
            var zStart = 1;
            var zEnd = Mathf.Clamp(MazeCenterCell.y + 6, zStart + 3, Height - 3);
            if (zEnd - zStart < 2)
                return null;

            var count = zEnd - zStart + 1;
            var pts = new Vector3[count];
            for (var i = 0; i < count; i++)
                pts[i] = CellToWorld(cx, zStart + i);
            return pts;
        }

        Vector3[] BuildCenterColumnPolyline()
        {
            var cx = Width / 2;
            var z0 = Mathf.Max(1, PlayEntranceCell.y);
            var z1 = Mathf.Min(Height - 2, MazeCenterCell.y + (Height - MazeCenterCell.y) / 2);
            if (z1 - z0 < 3)
                return null;

            var count = z1 - z0 + 1;
            var pts = new Vector3[count];
            for (var i = 0; i < count; i++)
                pts[i] = CellToWorld(cx, z0 + i);
            return pts;
        }

        /// <summary>Debug / markers only — do not use for heightmap carve (500+ edge segments).</summary>
        public List<Vector3[]> BuildCorridorPolylines(
            float playMinX,
            float playMaxX,
            float playMinZ,
            float playMaxZ)
        {
            var list = new List<Vector3[]>();
            var connector = BuildPlayConnectorPolyline(playMinX, playMaxX, playMinZ, playMaxZ);
            if (connector != null && connector.Length >= 2)
                list.Add(connector);

            var seen = new HashSet<long>();
            for (var z = 0; z < Height; z++)
            {
                for (var x = 0; x < Width; x++)
                {
                    if (!Passage[x, z])
                        continue;

                    if (x + 1 < Width && Passage[x + 1, z])
                    {
                        var key = EdgeKey(x, z, x + 1, z);
                        if (seen.Add(key))
                        {
                            list.Add(new[]
                            {
                                CellToWorld(x, z),
                                CellToWorld(x + 1, z),
                            });
                        }
                    }

                    if (z + 1 < Height && Passage[x, z + 1])
                    {
                        var key = EdgeKey(x, z, x, z + 1);
                        if (seen.Add(key))
                        {
                            list.Add(new[]
                            {
                                CellToWorld(x, z),
                                CellToWorld(x, z + 1),
                            });
                        }
                    }
                }
            }

            if (SolutionPath.Count >= 2)
            {
                var spine = new Vector3[SolutionPath.Count];
                for (var i = 0; i < SolutionPath.Count; i++)
                {
                    var c = SolutionPath[i];
                    spine[i] = CellToWorld(c.x, c.y);
                }

                list.Add(spine);
            }

            return list;
        }

        static long EdgeKey(int x0, int z0, int x1, int z1)
        {
            if (x0 > x1 || (x0 == x1 && z0 > z1))
                (x0, z0, x1, z1) = (x1, z1, x0, z0);
            return ((long)x0 << 32) | ((long)z0 << 16) | ((long)x1 << 8) | (uint)z1;
        }

        Vector3[] BuildPlayConnectorPolyline(float playMinX, float playMaxX, float playMinZ, float playMaxZ)
        {
            var entrance = PlayEntranceWorld;
            var playCx = (playMinX + playMaxX) * 0.5f;
            var southEdge = new Vector3(
                Mathf.Clamp(entrance.x, playMinX + 20f, playMaxX - 20f),
                entrance.y,
                playMinZ + 8f);
            var midField = new Vector3(
                Mathf.Lerp(southEdge.x, entrance.x, 0.45f),
                entrance.y,
                Mathf.Lerp(southEdge.z, entrance.z, 0.35f));

            if ((southEdge - entrance).sqrMagnitude < 16f)
                southEdge.x = playCx;

            return new[] { southEdge, midField, entrance };
        }
    }
}
#endif
