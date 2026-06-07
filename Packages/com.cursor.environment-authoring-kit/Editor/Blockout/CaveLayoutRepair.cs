#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Repairs enclosed tube visibility, water, and portal spawn on an existing cave.</summary>
    public static class CaveLayoutRepair
    {
        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Fix Cave Layout + Water")]
        public static void FixFromMenu()
        {
            var caveRoot = GameObject.Find("Grid")?.transform.Find("LavaTubeCaveSystem");
            if (caveRoot == null)
                caveRoot = GameObject.Find("LavaTubeCaveSystem")?.transform;

            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog("Fix Cave", "LavaTubeCaveSystem not found.", "OK");
                return;
            }

            var report = Run(caveRoot);
            EditorUtility.DisplayDialog("Fix Cave", report, "OK");
        }

        public static string Run(Transform caveRoot)
        {
            var ground = SceneGroundResolver.Resolve();
            if (ground.HasAnchor)
            {
                var edge = ground.Bounds.center -
                           ground.HorizontalForward * Mathf.Max(10f, ground.Bounds.extents.z * 0.3f);
                edge.y = ground.SurfaceY;
                CaveEditorUndo.RecordObject(caveRoot, "Align Cave Root");
                caveRoot.position = edge;
                caveRoot.rotation = Quaternion.LookRotation(ground.HorizontalForward, Vector3.up);
            }

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                return RunHybridRepair(caveRoot, ground);

            RegenerateTubeMeshes(caveRoot);
            CavePlayabilityFix.RunSilent(caveRoot);
            CaveWaterUtility.ClearAllWater(caveRoot);

            var entrance = caveRoot.Find("Entrance");
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            Transform spawn = null;
            if (authoring != null && entrance != null)
            {
                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(caveRoot, entrance, spline, keepAtSurfaceMouth: true);
            }

            CaveEntrancePortalPreserver.Apply(caveRoot, ground, spawn);
            EnvironmentSceneUtility.MarkSceneDirty();

            return "Tube mesh regenerated (interior visible), Ignite cave water rebuilt, spawn + PortalFive relinked.";
        }

        static string RunHybridRepair(Transform caveRoot, SceneGroundInfo ground)
        {
            CaveAdventureVisualPass.Apply(caveRoot);
            CaveCompactLayerPurge.Purge(caveRoot);
            CaveAdventureVisualPass.Apply(caveRoot);
            CavePlayabilityFix.RunSilent(caveRoot);
            CaveWaterBuilder.RebuildForCave(caveRoot);

            var entrance = caveRoot.Find("Entrance");
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            Transform spawn = null;
            if (authoring != null && entrance != null && authoring.Knots != null && authoring.Knots.Count >= 2)
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                CaveMazeLayout layout = null;
                if (meta != null)
                    layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);

                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(
                    caveRoot, entrance, spline, keepAtSurfaceMouth: false, layout);
            }

            LavaTubeCaveBuildPipeline.EnsureGameplaySpawns(caveRoot, ground);
            CaveEntrancePortalPreserver.Apply(caveRoot, ground, spawn);
            EnvironmentSceneUtility.MarkSceneDirty();

            return "Hybrid cave: sky seal, blocks, water, underground spawn + PortalFive relinked.";
        }

        static void RegenerateTubeMeshes(Transform caveRoot)
        {
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                return;

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var settings = CaveTubeMeshSettings.DefaultOrganic;
            settings.InteriorView = true;
            settings.RingSpacing = 2.1f;
            settings.SidesPerRing = 16;

            var meshRoot = caveRoot.Find("SplineMesh");
            if (meshRoot == null)
                return;

            var mesh = CaveTubeMeshBuilder.Build(spline, authoring.Knots, settings);
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            CaveSplineTubeMeshUtility.EnsureTubeMesh(
                meshRoot,
                "MainCaveTube",
                mesh,
                rockMat,
                "CaveSplineMainMesh.asset");

            var waterRoot = caveRoot.Find("Water");
            var branch = waterRoot != null ? waterRoot.Find("WaterBranchTube") : null;
            if (branch != null && caveRoot.GetComponent<CaveWaterBranchAnchor>() is { } anchor)
            {
                var branchKnots = new System.Collections.Generic.List<CavePathKnot>
                {
                    authoring.Knots[authoring.Knots.Count - 1]
                };
                branchKnots.Add(new CavePathKnot(
                    anchor.poolLocalPosition,
                    authoring.Knots[authoring.Knots.Count - 1].RadiusX * 0.9f,
                    authoring.Knots[authoring.Knots.Count - 1].RadiusY * 0.9f,
                    false));

                var branchSpline = new CaveSplinePath();
                branchSpline.SetKnots(branchKnots);
                var branchMesh = CaveTubeMeshBuilder.Build(branchSpline, branchKnots, settings);
                var bmf = branch.GetComponent<MeshFilter>();
                if (bmf != null && branchMesh != null)
                    bmf.sharedMesh = branchMesh;
            }

            foreach (var mr in meshRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.sharedMaterial = rockMat;
                mr.enabled = true;
            }

            var branchMr = branch != null ? branch.GetComponent<MeshRenderer>() : null;
            if (branchMr != null)
                branchMr.enabled = true;

            CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(caveRoot);
            CaveWalkwayBuilder.RebuildFromAuthoring(caveRoot);

            var catalog = LavaTubePrefabCatalog.Load();
            if (catalog.IsValid)
                CaveOrganicInteriorPass.Build(caveRoot, spline, catalog, new System.Random(authoring.Knots.Count * 17));
        }
    }
}
#endif
