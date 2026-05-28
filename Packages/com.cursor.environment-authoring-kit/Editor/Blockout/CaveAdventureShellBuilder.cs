using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Legacy slab shell (do not call Build). Full builds use <see cref="CaveEnclosureShellBuilder"/> only.
    /// </summary>
    public static class CaveAdventureShellBuilder
    {
        public const string ShellRootName = "AdventureShell";

        public static int Build(Transform meshRoot, CaveMazeLayout layout, Material rockMat, Material floorMat)
        {
            if (meshRoot == null || layout == null)
                return 0;

            var existing = meshRoot.Find(ShellRootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var existingMaze = meshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName);
            if (existingMaze != null)
                CaveEditorUndo.DestroyImmediate(existingMaze.gameObject);

            var root = new GameObject(ShellRootName);
            CaveEditorUndo.RegisterCreated(root, "Adventure Shell");
            root.transform.SetParent(meshRoot, false);
            if (root.GetComponent<CaveMazeVolumeMarker>() == null)
                root.AddComponent<CaveMazeVolumeMarker>();

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (floorMat == null)
                floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            var placed = 0;
            var floorThickness = 1.2f;
            var ceilingThickness = 1.15f;
            var span = layout.CellSize * 0.97f;

            placed += BuildOuterRockShell(root.transform, layout, rockMat);
            placed += BuildEntranceFloor(root.transform, layout, floorMat, floorThickness);
            placed += BuildEntranceRockLid(root.transform, layout, rockMat);
            placed += BuildSolutionPathCeiling(root.transform, layout, rockMat, ceilingThickness, span);

            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                {
                    if (!layout.IsPassage(x, z))
                        continue;

                    if (layout.IsCavernCell(x, z) && x == layout.CavernCenter.x && z == layout.CavernCenter.y)
                    {
                        placed += BuildCavern(root.transform, layout, rockMat, floorMat, floorThickness, ceilingThickness);
                        continue;
                    }

                    placed += BuildPassageCell(root.transform, layout, x, z, floorMat, rockMat,
                        span, floorThickness, ceilingThickness);
                }
            }

            return placed;
        }

        static int BuildPassageCell(
            Transform parent,
            CaveMazeLayout layout,
            int x,
            int z,
            Material floorMat,
            Material rockMat,
            float span,
            float floorThickness,
            float ceilingThickness)
        {
            var center = layout.CellToLocal(x, z);
            var floorSurface = layout.GetFloorSurfaceLocal(x, z);
            var h = layout.CorridorHeight;
            var count = 0;
            var isGap = layout.IsJumpGap(x, z);

            if (!isGap)
            {
                count += CreateSlabAt(parent, $"Floor_{x}_{z}", floorSurface + Vector3.up * (floorThickness * 0.5f),
                    new Vector3(span, floorThickness, span), floorMat, walkable: true);
            }

            return count;
        }

        /// <summary>Thick rock lid over the spawn cell so looking up does not show surface roads/sky.</summary>
        static int BuildEntranceRockLid(Transform parent, CaveMazeLayout layout, Material rockMat)
        {
            if (layout.SolutionPath == null || layout.SolutionPath.Count == 0)
                return 0;

            var start = layout.SolutionPath[0];
            var center = layout.CellToLocal(start.x, start.y);
            var h = layout.CorridorHeight;
            var lidY = center.y + h * 0.5f + 2.5f;
            var span = layout.CellSize * 1.35f;

            return CreateSlabAt(parent, "Entrance_RockLid", new Vector3(center.x, lidY, center.z),
                new Vector3(span, 5f, span), rockMat, walkable: false);
        }

        /// <summary>Re-applies rock lid + outer top from last build metadata (after visual/quality passes).</summary>
        public static void TryReapplyHybridSkySeal(Transform caveRoot)
        {
            if (caveRoot == null || !CaveGeometryPaths.IsAdventureCave(caveRoot))
                return;

            var geometry = caveRoot.Find(CaveAdventureCaveGenerator.GeometryRootName);
            if (geometry == null)
                return;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return;

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            EnsureHybridSkySeal(geometry, layout, CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial());
        }

        /// <summary>Re-applies sky seal after visual pass (never strip Entrance_RockLid / Outer_Top).</summary>
        public static void EnsureHybridSkySeal(Transform geometry, CaveMazeLayout layout, Material rockMat)
        {
            if (geometry == null || layout == null)
                return;

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            var shell = geometry.Find(ShellRootName);
            if (shell == null)
            {
                var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
                if (platforms != null && platforms.childCount >= 4)
                    return;

                CaveAdventureBlockBuilder.BuildSkyRockCap(geometry, layout, rockMat);
                return;
            }

            if (shell.Find("Entrance_RockLid") == null)
                BuildEntranceRockLid(shell, layout, rockMat);

            var outer = shell.Find(CaveMazeVolumeBuilder.OuterShellRootName);
            if (outer != null && outer.Find("Outer_Top") == null)
            {
                layout.ComputeLocalBounds(out var min, out var max);
                var pad = layout.CellSize * 0.55f;
                min -= new Vector3(pad, 2f, pad);
                max += new Vector3(pad, layout.CorridorHeight * 1.6f, pad);
                var center = (min + max) * 0.5f;
                var size = max - min;
                var shellThick = Mathf.Max(2.5f, layout.CellSize * 0.22f);
                CreateShellPanel(outer, "Outer_Top", center + new Vector3(0f, size.y * 0.5f, 0f),
                    new Vector3(size.x + shellThick * 2f, shellThick * 2.5f, size.z + shellThick * 2f), rockMat);
            }
        }

        /// <summary>Enclosed ramp from surface entrance down to the first maze cell (blocks view of roads).</summary>
        public static int BuildEntranceDropShaft(Transform geometry, CaveMazeLayout layout, Material rockMat)
        {
            if (layout.SolutionPath == null || layout.SolutionPath.Count == 0)
                return 0;

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            var shell = geometry.Find(ShellRootName);
            if (shell == null)
                return 0;

            var start = layout.SolutionPath[0];
            var endFloor = layout.GetFloorSurfaceLocal(start.x, start.y);
            var top = new Vector3(1f, CaveGeometryPaths.UndergroundDepthMeters - 1.5f, 3.5f);
            var bottom = endFloor + Vector3.up * 0.5f;
            var delta = bottom - top;
            var length = delta.magnitude;
            if (length < 4f)
                return 0;

            var mid = (top + bottom) * 0.5f;
            var run = new Vector3(delta.x, 0f, delta.z);
            if (run.sqrMagnitude < 0.01f)
                run = Vector3.forward;
            else
                run.Normalize();

            var placed = 0;
            var thickness = 2.2f;
            var width = layout.CellSize * 0.9f;
            var height = length + layout.CellSize * 0.35f;

            var shaftRot = Quaternion.LookRotation(run, Vector3.up);
            var right = Vector3.Cross(Vector3.up, run).normalized;

            placed += CreateSlabAt(shell, "Entrance_Shaft_Floor", mid,
                new Vector3(width, 1.1f, height), rockMat, walkable: true, shaftRot);
            placed += CreateSlabAt(shell, "Entrance_Shaft_Ceiling", mid + Vector3.up * (layout.CorridorHeight * 0.55f),
                new Vector3(width, 1.2f, height), rockMat, walkable: false, shaftRot);
            placed += CreateSlabAt(shell, "Entrance_Shaft_Left", mid + right * (width * 0.48f),
                new Vector3(thickness, layout.CorridorHeight, height), rockMat, walkable: false, shaftRot);
            placed += CreateSlabAt(shell, "Entrance_Shaft_Right", mid - right * (width * 0.48f),
                new Vector3(thickness, layout.CorridorHeight, height), rockMat, walkable: false, shaftRot);

            return placed;
        }

        /// <summary>One ceiling segment per path step — avoids stacked per-cell ceiling bands in the distance.</summary>
        public static int BuildSolutionPathCeiling(
            Transform parent,
            CaveMazeLayout layout,
            Material rockMat,
            float thickness,
            float span)
        {
            if (layout.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0;

            var placed = 0;
            for (var i = 0; i < layout.SolutionPath.Count - 1; i++)
            {
                var a = layout.SolutionPath[i];
                var b = layout.SolutionPath[i + 1];
                if (layout.IsJumpGap(a.x, a.y) || layout.IsJumpGap(b.x, b.y))
                    continue;

                var ca = layout.CellToLocal(a.x, a.y);
                var cb = layout.CellToLocal(b.x, b.y);
                var ha = layout.IsCavernCell(a.x, a.y) ? layout.CorridorHeight * 1.75f : layout.CorridorHeight;
                var hb = layout.IsCavernCell(b.x, b.y) ? layout.CorridorHeight * 1.75f : layout.CorridorHeight;
                var ya = ca.y + ha * 0.5f - thickness * 0.5f;
                var yb = cb.y + hb * 0.5f - thickness * 0.5f;
                var mid = (ca + cb) * 0.5f;
                var y = (ya + yb) * 0.5f;
                var delta = cb - ca;
                delta.y = 0f;
                var length = delta.magnitude + span * 0.35f;
                if (length < 0.5f)
                    continue;

                var forward = delta.normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = Vector3.forward;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CaveEditorUndo.RegisterCreated(go, "Path Ceiling");
                go.name = $"PathCeiling_{i:D2}";
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(mid.x, y, mid.z);
                go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
                go.transform.localScale = new Vector3(span, thickness, length);
                ApplySurface(go, rockMat, walkable: false);
                placed++;
            }

            return placed;
        }

        static int BuildCavern(
            Transform parent,
            CaveMazeLayout layout,
            Material rockMat,
            Material floorMat,
            float floorThickness,
            float ceilingThickness)
        {
            var r = layout.CavernRadiusCells;
            var span = (r * 2 + 1) * layout.CellSize - 1f;
            var center = layout.CellToLocal(layout.CavernCenter.x, layout.CavernCenter.y);
            var h = layout.CorridorHeight * 1.75f;
            var count = 0;

            count += CreateSlab(parent, "Cavern_Floor", center, h, floorThickness, span, floorMat, floor: true);
            count += CreateSlab(parent, "Cavern_Ceiling", center, h, ceilingThickness, span, rockMat, floor: false);

            var pillar = span * 0.2f;
            var offsets = new[]
            {
                new Vector3(pillar, 0f, pillar),
                new Vector3(-pillar, 0f, pillar),
                new Vector3(pillar, 0f, -pillar),
                new Vector3(-pillar, 0f, -pillar)
            };
            foreach (var off in offsets)
            {
                count += CreateSlab(parent, "Cavern_Pillar", center + off, h, h * 0.9f,
                    pillar * 0.65f, rockMat, floor: false, vertical: true);
            }

            return count;
        }

        static int BuildEntranceFloor(Transform parent, CaveMazeLayout layout, Material floorMat, float floorThickness)
        {
            var start = new Vector3(1f, 0f, 3.5f);
            var end = layout.CellToLocal(layout.StartCell.x, layout.StartCell.y);
            var delta = end - start;
            var length = new Vector3(delta.x, 0f, delta.z).magnitude;
            if (length < 3f)
                return 0;

            var mid = (start + end) * 0.5f;
            mid.y = layout.GetFloorSurfaceLocal(layout.StartCell.x, layout.StartCell.y).y + floorThickness * 0.5f;
            var run = new Vector3(delta.x, 0f, delta.z).normalized;
            if (run.sqrMagnitude < 0.01f)
                run = Vector3.forward;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Entrance Floor");
            go.name = "Entrance_Floor";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = mid;
            go.transform.localRotation = Quaternion.LookRotation(run, Vector3.up);
            go.transform.localScale = new Vector3(layout.CellSize * 0.85f, floorThickness, length + layout.CellSize * 0.4f);
            ApplySurface(go, floorMat, walkable: true);
            return 1;
        }

        static int BuildOuterRockShell(Transform parent, CaveMazeLayout layout, Material mat)
        {
            layout.ComputeLocalBounds(out var min, out var max);
            var pad = layout.CellSize * 0.55f;
            min -= new Vector3(pad, 2f, pad);
            max += new Vector3(pad, layout.CorridorHeight * 1.6f, pad);

            var center = (min + max) * 0.5f;
            var size = max - min;
            var shell = Mathf.Max(2.5f, layout.CellSize * 0.22f);
            var count = 0;

            var shellRoot = new GameObject(CaveMazeVolumeBuilder.OuterShellRootName);
            CaveEditorUndo.RegisterCreated(shellRoot, "Outer Shell");
            shellRoot.transform.SetParent(parent, false);

            count += CreateShellPanel(shellRoot.transform, "Outer_North", center + new Vector3(0f, 0f, size.z * 0.5f),
                new Vector3(size.x + shell * 2f, size.y, shell), mat);
            count += CreateShellPanel(shellRoot.transform, "Outer_South", center + new Vector3(0f, 0f, -size.z * 0.5f),
                new Vector3(size.x + shell * 2f, size.y, shell), mat);
            count += CreateShellPanel(shellRoot.transform, "Outer_East", center + new Vector3(size.x * 0.5f, 0f, 0f),
                new Vector3(shell, size.y, size.z + shell * 2f), mat);
            count += CreateShellPanel(shellRoot.transform, "Outer_West", center + new Vector3(-size.x * 0.5f, 0f, 0f),
                new Vector3(shell, size.y, size.z + shell * 2f), mat);
            count += CreateShellPanel(shellRoot.transform, "Outer_Top", center + new Vector3(0f, size.y * 0.5f, 0f),
                new Vector3(size.x + shell * 2f, shell * 2.5f, size.z + shell * 2f), mat);

            return count;
        }

        static int CreateSlabAt(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 scale,
            Material mat,
            bool walkable,
            Quaternion localRot = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, walkable ? "Adventure Floor" : "Adventure Ceiling");
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot == default ? Quaternion.identity : localRot;
            go.transform.localScale = scale;
            ApplySurface(go, mat, walkable: walkable);

            var col = go.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = false;

            return 1;
        }

        static int CreateSlab(
            Transform parent,
            string name,
            Vector3 cellCenter,
            float corridorHeight,
            float thickness,
            float span,
            Material mat,
            bool floor,
            bool vertical = false)
        {
            if (vertical)
            {
                return CreateSlabAt(parent, name, cellCenter, new Vector3(span, thickness, span), mat, walkable: false);
            }

            if (floor)
            {
                var floorY = cellCenter.y - corridorHeight * 0.5f + thickness * 0.5f;
                return CreateSlabAt(parent, name, new Vector3(cellCenter.x, floorY, cellCenter.z),
                    new Vector3(span, thickness, span), mat, walkable: true);
            }

            var ceilingY = cellCenter.y + corridorHeight * 0.5f - thickness * 0.5f;
            return CreateSlabAt(parent, name, new Vector3(cellCenter.x, ceilingY, cellCenter.z),
                new Vector3(span, thickness, span), mat, walkable: false);
        }

        static int CreateShellPanel(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Outer Shell");
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            }

            var col = go.GetComponent<Collider>();
            if (col != null)
                CaveEditorUndo.DestroyImmediate(col);

            go.isStatic = true;
            return 1;
        }

        static void ApplySurface(GameObject go, Material mat, bool walkable)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
                mr.enabled = true;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }

            var col = go.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.isTrigger = false;
                col.size = Vector3.one;
                col.center = Vector3.zero;
            }

            if (walkable)
            {
                go.name = go.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix)
                    ? go.name
                    : $"{CaveWalkwayBuilder.WalkFloorPrefix}{go.name}";
                if (go.GetComponent<CaveWalkableMarker>() == null)
                    go.AddComponent<CaveWalkableMarker>();
            }

            go.isStatic = true;
        }
    }
}
