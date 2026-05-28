using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using Terrain = UnityEngine.Terrain;
using ActiveSceneUtility = EnvironmentAuthoringKit.Editor.ActiveSceneUtility;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    /// <summary>
    /// Ensures a Unity Terrain exists for cave grading and carving when the scene only has mesh ground.
    /// NVIDIA 3D-GENERALIST (2026): outdoor layout/materials must accompany interior mesh enclosures.
    /// </summary>
    static class CaveTerrainIntegrationUtility
    {
        public const string IntegrationTerrainName = "CaveIntegrationTerrain";

        /// <summary>
        /// Ensures active-scene terrain for grading and mouth sampling (ignores NeverCreateNewTerrain).
        /// FDG HDPCG controlled grounding; IEEE CoG spatial anchor at entrance XZ.
        /// </summary>
        public static bool EnsureForGroundPlacement(
            SceneGroundInfo ground,
            Transform caveRoot,
            int seed,
            out string action)
        {
            action = string.Empty;
            if (ground == null || !ground.HasAnchor)
            {
                action = "Cannot ensure terrain — missing scene ground anchor.";
                return false;
            }

            var existing = ActiveSceneUtility.FindInActiveScene<Terrain>();
            if (existing != null)
            {
                ground.Terrain = existing;
                return false;
            }

            var envRoot = EnvironmentSceneUtility.GetOrCreateRoot(ground);
            return EnsureSceneTerrain(envRoot.transform, ground, caveRoot, seed, out action);
        }

        /// <summary>Creates and levels terrain at the scene ground surface when none exists.</summary>
        public static bool EnsureSceneTerrain(
            Transform environmentRoot,
            SceneGroundInfo ground,
            Transform caveRoot,
            int seed,
            out string action)
        {
            action = string.Empty;
            var existing = ActiveSceneUtility.FindInActiveScene<Terrain>();
            if (existing != null)
            {
                if (ground != null)
                    ground.Terrain = existing;
                return false;
            }

            if (ground == null || !ground.HasAnchor)
            {
                action = "Cannot create terrain — missing scene ground anchor.";
                return false;
            }

            var terrain = CreateIntegrationTerrain(environmentRoot, ground, caveRoot);
            if (terrain == null)
            {
                action = "Terrain creation blocked (Environment Kit settings).";
                return false;
            }

            ApplyDressing(terrain, seed);
            FlattenToGroundSurface(terrain, ground, caveRoot);
            CaveTerrainUtility.ApplyCaveEntranceMouth(terrain, seed, caveRoot);
            ground.Terrain = terrain;
            EnvironmentSceneUtility.MarkSceneDirty();
            action = $"Created {IntegrationTerrainName} aligned to ground surface (Y≈{ground.SurfaceY:F1}m).";
            return true;
        }

        static Terrain CreateIntegrationTerrain(
            Transform environmentRoot,
            SceneGroundInfo ground,
            Transform caveRoot)
        {
            var sample = ResolveTerrainCenter(ground, caveRoot);
            var padding = 48f;
            var extent = Mathf.Max(ground.Bounds.size.x, ground.Bounds.size.z) + padding;
            var size = Mathf.Clamp(Mathf.Ceil(extent / 64f) * 64f, 192f, 512f);
            size = EnvironmentKitHardwareBudget.ClampTerrainSizeMeters((int)size);
            var heightRange = EnvironmentKitHardwareBudget.Active.TerrainMaxHeightMeters;
            var heightmapRes = EnvironmentKitHardwareBudget.ClampHeightmapResolution(513);

            var data = new TerrainData
            {
                heightmapResolution = heightmapRes,
                size = new Vector3(size, heightRange, size)
            };

            var go = Terrain.CreateTerrainGameObject(data);
            CaveEditorUndo.RegisterCreated(go, "Create Cave Integration Terrain");
            go.name = IntegrationTerrainName;
            if (ActiveSceneUtility.HasValidActiveScene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    go, ActiveSceneUtility.ActiveScene);

            if (environmentRoot != null)
                go.transform.SetParent(environmentRoot, false);

            // Anchor sits inset so new terrain extends outward (does not center-cover existing main land).
            const float anchorNormX = 0.32f;
            const float anchorNormZ = 0.32f;
            const float normalizedSurface = 0.42f;
            go.transform.position = new Vector3(
                sample.x - size * anchorNormX,
                ground.SurfaceY - normalizedSurface * heightRange,
                sample.z - size * anchorNormZ);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var col = go.GetComponent<TerrainCollider>();
            if (col != null)
                col.terrainData = data;

            return go.GetComponent<Terrain>();
        }

        static Vector3 ResolveTerrainCenter(SceneGroundInfo ground, Transform caveRoot)
        {
            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth != Vector3.zero)
                    return new Vector3(mouth.x, 0f, mouth.z);
            }

            return new Vector3(ground.Bounds.center.x, 0f, ground.Bounds.center.z);
        }

        static void ApplyDressing(Terrain terrain, int seed)
        {
            var preset = AssetDatabase.LoadAssetAtPath<TerrainDressingPreset>(
                $"{EnvironmentKitSettings.PresetsFolder}/CaveTerrain.asset");
            if (preset != null)
                TerrainDressingApplier.Apply(terrain, preset);
        }

        static void FlattenToGroundSurface(Terrain terrain, SceneGroundInfo ground, Transform caveRoot)
        {
            var data = terrain.terrainData;
            if (data == null)
                return;

            CaveEditorUndo.RecordObject(data, "Flatten Cave Integration Terrain");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var scaleY = Mathf.Max(0.01f, terrain.transform.lossyScale.y);
            var targetY = ground.SurfaceY;
            var normalized = Mathf.Clamp01((targetY - origin.y) / (size.y * scaleY));

            for (var z = 0; z < res; z++)
            {
                for (var x = 0; x < res; x++)
                    heights[z, x] = normalized;
            }

            if (caveRoot != null)
            {
                var mouth = CaveGroundPlacementUtility.GetEntranceMouthWorld(caveRoot);
                if (mouth != Vector3.zero)
                {
                    const float padMeters = 24f;
                    StampFlatDisc(heights, res, size, origin, mouth, padMeters, normalized);
                }
            }

            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        static void StampFlatDisc(
            float[,] heights,
            int res,
            Vector3 terrainSize,
            Vector3 terrainOrigin,
            Vector3 worldCenter,
            float radiusMeters,
            float heightNormalized)
        {
            var nx = (worldCenter.x - terrainOrigin.x) / terrainSize.x;
            var nz = (worldCenter.z - terrainOrigin.z) / terrainSize.z;
            var radiusX = radiusMeters / terrainSize.x;
            var radiusZ = radiusMeters / terrainSize.z;

            var x0 = Mathf.Clamp(Mathf.FloorToInt((nx - radiusX) * res), 0, res - 1);
            var x1 = Mathf.Clamp(Mathf.CeilToInt((nx + radiusX) * res), 0, res - 1);
            var z0 = Mathf.Clamp(Mathf.FloorToInt((nz - radiusZ) * res), 0, res - 1);
            var z1 = Mathf.Clamp(Mathf.CeilToInt((nz + radiusZ) * res), 0, res - 1);

            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var px = x / (float)(res - 1);
                    var pz = z / (float)(res - 1);
                    var dx = (px - nx) / Mathf.Max(0.001f, radiusX);
                    var dz = (pz - nz) / Mathf.Max(0.001f, radiusZ);
                    if (dx * dx + dz * dz > 1f)
                        continue;

                    heights[z, x] = heightNormalized;
                }
            }
        }
    }
}
