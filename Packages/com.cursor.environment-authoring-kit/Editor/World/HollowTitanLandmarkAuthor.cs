#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Plan v4 — one random above-ground hollow tree portal per build seed (excludes mouths, labyrinth, trails).</summary>
    public static class HollowTitanLandmarkAuthor
    {
        public const string RootName = "HollowTitanLandmark";
        public const string PortalTag = "BossStagePortal";

        public static void Place(Terrain mainTerrain, WorldGenerationRequest request) =>
            PlaceMassiveRandomOnSurface(mainTerrain, request);

        /// <summary>Alias — massive seed-random surface landmark before terraform/sculpt.</summary>
        public static void PlaceAaaBeforeTerraform(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            System.Action onComplete = null) =>
            PlaceMassiveRandomOnSurface(mainTerrain, request, forceSyncFullBuild: true, onComplete: onComplete);

        /// <summary>
        /// One huge hollow dead tree per build — random XZ on a surface terrain tile every generation (never cave placement).
        /// Runs the 12-step phased meat pipeline when enabled in settings.
        /// </summary>
        public static void PlaceMassiveRandomOnSurface(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            bool forceSyncFullBuild = false,
            System.Action onComplete = null) =>
            PlaceInternal(mainTerrain, request, massive: true, forceSyncFullBuild, onComplete);

        /// <summary>Sculpt hollow stump bowl on terrain, then re-seat base (SampleHeight at stored site XZ).</summary>
        public static void ResnapToTerrain(Terrain mainTerrain)
        {
            var root = GameObject.Find(RootName);
            if (root != null)
                HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(root.transform);

            HollowTitanExteriorTerraform.SculptFromSceneLandmark(mainTerrain, force: true);
            HollowTitanLandmarkMeatPhases.ResnapAfterTerrainSculpt(mainTerrain);
            HollowTitanLandmarkMeatPhases.EnsureLandmarkExteriorVisible();
        }

        /// <summary>
        /// After a wilderness tile finishes LiDAR sculpt, carve the stump bowl if the landmark sits on that tile.
        /// </summary>
        public static void TrySculptStumpAfterTileTerraform(Terrain mainTerrain, Terrain completedTile)
        {
            if (mainTerrain == null || completedTile == null)
                return;

            var root = GameObject.Find(RootName);
            var data = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;
            if (data == null)
                return;

            if (!HollowTitanLandmarkCleanup.TerrainContainsWorldXZ(completedTile, data.SiteWorldPosition))
                return;

            HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(root.transform);
            HollowTitanExteriorTerraform.SculptFromSceneLandmark(mainTerrain, force: true);
            HollowTitanLandmarkMeatPhases.ResnapAfterTerrainSculpt(mainTerrain);
            HollowTitanLandmarkMeatPhases.EnsureLandmarkExteriorVisible();
            CaveBuildEditorLog.LogSurface(
                "[HollowTitan] Stump bowl sculpted on titan tile after terraform — radial CC0 trim stripped.",
                forceUnityConsole: false);
        }

        [MenuItem("Window/Environment Kit/World/Build Hollow Titan (phased meat)")]
        public static void BuildFromMenuPhased()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            var request = new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = Random.Range(1, int.MaxValue),
                SurfaceScope = SurfaceBuildScope.FullWorld,
            };
            PlaceMassiveRandomOnSurface(terrain, request, forceSyncFullBuild: true);
        }

        [MenuItem("Window/Environment Kit/World/Resnap Hollow Titan to terrain")]
        public static void ResnapFromMenu()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            ResnapToTerrain(terrain);
        }

        [MenuItem("Window/Environment Kit/Surface/Weld All Terrain Seams Now")]
        public static void WeldAllTerrainSeamsFromMenu()
        {
            var main = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            if (main == null)
            {
                Debug.LogWarning("[Surface] No terrain in scene.");
                return;
            }

            SurfaceTerrainTileExpansion.WeldAllFullWorldGridEdgesFromScene(main, () =>
            {
                SurfaceTerrainSeamWelds.QueuePeakFoothillSeamWeld(main, () =>
                    Debug.Log("[Surface] All terrain seams welded."));
            });
        }

        [MenuItem("Window/Environment Kit/Surface/Reapply Foothill Rolling Hills")]
        public static void ReapplyFoothillRollingFromMenu()
        {
            var main = UnityEngine.Object.FindAnyObjectByType<Terrain>();
            if (main == null)
            {
                Debug.LogWarning("[Surface] No terrain in scene.");
                return;
            }

            var request = new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = CaveBuildLayoutRollSession.LastRecordedSeed > 0
                    ? CaveBuildLayoutRollSession.LastRecordedSeed
                    : Random.Range(1, int.MaxValue),
                SurfaceScope = SurfaceBuildScope.FullWorld,
                SurfaceIncludeMountains = true,
                UseOuterRingMountains = true,
            };
            request.EnsureFullWorldSurfaceContract();
            SurfaceOuterRingMountainsAuthor.QueueApplyFoothillRing(main, request, () =>
            {
                ResnapToTerrain(main);
                SurfaceTerrainTileExpansion.WeldAllFullWorldGridEdgesFromScene(main, () =>
                {
                    SurfaceTerrainSeamWelds.QueuePeakFoothillSeamWeld(main, () =>
                        Debug.Log("[Surface] Foothill rolling hills re-applied and seams welded."));
                });
            });
        }

        [MenuItem("Window/Environment Kit/World/Restore Hollow Titan Visibility")]
        public static void RestoreVisibilityFromMenu() =>
            HollowTitanLandmarkMeatPhases.RestoreLandmarkSceneVisibility();

        [MenuItem("Window/Environment Kit/World/Strip Hollow Titan Legacy Props (radial trim)")]
        public static void StripLegacyPropsFromMenu()
        {
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                Debug.LogWarning("[HollowTitan] No HollowTitanLandmark in scene.");
                return;
            }

            HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(root.transform);
            HollowTitanLandmarkMeatPhases.RestoreLandmarkSceneVisibility();
        }

        [MenuItem("Window/Environment Kit/World/Refresh Hollow Titan Exterior (terraform stump)")]
        public static void RefreshExteriorFromMenu()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            var root = GameObject.Find(RootName);
            if (terrain == null || root == null)
            {
                Debug.LogWarning("[HollowTitan] Need active terrain + existing HollowTitanLandmark in scene.");
                return;
            }

            var data = root.GetComponent<HollowTitanLandmarkBuildData>();
            var request = new WorldGenerationRequest
            {
                ContentTier = WorldBuildContentTier.Aaa,
                Seed = data != null ? data.BuildSeed : Random.Range(1, int.MaxValue),
                SurfaceScope = SurfaceBuildScope.FullWorld,
            };

            HollowTitanLandmarkMeatPhases.RunSinglePhase(
                HollowTitanLandmarkMeatPhases.Phase.TrunkShell,
                terrain,
                request);
            HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(root.transform);
            HollowTitanExteriorTerraform.SculptFromSceneLandmark(terrain, force: true);
            HollowTitanLandmarkMeatPhases.ResnapAfterTerrainSculpt(terrain);
            HollowTitanLandmarkMeatPhases.RunSinglePhase(
                HollowTitanLandmarkMeatPhases.Phase.EntranceFraming,
                terrain,
                request);
            Debug.Log("[HollowTitan] Exterior refreshed — terraformed hollow stump bowl (no prop limbs).", root);
        }

        /// <summary>
        /// True when an existing root should survive automated surface/terraform (no SitePick destroy).
        /// </summary>
        public static bool TryFindPreservableRoot(
            WorldGenerationRequest request,
            out Transform root,
            out HollowTitanLandmarkBuildData data)
        {
            root = null;
            data = null;
            var existing = GameObject.Find(RootName);
            if (existing == null)
                return false;

            data = existing.GetComponent<HollowTitanLandmarkBuildData>();
            if (data == null)
                return false;

            root = existing.transform;
            if (data.MeatPhasesComplete)
                return true;

            if (request != null && data.BuildSeed != 0 && data.BuildSeed != request.Seed)
                return false;

            return HasMeatBuildProgress(root);
        }

        public static bool ShouldPreserveDuringAutomatedSurfaceBuild =>
            CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive;

        internal static bool HasMeatBuildProgress(Transform root)
        {
            if (root == null)
                return false;

            var data = root.GetComponent<HollowTitanLandmarkBuildData>();
            if (data != null && data.MeatPhasesComplete)
                return true;

            if (root.Find("Exterior/StumpSilhouette/BarkTrunkWall") != null)
                return true;

            if (root.Find("Exterior/TrunkShell") != null)
                return true;

            if (root.Find("Interior") != null)
                return true;

            return root.Find("Exterior/Entrance") != null;
        }

        public static bool IsMeatBuildComplete(Transform root)
        {
            if (root == null)
                return false;

            var data = root.GetComponent<HollowTitanLandmarkBuildData>();
            return data != null && data.MeatPhasesComplete;
        }

        static void DestroyIncompleteLandmark()
        {
            var existing = GameObject.Find(RootName);
            if (existing == null)
                return;

            if (IsMeatBuildComplete(existing.transform))
                return;

            if (HasMeatBuildProgress(existing.transform))
            {
                CaveBuildEditorLog.LogSurface(
                    "[HollowTitan] Keeping in-progress landmark — will resume meat phases instead of deleting.",
                    forceUnityConsole: false);
                return;
            }

            if (existing.GetComponent<HollowTitanLandmarkBuildData>() != null)
                return;

            UnityEngine.Object.DestroyImmediate(existing);
        }

        static void PlaceInternal(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            bool massive,
            bool forceSyncFullBuild,
            System.Action onComplete = null)
        {
            if (mainTerrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (TryFindPreservableRoot(request, out var preservedRoot, out _))
            {
                HollowTitanLandmarkMeatPhases.EnsureLandmarkExteriorVisible();
                HollowTitanLandmarkCleanup.StripLegacyExteriorAndTrim(preservedRoot);
                CaveBuildEditorLog.LogSurface(
                    forceSyncFullBuild
                        ? "[HollowTitan] Preserving existing landmark through Full AAA sync (no delete/rebuild)."
                        : "[HollowTitan] Preserving existing landmark during surface build (stripped legacy trim/props).",
                    forceUnityConsole: false);
                if (forceSyncFullBuild)
                {
                    HollowTitanStumpSculptPhases.Begin(
                        mainTerrain,
                        request,
                        onComplete,
                        forceSyncFullBuild: true);
                    return;
                }

                onComplete?.Invoke();
                return;
            }

            DestroyIncompleteLandmark();
            void AfterMeatBuild()
            {
                HollowTitanStumpSculptPhases.Begin(
                    mainTerrain,
                    request,
                    onComplete,
                    forceSyncFullBuild: forceSyncFullBuild);
            }

            HollowTitanLandmarkMeatPhases.BeginBuild(
                mainTerrain,
                request,
                massive,
                AfterMeatBuild,
                forceSyncFullBuild: forceSyncFullBuild);
        }

        /// <summary>Exposed for meat phase site-pick step.</summary>
        public static bool TryPickLandmarkSpotPublic(
            Terrain mainTerrain,
            int seed,
            bool useAllSurfaceTiles,
            out Terrain tile,
            out Vector3 worldPos) =>
            HollowTitanLandmarkSitePicker.TryPickLandmarkSpot(
                mainTerrain,
                seed,
                useAllSurfaceTiles,
                out tile,
                out worldPos);
    }
}
#endif
