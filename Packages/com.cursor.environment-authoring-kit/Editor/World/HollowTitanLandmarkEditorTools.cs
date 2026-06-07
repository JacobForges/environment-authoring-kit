#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Editor helpers — floor plan preview, manifest export, single-phase debug runs.</summary>
    public static class HollowTitanLandmarkEditorTools
    {
        [MenuItem("Window/Environment Kit/World/Hollow Titan/Export floor plan JSON")]
        public static void ExportFloorPlanJson()
        {
            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            var data = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;
            if (data == null)
            {
                Debug.LogWarning("[HollowTitan] No landmark in scene — run Build Hollow Titan first.");
                return;
            }

            var plan = HollowTitanLandmarkFloorPlanner.Build(
                data.BuildSeed,
                data.FloorCount,
                data.TrunkRadius,
                data.TrunkHeight,
                data.FloorHeightMeters);

            var path = EditorUtility.SaveFilePanel(
                "Export Hollow Titan floor plan",
                CaveBuildCursorSettings.ResolveHubRoot(),
                "HollowTitanFloorPlan.json",
                "json");
            if (string.IsNullOrEmpty(path))
                return;

            var dto = new FloorPlanDto
            {
                floorCount = plan.FloorCount,
                floorHeightMeters = plan.FloorHeightMeters,
                trunkRadius = plan.TrunkRadius,
                trunkHeight = plan.TrunkHeight,
                entranceYawDegrees = plan.EntranceYawDegrees,
                stairCount = plan.Stairs.Count,
            };
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(dto, true));
            Debug.Log($"[HollowTitan] Floor plan exported — {plan.FloorCount} floors, {plan.Stairs.Count} stair nodes.", root);
        }

        [MenuItem("Window/Environment Kit/World/Hollow Titan/Write spawn manifest only")]
        public static void WriteSpawnManifestOnly()
        {
            var root = GameObject.Find(HollowTitanLandmarkAuthor.RootName);
            var data = root != null ? root.GetComponent<HollowTitanLandmarkBuildData>() : null;
            var seed = data != null ? data.BuildSeed : 4242;
            HollowTitanLandmarkSpawnManifest.Write(seed, root != null ? root.transform : null);
        }

        [MenuItem("Window/Environment Kit/World/Hollow Titan/Reveal master concept guide")]
        public static void RevealMasterConcept()
        {
            if (!HollowTitanConceptCatalog.TryRevealMasterConcept())
                HollowTitanConceptImageAuthor.GenerateAll();
        }

        [MenuItem("Window/Environment Kit/World/Hollow Titan/Export phase prompt manifest")]
        public static void ExportManifest()
        {
            HollowTitanConceptCatalog.ExportPhasePromptManifest(out var msg);
            Debug.Log($"[HollowTitan] {msg}");
        }

        [MenuItem("Window/Environment Kit/World/Hollow Titan/Re-run base snap phase")]
        public static void RerunBaseSnapPhase()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            var request = new WorldGenerationRequest { Seed = 4242, SurfaceScope = SurfaceBuildScope.FullWorld };
            HollowTitanLandmarkMeatPhases.RunSinglePhase(
                HollowTitanLandmarkMeatPhases.Phase.BaseSnap,
                terrain,
                request);
        }

        [System.Serializable]
        sealed class FloorPlanDto
        {
            public int floorCount;
            public float floorHeightMeters;
            public float trunkRadius;
            public float trunkHeight;
            public float entranceYawDegrees;
            public int stairCount;
        }
    }
}
#endif
