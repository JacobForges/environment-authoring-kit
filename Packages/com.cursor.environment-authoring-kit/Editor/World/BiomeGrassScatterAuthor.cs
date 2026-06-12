#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>
    /// Ensures CC0 grass (G-G/G-C/G-T) and biome props scatter on all ground tiles after Full AAA grid snap.
    /// </summary>
    public static class BiomeGrassScatterAuthor
    {
        const int MinGrassInstancesBeforeSkip = 280;

        public static void QueueEnsureBiomePropsScattered(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Transform surfaceRoot,
            Action onComplete)
        {
            if (mainTerrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (!TryBeginScatterSession(mainTerrain, request, ref surfaceRoot, out var session, out var vegRoot))
            {
                onComplete?.Invoke();
                return;
            }

            var placed = 0;
            void PlaceNextChunk()
            {
                if (session.IsComplete)
                {
                    if (SurfaceIntelligentPropPlacer.TryFinalizeCategoryPlacementSession(
                            mainTerrain,
                            vegRoot,
                            SurfacePropCategory.Grass,
                            session,
                            out var finalizeMsg))
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Biome grass scatter — {placed} CC0 grass instances ({finalizeMsg}).",
                            forceUnityConsole: true);
                    }

                    onComplete?.Invoke();
                    return;
                }

                CaveBuildActionPacing.ScheduleLight(
                    () =>
                    {
                        SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPassChunk(
                            mainTerrain,
                            vegRoot,
                            request.Seed + 0x47524153,
                            SurfacePropCategory.Grass,
                            session,
                            SurfaceIntelligentPropPlacer.DefaultPropsPerEditorChunk,
                            out var chunk);
                        placed += chunk;
                        PlaceNextChunk();
                    },
                    CaveBuildPipelineDomains.QueueLabel("surface biome grass scatter"));
            }

            PlaceNextChunk();
        }

        public static void EnsureBiomePropsScattered(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            Transform surfaceRoot)
        {
            if (mainTerrain == null || request == null)
                return;

            if (!TryBeginScatterSession(mainTerrain, request, ref surfaceRoot, out var session, out var vegRoot))
                return;

            var placed = 0;
            while (!session.IsComplete)
            {
                SurfaceIntelligentPropPlacer.TryPlaceCategoryLadderPassChunk(
                    mainTerrain,
                    vegRoot,
                    request.Seed + 0x47524153,
                    SurfacePropCategory.Grass,
                    session,
                    SurfaceIntelligentPropPlacer.DefaultPropsPerEditorChunk * 2,
                    out var chunk);
                placed += chunk;
            }

            if (SurfaceIntelligentPropPlacer.TryFinalizeCategoryPlacementSession(
                    mainTerrain,
                    vegRoot,
                    SurfacePropCategory.Grass,
                    session,
                    out var finalizeMsg))
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Biome grass scatter — {placed} CC0 grass instances ({finalizeMsg}).",
                    forceUnityConsole: true);
            }
        }

        static bool TryBeginScatterSession(
            Terrain mainTerrain,
            WorldGenerationRequest request,
            ref Transform surfaceRoot,
            out SurfaceIntelligentPropPlacer.CategoryPlacementSession session,
            out Transform vegRoot)
        {
            session = null;
            vegRoot = null;

            Cc0ContentImportUtility.EnsureAll(importItems: true);
            surfaceRoot ??= mainTerrain.transform.parent;

            vegRoot = surfaceRoot != null ? surfaceRoot.Find(SurfaceIntelligentPropPlacer.VegetationLayerName) : null;
            var existingGrass = CountGrassInstances(vegRoot);
            if (existingGrass >= MinGrassInstancesBeforeSkip)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Biome grass scatter skipped — {existingGrass} vegetation instances already placed.",
                    forceUnityConsole: false);
                return false;
            }

            var catalog = SurfaceIntelligentPropPlacer.LoadVegetationCatalog();
            InjectCc0IntoCatalog(catalog);
            var biomeCatalog = BiomePropCatalog.Load(request);

            if (!catalog.HasCategory(SurfacePropCategory.Grass) && !biomeCatalog.HasAny)
            {
                CaveBuildEditorLog.LogSurfaceWarning(
                    "[Surface] Biome grass scatter — no G-G/G-C/G-T prefabs found after CC0 import.");
                return false;
            }

            SurfaceIntelligentPropPlacer.ImportCatalogPrefabsOnce(catalog);
            var ground = SceneGroundResolver.ResolveForFullWorld();
            if (ground?.Terrain == null)
                ground = new SceneGroundInfo { Terrain = mainTerrain, Anchor = mainTerrain.transform };
            SceneGroundResolver.RefreshSurface(ground);
            SurfaceTerrainPropPlacementRegion.LockAndMarkSurfaceTerrains(mainTerrain, ground, request);

            var center = mainTerrain.transform.position +
                         new Vector3(
                             mainTerrain.terrainData.size.x * 0.5f,
                             0f,
                             mainTerrain.terrainData.size.z * 0.5f);
            var extent = SurfaceTerrainPlayRegion.ResolveUnifiedSurfaceExtent(
                mainTerrain,
                center,
                request.SurfaceExtentMeters);

            if (!SurfaceIntelligentPropPlacer.TryBeginCategoryPlacementSession(
                    surfaceRoot,
                    mainTerrain,
                    center,
                    extent,
                    request.Seed + 0x47524153,
                    SurfacePropCategory.Grass,
                    catalog,
                    null,
                    biomeCatalog,
                    request,
                    out session,
                    out vegRoot,
                    out var beginMsg))
            {
                CaveBuildEditorLog.LogSurfaceWarning($"[Surface] Biome grass scatter begin failed: {beginMsg}");
                return false;
            }

            return true;
        }

        static int CountGrassInstances(Transform vegRoot)
        {
            if (vegRoot == null)
                return 0;

            var count = 0;
            for (var i = 0; i < vegRoot.childCount; i++)
            {
                var child = vegRoot.GetChild(i);
                if (child.name.IndexOf("grass", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.StartsWith("G-G", System.StringComparison.Ordinal) ||
                    child.name.StartsWith("G-C", System.StringComparison.Ordinal) ||
                    child.name.StartsWith("G-T", System.StringComparison.Ordinal))
                    count++;
            }

            return count > 0 ? count : vegRoot.childCount / 3;
        }

        static void InjectCc0IntoCatalog(SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog catalog)
        {
            if (catalog == null)
                return;

            for (var g = 1; g <= 6; g++)
            {
                TryAddGrass(catalog, $"G-G{g:D2}");
                TryAddGrass(catalog, $"G-C{g:D2}");
                TryAddGrass(catalog, $"G-T{g:D2}");
            }
        }

        static void TryAddGrass(SurfaceIntelligentPropPlacer.SurfaceVegetationCatalog catalog, string id)
        {
            var prefab = Cc0ContentImportUtility.LoadItemPrefab(id);
            if (prefab != null && !catalog.Grass.Contains(prefab))
                catalog.Grass.Add(prefab);
        }
    }
}
#endif
