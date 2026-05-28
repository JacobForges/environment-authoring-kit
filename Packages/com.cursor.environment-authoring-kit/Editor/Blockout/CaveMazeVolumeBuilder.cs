using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Debug-room technique: every corridor cell is a fully enclosed box.
    /// Doorways connect passages; a rock outer shell seals the maze from the sky.
    /// </summary>
    public static class CaveMazeVolumeBuilder
    {
        public const string MazeVolumeRootName = "CaveMazeVolume";
        public const string OuterShellRootName = "MazeOuterShell";

        const float DoorWidthRatio = 0.28f;
        const float DoorHeightMeters = 5.8f;

        public static int Build(Transform meshRoot, CaveMazeLayout layout, Material rockMat, bool adventureHybrid = false)
        {
            if (meshRoot == null || layout == null)
                return 0;

            var existing = meshRoot.Find(MazeVolumeRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var root = new GameObject(MazeVolumeRootName);
            CaveEditorUndo.RegisterCreated(root, "Cave Maze Volume");
            root.transform.SetParent(meshRoot, false);
            if (root.GetComponent<CaveMazeVolumeMarker>() == null)
                root.AddComponent<CaveMazeVolumeMarker>();

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            var wallThickness = 0.75f;
            var interior = layout.CellSize - wallThickness * 2f;
            var placed = 0;

            placed += BuildOuterShell(root.transform, layout, rockMat);
            placed += BuildEntranceCorridor(root.transform, layout, rockMat, floorMat, interior, wallThickness);

            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                {
                    if (!layout.IsPassage(x, z))
                    {
                        if (!adventureHybrid)
                            placed += BuildRockFill(root.transform, layout, x, z, rockMat);
                        continue;
                    }

                    if (layout.IsCavernCell(x, z))
                    {
                        if (x == layout.CavernCenter.x && z == layout.CavernCenter.y)
                            placed += BuildGrandCavern(root.transform, layout, rockMat, floorMat, adventureHybrid);
                        continue;
                    }

                    if (adventureHybrid)
                        placed += BuildCorridorCellFloorCeilingOnly(root.transform, layout, x, z, rockMat, floorMat, interior, wallThickness);
                    else
                        placed += BuildCorridorCell(root.transform, layout, x, z, rockMat, floorMat, interior, wallThickness);
                }
            }

            return placed;
        }

        static int BuildCorridorCellFloorCeilingOnly(
            Transform parent,
            CaveMazeLayout layout,
            int x,
            int z,
            Material rockMat,
            Material floorMat,
            float interior,
            float wallThickness)
        {
            var center = layout.CellToLocal(x, z);
            var h = layout.CorridorHeight;
            if (layout.IsJumpGap(x, z))
            {
                var gapCeilingSpan = layout.CellSize * 0.96f;
                var gapCeilingThickness = 1.1f;
                return CreateWall(parent, $"Ceiling_{x}_{z}", center + Vector3.up * (h * 0.5f - gapCeilingThickness * 0.5f),
                    new Vector3(gapCeilingSpan, gapCeilingThickness, gapCeilingSpan), rockMat);
            }

            var count = 0;
            var floorThickness = 1.05f;

            count += CreateWall(parent, $"Floor_{x}_{z}", center + Vector3.down * (h * 0.5f - floorThickness * 0.5f),
                new Vector3(interior, floorThickness, interior), floorMat, walkable: true);
            var ceilingSpan = layout.CellSize * 0.96f;
            var ceilingThickness = 1.1f;
            count += CreateWall(parent, $"Ceiling_{x}_{z}", center + Vector3.up * (h * 0.5f - ceilingThickness * 0.5f),
                new Vector3(ceilingSpan, ceilingThickness, ceilingSpan), rockMat);
            return count;
        }

        static int BuildRockFill(Transform parent, CaveMazeLayout layout, int x, int z, Material rockMat)
        {
            var center = layout.CellToLocal(x, z);
            var h = layout.CorridorHeight;
            return CreateWall(parent, $"RockFill_{x}_{z}", center,
                new Vector3(layout.CellSize * 0.98f, h, layout.CellSize * 0.98f), rockMat);
        }

        static int BuildOuterShell(Transform parent, CaveMazeLayout layout, Material mat)
        {
            layout.ComputeLocalBounds(out var min, out var max);
            var pad = layout.CellSize * 0.65f;
            min -= new Vector3(pad, 2f, pad);
            max += new Vector3(pad, layout.CorridorHeight * 1.8f, pad);

            var center = (min + max) * 0.5f;
            var size = max - min;
            var shell = Mathf.Max(2.5f, layout.CellSize * 0.22f);
            var count = 0;

            var shellRoot = new GameObject(OuterShellRootName);
            CaveEditorUndo.RegisterCreated(shellRoot, "Maze Outer Shell");
            shellRoot.transform.SetParent(parent, false);

            count += CreateShellPanel(shellRoot.transform, "Outer_Floor",
                center + Vector3.down * (size.y * 0.5f - shell * 0.5f),
                new Vector3(size.x + shell * 2f, shell, size.z + shell * 2f), mat, collider: false);
            count += CreateShellPanel(shellRoot.transform, "Outer_Ceiling",
                center + Vector3.up * (size.y * 0.5f - shell * 0.5f),
                new Vector3(size.x + shell * 2f, shell, size.z + shell * 2f), mat, collider: false);
            count += CreateWall(shellRoot.transform, "Outer_North",
                center + new Vector3(0f, 0f, size.z * 0.5f),
                new Vector3(size.x + shell * 2f, size.y, shell), mat);
            count += CreateWall(shellRoot.transform, "Outer_South",
                center + new Vector3(0f, 0f, -size.z * 0.5f),
                new Vector3(size.x + shell * 2f, size.y, shell), mat);
            count += CreateWall(shellRoot.transform, "Outer_East",
                center + new Vector3(size.x * 0.5f, 0f, 0f),
                new Vector3(shell, size.y, size.z + shell * 2f), mat);
            count += CreateWall(shellRoot.transform, "Outer_West",
                center + new Vector3(-size.x * 0.5f, 0f, 0f),
                new Vector3(shell, size.y, size.z + shell * 2f), mat);

            return count;
        }

        static int BuildCorridorCell(
            Transform parent,
            CaveMazeLayout layout,
            int x,
            int z,
            Material rockMat,
            Material floorMat,
            float interior,
            float wallThickness)
        {
            var center = layout.CellToLocal(x, z);
            var h = layout.CorridorHeight;
            var count = 0;

            count += CreateWall(parent, $"Floor_{x}_{z}", center + Vector3.down * (h * 0.5f - wallThickness * 0.5f),
                new Vector3(interior, wallThickness, interior), floorMat);
            count += CreateWall(parent, $"Ceiling_{x}_{z}", center + Vector3.up * (h * 0.5f - wallThickness * 0.5f),
                new Vector3(interior, wallThickness, interior), rockMat);

            count += BuildFace(parent, layout, x, z, center, h, wallThickness, interior, rockMat, Face.North);
            count += BuildFace(parent, layout, x, z, center, h, wallThickness, interior, rockMat, Face.South);
            count += BuildFace(parent, layout, x, z, center, h, wallThickness, interior, rockMat, Face.East);
            count += BuildFace(parent, layout, x, z, center, h, wallThickness, interior, rockMat, Face.West);

            return count;
        }

        enum Face { North, South, East, West }

        static int BuildFace(
            Transform parent,
            CaveMazeLayout layout,
            int x,
            int z,
            Vector3 center,
            float h,
            float wallThickness,
            float interior,
            Material mat,
            Face face)
        {
            var neighborPassage = face switch
            {
                Face.North => layout.IsPassage(x, z + 1),
                Face.South => layout.IsPassage(x, z - 1),
                Face.East => layout.IsPassage(x + 1, z),
                _ => layout.IsPassage(x - 1, z)
            };

            var faceCenter = face switch
            {
                Face.North => center + new Vector3(0f, 0f, interior * 0.5f),
                Face.South => center + new Vector3(0f, 0f, -interior * 0.5f),
                Face.East => center + new Vector3(interior * 0.5f, 0f, 0f),
                _ => center + new Vector3(-interior * 0.5f, 0f, 0f)
            };

            var faceSize = face is Face.North or Face.South
                ? new Vector3(interior, h, wallThickness)
                : new Vector3(wallThickness, h, interior);

            var prefix = face switch
            {
                Face.North => $"WallN_{x}_{z}",
                Face.South => $"WallS_{x}_{z}",
                Face.East => $"WallE_{x}_{z}",
                _ => $"WallW_{x}_{z}"
            };

            if (!neighborPassage)
                return CreateWall(parent, prefix, faceCenter, faceSize, mat);

            return BuildWallWithDoorway(parent, prefix, faceCenter, faceSize, h, interior, mat, face);
        }

        static int BuildWallWithDoorway(
            Transform parent,
            string prefix,
            Vector3 faceCenter,
            Vector3 faceSize,
            float h,
            float interior,
            Material mat,
            Face face)
        {
            var doorWidth = Mathf.Clamp(interior * DoorWidthRatio, 3.8f, interior * 0.7f);
            var doorHeight = Mathf.Min(DoorHeightMeters, h * 0.82f);
            var panelWidth = Mathf.Max(0.8f, (interior - doorWidth) * 0.5f);
            var lintelHeight = Mathf.Max(0.8f, h - doorHeight);
            var count = 0;

            var horizontalFace = face is Face.North or Face.South;
            var right = horizontalFace ? Vector3.right : Vector3.forward;
            var up = Vector3.up;

            var panelOffset = horizontalFace
                ? new Vector3((doorWidth + panelWidth) * 0.5f, 0f, 0f)
                : new Vector3(0f, 0f, (doorWidth + panelWidth) * 0.5f);

            var panelSize = horizontalFace
                ? new Vector3(panelWidth, h, faceSize.z)
                : new Vector3(faceSize.x, h, panelWidth);

            count += CreateWall(parent, $"{prefix}_L", faceCenter - panelOffset, panelSize, mat);
            count += CreateWall(parent, $"{prefix}_R", faceCenter + panelOffset, panelSize, mat);

            var lintelCenter = faceCenter + up * (doorHeight * 0.5f + lintelHeight * 0.5f);
            var lintelSize = horizontalFace
                ? new Vector3(doorWidth, lintelHeight, faceSize.z)
                : new Vector3(faceSize.x, lintelHeight, doorWidth);
            count += CreateWall(parent, $"{prefix}_Lint", lintelCenter, lintelSize, mat);

            return count;
        }

        static int BuildGrandCavern(Transform parent, CaveMazeLayout layout, Material rockMat, Material floorMat, bool adventureHybrid = false)
        {
            var r = layout.CavernRadiusCells;
            var span = (r * 2 + 1) * layout.CellSize - 1.2f;
            var center = layout.CellToLocal(layout.CavernCenter.x, layout.CavernCenter.y);
            var height = layout.CorridorHeight * 1.75f;
            var wallThickness = 0.85f;
            var interior = span - wallThickness * 2f;
            var count = 0;

            var cavernFloorThickness = 1.1f;
            count += CreateWall(parent, "Cavern_Floor", center + Vector3.down * (height * 0.5f - cavernFloorThickness * 0.5f),
                new Vector3(span, cavernFloorThickness, span), floorMat, walkable: true);
            count += CreateWall(parent, "Cavern_Ceiling", center + Vector3.up * (height * 0.5f - wallThickness * 0.5f),
                new Vector3(span, wallThickness, span), rockMat);

            if (!adventureHybrid)
            {
                var cx = layout.CavernCenter.x;
                var cz = layout.CavernCenter.y;
                count += BuildFace(parent, layout, cx, cz, center, height, wallThickness, interior, rockMat, Face.North);
                count += BuildFace(parent, layout, cx, cz, center, height, wallThickness, interior, rockMat, Face.South);
                count += BuildFace(parent, layout, cx, cz, center, height, wallThickness, interior, rockMat, Face.East);
                count += BuildFace(parent, layout, cx, cz, center, height, wallThickness, interior, rockMat, Face.West);
            }

            var pillarSpan = span * 0.22f;
            var offsets = new[]
            {
                new Vector3(pillarSpan, 0f, pillarSpan),
                new Vector3(-pillarSpan, 0f, pillarSpan),
                new Vector3(pillarSpan, 0f, -pillarSpan),
                new Vector3(-pillarSpan, 0f, -pillarSpan)
            };
            foreach (var off in offsets)
            {
                count += CreateWall(parent, "Cavern_Pillar", center + off,
                    new Vector3(pillarSpan * 0.7f, height * 0.92f, pillarSpan * 0.7f), rockMat);
            }

            return count;
        }

        static int BuildEntranceCorridor(
            Transform parent,
            CaveMazeLayout layout,
            Material rockMat,
            Material floorMat,
            float interior,
            float wallThickness)
        {
            var h = layout.CorridorHeight;
            var start = new Vector3(1f, 0f, 3.5f);
            var end = layout.CellToLocal(layout.StartCell.x, layout.StartCell.y);
            var delta = end - start;
            var length = new Vector3(delta.x, 0f, delta.z).magnitude;
            if (length < 4f)
                return 0;

            var mid = (start + end) * 0.5f;
            mid.y = 0f;
            var run = new Vector3(delta.x, 0f, delta.z).normalized;
            var right = Vector3.Cross(Vector3.up, run).normalized;
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;

            var count = 0;
            var segLen = length + interior * 0.5f;
            var entranceFloorThickness = 1.05f;
            count += CreateWall(parent, "Entrance_Floor", mid + Vector3.down * (h * 0.5f - entranceFloorThickness * 0.5f),
                new Vector3(interior * 0.9f, entranceFloorThickness, segLen), floorMat, walkable: true);
            count += CreateWall(parent, "Entrance_Ceiling", mid + Vector3.up * (h * 0.5f - wallThickness * 0.5f),
                new Vector3(interior * 0.9f, wallThickness, segLen), rockMat);
            count += CreateWall(parent, "Entrance_WallL", mid - right * (interior * 0.5f),
                new Vector3(wallThickness, h, segLen), rockMat);
            count += CreateWall(parent, "Entrance_WallR", mid + right * (interior * 0.5f),
                new Vector3(wallThickness, h, segLen), rockMat);
            count += CreateWall(parent, "Entrance_EndCap", start - run * (interior * 0.35f),
                new Vector3(interior * 0.9f, h, wallThickness), rockMat);

            return count;
        }

        static int CreateShellPanel(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            CaveEditorUndo.RegisterCreated(go, "Maze Shell");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
                mr.receiveShadows = true;
            }

            if (!collider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null)
                    CaveEditorUndo.DestroyImmediate(col);
            }

            go.isStatic = true;
            return 1;
        }

        static int CreateWall(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool walkable = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            CaveEditorUndo.RegisterCreated(go, "Maze Wall");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
                mr.receiveShadows = true;
            }

            var box = go.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.isTrigger = false;
                box.size = Vector3.one;
                box.center = Vector3.zero;
            }

            if (walkable && go.GetComponent<CaveWalkableMarker>() == null)
                go.AddComponent<CaveWalkableMarker>();

            go.isStatic = true;
            return 1;
        }
    }
}
