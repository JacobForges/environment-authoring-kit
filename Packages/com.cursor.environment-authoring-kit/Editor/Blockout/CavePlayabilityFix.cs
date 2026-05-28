using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// One-click playability repair. Adventure caves: strip duplicate geometry + single atmosphere (no stacking).
    /// Legacy spline caves: collider/walkway cleanup (not used for true3D adventure builds).
    /// </summary>
    public static class CavePlayabilityFix
    {
        public struct Report
        {
            public bool AdventureMode;
            public int LayersStripped;
            public int FallbackShellCleared;
            public bool SpawnAligned;
            public bool NavMeshBuilt;
            public int WalkFloors;
            public string Summary;
        }

        public static void RunSilent(Transform caveRoot) => Run(caveRoot, silent: true);

        public static Report Run(Transform caveRoot, bool silent = false)
        {
            var report = new Report();
            if (caveRoot == null)
            {
                report.Summary = "No cave root.";
                return report;
            }

            CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
            CaveBlockTunnelRuntimeSetup.EnsureOnCaveRoot(caveRoot);

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                report.AdventureMode = true;
                FixAdventure(caveRoot, ref report);
            }
            else
            {
                FixLegacySpline(caveRoot, ref report);
            }

            if (!silent)
                ShowDialog(report);

            EditorUtility.SetDirty(caveRoot.gameObject);
            return report;
        }

        [MenuItem("Window/Environment Kit/Fix Cave Playability (Active Scene)")]
        public static void FixFromMenu()
        {
            var caveRoot = FindCaveRoot();
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Fix Cave Playability",
                    "LavaTubeCaveSystem not found. Run Build Complete Cave Level first.",
                    "OK");
                return;
            }

            Run(caveRoot, silent: false);
        }

        static Transform FindCaveRoot()
        {
            var caveRoot = GameObject.Find("Grid")?.transform.Find("LavaTubeCaveSystem");
            return caveRoot != null ? caveRoot : GameObject.Find("LavaTubeCaveSystem")?.transform;
        }

        /// <summary>True3D adventure: no walkway rebuild, no occlusion shell, one atmosphere pass.</summary>
        static void FixAdventure(Transform caveRoot, ref Report report)
        {
            CavePrefabInstanceUtility.RestoreMissingPrefabVisuals(caveRoot);
            report.LayersStripped = CaveAdventureVisualPass.StripLayeredGeometry(caveRoot);
            report.FallbackShellCleared = ClearLayeredFallbackShells(caveRoot);

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var request = meta != null
                ? new WorldGenerationRequest
                {
                    Seed = meta.seed,
                    CaveTunnelSegments = meta.tunnelSegments,
                    CaveChamberCount = meta.chamberCount,
                    UseTrue3DCaveSystem = true,
                    UseBlockTunnel = true
                }
                : null;

            if (request != null)
            {
                CaveAdventurePlayabilityPipeline.RunStep(2, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(3, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(4, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(6, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(9, caveRoot, request, default);
                CaveAdventurePlayabilityPipeline.RunStep(10, caveRoot, request, default);
            }

            CaveInvisibleColliderUtility.StripForAdventure(caveRoot);

            CaveAdventureVisualPass.Apply(caveRoot);
            CaveFloorSafetyUtility.Apply(caveRoot);
            CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot);

            var entrance = caveRoot.Find("Entrance");
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring != null && entrance != null && request != null)
            {
                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                var layout = CaveMazeLayoutGenerator.Generate(
                    request.Seed, request.CaveTunnelSegments, request.CaveChamberCount);
                var spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(
                    caveRoot, entrance, spline, keepAtSurfaceMouth: false, layout);
                CaveEntrancePortalPreserver.Apply(caveRoot, SceneGroundResolver.Resolve(), spawn);
                report.SpawnAligned = spawn != null;
            }

            EnsureSingleUndergroundAtmosphere(caveRoot);
            var guard = caveRoot.GetComponent<CavePlayerMovementGuard>();
            if (guard == null)
                guard = caveRoot.gameObject.AddComponent<CavePlayerMovementGuard>();
            guard.snapNearbyPlayerOnPlay = false;

            report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);
            report.WalkFloors = CaveAdventurePlayabilityPipeline.CountWalkFloors(caveRoot);

            report.Summary =
                $"Adventure fix: stripped {report.LayersStripped} layered piece(s), " +
                $"cleared {report.FallbackShellCleared} fallback shell root(s), " +
                $"{report.WalkFloors} walk floors, nav={(report.NavMeshBuilt ? "OK" : "fail")}.";
        }

        static void FixLegacySpline(Transform caveRoot, ref Report report)
        {
            var removedTube = RemoveTubeMeshColliders(caveRoot);
            var removedInvisible = RemoveInvisibleGroundColliders(caveRoot);
            var removedDecorative = CavePlayabilityValidator.RemoveDecorativeColliders(caveRoot);

            Transform spawn = null;
            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            var entrance = caveRoot.Find("Entrance");
            if (authoring != null && entrance != null)
            {
                var spline = CaveSplinePathSpace.CreateLocalSpline(authoring);
                if (spline != null)
                    spawn = SplineCaveSpawnAligner.AlignEntranceSpawn(caveRoot, entrance, spline, keepAtSurfaceMouth: false);
            }

            CaveEntrancePortalPreserver.Apply(caveRoot, SceneGroundResolver.Resolve(), spawn);
            EnsureLegacyWalkways(caveRoot);
            CaveFloorSafetyUtility.Apply(caveRoot);
            CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot);
            CaveWaterUtility.ClearAllWater(caveRoot);

            report.NavMeshBuilt = LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);
            report.WalkFloors = caveRoot.Find("Walkways")?.childCount ?? 0;
            report.SpawnAligned = spawn != null;
            report.Summary =
                $"Spline fix: removed {removedTube} tube colliders, {removedInvisible} invisible, " +
                $"{removedDecorative} decorative, {report.WalkFloors} walk floors.";
        }

        /// <summary>Remove stacked closure systems that true3D adventure does not use.</summary>
        static int ClearLayeredFallbackShells(Transform caveRoot)
        {
            var cleared = 0;
            cleared += ClearChildrenIfPresent(caveRoot.Find("SeamlessTunnel"));
            cleared += ClearChildrenIfPresent(caveRoot.Find("OcclusionShell"));
            return cleared;
        }

        static int ClearChildrenIfPresent(Transform root)
        {
            if (root == null || root.childCount == 0)
                return 0;

            var n = root.childCount;
            for (var i = n - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
            return 1;
        }

        /// <summary>
        /// One trigger + one fog profile — not stacked URP volume + global fog + duplicate zones.
        /// Not related to XR; layering came from piling post-processing on each fix pass.
        /// </summary>
        public static void EnsureSingleUndergroundAtmosphere(Transform caveRoot)
        {
            var zones = caveRoot.GetComponentsInChildren<CaveUndergroundAtmosphere>(true);
            Transform keep = caveRoot.Find("CaveAtmosphereZone");
            if (keep == null && zones.Length > 0)
                keep = zones[0].transform;

            if (keep == null)
            {
                var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
                if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
                    return;

                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                var nodes = new System.Collections.Generic.List<Vector3>();
                for (var i = 0; i < 24; i++)
                {
                    var t = i / 23f;
                    nodes.Add(spline.SampleAtNormalized(t).Position);
                }

                LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(caveRoot, nodes);
                keep = caveRoot.Find("CaveAtmosphereZone");
            }

            for (var i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null || zones[i].transform == keep)
                    continue;
                CaveEditorUndo.DestroyImmediate(zones[i].gameObject);
            }

            if (keep == null)
                return;

            var atmosphere = keep.GetComponent<CaveUndergroundAtmosphere>();
            if (atmosphere == null)
                atmosphere = keep.gameObject.AddComponent<CaveUndergroundAtmosphere>();

            atmosphere.overrideFog = true;
            atmosphere.fogDensity = 0.032f;
            atmosphere.fogColor = new Color(0.08f, 0.04f, 0.025f, 1f);
            atmosphere.overrideAmbient = true;
            atmosphere.ambientIntensity = 0.55f;
            atmosphere.ambientEquator = new Color(0.12f, 0.06f, 0.03f, 1f);
            atmosphere.ambientGround = new Color(0.18f, 0.08f, 0.03f, 1f);

            var volume = keep.GetComponent<Volume>();
            if (volume != null)
                CaveEditorUndo.DestroyImmediate(volume);

            var mist = caveRoot.Find(CaveFogMistBuilder.MistRootName);
            if (mist != null)
            {
                foreach (var ps in mist.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null)
                        continue;
                    var main = ps.main;
                    main.maxParticles = Mathf.Min(main.maxParticles, 8);
                }
            }

            ExtendAtmosphereForSurfaceDescent(caveRoot, keep);
        }

        /// <summary>Pull the underground atmosphere trigger down the surface mouth ramp so fog/blackout starts at the cave lip.</summary>
        public static void ExtendAtmosphereForSurfaceDescent(Transform caveRoot, Transform atmosphereZone = null)
        {
            if (caveRoot == null)
                return;

            atmosphereZone ??= caveRoot.Find("CaveAtmosphereZone");
            if (atmosphereZone == null)
                return;

            var col = atmosphereZone.GetComponent<BoxCollider>();
            if (col == null)
                return;

            var min = col.center - col.size * 0.5f;
            var max = col.center + col.size * 0.5f;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
            {
                var walkIn = geometry.Find(CaveSurfaceEntranceBuilder.RootName);
                if (walkIn != null)
                {
                    foreach (var r in walkIn.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || !r.enabled)
                            continue;
                        var lp = atmosphereZone.InverseTransformPoint(r.bounds.center);
                        var ext = r.bounds.extents;
                        min = Vector3.Min(min, lp - ext);
                        max = Vector3.Max(max, lp + ext);
                    }
                }
            }

            var ground = SceneGroundResolver.Resolve();
            if (ground != null && ground.HasAnchor)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot, ground);
                if (mouth.sqrMagnitude > 0.01f)
                {
                    var lip = atmosphereZone.InverseTransformPoint(mouth);
                    lip.y = atmosphereZone.InverseTransformPoint(
                        new Vector3(mouth.x, mouth.y + 2f, mouth.z)).y;
                    min = Vector3.Min(min, lip - new Vector3(5f, 1f, 5f));
                    max = Vector3.Max(max, lip + new Vector3(5f, 6f, 5f));
                }
            }

            var size = max - min;
            if (size.sqrMagnitude < 0.01f)
                return;

            CaveEditorUndo.RecordObject(col, "Extend cave atmosphere");
            col.center = (min + max) * 0.5f;
            col.size = size + new Vector3(4f, 6f, 4f);
            atmosphereZone.localPosition = col.center;
        }

        static void EnsureLegacyWalkways(Transform caveRoot)
        {
            var maze = caveRoot.Find($"SplineMesh/{CaveMazeVolumeBuilder.MazeVolumeRootName}");
            var walkways = caveRoot.Find("Walkways");
            if (maze != null && walkways != null && walkways.childCount > 0)
                return;

            CaveWalkwayBuilder.RebuildFromAuthoring(caveRoot);
        }

        static void ShowDialog(Report report)
        {
            var mode = report.AdventureMode ? "Adventure (grid 3D)" : "Spline / legacy";
            EditorUtility.DisplayDialog(
                "Fix Cave Playability",
                $"{mode}\n\n{report.Summary}\n\n" +
                "• Removed duplicate ceiling/walkway/sky layers (not XR — extra fix passes were stacking fog + meshes)\n" +
                "• Single underground atmosphere trigger (no stacked post volumes)\n" +
                "• Spawn pad + walk colliders + movement guard\n" +
                $"• NavMesh: {(report.NavMeshBuilt ? "OK" : "check walk floors")}\n\n" +
                "Press Play near the cave or use the surface portal (F).",
                "OK");
        }

        static int RemoveTubeMeshColliders(Transform caveRoot)
        {
            var removed = 0;
            foreach (var mc in caveRoot.GetComponentsInChildren<MeshCollider>(true))
            {
                var n = mc.gameObject.name;
                if (!n.Contains("MainCaveTube") && !n.Contains("WaterBranchTube"))
                    continue;

                CaveEditorUndo.DestroyImmediate(mc);
                removed++;
            }

            return removed;
        }

        static int RemoveInvisibleGroundColliders(Transform caveRoot)
        {
            var removed = 0;
            foreach (var collider in caveRoot.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger)
                    continue;

                if (CaveColliderUtility.IsProtectedPlayCollider(collider, caveRoot))
                    continue;

                if (!CaveRendererVisibility.HasVisibleRenderer(collider, true))
                {
                    CaveEditorUndo.DestroyImmediate(collider);
                    removed++;
                }
            }

            return removed;
        }
    }
}
