#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// FullWorld 9×9 build order: center (Ground) → north arm → south → west → east, then back to main.
    /// </summary>
    public static class SurfaceFullWorldDirectionalBuild
    {
        public enum DirectionArm
        {
            Center = 0,
            North = 1,
            South = 2,
            West = 3,
            East = 4,
        }

        public const float FlatNormalizedHeight = 0.42f;

        /// <summary>All 81 grid slots: center, then N → S → W → E arms (each arm sorted center-outward).</summary>
        public static Vector2Int[] BuildDirectionalOffsetOrder()
        {
            var list = new List<Vector2Int>(SurfaceTerrainTileExpansion.FullWorldTerrainTileCount)
            {
                Vector2Int.zero,
            };

            AppendArm(list, CollectNorthArm());
            AppendArm(list, CollectSouthArm());
            AppendArm(list, CollectWestArm());
            AppendArm(list, CollectEastArm());
            return list.ToArray();
        }

        public static DirectionArm ClassifyArm(Vector2Int off)
        {
            if (off == Vector2Int.zero)
                return DirectionArm.Center;

            if (off.y > 0)
                return DirectionArm.North;

            if (off.y < 0)
                return DirectionArm.South;

            if (off.x < 0)
                return DirectionArm.West;

            if (off.x > 0)
                return DirectionArm.East;

            return DirectionArm.Center;
        }

        public static string ArmLabel(DirectionArm arm) =>
            arm switch
            {
                DirectionArm.Center => "center (Ground)",
                DirectionArm.North => "north",
                DirectionArm.South => "south",
                DirectionArm.West => "west",
                DirectionArm.East => "east",
                _ => "center",
            };

        /// <summary>All 81 slots for flat spawn — center first, then row-major on the 9×9 grid.</summary>
        public static Vector2Int[] BuildFullWorldGridSpawnOffsets()
        {
            var list = new List<Vector2Int>(SurfaceTerrainTileExpansion.FullWorldTerrainTileCount)
            {
                Vector2Int.zero,
            };

            for (var x = -SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                 x <= SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                 x++)
            {
                for (var z = -SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                     z <= SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                     z++)
                {
                    if (x == 0 && z == 0)
                        continue;

                    list.Add(new Vector2Int(x, z));
                }
            }

            return list.ToArray();
        }

        static void AppendArm(List<Vector2Int> list, IReadOnlyList<Vector2Int> arm)
        {
            for (var i = 0; i < arm.Count; i++)
            {
                var off = arm[i];
                if (list.Contains(off))
                    continue;

                list.Add(off);
            }
        }

        static List<Vector2Int> CollectNorthArm()
        {
            var arm = new List<Vector2Int>(16);
            for (var x = -SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                 x <= SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                 x++)
            {
                for (var z = 1; z <= SurfaceTerrainTileExpansion.FullWorldChebyshevRadius; z++)
                {
                    var off = new Vector2Int(x, z);
                    if (SurfaceTerrainTileExpansion.GetOuterRingChebyshevDistance(off) >
                        SurfaceTerrainTileExpansion.FullWorldChebyshevRadius)
                        continue;

                    arm.Add(off);
                }
            }

            arm.Sort(CompareNorth);
            return arm;
        }

        static List<Vector2Int> CollectSouthArm()
        {
            var arm = new List<Vector2Int>(16);
            for (var x = -SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                 x <= SurfaceTerrainTileExpansion.FullWorldChebyshevRadius;
                 x++)
            {
                for (var z = -1; z >= -SurfaceTerrainTileExpansion.FullWorldChebyshevRadius; z--)
                {
                    var off = new Vector2Int(x, z);
                    if (SurfaceTerrainTileExpansion.GetOuterRingChebyshevDistance(off) >
                        SurfaceTerrainTileExpansion.FullWorldChebyshevRadius)
                        continue;

                    arm.Add(off);
                }
            }

            arm.Sort(CompareSouth);
            return arm;
        }

        static List<Vector2Int> CollectWestArm()
        {
            var arm = new List<Vector2Int>(3);
            for (var x = -1; x >= -SurfaceTerrainTileExpansion.FullWorldChebyshevRadius; x--)
                arm.Add(new Vector2Int(x, 0));

            return arm;
        }

        static List<Vector2Int> CollectEastArm()
        {
            var arm = new List<Vector2Int>(3);
            for (var x = 1; x <= SurfaceTerrainTileExpansion.FullWorldChebyshevRadius; x++)
                arm.Add(new Vector2Int(x, 0));

            return arm;
        }

        static int CompareNorth(Vector2Int a, Vector2Int b)
        {
            var ringA = SurfaceTerrainTileExpansion.GetOuterRingChebyshevDistance(a);
            var ringB = SurfaceTerrainTileExpansion.GetOuterRingChebyshevDistance(b);
            if (ringA != ringB)
                return ringA.CompareTo(ringB);

            if (a.y != b.y)
                return a.y.CompareTo(b.y);

            return Mathf.Abs(a.x).CompareTo(Mathf.Abs(b.x));
        }

        static int CompareSouth(Vector2Int a, Vector2Int b)
        {
            var ringA = SurfaceTerrainTileExpansion.GetOuterRingChebyshevDistance(a);
            var ringB = SurfaceTerrainTileExpansion.GetOuterRingChebyshevDistance(b);
            if (ringA != ringB)
                return ringA.CompareTo(ringB);

            if (a.y != b.y)
                return b.y.CompareTo(a.y);

            return Mathf.Abs(a.x).CompareTo(Mathf.Abs(b.x));
        }
    }
}
#endif
