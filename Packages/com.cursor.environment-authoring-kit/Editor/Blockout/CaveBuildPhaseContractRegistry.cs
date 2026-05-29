#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Ladder rung contracts: inputs, outputs, invalidation, completion state per seed.
    /// </summary>
    public static class CaveBuildPhaseContractRegistry
    {
        public const string ContractsExportRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildPhaseContracts.json";
        public const string CompletionRel = CaveBuildAgentContextExporter.Folder + "/CaveBuildLadderCompletion.json";

        public const string RungResearchSeed = "research_seed";
        public const string RungMacroTerrain = "macro_terrain";
        public const string RungHydrologyMasks = "hydrology_masks";
        public const string RungTrailsNav = "trails_nav";
        public const string RungSurfaceProps = "surface_props";
        public const string RungPreBuildGate = "pre_build_gate";
        public const string RungCaveLayout = "cave_layout";
        public const string RungRouteMeshNav = "route_mesh_nav";
        public const string RungShellMaterials = "shell_materials";
        public const string RungGameplayProps = "gameplay_props";
        public const string RungValidation = "validation";
        public const string RungPolish = "polish";

        [Serializable]
        public class RungContract
        {
            public string id;
            public string title;
            public string[] inputs;
            public string[] outputs;
            public string[] invalidates;
            public int maxRuntimeSecondsTarget;
        }

        [Serializable]
        public class ContractsFile
        {
            public string productScope =
                "Florida karst surface + lava-tube cave for Unity XR (Environment Authoring Kit)";
            public string docPath = "Packages/com.cursor.environment-authoring-kit/docs/PHASE_CONTRACTS.md";
            public RungContract[] rungs;
        }

        [Serializable]
        public class CompletionEntry
        {
            public string rungId;
            public int seed;
            public string completedUtc;
            public string artifactFingerprint;
        }

        [Serializable]
        public class CompletionFile
        {
            public int seed;
            public string sessionUtc;
            public CompletionEntry[] entries = Array.Empty<CompletionEntry>();
            public string[] dirtyRungs = Array.Empty<string>();
        }

        static readonly RungContract[] Catalog =
        {
            new()
            {
                id = RungResearchSeed,
                title = "Research + seed lock",
                inputs = new[] { "Assets/EnvironmentKit/ResearchCache/index.json" },
                outputs = new[]
                {
                    "Assets/EnvironmentKit/Generated/CaveBuildResearchExecutionBrief.json",
                    "Assets/EnvironmentKit/Generated/CaveBuildPhaseResearchGate.json",
                },
                invalidates = new[] { "*" },
                maxRuntimeSecondsTarget = 30,
            },
            new()
            {
                id = RungMacroTerrain,
                title = "Macro terrain + hillshade",
                inputs = new[] { "Ground anchor", "seed" },
                outputs = new[]
                {
                    "Terrain heightmap",
                    "Assets/EnvironmentKit/Generated/SurfaceDemGeorefStatus.json",
                },
                invalidates = new[] { RungTrailsNav, RungSurfaceProps, RungPreBuildGate, RungCaveLayout },
                maxRuntimeSecondsTarget = 90,
            },
            new()
            {
                id = RungHydrologyMasks,
                title = "Hydrology / karst structure masks",
                inputs = new[] { "Terrain heightmap" },
                outputs = new[] { "Road/water feature nodes under GeneratedSurfaceWorld" },
                invalidates = new[] { RungTrailsNav, RungSurfaceProps },
                maxRuntimeSecondsTarget = 45,
            },
            new()
            {
                id = RungTrailsNav,
                title = "Trails + surface NavMesh",
                inputs = new[] { "heightmap", "masks" },
                outputs = new[]
                {
                    "GeneratedSurfaceWorld/Trails",
                    "Assets/EnvironmentKit/Generated/SurfaceWorldManifest.json",
                },
                invalidates = new[] { RungSurfaceProps, RungValidation },
                maxRuntimeSecondsTarget = 60,
            },
            new()
            {
                id = RungSurfaceProps,
                title = "Surface prop scatter",
                inputs = new[] { "trails", "surface NavMesh" },
                outputs = new[] { "GeneratedSurfaceWorld scatter children" },
                invalidates = new[] { RungValidation },
                maxRuntimeSecondsTarget = 45,
            },
            new()
            {
                id = RungPreBuildGate,
                title = "Cursor pre-build gate",
                inputs = new[] { "surface artifacts" },
                outputs = new[] { "Assets/EnvironmentKit/Generated/CaveBuildPreBuildLadderReport.json" },
                invalidates = new[] { RungCaveLayout, RungRouteMeshNav, RungShellMaterials, RungGameplayProps },
                maxRuntimeSecondsTarget = 120,
            },
            new()
            {
                id = RungCaveLayout,
                title = "Cave layout + spline",
                inputs = new[] { "pre_build_gate pass" },
                outputs = new[] { "UndergroundCaveSystem / CaveGeometry" },
                invalidates = new[] { RungRouteMeshNav, RungShellMaterials, RungGameplayProps, RungValidation },
                maxRuntimeSecondsTarget = 120,
            },
            new()
            {
                id = RungRouteMeshNav,
                title = "Route floor/ceiling + NavMesh",
                inputs = new[] { "cave layout" },
                outputs = new[] { "RouteTerrainFloor", "NavMesh on cave floor" },
                invalidates = new[] { RungShellMaterials, RungGameplayProps, RungValidation },
                maxRuntimeSecondsTarget = 90,
            },
            new()
            {
                id = RungShellMaterials,
                title = "Shell rings + materials",
                inputs = new[] { "route mesh" },
                outputs = new[] { "AdventureShell / block rings" },
                invalidates = new[] { RungGameplayProps, RungPolish },
                maxRuntimeSecondsTarget = 120,
            },
            new()
            {
                id = RungGameplayProps,
                title = "Gameplay props + mobs",
                inputs = new[] { "shell" },
                outputs = new[] { "Spawners, portals under cave root" },
                invalidates = new[] { RungValidation, RungPolish },
                maxRuntimeSecondsTarget = 60,
            },
            new()
            {
                id = RungValidation,
                title = "Validation bots (read-only)",
                inputs = new[] { "cave + surface" },
                outputs = new[]
                {
                    "Assets/EnvironmentKit/Generated/CaveBuildRouteProbe.json",
                    "Assets/EnvironmentKit/Generated/CaveBuildSurfaceRouteProbe.json",
                },
                invalidates = new[] { RungPolish },
                maxRuntimeSecondsTarget = 30,
            },
            new()
            {
                id = RungPolish,
                title = "Polish / post (grade-driven)",
                inputs = new[] { "validation reports" },
                outputs = new[] { "Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json" },
                invalidates = Array.Empty<string>(),
                maxRuntimeSecondsTarget = 300,
            },
        };

        public static IReadOnlyList<RungContract> AllRungs => Catalog;

        public static void ExportContractsCatalog()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, ContractsExportRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var file = new ContractsFile { rungs = Catalog };
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }

        public static string MapQueuedStepToRung(int step)
        {
            if (step == 0)
                return RungResearchSeed;
            if (step >= CaveBuildQueuedPipelineSchedule.GeoFirst &&
                step < CaveBuildQueuedPipelineSchedule.PlayabilityFirst)
                return RungCaveLayout;
            if (step >= CaveBuildQueuedPipelineSchedule.PlayabilityFirst &&
                step < CaveBuildQueuedPipelineSchedule.ValidationFirst)
                return RungRouteMeshNav;
            if (step >= CaveBuildQueuedPipelineSchedule.ValidationFirst &&
                step < CaveBuildQueuedPipelineSchedule.GroundPolishFirst)
                return RungValidation;
            if (step >= CaveBuildQueuedPipelineSchedule.GroundPolishFirst &&
                step < CaveBuildQueuedPipelineSchedule.WorldFirst + CaveBuildQueuedPipelineSchedule.WorldCount)
                return step < CaveBuildQueuedPipelineSchedule.Meat - 11
                    ? RungShellMaterials
                    : RungGameplayProps;
            if (step == CaveBuildQueuedPipelineSchedule.Meat)
                return RungPolish;
            if (step >= CaveBuildQueuedPipelineSchedule.PostMeatFirst &&
                step < CaveBuildQueuedPipelineSchedule.FinalizePolishFirst)
                return RungPolish;
            // Research macro steps only — manifest + finalize must never share RungResearchSeed
            // or incremental ladder skips finalize after manifest (stuck at 120/121).
            if (step >= CaveBuildQueuedPipelineSchedule.ResearchFirst &&
                step < CaveBuildQueuedPipelineSchedule.AaaManifest)
                return RungResearchSeed;
            return null;
        }

        public static bool AreOutputsPresent(string rungId, int seed)
        {
            if (rungId == RungCaveLayout)
                return IsSceneCaveLayoutPresent();

            if (rungId == RungSurfaceProps)
                return HasSurfaceVegetationInScene();

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            foreach (var c in Catalog)
            {
                if (c.id != rungId)
                    continue;
                foreach (var rel in c.outputs)
                {
                    if (rel.StartsWith("Terrain", StringComparison.Ordinal) ||
                        rel.StartsWith("Ground", StringComparison.Ordinal) ||
                        rel.StartsWith("GeneratedSurfaceWorld", StringComparison.Ordinal) ||
                        rel.StartsWith("Underground", StringComparison.Ordinal) ||
                        rel.StartsWith("RouteTerrain", StringComparison.Ordinal) ||
                        rel.StartsWith("Adventure", StringComparison.Ordinal) ||
                        rel.StartsWith("Spawners", StringComparison.Ordinal))
                        continue;

                    if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                        continue;

                    var path = Path.Combine(hub, rel);
                    if (!File.Exists(path) && !Directory.Exists(path))
                        return false;
                }

                return true;
            }

            return false;
        }

        public static bool IsRungComplete(string rungId, int seed)
        {
            var file = LoadCompletion();
            if (file.seed != seed)
                return false;
            if (file.dirtyRungs != null)
            {
                foreach (var d in file.dirtyRungs)
                {
                    if (d == rungId)
                        return false;
                }
            }

            foreach (var e in file.entries)
            {
                if (e.rungId == rungId && e.seed == seed)
                    return AreOutputsPresent(rungId, seed);
            }

            return false;
        }

        public static void MarkRungComplete(string rungId, int seed)
        {
            var file = LoadCompletion();
            file.seed = seed;
            var list = new List<CompletionEntry>(file.entries ?? Array.Empty<CompletionEntry>());
            list.RemoveAll(e => e.rungId == rungId);
            list.Add(new CompletionEntry
            {
                rungId = rungId,
                seed = seed,
                completedUtc = DateTime.UtcNow.ToString("o"),
                artifactFingerprint = FingerprintOutputs(rungId),
            });
            file.entries = list.ToArray();
            ClearDirtyFlag(file, rungId);
            SaveCompletion(file);
            CaveBuildLadderMetrics.RecordRungSkipped(rungId, false);
        }

        /// <summary>Marks cave geometry ladder dirty when starting a fresh queued pipeline or destroying the old cave root.</summary>
        public static void InvalidateCaveGeometryLadderRungs() => InvalidateRung(RungCaveLayout);

        static bool IsSceneCaveLayoutPresent() => HasPlayableCaveLayoutInScene();

        /// <summary>
        /// True when a full cave was generated (blocks, shell floor+ceiling, or spline tube) —
        /// not a terrain-ladder mouth patch (ramp + partial floor only).
        /// </summary>
        public static bool HasPlayableCaveLayoutInScene()
        {
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave == null)
                return false;

            if (HasMonolithicCaveTube(cave))
                return true;

            var geometry = cave.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
                return false;

            var blockSection = geometry.Find($"{CaveAdventureBlockBuilder.RootName}/Main");
            if (blockSection != null && blockSection.childCount >= 4)
                return true;

            var floor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
            var ceiling = geometry.Find(CaveEnclosureShellBuilder.CeilingRootName);
            if (HasRouteMesh(floor) && HasRouteMesh(ceiling))
                return true;

            return false;
        }

        static bool HasMonolithicCaveTube(Transform caveRoot)
        {
            if (caveRoot == null)
                return false;

            if (caveRoot.Find("MainCaveTube") != null)
                return true;

            var splineMesh = caveRoot.Find("SplineMesh");
            return splineMesh != null && splineMesh.Find("MainCaveTube") != null;
        }

        static bool HasRouteMesh(Transform root) =>
            root != null && root.GetComponent<MeshFilter>()?.sharedMesh != null;

        static bool HasSurfaceVegetationInScene()
        {
            var envRoot = UnityEngine.Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
            if (envRoot == null)
                return false;

            var surface = envRoot.transform.Find(SurfaceWorldPaths.RootName);
            if (surface == null)
                return false;

            var veg = surface.Find(SurfaceWorldPaths.VegetationName);
            var ground = SceneGroundResolver.Resolve(envRoot.transform);
            return veg != null &&
                   ground?.Terrain != null &&
                   SurfaceTerrainPropPlacementRegion.IsNineTileVegetationSufficient(veg, ground.Terrain);
        }

        public static void InvalidateRung(string rungId)
        {
            var file = LoadCompletion();
            var dirty = new HashSet<string>(file.dirtyRungs ?? Array.Empty<string>());
            dirty.Add(rungId);
            foreach (var c in Catalog)
            {
                if (c.id != rungId)
                    continue;
                foreach (var downstream in c.invalidates)
                {
                    if (downstream == "*")
                    {
                        foreach (var r in Catalog)
                            dirty.Add(r.id);
                        break;
                    }

                    dirty.Add(downstream);
                }
            }

            file.dirtyRungs = new List<string>(dirty).ToArray();
            SaveCompletion(file);
        }

        public static void InvalidateAll()
        {
            var file = LoadCompletion();
            file.entries = Array.Empty<CompletionEntry>();
            file.dirtyRungs = Array.Empty<string>();
            SaveCompletion(file);
            Debug.Log("[CaveBuild] All ladder rungs invalidated — next build runs full pipeline.");
        }

        static void ClearDirtyFlag(CompletionFile file, string rungId)
        {
            if (file.dirtyRungs == null || file.dirtyRungs.Length == 0)
                return;
            var list = new List<string>(file.dirtyRungs);
            list.Remove(rungId);
            file.dirtyRungs = list.ToArray();
        }

        static string FingerprintOutputs(string rungId)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            foreach (var c in Catalog)
            {
                if (c.id != rungId)
                    continue;
                long sum = 0;
                foreach (var rel in c.outputs)
                {
                    if (!rel.StartsWith("Assets/", StringComparison.Ordinal))
                        continue;
                    var path = Path.Combine(hub, rel);
                    if (!File.Exists(path))
                        continue;
                    try
                    {
                        sum += new FileInfo(path).LastWriteTimeUtc.Ticks;
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                return sum.ToString();
            }

            return "0";
        }

        static CompletionFile LoadCompletion()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CompletionRel);
            if (!File.Exists(path))
                return new CompletionFile { sessionUtc = DateTime.UtcNow.ToString("o") };
            try
            {
                return JsonUtility.FromJson<CompletionFile>(File.ReadAllText(path)) ??
                       new CompletionFile();
            }
            catch
            {
                return new CompletionFile();
            }
        }

        static void SaveCompletion(CompletionFile file)
        {
            file.sessionUtc = DateTime.UtcNow.ToString("o");
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CompletionRel);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }
    }
}
#endif
