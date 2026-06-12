#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Parses planner brief labyrinthNote + markers into wall topology (replaces hardcoded arrays).</summary>
    public static class CaveBuildPlannerMazeTopology
    {
        public sealed class Topology
        {
            public bool[,] HorizontalWall = new bool[2, 3];
            public bool[,] VerticalWall = new bool[3, 2];
            public readonly List<(int row, int col)> Exits = new();
            public string Source = "fallback";
        }

        static readonly Regex ExitPattern = new(
            @"([NESW])\s*\(\s*(\d)\s*,\s*(\d)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static Topology Resolve(CaveBuildPlannerLayoutBridge.LayoutPlan plan)
        {
            var topo = BuildFallback();
            if (plan?.playDisk == null || !plan.playDisk.labyrinth)
                return topo;

            var note = plan.playDisk.labyrinthNote ?? string.Empty;
            var exits = ParseExits(note);
            if (exits.Count == 0)
                exits.AddRange(ParseExits(plan.gridNote ?? string.Empty));

            if (exits.Count > 0)
            {
                ApplyFullGridMaze(topo, exits);
                PunchSwitchGates(topo, plan);
                topo.Source = $"labyrinthNote ({exits.Count} exit(s))";
                return topo;
            }

            PunchSwitchGates(topo, plan);
            if (topo.Exits.Count > 0)
                topo.Source = "switch markers";
            return topo;
        }

        public static bool HasHorizontalWall(Topology topo, int row, int col) =>
            topo != null && row >= 0 && row < 2 && col >= 0 && col < 3 && topo.HorizontalWall[row, col];

        public static bool HasVerticalWall(Topology topo, int row, int col) =>
            topo != null && row >= 0 && row < 3 && col >= 0 && col < 2 && topo.VerticalWall[row, col];

        static Topology BuildFallback()
        {
            return new Topology
            {
                HorizontalWall =
                {
                    [0, 0] = true,
                    [0, 1] = false,
                    [0, 2] = true,
                    [1, 0] = false,
                    [1, 1] = false,
                    [1, 2] = false,
                },
                VerticalWall =
                {
                    [0, 0] = true,
                    [0, 1] = true,
                    [1, 0] = false,
                    [1, 1] = false,
                    [2, 0] = true,
                    [2, 1] = true,
                },
                Source = "fallback",
            };
        }

        static List<(char dir, int row, int col)> ParseExits(string note)
        {
            var list = new List<(char, int, int)>();
            if (string.IsNullOrEmpty(note))
                return list;

            foreach (Match m in ExitPattern.Matches(note))
            {
                if (!m.Success)
                    continue;
                var dir = char.ToUpperInvariant(m.Groups[1].Value[0]);
                if (int.TryParse(m.Groups[2].Value, out var row) &&
                    int.TryParse(m.Groups[3].Value, out var col))
                    list.Add((dir, row, col));
            }

            return list;
        }

        static void ApplyFullGridMaze(Topology topo, List<(char dir, int row, int col)> exits)
        {
            for (var r = 0; r < 2; r++)
            for (var c = 0; c < 3; c++)
                topo.HorizontalWall[r, c] = true;

            for (var r = 0; r < 3; r++)
            for (var c = 0; c < 2; c++)
                topo.VerticalWall[r, c] = true;

            const int centerRow = 1;
            const int centerCol = 1;
            topo.HorizontalWall[centerRow, centerCol] = false;
            topo.VerticalWall[centerRow, centerCol] = false;

            foreach (var (dir, row, col) in exits)
            {
                topo.Exits.Add((row, col));
                OpenCorridorToCell(topo, centerRow, centerCol, row, col);
                OpenPerimeterGap(topo, dir, row, col);
            }
        }

        static void OpenCorridorToCell(Topology topo, int fromRow, int fromCol, int toRow, int toCol)
        {
            var row = fromRow;
            var col = fromCol;
            while (row != toRow || col != toCol)
            {
                if (row < toRow)
                {
                    topo.HorizontalWall[row, col] = false;
                    row++;
                }
                else if (row > toRow)
                {
                    topo.HorizontalWall[row - 1, col] = false;
                    row--;
                }
                else if (col < toCol)
                {
                    topo.VerticalWall[row, col] = false;
                    col++;
                }
                else
                {
                    topo.VerticalWall[row, col - 1] = false;
                    col--;
                }
            }
        }

        static void OpenPerimeterGap(Topology topo, char dir, int row, int col)
        {
            switch (dir)
            {
                case 'N' when row == 0:
                    topo.HorizontalWall[0, col] = false;
                    break;
                case 'S' when row == 2:
                    topo.HorizontalWall[1, col] = false;
                    break;
                case 'E' when col == 2:
                    topo.VerticalWall[row, 1] = false;
                    break;
                case 'W' when col == 0:
                    topo.VerticalWall[row, 0] = false;
                    break;
            }
        }

        static void PunchSwitchGates(Topology topo, CaveBuildPlannerLayoutBridge.LayoutPlan plan)
        {
            if (plan?.markers == null)
                return;

            foreach (var m in plan.markers)
            {
                if (m == null || !CaveBuildPlannerLayoutBridge.IsSwitchMarker(m))
                    continue;

                var row = m.row;
                var col = m.col;
                if (row == 0)
                    topo.HorizontalWall[0, col] = false;
                if (row == 2)
                    topo.HorizontalWall[1, col] = false;
                if (col == 0)
                    topo.VerticalWall[row, 0] = false;
                if (col == 2)
                    topo.VerticalWall[row, 1] = false;
            }
        }
    }
}
#endif
