using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Jump pits, landing ledges, and minable surprise pockets for hybrid maze + block caves.</summary>
    public static class CaveAdventureFeaturesBuilder
    {
        public const string RootName = "AdventureFeatures";

        static Material _warningTorchMaterial;

        public static int Build(
            Transform cavesRoot,
            CaveMazeLayout layout,
            Material rockMat,
            Material floorMat,
            int seed)
        {
            if (cavesRoot == null || layout == null)
                return 0;

            var root = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, RootName);
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);

            if (rockMat == null)
                rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (floorMat == null)
                floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();

            var rng = new System.Random(seed + 401);
            var placed = 0;
            placed += BuildJumpGaps(root, layout, rockMat, floorMat, rng);
            placed += BuildSurprisePockets(root, layout, rockMat, rng);
            return placed;
        }

        static int BuildJumpGaps(
            Transform parent,
            CaveMazeLayout layout,
            Material rockMat,
            Material floorMat,
            System.Random rng)
        {
            if (layout.JumpGapCells == null || layout.JumpGapCells.Count == 0)
                return 0;

            var placed = 0;
            var gapWidth = Mathf.Clamp(layout.CellSize * 0.55f, 1.1f, 1.65f);
            var ledgeDepth = layout.PlatformDepth * 0.85f;
            var ledgeWidth = Mathf.Max(1.1f, (layout.PlatformSpan - gapWidth) * 0.5f - 0.15f);
            foreach (var cell in layout.JumpGapCells)
            {
                var pitDepth = Mathf.Max(8f, layout.GetCeilingClearanceAt(cell.x, cell.y) * 0.65f);
                var center = layout.CellToLocal(cell.x, cell.y);
                var floorY = layout.GetFloorSurfaceLocal(cell.x, cell.y).y;
                var forward = ResolvePathForward(layout, cell);
                var right = Vector3.Cross(Vector3.up, forward).normalized;
                if (right.sqrMagnitude < 0.01f)
                    right = Vector3.right;

                var gapRoot = new GameObject($"JumpGap_{cell.x}_{cell.y}");
                CaveEditorUndo.RegisterCreated(gapRoot, "Jump Gap");
                gapRoot.transform.SetParent(parent, false);

                var floorH = 0.45f;
                var ledgeY = floorY + 0.08f;
                placed += CreateLedge(gapRoot.transform, "Ledge_A", center - right * (gapWidth * 0.5f + ledgeWidth * 0.5f),
                    new Vector3(ledgeWidth, floorH, ledgeDepth), forward, floorMat, ledgeY);
                placed += CreateLedge(gapRoot.transform, "Ledge_B", center + right * (gapWidth * 0.5f + ledgeWidth * 0.5f),
                    new Vector3(ledgeWidth, floorH, ledgeDepth), forward, floorMat, ledgeY);

                var pitCenter = new Vector3(center.x, floorY - pitDepth * 0.42f, center.z);
                placed += CreateLavaPit(
                    gapRoot.transform,
                    pitCenter,
                    new Vector3(gapWidth * 1.15f, pitDepth, ledgeDepth * 1.1f),
                    floorY);

                if (rng.NextDouble() < 0.55)
                    placed += PlaceWarningTorch(gapRoot.transform, center + forward * (layout.CellSize * 0.32f), floorY + 1.8f);
            }

            return placed;
        }

        static int BuildSurprisePockets(Transform parent, CaveMazeLayout layout, Material rockMat, System.Random rng)
        {
            var deadEnds = FindDeadEndCells(layout);
            if (deadEnds.Count == 0)
                return 0;

            Shuffle(deadEnds, rng);
            var take = Mathf.Min(5, deadEnds.Count);
            var placed = 0;
            var block = CaveBlockTunnelBuilder.Settings.Default.BlockSize;

            for (var i = 0; i < take; i++)
            {
                var cell = deadEnds[i];
                var center = layout.CellToLocal(cell.x, cell.y);
                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                var pocket = new GameObject($"SurprisePocket_{cell.x}_{cell.y}");
                CaveEditorUndo.RegisterCreated(pocket, "Surprise Pocket");
                pocket.transform.SetParent(parent, false);

                var cluster = 5 + rng.Next(4);
                for (var b = 0; b < cluster; b++)
                {
                    var offset = new Vector3(
                        (float)(rng.NextDouble() * 2 - 1) * layout.CellSize * 0.28f,
                        b * block * 0.85f + 0.4f,
                        (float)(rng.NextDouble() * 2 - 1) * layout.CellSize * 0.28f);
                    placed += PlaceMinableBlock(pocket.transform, floor + offset, rockMat, rng);
                }
            }

            return placed;
        }

        static int CreateLedge(Transform parent, string name, Vector3 pos, Vector3 size, Vector3 forward, Material mat, float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Jump Ledge");
            go.name = $"{CaveWalkwayBuilder.WalkFloorPrefix}{name}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(pos.x, y, pos.z);
            go.transform.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            go.transform.localScale = size;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
                mr.sharedMaterial = mat;

            if (go.GetComponent<CaveWalkableMarker>() == null)
                go.AddComponent<CaveWalkableMarker>();

            var col = go.GetComponent<BoxCollider>();
            if (col != null)
                col.isTrigger = false;

            go.isStatic = true;
            return 1;
        }

        static int CreateLavaPit(Transform parent, Vector3 center, Vector3 size, float walkFloorY)
        {
            var root = new GameObject("Pit_Lava");
            CaveEditorUndo.RegisterCreated(root, "Lava Pit");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = center;

            var lavaMat = CaveWaterMaterialFactory.GetOrCreateLava();
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            CaveEditorUndo.RegisterCreated(surface, "Lava Surface");
            surface.name = "LavaSurface";
            surface.transform.SetParent(root.transform, false);
            surface.transform.localPosition = Vector3.up * (size.y * 0.45f);
            surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            surface.transform.localScale = new Vector3(size.x * 1.1f, size.z * 1.1f, 1f);
            var smr = surface.GetComponent<MeshRenderer>();
            if (smr != null && lavaMat != null)
                smr.sharedMaterial = lavaMat;
            Object.DestroyImmediate(surface.GetComponent<Collider>());

            if (surface.GetComponent<CaveLavaGlow>() == null)
                surface.AddComponent<CaveLavaGlow>();

            var lightGo = new GameObject("LavaLight");
            CaveEditorUndo.RegisterCreated(lightGo, "Lava Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.6f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.35f, 0.08f);
            light.intensity = 4.5f;
            light.range = Mathf.Max(6f, size.x * 2f);

            var trigger = root.AddComponent<BoxCollider>();
            trigger.size = size;
            trigger.center = Vector3.zero;
            trigger.isTrigger = true;

            var recovery = root.GetComponent<CavePitFallRecovery>();
            if (recovery == null)
                recovery = root.AddComponent<CavePitFallRecovery>();
            recovery.respawnToMainArea = true;
            recovery.killFloorLocalY = walkFloorY - Mathf.Max(2.5f, size.y * 0.35f);

            return 1;
        }

        static int PlaceWarningTorch(Transform parent, Vector3 localPos, float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            CaveEditorUndo.RegisterCreated(go, "Gap Torch");
            go.name = "GapWarning";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(localPos.x, y, localPos.z);
            go.transform.localScale = new Vector3(0.18f, 0.45f, 0.18f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = GetOrCreateWarningTorchMaterial();

            return 1;
        }

        static Material GetOrCreateWarningTorchMaterial()
        {
            if (_warningTorchMaterial != null)
                return _warningTorchMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _warningTorchMaterial = new Material(shader) { name = "CaveGapWarningTorch" };
            var emission = new Color(2.4f, 1.1f, 0.35f);
            if (_warningTorchMaterial.HasProperty("_BaseColor"))
                _warningTorchMaterial.SetColor("_BaseColor", new Color(0.95f, 0.55f, 0.15f));
            if (_warningTorchMaterial.HasProperty("_EmissionColor"))
            {
                _warningTorchMaterial.SetColor("_EmissionColor", emission);
                _warningTorchMaterial.EnableKeyword("_EMISSION");
            }

            return _warningTorchMaterial;
        }

        static int PlaceMinableBlock(Transform parent, Vector3 localPos, Material rockMat, System.Random rng)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "Surprise Block");
            go.name = "CaveBlock_Minable";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.localScale = Vector3.one * (0.9f + (float)rng.NextDouble() * 0.35f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = rockMat;

            go.tag = CaveTags.Minable;
            if (go.GetComponent<MinableRock>() == null)
            {
                var rock = go.AddComponent<MinableRock>();
                rock.hitPoints = 3 + rng.Next(3);
            }

            if (go.GetComponent<CaveTunnelBlock>() == null)
                go.AddComponent<CaveTunnelBlock>();

            go.isStatic = true;
            return 1;
        }

        static Vector3 ResolvePathForward(CaveMazeLayout layout, Vector2Int cell)
        {
            var path = layout.SolutionPath;
            for (var i = 0; i < path.Count; i++)
            {
                if (path[i] != cell)
                    continue;

                if (i < path.Count - 1)
                {
                    var next = layout.CellToLocal(path[i + 1].x, path[i + 1].y);
                    var cur = layout.CellToLocal(cell.x, cell.y);
                    var f = next - cur;
                    f.y = 0f;
                    if (f.sqrMagnitude > 0.01f)
                        return f.normalized;
                }

                if (i > 0)
                {
                    var prev = layout.CellToLocal(path[i - 1].x, path[i - 1].y);
                    var cur = layout.CellToLocal(cell.x, cell.y);
                    var f = cur - prev;
                    f.y = 0f;
                    if (f.sqrMagnitude > 0.01f)
                        return f.normalized;
                }
            }

            return Vector3.forward;
        }

        static List<Vector2Int> FindDeadEndCells(CaveMazeLayout layout)
        {
            var list = new List<Vector2Int>();
            for (var x = 0; x < layout.Width; x++)
            {
                for (var z = 0; z < layout.Height; z++)
                {
                    if (!layout.IsPassage(x, z))
                        continue;
                    if (layout.IsCavernCell(x, z))
                        continue;

                    var neighbors = 0;
                    if (layout.IsPassage(x + 1, z)) neighbors++;
                    if (layout.IsPassage(x - 1, z)) neighbors++;
                    if (layout.IsPassage(x, z + 1)) neighbors++;
                    if (layout.IsPassage(x, z - 1)) neighbors++;

                    if (neighbors == 1)
                        list.Add(new Vector2Int(x, z));
                }
            }

            return list;
        }

        static void Shuffle(List<Vector2Int> list, System.Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
