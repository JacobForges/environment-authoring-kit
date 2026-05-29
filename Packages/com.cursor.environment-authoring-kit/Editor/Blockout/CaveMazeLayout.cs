using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Grid maze layout + spline knots for gameplay systems (walkways, water, lights).</summary>
    public sealed class CaveMazeLayout
    {
        /// <summary>Minimum walkable interior height (meters) — third-person humanoid + jump room.</summary>
        public static float MinWalkClearanceMeters => CaveThirdPersonClearance.ResolveMinWalkClearance();

        /// <summary>Headroom above walk floor vs corridor height (ceiling mesh).</summary>
        public static float MinCeilingClearanceMultiplier => CaveThirdPersonClearance.MinCeilingMultiplier;

        public int Width;
        public int Height;
        public bool[,] Passage;
        public Vector2Int StartCell;
        public Vector2Int CavernCenter;
        public int CavernRadiusCells = 1;
        /// <summary>World spacing per grid step (corridor cell center-to-center).</summary>
        public float CellSize = 3f;
        /// <summary>Walkable platform footprint (~1/4 of old 12m cells).</summary>
        public float PlatformSpan = 2.75f;
        public float PlatformDepth = 2.5f;
        public float CorridorHeight = 8.75f;
        /// <summary>Vertical clearance from walk floor to ceiling mesh (independent of floor placement).</summary>
        public float CeilingClearanceAboveFloor = 26f;
        public float DropPerRow = 0.28f;
        public Vector3 OriginOffset;
        public List<Vector2Int> SolutionPath = new();
        /// <summary>Playable Dreadhalls-style annex between walkway end and grand cavern.</summary>
        public bool HasLabyrinthAnnex;
        public Vector2Int LabyrinthEntranceCell = new(-1, -1);
        public int LabyrinthOriginX;
        public int LabyrinthOriginZ;
        public int LabyrinthWidth;
        public int LabyrinthHeight;
        public List<CavePathKnot> PathKnots = new();

        HashSet<Vector2Int> _solutionPathSet;

        public HashSet<Vector2Int> SolutionPathSet
        {
            get
            {
                if (_solutionPathSet == null && SolutionPath != null)
                {
                    _solutionPathSet = new HashSet<Vector2Int>();
                    foreach (var c in SolutionPath)
                        _solutionPathSet.Add(c);
                }

                return _solutionPathSet;
            }
        }

        public bool IsLabyrinthAnnexCell(int x, int z)
        {
            if (!HasLabyrinthAnnex || LabyrinthWidth <= 0 || LabyrinthHeight <= 0)
                return false;
            return x >= LabyrinthOriginX && x < LabyrinthOriginX + LabyrinthWidth &&
                   z >= LabyrinthOriginZ && z < LabyrinthOriginZ + LabyrinthHeight;
        }
        /// <summary>Cells along the route where floor/walk is omitted — player must jump the pit.</summary>
        public HashSet<Vector2Int> JumpGapCells = new();
        /// <summary>Mid-route landmark pause (spatial cinematography — sightline rest before finale).</summary>
        public Vector2Int LandmarkCell = new(-1, -1);
        /// <summary>Extra vertical offset (meters) for platformer jumps along the route.</summary>
        public Dictionary<Vector2Int, float> PlatformHeightOffsets = new();

        public bool HasLandmarkCell => LandmarkCell.x >= 0 && LandmarkCell.y >= 0;

        public bool IsJumpGap(int x, int z) => JumpGapCells != null && JumpGapCells.Contains(new Vector2Int(x, z));

        /// <summary>Headroom above walk surface for ceiling mesh and wall rings (does not move the floor).</summary>
        public float GetCeilingClearanceAt(int x, int z)
        {
            var baseClear = CeilingClearanceAboveFloor > 0.01f
                ? CeilingClearanceAboveFloor
                : Mathf.Max(MinWalkClearanceMeters + 1.2f, CorridorHeight) * MinCeilingClearanceMultiplier;
            return IsCavernCell(x, z) ? baseClear * CaveThirdPersonClearance.CavernHeadroomScale : baseClear;
        }

        public void SyncCeilingClearanceFromCorridor()
        {
            CeilingClearanceAboveFloor = Mathf.Max(
                CaveThirdPersonClearance.ResolveMinCeilingAboveFloor(),
                CorridorHeight * MinCeilingClearanceMultiplier,
                MinWalkClearanceMeters * MinCeilingClearanceMultiplier + 5f);
        }

        public float GetPlatformHeightOffset(int x, int z)
        {
            if (PlatformHeightOffsets == null)
                return 0f;
            return PlatformHeightOffsets.TryGetValue(new Vector2Int(x, z), out var o) ? o : 0f;
        }

        public bool IsPassage(int x, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Height)
                return false;
            return Passage[x, z];
        }

        public bool IsCavernCell(int x, int z)
        {
            var dx = Mathf.Abs(x - CavernCenter.x);
            var dz = Mathf.Abs(z - CavernCenter.y);
            return dx <= CavernRadiusCells && dz <= CavernRadiusCells && IsPassage(x, z);
        }

        public Vector3 CellToLocal(int x, int z) => CellToLocalBase(x, z) + OriginOffset;

        public Vector3 CellToLocalBase(int x, int z)
        {
            var ox = -(Width * CellSize) * 0.5f + CellSize * 0.5f;
            var oz = -(Height * CellSize) * 0.5f + CellSize * 0.5f;
            return new Vector3(ox + x * CellSize, -z * DropPerRow, oz + z * CellSize);
        }

        public Vector3 GetFloorSurfaceLocal(int x, int z)
        {
            var center = CellToLocal(x, z);
            var h = IsCavernCell(x, z) ? CorridorHeight * 1.75f : CorridorHeight;
            const float wallThickness = 0.75f;
            var floor = center + Vector3.down * (h * 0.5f - wallThickness);
            floor.y += GetPlatformHeightOffset(x, z);
            return floor;
        }

        public void ComputeLocalBounds(out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            if (SolutionPath != null && SolutionPath.Count > 0)
            {
                foreach (var cell in SolutionPath)
                {
                    var c = CellToLocal(cell.x, cell.y);
                    var h = IsCavernCell(cell.x, cell.y) ? CorridorHeight * 1.45f : CorridorHeight;
                    var ext = new Vector3(PlatformSpan * 0.6f, h * 0.55f, PlatformDepth * 0.6f);
                    min = Vector3.Min(min, c - ext);
                    max = Vector3.Max(max, c + ext);
                }

                if (!float.IsPositiveInfinity(min.x))
                    return;
            }

            min = Vector3.zero;
            max = Vector3.one * CellSize;
        }
    }

    public enum CaveMazeGenFlavor
    {
        WindingCompact = 0,
        VerticalClimb = 1,
        OrganicCellular = 2,
        InterviewWinding = 3,
        SparseJumps = 4,
        /// <summary>Walkway corridor → carved labyrinth annex → grand cavern (Tomb Raider / Dreadhalls cadence).</summary>
        WalkwayLabyrinthCavern = 5,
    }

    public static class CaveMazeLayoutGenerator
    {
        /// <summary>
        /// Enclosed cave course (not open void):
        /// RogueBasin cellular automata (45% fill, 4-5 rule, 5 passes) → largest connected cavern →
        /// critical path only (Harvard CS50 Dreadhalls-style grid route) → platformer jumps.
        /// </summary>
        /// <summary>Flat Y, interview pacing: jumps + finish cavern — no ceiling/block art geometry.</summary>
        public static CaveMazeLayout GeneratePrototype(int seed, int tunnelSegments, int chamberCount)
        {
            var rng = new System.Random(seed);
            var stepCount = Mathf.Clamp(tunnelSegments * 2 + chamberCount * 3 + 12, 16, 28);
            var layout = BuildInterviewCourse(rng, stepCount);
            layout.CavernCenter = layout.SolutionPath[layout.SolutionPath.Count - 1];
            layout.StartCell = layout.SolutionPath[0];
            layout.DropPerRow = 0f;
            layout.PlatformHeightOffsets = new Dictionary<Vector2Int, float>();

            StampInterviewLandmark(layout);
            OpenFinishCavern(layout);
            PickJumpGaps(layout, rng, minGaps: 2, maxGaps: 4);
            CenterLayoutOnPath(layout);
            CaveThirdPersonLayoutUtility.ApplyToLayout(layout);
            layout.PathKnots = BuildPathKnots(layout, chamberCount, seed);
            return layout;
        }

        public static CaveMazeLayout Generate(int seed, int tunnelSegments, int chamberCount, int mazeFlavor = -1)
        {
            var rng = new System.Random(seed);
            var flavor = mazeFlavor >= 0
                ? (CaveMazeGenFlavor)Mathf.Clamp(mazeFlavor, 0, 5)
                : (CaveMazeGenFlavor)rng.Next(0, 6);
            var stepCount = Mathf.Clamp(
                tunnelSegments * 2 + chamberCount * 2 + rng.Next(4, 14),
                12,
                34);

            var layout = flavor switch
            {
                CaveMazeGenFlavor.OrganicCellular => BuildOrganicCellularCourse(rng, stepCount),
                CaveMazeGenFlavor.InterviewWinding => BuildInterviewCourse(rng, stepCount),
                CaveMazeGenFlavor.VerticalClimb => BuildCompactCourse(rng, stepCount, 0.3f, 0.58f, 0.2f),
                CaveMazeGenFlavor.SparseJumps => BuildCompactCourse(rng, stepCount, 0.5f, 0.35f, 0.15f),
                CaveMazeGenFlavor.WalkwayLabyrinthCavern => BuildWalkwayLabyrinthCavernCourse(rng, stepCount),
                _ => BuildCompactCourse(rng, stepCount, 0.58f, 0.42f, 0.28f),
            };

            layout.CavernCenter = layout.SolutionPath[layout.SolutionPath.Count - 1];
            layout.StartCell = layout.SolutionPath[0];

            OpenFinishCavern(layout);
            var gapMax = flavor switch
            {
                CaveMazeGenFlavor.SparseJumps => 3,
                CaveMazeGenFlavor.VerticalClimb => 7,
                CaveMazeGenFlavor.OrganicCellular => 5 + rng.Next(0, 2),
                _ => 4 + rng.Next(0, 3),
            };
            PickJumpGaps(layout, rng, flavor == CaveMazeGenFlavor.SparseJumps ? 1 : 0, gapMax);
            ApplyPlatformerHeights(layout, rng);
            EnsureMinimumPathDescent(
                layout,
                flavor == CaveMazeGenFlavor.VerticalClimb ? 2.8f : 2.1f + (float)rng.NextDouble() * 0.5f);
            CenterLayoutOnPath(layout);
            CaveThirdPersonLayoutUtility.ApplyToLayout(layout);
            layout.PathKnots = BuildPathKnots(layout, chamberCount, seed);
            return layout;
        }

        static CaveMazeLayout BuildOrganicCellularCourse(System.Random rng, int stepCount)
        {
            var w = 22 + rng.Next(10);
            var h = 18 + rng.Next(10);
            var layout = new CaveMazeLayout
            {
                Width = w,
                Height = h,
                Passage = new bool[w, h],
                CellSize = 2.75f + (float)rng.NextDouble() * 0.65f,
                PlatformSpan = 2.5f + (float)rng.NextDouble() * 0.6f,
                PlatformDepth = 2.2f + (float)rng.NextDouble() * 0.5f,
                CorridorHeight = CaveThirdPersonClearance.ResolveDefaultCorridorHeight() +
                                 (float)rng.NextDouble() * 1.8f,
                CeilingClearanceAboveFloor = 0f,
                DropPerRow = 0.18f + (float)rng.NextDouble() * 0.12f,
                SolutionPath = new List<Vector2Int>(),
            };

            GenerateCellularAutomataCave(layout, rng, 0.4f + (float)rng.NextDouble() * 0.1f, 4 + rng.Next(3));
            KeepLargestConnectedRegion(layout);
            PickStartAndGoalFarApart(layout, rng);
            layout.SolutionPath = FindPath(layout, layout.StartCell, layout.CavernCenter);
            if (layout.SolutionPath.Count < 8)
                return BuildCompactCourse(rng, stepCount);

            CollapsePassageToCriticalPath(layout);
            layout.StartCell = layout.SolutionPath[0];
            return layout;
        }

        /// <summary>Only the playable route exists in the grid — no full-map rock fill.</summary>
        static CaveMazeLayout BuildCompactCourse(
            System.Random rng,
            int stepCount,
            float eastBias = 0.58f,
            float upBias = 0.42f,
            float otherBias = 0.28f)
        {
            var path = new List<Vector2Int> { Vector2Int.zero };
            var cursor = Vector2Int.zero;

            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            for (var i = 1; i < stepCount; i++)
            {
                var options = new List<Vector2Int>(4);
                foreach (var d in dirs)
                {
                    if (d == Vector2Int.right && rng.NextDouble() < eastBias)
                        options.Add(d);
                    else if (d == Vector2Int.up && rng.NextDouble() < upBias)
                        options.Add(d);
                    else if (rng.NextDouble() < otherBias)
                        options.Add(d);
                }

                if (options.Count == 0)
                    options.Add(dirs[rng.Next(dirs.Length)]);

                var step = options[rng.Next(options.Count)];
                cursor += step;
                path.Add(cursor);
            }

            var minX = int.MaxValue;
            var minZ = int.MaxValue;
            var maxX = int.MinValue;
            var maxZ = int.MinValue;
            foreach (var c in path)
            {
                minX = Mathf.Min(minX, c.x);
                minZ = Mathf.Min(minZ, c.y);
                maxX = Mathf.Max(maxX, c.x);
                maxZ = Mathf.Max(maxZ, c.y);
            }

            const int pad = 2;
            var w = maxX - minX + 1 + pad * 2;
            var h = maxZ - minZ + 1 + pad * 2;

            var layout = new CaveMazeLayout
            {
                Width = w,
                Height = h,
                Passage = new bool[w, h],
                CellSize = 3f,
                PlatformSpan = 2.75f,
                PlatformDepth = 2.5f,
                CorridorHeight = CaveThirdPersonClearance.ResolveDefaultCorridorHeight(),
                CeilingClearanceAboveFloor = 0f,
                DropPerRow = 0.22f,
                SolutionPath = new List<Vector2Int>()
            };

            foreach (var c in path)
            {
                var gx = c.x - minX + pad;
                var gz = c.y - minZ + pad;
                layout.Passage[gx, gz] = true;
                layout.SolutionPath.Add(new Vector2Int(gx, gz));
            }

            layout.StartCell = layout.SolutionPath[0];
            return layout;
        }

        /// <summary>Linear approach tunnel, proper maze annex (CS50 Dreadhalls), then grand cavern goal.</summary>
        static CaveMazeLayout BuildWalkwayLabyrinthCavernCourse(System.Random rng, int stepCount)
        {
            var walkwaySteps = Mathf.Clamp(stepCount / 2 + rng.Next(2, 6), 10, 20);
            var walkway = BuildRandomWalkCells(rng, walkwaySteps);

            var annexW = 17 + rng.Next(0, 6);
            if ((annexW & 1) == 0)
                annexW++;
            var annexH = 15 + rng.Next(0, 5);
            if ((annexH & 1) == 0)
                annexH++;

            var minX = int.MaxValue;
            var minZ = int.MaxValue;
            var maxX = int.MinValue;
            var maxZ = int.MinValue;
            foreach (var c in walkway)
            {
                minX = Mathf.Min(minX, c.x);
                minZ = Mathf.Min(minZ, c.y);
                maxX = Mathf.Max(maxX, c.x);
                maxZ = Mathf.Max(maxZ, c.y);
            }

            const int pad = 3;
            var gridW = maxX - minX + 1 + pad * 2 + annexW + 4;
            var gridH = Mathf.Max(maxZ - minZ + 1 + pad * 2, annexH + pad * 2);

            var layout = new CaveMazeLayout
            {
                Width = gridW,
                Height = gridH,
                Passage = new bool[gridW, gridH],
                CellSize = 2.85f + (float)rng.NextDouble() * 0.55f,
                PlatformSpan = 2.55f + (float)rng.NextDouble() * 0.45f,
                PlatformDepth = 2.35f + (float)rng.NextDouble() * 0.4f,
                CorridorHeight = CaveThirdPersonClearance.ResolveDefaultCorridorHeight() +
                                 (float)rng.NextDouble() * 1.4f,
                CeilingClearanceAboveFloor = 0f,
                DropPerRow = 0.16f + (float)rng.NextDouble() * 0.1f,
                SolutionPath = new List<Vector2Int>(),
                HasLabyrinthAnnex = true,
            };

            foreach (var c in walkway)
            {
                var gx = c.x - minX + pad;
                var gz = c.y - minZ + pad;
                layout.Passage[gx, gz] = true;
                layout.SolutionPath.Add(new Vector2Int(gx, gz));
            }

            layout.StartCell = layout.SolutionPath[0];
            var walkwayEnd = layout.SolutionPath[layout.SolutionPath.Count - 1];
            layout.LabyrinthEntranceCell = walkwayEnd;

            var annexOriginX = walkwayEnd.x + 2;
            var annexOriginZ = Mathf.Clamp(walkwayEnd.y - annexH / 2, pad, layout.Height - annexH - pad);
            layout.LabyrinthOriginX = annexOriginX;
            layout.LabyrinthOriginZ = annexOriginZ;
            layout.LabyrinthWidth = annexW;
            layout.LabyrinthHeight = annexH;

            var bridgeX = walkwayEnd.x + 1;
            if (bridgeX < layout.Width)
                layout.Passage[bridgeX, walkwayEnd.y] = true;

            var mazeStart = new Vector2Int(annexOriginX + 1, annexOriginZ + 1);
            if (mazeStart.x < layout.Width && mazeStart.y < layout.Height)
                layout.Passage[mazeStart.x, mazeStart.y] = true;

            var savedStart = layout.StartCell;
            layout.StartCell = mazeStart;
            CarveProperMazeInRegion(layout, annexOriginX, annexOriginZ, annexW, annexH, rng);
            layout.StartCell = savedStart;

            layout.CavernCenter = PickFarthestAnnexCell(layout, mazeStart, rng);
            OpenFinishCavern(layout);
            layout.CavernRadiusCells = 1 + rng.Next(0, 2);

            var labyrinthPath = FindPath(layout, walkwayEnd, layout.CavernCenter);
            if (labyrinthPath.Count > 1)
            {
                for (var i = 1; i < labyrinthPath.Count; i++)
                {
                    if (!layout.SolutionPath.Contains(labyrinthPath[i]))
                        layout.SolutionPath.Add(labyrinthPath[i]);
                }
            }

            PickJumpGaps(layout, rng, 1, 3);
            ApplyPlatformerHeights(layout, rng);
            EnsureMinimumPathDescent(layout, 2f + (float)rng.NextDouble() * 0.6f);
            CenterLayoutOnPath(layout);
            CaveThirdPersonLayoutUtility.ApplyToLayout(layout);
            return layout;
        }

        static List<Vector2Int> BuildRandomWalkCells(System.Random rng, int steps)
        {
            var path = new List<Vector2Int> { Vector2Int.zero };
            var cursor = Vector2Int.zero;
            var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

            for (var i = 1; i < steps; i++)
            {
                var options = new List<Vector2Int>(4);
                foreach (var d in dirs)
                {
                    var bias = d == Vector2Int.right ? 0.52f : d == Vector2Int.up ? 0.4f : 0.28f;
                    if (rng.NextDouble() < bias)
                        options.Add(d);
                }

                if (options.Count == 0)
                    options.Add(dirs[rng.Next(dirs.Length)]);

                cursor += options[rng.Next(options.Count)];
                path.Add(cursor);
            }

            return path;
        }

        static void CarveProperMazeInRegion(
            CaveMazeLayout layout,
            int originX,
            int originZ,
            int regionW,
            int regionH,
            System.Random rng)
        {
            var visited = new bool[layout.Width, layout.Height];
            var stack = new Stack<Vector2Int>();
            var start = layout.StartCell;
            if (start.x < 0 || start.x >= layout.Width || start.y < 0 || start.y >= layout.Height)
                return;

            visited[start.x, start.y] = true;
            layout.Passage[start.x, start.y] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var current = stack.Peek();
                var neighbors = new List<Vector2Int>();

                foreach (var step in new[] { new Vector2Int(2, 0), new Vector2Int(-2, 0), new Vector2Int(0, 2), new Vector2Int(0, -2) })
                {
                    var nx = current.x + step.x;
                    var nz = current.y + step.y;
                    if (nx < originX + 1 || nz < originZ + 1 ||
                        nx >= originX + regionW - 1 || nz >= originZ + regionH - 1)
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
        }

        static Vector2Int PickFarthestAnnexCell(CaveMazeLayout layout, Vector2Int from, System.Random rng)
        {
            var best = from;
            var bestDist = -1;
            for (var x = layout.LabyrinthOriginX; x < layout.LabyrinthOriginX + layout.LabyrinthWidth; x++)
            {
                for (var z = layout.LabyrinthOriginZ; z < layout.LabyrinthOriginZ + layout.LabyrinthHeight; z++)
                {
                    if (!layout.Passage[x, z])
                        continue;
                    var d = Mathf.Abs(x - from.x) + Mathf.Abs(z - from.y);
                    if (d > bestDist)
                    {
                        bestDist = d;
                        best = new Vector2Int(x, z);
                    }
                }
            }

            if (bestDist < 4)
            {
                var corner = new Vector2Int(
                    layout.LabyrinthOriginX + layout.LabyrinthWidth - 2,
                    layout.LabyrinthOriginZ + layout.LabyrinthHeight - 2);
                if (corner.x > 0 && corner.y > 0 && corner.x < layout.Width && corner.y < layout.Height)
                    layout.Passage[corner.x, corner.y] = true;
                best = corner;
            }

            return best;
        }

        static void CenterLayoutOnPath(CaveMazeLayout layout)
        {
            if (layout.SolutionPath == null || layout.SolutionPath.Count == 0)
                return;

            var min = layout.CellToLocalBase(layout.SolutionPath[0].x, layout.SolutionPath[0].y);
            var max = min;
            foreach (var cell in layout.SolutionPath)
            {
                var p = layout.CellToLocalBase(cell.x, cell.y);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            var center = (min + max) * 0.5f;
            layout.OriginOffset = new Vector3(0f, 0f, 6f) - center;
        }

        /// <summary>RogueBasin CA: random fill then 4-5 rule smoothing until organic enclosed rock.</summary>
        static void GenerateCellularAutomataCave(
            CaveMazeLayout layout,
            System.Random rng,
            float wallChance,
            int iterations)
        {
            var w = layout.Width;
            var h = layout.Height;
            var wall = new bool[w, h];

            for (var x = 0; x < w; x++)
            {
                for (var z = 0; z < h; z++)
                {
                    if (x == 0 || z == 0 || x == w - 1 || z == h - 1)
                        wall[x, z] = true;
                    else
                        wall[x, z] = rng.NextDouble() < wallChance;
                }
            }

            for (var iter = 0; iter < iterations; iter++)
            {
                var next = new bool[w, h];
                for (var x = 1; x < w - 1; x++)
                {
                    for (var z = 1; z < h - 1; z++)
                    {
                        var neighbors = CountSolidNeighbors(wall, x, z, w, h);
                        next[x, z] = neighbors >= 5 || (wall[x, z] && neighbors >= 4);
                    }
                }

                wall = next;
            }

            for (var x = 0; x < w; x++)
            {
                for (var z = 0; z < h; z++)
                    layout.Passage[x, z] = !wall[x, z];
            }
        }

        static int CountSolidNeighbors(bool[,] wall, int x, int z, int w, int h)
        {
            var count = 0;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    var nx = x + dx;
                    var nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h || wall[nx, nz])
                        count++;
                }
            }

            return count;
        }

        static void KeepLargestConnectedRegion(CaveMazeLayout layout)
        {
            var w = layout.Width;
            var h = layout.Height;
            var visited = new bool[w, h];
            List<Vector2Int> best = null;

            for (var x = 0; x < w; x++)
            {
                for (var z = 0; z < h; z++)
                {
                    if (!layout.Passage[x, z] || visited[x, z])
                        continue;

                    var region = FloodRegion(layout, x, z, visited);
                    if (best == null || region.Count > best.Count)
                        best = region;
                }
            }

            for (var x = 0; x < w; x++)
            {
                for (var z = 0; z < h; z++)
                    layout.Passage[x, z] = false;
            }

            if (best == null)
                return;

            foreach (var cell in best)
                layout.Passage[cell.x, cell.y] = true;
        }

        static List<Vector2Int> FloodRegion(CaveMazeLayout layout, int sx, int sz, bool[,] visited)
        {
            var w = layout.Width;
            var h = layout.Height;
            var region = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(sx, sz));
            visited[sx, sz] = true;

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                region.Add(c);

                Try(c + Vector2Int.right);
                Try(c + Vector2Int.left);
                Try(c + Vector2Int.up);
                Try(c + Vector2Int.down);

                void Try(Vector2Int next)
                {
                    if (next.x < 0 || next.y < 0 || next.x >= w || next.y >= h)
                        return;
                    if (!layout.Passage[next.x, next.y] || visited[next.x, next.y])
                        return;
                    visited[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }

            return region;
        }

        static void PickStartAndGoalFarApart(CaveMazeLayout layout, System.Random rng)
        {
            var floors = new List<Vector2Int>();
            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                {
                    if (layout.Passage[x, z])
                        floors.Add(new Vector2Int(x, z));
                }
            }

            if (floors.Count == 0)
                return;

            layout.StartCell = floors[rng.Next(floors.Count)];
            var best = layout.StartCell;
            var bestDist = 0;

            foreach (var cell in floors)
            {
                var d = Mathf.Abs(cell.x - layout.StartCell.x) + Mathf.Abs(cell.y - layout.StartCell.y);
                if (d > bestDist)
                {
                    bestDist = d;
                    best = cell;
                }
            }

            layout.CavernCenter = best;
        }

        /// <summary>Only the route stays air; everything else becomes solid rock in the mesh pass.</summary>
        static void CollapsePassageToCriticalPath(CaveMazeLayout layout)
        {
            if (layout.SolutionPath == null || layout.SolutionPath.Count == 0)
                return;

            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                    layout.Passage[x, z] = false;
            }

            foreach (var cell in layout.SolutionPath)
                layout.Passage[cell.x, cell.y] = true;

            layout.Passage[layout.CavernCenter.x, layout.CavernCenter.y] = true;
        }

        static void OpenFinishCavern(CaveMazeLayout layout)
        {
            var c = layout.CavernCenter;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dz = -1; dz <= 1; dz++)
                {
                    var x = c.x + dx;
                    var z = c.y + dz;
                    if (x > 0 && z > 0 && x < layout.Width - 1 && z < layout.Height - 1)
                        layout.Passage[x, z] = true;
                }
            }
        }

        /// <summary>Fallback: perfect maze carve (CS50 Dreadhalls-style) if CA path is too short.</summary>
        static void CarveProperMaze(CaveMazeLayout layout, System.Random rng)
        {
            var w = layout.Width;
            var h = layout.Height;
            var visited = new bool[w, h];
            var stack = new Stack<Vector2Int>();
            var start = layout.StartCell;
            visited[start.x, start.y] = true;
            layout.Passage[start.x, start.y] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                var current = stack.Peek();
                var neighbors = new List<Vector2Int>();

                foreach (var step in new[] { new Vector2Int(2, 0), new Vector2Int(-2, 0), new Vector2Int(0, 2), new Vector2Int(0, -2) })
                {
                    var nx = current.x + step.x;
                    var nz = current.y + step.y;
                    if (nx <= 0 || nz <= 0 || nx >= w - 1 || nz >= h - 1)
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
        }

        static void OpenGrandCavern(CaveMazeLayout layout)
        {
            var c = layout.CavernCenter;
            var r = layout.CavernRadiusCells;
            for (var x = c.x - r; x <= c.x + r; x++)
            {
                for (var z = c.y - r; z <= c.y + r; z++)
                {
                    if (x > 0 && z > 0 && x < layout.Width - 1 && z < layout.Height - 1)
                        layout.Passage[x, z] = true;
                }
            }

            if (c.x - r - 1 > 0)
                layout.Passage[c.x - r - 1, c.y] = true;
            if (c.y - r - 1 > 0)
                layout.Passage[c.x, c.y - r - 1] = true;
        }

        static void AddFewLoops(CaveMazeLayout layout, System.Random rng, int count)
        {
            var w = layout.Width;
            var h = layout.Height;
            for (var attempt = 0; attempt < count * 12 && count > 0; attempt++)
            {
                var x = 1 + rng.Next(0, (w - 2) / 2) * 2;
                var z = 1 + rng.Next(0, (h - 2) / 2) * 2;
                if (!layout.Passage[x, z])
                    continue;

                if (x + 1 < w - 1 && !layout.Passage[x + 1, z] && rng.NextDouble() < 0.4)
                {
                    layout.Passage[x + 1, z] = true;
                    count--;
                }
                else if (z + 1 < h - 1 && !layout.Passage[x, z + 1] && rng.NextDouble() < 0.4)
                {
                    layout.Passage[x, z + 1] = true;
                    count--;
                }
            }
        }

        static List<Vector2Int> FindPath(CaveMazeLayout layout, Vector2Int start, Vector2Int goal)
        {
            var w = layout.Width;
            var h = layout.Height;
            var queue = new Queue<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            queue.Enqueue(start);
            cameFrom[start] = start;

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == goal)
                    break;

                TryEnqueue(cur + Vector2Int.right);
                TryEnqueue(cur + Vector2Int.left);
                TryEnqueue(cur + Vector2Int.up);
                TryEnqueue(cur + Vector2Int.down);

                void TryEnqueue(Vector2Int next)
                {
                    if (next.x < 0 || next.y < 0 || next.x >= w || next.y >= h)
                        return;
                    if (!layout.Passage[next.x, next.y] || cameFrom.ContainsKey(next))
                        return;
                    cameFrom[next] = cur;
                    queue.Enqueue(next);
                }
            }

            var path = new List<Vector2Int>();
            if (!cameFrom.ContainsKey(goal))
            {
                path.Add(start);
                return path;
            }

            var p = goal;
            while (p != start)
            {
                path.Add(p);
                p = cameFrom[p];
            }

            path.Add(start);
            path.Reverse();
            return path;
        }

        static void PickJumpGaps(CaveMazeLayout layout, System.Random rng, int minGaps = 0, int maxGaps = 0)
        {
            layout.JumpGapCells = new HashSet<Vector2Int>();
            if (layout.SolutionPath == null || layout.SolutionPath.Count < 8)
                return;

            var landmarkIdx = layout.HasLandmarkCell
                ? layout.SolutionPath.IndexOf(layout.LandmarkCell)
                : layout.SolutionPath.Count / 2;
            if (landmarkIdx < 0)
                landmarkIdx = layout.SolutionPath.Count / 2;

            var stride = 2 + rng.Next(2);
            var startIdx = Mathf.Max(stride, landmarkIdx + 2);
            for (var i = startIdx; i < layout.SolutionPath.Count - 3; i += stride)
            {
                var cell = layout.SolutionPath[i];
                if (layout.IsCavernCell(cell.x, cell.y))
                    continue;
                if (cell == layout.StartCell)
                    continue;
                layout.JumpGapCells.Add(cell);
                if (maxGaps > 0 && layout.JumpGapCells.Count >= maxGaps)
                    break;
            }

            while (minGaps > 0 && layout.JumpGapCells.Count < minGaps && layout.SolutionPath.Count > 6)
            {
                var idx = 3 + rng.Next(layout.SolutionPath.Count - 6);
                layout.JumpGapCells.Add(layout.SolutionPath[idx]);
            }
        }

        /// <summary>Opens a 5×5 landmark cavern ~⅔ along the route (Rockstar-style pacing beat before finale).</summary>
        static void StampInterviewLandmark(CaveMazeLayout layout)
        {
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 10)
                return;

            var idx = Mathf.Clamp(layout.SolutionPath.Count * 2 / 3, 4, layout.SolutionPath.Count - 4);
            var c = layout.SolutionPath[idx];
            layout.LandmarkCell = c;

            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dz = -2; dz <= 2; dz++)
                {
                    var x = c.x + dx;
                    var z = c.y + dz;
                    if (x > 0 && z > 0 && x < layout.Width - 1 && z < layout.Height - 1)
                        layout.Passage[x, z] = true;
                }
            }
        }

        /// <summary>Winding platformer route with turns (flat prototype — no vertical shelf steps).</summary>
        static CaveMazeLayout BuildInterviewCourse(System.Random rng, int stepCount)
        {
            var path = new List<Vector2Int> { Vector2Int.zero };
            var cursor = Vector2Int.zero;

            for (var i = 1; i < stepCount; i++)
            {
                var options = new List<Vector2Int> { Vector2Int.right, Vector2Int.up };
                if (rng.NextDouble() < 0.45)
                    options.Add(Vector2Int.left);
                if (rng.NextDouble() < 0.35)
                    options.Add(Vector2Int.down);

                if (i % 4 == 0 && rng.NextDouble() < 0.55)
                {
                    options.Clear();
                    options.Add(rng.NextDouble() < 0.5 ? Vector2Int.right : Vector2Int.up);
                }

                cursor += options[rng.Next(options.Count)];
                path.Add(cursor);
            }

            var minX = int.MaxValue;
            var minZ = int.MaxValue;
            var maxX = int.MinValue;
            var maxZ = int.MinValue;
            foreach (var c in path)
            {
                minX = Mathf.Min(minX, c.x);
                minZ = Mathf.Min(minZ, c.y);
                maxX = Mathf.Max(maxX, c.x);
                maxZ = Mathf.Max(maxZ, c.y);
            }

            const int pad = 3;
            var w = maxX - minX + 1 + pad * 2;
            var h = maxZ - minZ + 1 + pad * 2;

            var layout = new CaveMazeLayout
            {
                Width = w,
                Height = h,
                Passage = new bool[w, h],
                CellSize = 3f,
                PlatformSpan = 2.75f,
                PlatformDepth = 2.5f,
                CorridorHeight = CaveThirdPersonClearance.ResolveDefaultCorridorHeight(),
                DropPerRow = 0f,
                SolutionPath = new List<Vector2Int>()
            };

            foreach (var c in path)
            {
                var gx = c.x - minX + pad;
                var gz = c.y - minZ + pad;
                layout.Passage[gx, gz] = true;
                layout.SolutionPath.Add(new Vector2Int(gx, gz));
            }

            layout.StartCell = layout.SolutionPath[0];
            CaveThirdPersonLayoutUtility.ApplyToLayout(layout);
            return layout;
        }

        /// <summary>Ensures spline/path grading sees at least <paramref name="minNetDropMeters"/> descent start → finish.</summary>
        static void EnsureMinimumPathDescent(CaveMazeLayout layout, float minNetDropMeters)
        {
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return;

            var start = layout.SolutionPath[0];
            var end = layout.SolutionPath[layout.SolutionPath.Count - 1];
            var startY = layout.GetFloorSurfaceLocal(start.x, start.y).y;
            var endY = layout.GetFloorSurfaceLocal(end.x, end.y).y;
            var netDrop = startY - endY;
            if (netDrop >= minNetDropMeters)
                return;

            layout.PlatformHeightOffsets ??= new Dictionary<Vector2Int, float>();
            var extra = minNetDropMeters - netDrop;
            var steps = layout.SolutionPath.Count - 1;
            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                var t = steps > 0 ? (float)i / steps : 0f;
                layout.PlatformHeightOffsets.TryGetValue(cell, out var off);
                layout.PlatformHeightOffsets[cell] = off - t * extra;
            }
        }

        /// <summary>Stair-step heights along the route so traversal is jump/platform focused.</summary>
        static void ApplyPlatformerHeights(CaveMazeLayout layout, System.Random rng)
        {
            layout.PlatformHeightOffsets = new Dictionary<Vector2Int, float>();
            if (layout.SolutionPath == null || layout.SolutionPath.Count < 4)
                return;

            var cumulative = 0f;
            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                if (layout.IsCavernCell(cell.x, cell.y))
                    continue;

                if (i > 0 && i % 2 == 0 && !layout.IsJumpGap(cell.x, cell.y))
                {
                    if (rng.NextDouble() < 0.5)
                        cumulative += 0.75f + (float)rng.NextDouble() * 0.55f;
                    else if (rng.NextDouble() < 0.22 && cumulative > 0.6f)
                        cumulative -= 0.65f;
                }

                layout.PlatformHeightOffsets[cell] = cumulative;
            }
        }

        static List<CavePathKnot> BuildPathKnots(CaveMazeLayout layout, int chamberCount, int seed)
        {
            var knots = new List<CavePathKnot>();
            // Radii sized for block-tunnel shell around ~10m corridor interior (CellSize 12).
            var rx = layout.CellSize * 0.95f;
            var ry = layout.CorridorHeight * 0.42f;
            var chamberEvery = Mathf.Max(2, layout.SolutionPath.Count / Mathf.Max(2, chamberCount));
            knots.Add(new CavePathKnot(new Vector3(1f, -0.15f, 3.5f), rx, ry, false));

            for (var i = 0; i < layout.SolutionPath.Count; i++)
            {
                var cell = layout.SolutionPath[i];
                var pos = layout.CellToLocal(cell.x, cell.y);
                var isChamber = layout.IsCavernCell(cell.x, cell.y) ||
                                (i > 0 && i % chamberEvery == 0);
                var mul = isChamber ? 2.2f : 1f;
                if (layout.IsCavernCell(cell.x, cell.y))
                    mul = 3.5f;

                knots.Add(new CavePathKnot(pos, rx * mul, ry * mul, isChamber));
            }

            DensifyPathKnots(knots);
            SnapKnotsToGridY(knots, layout);
            return knots;
        }

        /// <summary>Keeps path grading descent without pulling blocks off the maze floor.</summary>
        static void SnapKnotsToGridY(List<CavePathKnot> knots, CaveMazeLayout layout)
        {
            if (knots == null || knots.Count == 0 || layout == null)
                return;

            for (var i = 0; i < knots.Count; i++)
            {
                var k = knots[i];
                var nearest = FindNearestPathCell(k.Position, layout);
                var cellCenter = layout.CellToLocal(nearest.x, nearest.y);
                var floorY = layout.GetFloorSurfaceLocal(nearest.x, nearest.y).y;
                knots[i] = new CavePathKnot(
                    new Vector3(k.Position.x, Mathf.Lerp(floorY, cellCenter.y, 0.35f), k.Position.z),
                    k.RadiusX,
                    k.RadiusY,
                    k.IsChamber);
            }
        }

        static Vector2Int FindNearestPathCell(Vector3 localPos, CaveMazeLayout layout)
        {
            var best = layout.SolutionPath.Count > 0 ? layout.SolutionPath[0] : layout.StartCell;
            var bestDist = float.PositiveInfinity;
            foreach (var cell in layout.SolutionPath)
            {
                var c = layout.CellToLocal(cell.x, cell.y);
                var d = (new Vector2(c.x, c.z) - new Vector2(localPos.x, localPos.z)).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = cell;
                }
            }

            return best;
        }

        static void DensifyPathKnots(List<CavePathKnot> knots)
        {
            if (knots == null || knots.Count < 3)
                return;

            var dense = new List<CavePathKnot>(knots.Count * 2) { knots[0] };
            for (var i = 1; i < knots.Count; i++)
            {
                var a = knots[i - 1];
                var b = knots[i];
                var midPos = Vector3.Lerp(a.Position, b.Position, 0.5f);
                dense.Add(new CavePathKnot(
                    midPos,
                    Mathf.Lerp(a.RadiusX, b.RadiusX, 0.5f),
                    Mathf.Lerp(a.RadiusY, b.RadiusY, 0.5f),
                    b.IsChamber));
                dense.Add(b);
            }

            knots.Clear();
            knots.AddRange(dense);
        }

        static void EnforceStrictDescent(List<CavePathKnot> knots, float dropPerKnot)
        {
            if (knots == null || knots.Count < 2)
                return;

            var y = knots[0].Position.y;
            for (var i = 0; i < knots.Count; i++)
            {
                var k = knots[i];
                if (i > 0)
                    y -= dropPerKnot;

                knots[i] = new CavePathKnot(
                    new Vector3(k.Position.x, y, k.Position.z),
                    k.RadiusX,
                    k.RadiusY,
                    k.IsChamber);
            }
        }
    }
}
