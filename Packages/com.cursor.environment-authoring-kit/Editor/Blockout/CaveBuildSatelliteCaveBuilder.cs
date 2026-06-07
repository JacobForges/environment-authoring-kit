#if UNITY_EDITOR
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Builds POI cave systems at mountain wilderness mouths (sectors 201–203) and optional play-disk satellites.
    /// </summary>
    public static class CaveBuildSatelliteCaveBuilder
    {
        public const string PoiRootPrefix = "UndergroundCaveSystem_Poi_";

        public static void QueueBuildAll(
            SceneGroundInfo ground,
            WorldGenerationRequest template,
            Transform environmentRoot)
        {
            if (ground == null || !ground.HasAnchor || template == null || environmentRoot == null)
                return;

            var satellites = CollectSatelliteMarkers();
            if (satellites.Count == 0)
            {
                Debug.Log("[CaveBuild] Satellite caves — no mountain POI opening markers.");
                return;
            }

            Debug.Log($"[CaveBuild] Mountain POI caves queued — {satellites.Count} system(s).");
            QueueBuildIndex(0, satellites, ground, template, environmentRoot);
        }

        public static int CountBuiltPoiRoots(Transform environmentRoot)
        {
            if (environmentRoot == null)
                return 0;

            var n = 0;
            for (var i = 0; i < environmentRoot.childCount; i++)
            {
                var c = environmentRoot.GetChild(i);
                if (c != null && c.name.StartsWith(PoiRootPrefix))
                    n++;
            }

            return n;
        }

        static void QueueBuildIndex(
            int index,
            IReadOnlyList<SurfaceCaveOpeningMarker> markers,
            SceneGroundInfo ground,
            WorldGenerationRequest template,
            Transform environmentRoot)
        {
            if (index >= markers.Count)
            {
                Debug.Log($"[CaveBuild] Mountain POI caves complete — {markers.Count} system(s).");
                return;
            }

            CaveBuildActionPacing.ScheduleHeavy(
                () =>
                {
                    BuildOne(markers[index], ground, template, environmentRoot);
                    QueueBuildIndex(index + 1, markers, ground, template, environmentRoot);
                },
                CaveBuildPipelineDomains.QueueLabel($"mountain POI cave {index + 1}/{markers.Count}"));
        }

        static List<SurfaceCaveOpeningMarker> CollectSatelliteMarkers()
        {
            var list = new List<SurfaceCaveOpeningMarker>();
            foreach (var m in SurfaceWorldGenerator.FindCaveOpenings())
            {
                if (m == null || m.isPrimaryEntrance)
                    continue;

                if (IsMountainPoiSector(m.sectorIndex) ||
                    m.transform.parent?.name == SurfaceWorldPaths.CaveOpeningsName &&
                    (m.name.StartsWith(SurfaceMountainWildernessCaveMouthAuthor.MarkerName, System.StringComparison.Ordinal) ||
                     m.name.StartsWith(SurfaceMountainWildernessCaveMouthAuthor.MarkerNamePrefix, System.StringComparison.Ordinal)))
                    list.Add(m);
            }

            list.Sort((a, b) => a.sectorIndex.CompareTo(b.sectorIndex));
            return list;
        }

        static bool IsMountainPoiSector(int sector) =>
            sector == SurfaceMountainWildernessCaveMouthAuthor.SectorMainMountain ||
            sector == SurfaceMountainWildernessCaveMouthAuthor.SectorTrapMountain ||
            sector == SurfaceMountainWildernessCaveMouthAuthor.SectorLabyrinthMountain;

        static void BuildOne(
            SurfaceCaveOpeningMarker marker,
            SceneGroundInfo ground,
            WorldGenerationRequest template,
            Transform environmentRoot)
        {
            var rootName = $"{PoiRootPrefix}{marker.sectorIndex:D3}";
            var existing = environmentRoot.Find(rootName);
            if (existing != null)
                CaveEditorUndo.DestroyImmediate(existing.gameObject);

            var rootGo = new GameObject(rootName);
            CaveEditorUndo.RegisterCreated(rootGo, "Mountain POI cave");
            rootGo.transform.SetParent(environmentRoot, false);

            var mouth = marker.transform.position;
            rootGo.transform.position = new Vector3(
                mouth.x,
                mouth.y - CaveGeometryPaths.UndergroundDepthMeters * 0.72f,
                mouth.z);

            var poiRequest = ConfigurePoiRequest(template, marker);
            var segments = poiRequest.CaveTunnelSegments;
            var chambers = poiRequest.CaveChamberCount;
            var layout = CaveMazeLayoutGenerator.Generate(poiRequest.Seed, segments, chambers);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
            {
                Debug.LogWarning($"[CaveBuild] POI {rootName} — maze layout failed.");
                return;
            }

            var geometry = CaveAdventureCaveGenerator.EnsureGeometryRoot(rootGo.transform);
            var floorMat = ProjectCaveMaterialResolver.Resolve(ProjectCaveMaterialResolver.MaterialRole.Floor)
                           ?? CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = ProjectCaveMaterialResolver.Resolve(ProjectCaveMaterialResolver.MaterialRole.Rock)
                          ?? CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, poiRequest.Seed);

            CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly(
                rootGo.transform, ground, allowRaise: false, out _);

            Debug.Log(
                $"[CaveBuild] Mountain POI {rootName} — {segments} segments, {chambers} chambers " +
                $"(labyrinth={poiRequest.UseTombRaiderLabyrinthCadence}).");
        }

        static WorldGenerationRequest ConfigurePoiRequest(
            WorldGenerationRequest template,
            SurfaceCaveOpeningMarker marker)
        {
            var poiRequest = template.Clone();
            poiRequest.Seed = template.Seed + marker.sectorIndex * 991;

            switch (marker.sectorIndex)
            {
                case SurfaceMountainWildernessCaveMouthAuthor.SectorMainMountain:
                    poiRequest.CaveTunnelSegments = Mathf.Max(8, template.CaveTunnelSegments);
                    poiRequest.CaveChamberCount = Mathf.Max(2, template.CaveChamberCount);
                    poiRequest.UseTombRaiderLabyrinthCadence = false;
                    break;

                case SurfaceMountainWildernessCaveMouthAuthor.SectorTrapMountain:
                    poiRequest.CaveTunnelSegments = Mathf.Max(18, template.CaveTunnelSegments + 10);
                    poiRequest.CaveChamberCount = Mathf.Max(3, template.CaveChamberCount + 1);
                    poiRequest.UseTombRaiderLabyrinthCadence = false;
                    poiRequest.MazeGenFlavor = 2;
                    break;

                case SurfaceMountainWildernessCaveMouthAuthor.SectorLabyrinthMountain:
                    poiRequest.CaveTunnelSegments = Mathf.Max(22, template.CaveTunnelSegments + 12);
                    poiRequest.CaveChamberCount = Mathf.Max(5, template.CaveChamberCount + 3);
                    poiRequest.UseTombRaiderLabyrinthCadence = true;
                    poiRequest.MazeGenFlavor = 4;
                    break;

                default:
                    poiRequest.CaveTunnelSegments = Mathf.Max(4, template.CaveTunnelSegments / 2);
                    poiRequest.CaveChamberCount = Mathf.Max(1, template.CaveChamberCount / 2);
                    poiRequest.UseTombRaiderLabyrinthCadence = false;
                    break;
            }

            return poiRequest;
        }
    }
}
#endif
