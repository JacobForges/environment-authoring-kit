using EnvironmentAuthoringKit.Cave;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Hard playability checks inspired by MMO dungeon production gates:
    /// readable traversal, sealed spaces, and no hidden blockers.
    /// </summary>
    static class CavePlayabilityValidator
    {
        public static bool AutoFix(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            if (IsAdventureCave(caveRoot))
                return AutoFixAdventure(caveRoot);

            var true3DMode = IsTrue3DMode(caveRoot);
            var changed = false;
            if (RemoveDecorativeColliders(caveRoot) > 0)
                changed = true;
            if (CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot) > 0)
                changed = true;
            if (CapMinables(caveRoot, 220))
                changed = true;
            if (RebuildWalkways(caveRoot))
                changed = true;
            if (FixSpawnAlignment(caveRoot))
                changed = true;
            if (SnapSpawnToWalkway(caveRoot))
                changed = true;

            var openBefore = CountOpenCeilingSamples(caveRoot, samples: 16);
            if (true3DMode && !IsAdventureCave(caveRoot))
            {
                if (RemoveLayeredFallbackGeometry(caveRoot) > 0)
                    changed = true;
                if (openBefore > 2 && ReinforceMazeSkySeal(caveRoot))
                    changed = true;
            }
            else if (openBefore > 2 && ReinforceSkySeal(caveRoot))
                changed = true;

            return changed;
        }

        static bool AutoFixAdventure(Transform caveRoot)
        {
            CavePlayabilityFix.RunSilent(caveRoot);
            CaveInvisibleColliderUtility.StripForAdventure(caveRoot);
            return true;
        }

        static bool SnapAdventureSpawn(Transform caveRoot)
        {
            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
                return false;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return false;

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            if (layout.SolutionPath.Count == 0)
                return false;

            var start = layout.SolutionPath[0];
            var floor = layout.GetFloorSurfaceLocal(start.x, start.y);
            var forward = Vector3.forward;
            if (layout.SolutionPath.Count > 1)
            {
                var next = layout.CellToLocal(layout.SolutionPath[1].x, layout.SolutionPath[1].y);
                forward = next - floor;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.01f)
                    forward.Normalize();
            }

            spawn.localPosition = floor + Vector3.up * 1.05f;
            spawn.localRotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }

        public static bool IsAdventureCave(Transform caveRoot) =>
            CaveGeometryPaths.IsAdventureCave(caveRoot);

        public static int CountInvisibleSolidColliders(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var count = 0;
            foreach (var c in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger)
                    continue;

                if (CaveColliderUtility.IsProtectedPlayCollider(c, caveRoot))
                    continue;
                if (c.GetComponentInParent<MinableRock>() != null)
                    continue;

                if (!CaveRendererVisibility.HasVisibleRenderer(c, true))
                    count++;
            }

            return count;
        }

        public static int CountOpenCeilingSamples(Transform caveRoot, int samples = 20, float rayLength = 55f)
        {
            if (caveRoot == null)
                return samples;

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return samples;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var renderers = caveRoot.GetComponentsInChildren<Renderer>(true);
            var open = 0;
            for (var i = 0; i < samples; i++)
            {
                var t = samples <= 1 ? 0f : i / (float)(samples - 1);
                var s = spline.SampleAtNormalized(t);
                var world = caveRoot.TransformPoint(s.Position);
                if (!HasCeilingRendererAbove(world, renderers, rayLength) &&
                    !Physics.Raycast(world + Vector3.up * 0.5f, Vector3.up, rayLength, ~0, QueryTriggerInteraction.Ignore))
                    open++;
            }

            return open;
        }

        public static int RemoveDecorativeColliders(Transform caveRoot)
        {
            if (caveRoot == null || IsAdventureCave(caveRoot))
                return 0;

            var removed = 0;
            removed += RemoveUnder(caveRoot.Find("OcclusionShell"), keepMinables: false);
            removed += RemoveUnder(caveRoot.Find("SeamlessTunnel"), keepMinables: false);
            removed += RemoveUnder(caveRoot.Find("SplineMesh"), keepMinables: false);
            removed += RemoveUnder(caveRoot.Find("Water"), keepMinables: false);
            removed += RemoveUnder(caveRoot.Find("Details"), keepMinables: true);
            return removed;
        }

        public static bool EnsureWaterMaterial(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var pool = caveRoot.Find("Water/UndergroundRiver_Pool");
            var mr = pool != null ? pool.GetComponent<MeshRenderer>() : null;
            if (mr == null)
                return false;

            CaveWaterMaterialFactory.ForceCaveWaterMaterial(mr);
            return CaveWaterMaterialFactory.IsCaveWaterMaterial(mr.sharedMaterial);
        }

        /// <summary>Raycast from entrance spawn — must hit walkable floor (matches runtime PlayerGroundSnap).</summary>
        public static bool CheckEntranceSpawnGrounded(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
                return false;

            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(9f, 0.85f, 9f));
            Physics.SyncTransforms();

            var near = spawn.position;
            var origin = near + Vector3.up * 10f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 22f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            foreach (var hit in hits)
            {
                if (hit.collider == null)
                    continue;
                if (!CaveWalkableSurface.IsWalkableCollider(hit.collider))
                    continue;
                if (hit.point.y > near.y + 3f)
                    continue;
                return true;
            }

            return false;
        }

        public static bool CheckSpawnReachability(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                return CaveAdventurePlayabilityPipeline.CheckSpawnReachability(caveRoot);

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            var walkRoot = caveRoot.Find("Walkways");
            if (spawn == null || walkRoot == null || walkRoot.childCount == 0)
                return false;

            var nearestWalkDist = float.PositiveInfinity;
            var nearestWalkYDiff = float.PositiveInfinity;
            var checkedTargets = 0;
            var reached = 0;
            var hasNavSampledTargets = false;
            var hasNavSpawn = TrySampleNav(spawn.position, out var navSpawn);
            foreach (Transform c in walkRoot)
            {
                if (!c.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                    continue;

                var d = Vector3.Distance(spawn.position, c.position);
                if (d < nearestWalkDist)
                {
                    nearestWalkDist = d;
                    nearestWalkYDiff = Mathf.Abs(spawn.position.y - c.position.y);
                }

                if (!TrySampleNav(c.position, out var navTarget))
                    continue;
                hasNavSampledTargets = true;
                checkedTargets++;
                if (hasNavSpawn)
                {
                    var path = new NavMeshPath();
                    if (NavMesh.CalculatePath(navSpawn, navTarget, NavMesh.AllAreas, path) &&
                        path.status == NavMeshPathStatus.PathComplete)
                        reached++;
                }

                if (checkedTargets >= 8)
                    break;
            }

            // Fallback physical reachability for controller-based traversal even when nav samples are sparse.
            // In long cave systems, spawn can be intentionally offset from the first nav sample.
            // Treat near-enough physical proximity to walk floors as reachable fallback.
            var physicallyNearWalkway = nearestWalkDist < 80f && nearestWalkYDiff < 18f;

            if (!hasNavSpawn || !hasNavSampledTargets)
                return physicallyNearWalkway;

            if (checkedTargets <= 0)
                return physicallyNearWalkway;

            // Require at least one complete path and a reasonable ratio; don't over-fail on fragmented nav strips.
            var navReachable = reached >= 1 && reached >= Mathf.Max(1, checkedTargets / 3);
            return navReachable || physicallyNearWalkway;
        }

        static bool RebuildWalkways(Transform caveRoot)
        {
            var count = CaveWalkwayBuilder.RebuildFromAuthoring(caveRoot);
            var visible = CaveFloorSafetyUtility.EnsureVisibleWalkways(caveRoot);
            return count > 0 || visible > 0;
        }

        static bool FixSpawnAlignment(Transform caveRoot)
        {
            var entrance = caveRoot.Find("Entrance");
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (entrance == null || authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return false;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(caveRoot, entrance, spline, keepAtSurfaceMouth: false);
            return spawn != null;
        }

        static bool ReinforceSkySeal(Transform caveRoot)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return false;

            var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rock == null)
                return false;

            var seam = caveRoot.Find("SeamlessTunnel");
            if (seam == null)
                return false;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            CaveCeilingSealUtility.BuildAlongSpline(seam, spline, rock, mazeMode: false);
            return true;
        }

        static bool ReinforceMazeSkySeal(Transform caveRoot)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return false;

            var rock = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            if (rock == null)
                return false;

            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null)
                return false;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var mazeVol = meshRoot.Find(CaveMazeVolumeBuilder.MazeVolumeRootName);
            if (meta != null && mazeVol != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
                CaveMazeCeilingCoverBuilder.Build(mazeVol, layout, rock);
            }
            else
            {
                CaveCeilingSealUtility.BuildAlongSpline(meshRoot, spline, rock, mazeMode: true);
            }

            return true;
        }

        static bool CapMinables(Transform caveRoot, int maxCount)
        {
            var all = caveRoot.GetComponentsInChildren<MinableRock>(true);
            if (all == null || all.Length <= maxCount)
                return false;

            for (var i = maxCount; i < all.Length; i++)
            {
                if (all[i] != null)
                    CaveEditorUndo.DestroyImmediate(all[i].gameObject);
            }

            return true;
        }

        public static int RemoveLayeredFallbackGeometry(Transform caveRoot)
        {
            var removed = 0;
            var seamless = caveRoot.Find("SeamlessTunnel");
            if (seamless != null)
            {
                for (var i = seamless.childCount - 1; i >= 0; i--)
                {
                    CaveEditorUndo.DestroyImmediate(seamless.GetChild(i).gameObject);
                    removed++;
                }
            }

            var shell = caveRoot.Find("OcclusionShell");
            if (shell != null)
            {
                for (var i = shell.childCount - 1; i >= 0; i--)
                {
                    CaveEditorUndo.DestroyImmediate(shell.GetChild(i).gameObject);
                    removed++;
                }
            }

            return removed;
        }

        static bool SnapSpawnToWalkway(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            var walk = caveRoot.Find("Walkways");
            if (spawn == null || walk == null || walk.childCount == 0)
                return false;

            Transform nearest = null;
            var nearestDist = float.PositiveInfinity;
            foreach (Transform c in walk)
            {
                if (!c.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                    continue;
                var d = (c.position - spawn.position).sqrMagnitude;
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearest = c;
                }
            }

            if (nearest == null)
                return false;

            var target = nearest.position + Vector3.up * 1.15f;
            if (TrySampleNav(target, out var nav))
                target = nav + Vector3.up * 1.15f;

            spawn.position = target;
            var forward = Vector3.ProjectOnPlane(nearest.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude > 0.1f)
                spawn.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }

        static int RemoveUnder(Transform root, bool keepMinables)
        {
            if (root == null)
                return 0;

            var removed = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null)
                    continue;

                var n = c.gameObject.name;
                if (CaveColliderUtility.IsProtectedPlayCollider(c))
                    continue;
                if (keepMinables && c.GetComponentInParent<MinableRock>() != null)
                    continue;

                CaveEditorUndo.DestroyImmediate(c);
                removed++;
            }

            return removed;
        }

        static void CreateSealPiece(
            Transform parent,
            Material rock,
            string name,
            CaveSplineSample s,
            float horizontalOffset,
            float widthMul)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            CaveEditorUndo.RegisterCreated(go, "AutoFix Sky Seal");
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition =
                s.Position +
                s.Right * horizontalOffset +
                s.Up * (s.RadiusY + 4.7f);
            go.transform.localRotation = Quaternion.LookRotation(s.Tangent, s.Up);
            go.transform.localScale = new Vector3(Mathf.Max(3.5f, s.RadiusX * widthMul), 1.7f, 11.5f);

            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = rock;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }

        static bool HasCeilingRendererAbove(Vector3 worldPos, Renderer[] renderers, float maxHeight)
        {
            if (renderers == null)
                return false;

            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled)
                    continue;

                var b = r.bounds;
                if (b.max.y <= worldPos.y + 0.35f)
                    continue;
                if (b.min.y - worldPos.y > maxHeight)
                    continue;
                if (worldPos.x < b.min.x - 0.25f || worldPos.x > b.max.x + 0.25f)
                    continue;
                if (worldPos.z < b.min.z - 0.25f || worldPos.z > b.max.z + 0.25f)
                    continue;

                return true;
            }

            return false;
        }

        static bool TrySampleNav(Vector3 worldPos, out Vector3 sampled)
        {
            if (NavMesh.SamplePosition(worldPos, out var hit, 8f, NavMesh.AllAreas))
            {
                sampled = hit.position;
                return true;
            }

            sampled = worldPos;
            return false;
        }

        static bool IsTrue3DMode(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            return caveRoot.Find("SplineMesh/CaveMazeVolume") != null ||
                   caveRoot.Find("SplineMesh/MainCaveOuterShell") != null;
        }
    }
}
