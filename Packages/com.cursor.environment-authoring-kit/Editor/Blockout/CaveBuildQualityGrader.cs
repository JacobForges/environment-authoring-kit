using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class CaveBuildQualityGrader
    {
        public const int PassThreshold = CaveBuildQualityRubric.TargetOverallScore;

        public static CaveBuildQualityReport GradeFullBuild(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport)
        {
            var mode = request != null && (request.UseLayoutPrototype ||
                                           CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot))
                ? "layout_prototype"
                : "full_build";
            return CaveBuildQualitySystem.Grade(caveRoot, ground, request, buildReport, mode);
        }

        internal static CaveBuildQualityReport BuildStageReport(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport)
        {
            if (request != null && (request.UseLayoutPrototype || CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot)))
                return BuildLayoutPrototypeStages(caveRoot, ground, request, buildReport);

            var report = new CaveBuildQualityReport
            {
                Seed = request.Seed,
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                AdventureMode = CaveGeometryPaths.IsAdventureCave(caveRoot)
            };

            report.Stages.Add(GradeSceneGround(ground));
            report.Stages.Add(GradePath(caveRoot));
            report.Stages.Add(GradeLayoutIntegrity(caveRoot));
            report.Stages.Add(GradeVisualShell(caveRoot));
            report.Stages.Add(GradeEnclosurePolicy(caveRoot));
            report.Stages.Add(GradeOrganicTube(caveRoot));
            report.Stages.Add(GradeBlockTunnel(caveRoot, request));
            report.Stages.Add(GradeGeometryIntegrity(caveRoot, request));
            report.Stages.Add(GradeCaveMouthSeal(caveRoot, ground));
            report.Stages.Add(GradeInteriorRibs(caveRoot));
            report.Stages.Add(GradeWalkways(caveRoot));
            report.Stages.Add(GradePlayerFloor(caveRoot));
            report.Stages.Add(GradeEnclosure(caveRoot, buildReport, request));
            report.Stages.Add(GradeMaterials(caveRoot));
            report.Stages.Add(GradeWater(caveRoot, request));
            report.Stages.Add(GradeLighting(caveRoot));
            report.Stages.Add(GradeAtmosphere(caveRoot));
            report.Stages.Add(GradeNavMesh(buildReport));
            report.Stages.Add(GradePortalSpawn(caveRoot));
            report.Stages.Add(GradeSpawnReachability(caveRoot));
            report.Stages.Add(GradeMobSpawns(caveRoot));
            report.Stages.Add(GradePlayability(caveRoot));
            report.Stages.Add(GradePerformance(caveRoot));
            report.Stages.Add(GradeExportArtifacts(report));
            report.Stages.Add(GradePackagingReadiness(caveRoot, ground, buildReport));
            report.Stages.Add(GradeModeConsistency(caveRoot, layoutPrototype: false));

            return report;
        }

        static CaveBuildStageGrade GradeSceneGround(SceneGroundInfo ground)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "ground",
                StageName = "Scene ground anchor",
                Weight = 8
            };

            if (ground.HasAnchor)
                g.Score = 100;
            else
            {
                g.Score = 0;
                g.AddIssue("No Ground-tagged walkable surface found.");
                g.AddFix("Tag your terrain/floor as Ground or assign anchor in Environment Kit.");
            }

            return g;
        }

        static CaveBuildStageGrade GradePath(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "path",
                StageName = "Descending spline path",
                Weight = 12
            };

            var authoring = caveRoot != null ? caveRoot.GetComponent<CaveSplinePathAuthoring>() : null;
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 4)
            {
                g.Score = 10;
                g.AddIssue("Missing CaveSplinePathAuthoring or too few knots.");
                return g;
            }

            var knots = authoring.Knots;
            var length = authoring.TotalLength;

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                g.StageName = "Maze path (grid adventure)";
                var netDrop = knots[0].Position.y - knots[knots.Count - 1].Position.y;
                netDrop = Mathf.Max(netDrop, MeasureRouteVerticalDrop(caveRoot));
                var xzSpan = 0f;
                for (var i = 1; i < knots.Count; i++)
                {
                    var a = knots[i - 1].Position;
                    var b = knots[i].Position;
                    xzSpan += new Vector2(b.x - a.x, b.z - a.z).magnitude;
                }

                g.Score = length >= 55f ? 100 : length >= 40f ? 96 : 82;
                if (netDrop < 2f)
                {
                    g.Score -= 20;
                    g.AddIssue($"Maze path net drop only {netDrop:F1}m (want 2m+ toward cavern).");
                }

                if (xzSpan < 45f)
                {
                    g.Score -= 15;
                    g.AddIssue($"Maze horizontal span short ({xzSpan:F0}m) — increase CaveTunnelSegments.");
                }

                if (length < 40f)
                {
                    g.Score -= 15;
                    g.AddIssue($"Path short ({length:F0}m) — increase CaveTunnelSegments.");
                }

                g.Score = Mathf.Clamp(g.Score, 0, 100);
                return g;
            }

            var descending = 0;
            for (var i = 1; i < knots.Count; i++)
            {
                if (knots[i].Position.y < knots[i - 1].Position.y - 0.2f)
                    descending++;
            }

            var descentRatio = descending / (float)(knots.Count - 1);
            g.Score = Mathf.RoundToInt(Mathf.Lerp(40f, 100f, descentRatio));
            if (length < 40f)
            {
                g.Score -= 15;
                g.AddIssue($"Path short ({length:F0}m) — increase CaveTunnelSegments.");
            }

            if (length < 55f)
            {
                g.Score -= 20;
                g.AddIssue($"Path short for AAA ({length:F0}m) — need ~60m+.");
            }

            if (descentRatio < 0.9f)
                g.AddIssue("Path is not strictly descending.");

            return g;
        }

        static CaveBuildStageGrade GradeVisualShell(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "visual_shell",
                StageName = "Visual shell (no onion layers)",
                Weight = 26
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                g.AddIssue("Missing cave root.");
                return g;
            }

            var layoutProto = CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot);
            var compact = !layoutProto && IsCompactRouteBuild(caveRoot, out _, out _);
            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            audit.CollectIssues(compact, layoutProto);
            g.Score = audit.ComputeScore(compact, layoutProto);
            foreach (var issue in audit.Issues)
                g.AddIssue(issue);

            if (g.Score < CaveBuildQualityRubric.StagePassScore)
            {
                if (layoutProto)
                    g.AddFix("Window → Environment Kit → Build Cave Layout Prototype (removes legacy ceiling meshes).");
                else
                {
                    g.AddFix("Run meat loop: purge layered shells, rebuild route terrain + single-wall block tunnel.");
                    g.AddFix("Window → Environment Kit → Remove Cave Layered Shells, then Build Complete Cave Level.");
                }
            }

            return g;
        }

        static CaveBuildQualityReport BuildLayoutPrototypeStages(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport)
        {
            var report = new CaveBuildQualityReport
            {
                Seed = request.Seed,
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                AdventureMode = true,
                LayoutPrototypeMode = true
            };

            report.Stages.Add(GradeSceneGround(ground));
            report.Stages.Add(GradeLayoutPath(caveRoot));
            report.Stages.Add(GradeLayoutIntegrity(caveRoot));
            report.Stages.Add(GradeVisualShell(caveRoot));
            report.Stages.Add(GradeEnclosurePolicy(caveRoot, layoutPrototype: true));
            report.Stages.Add(WaivedStage("block_tunnel", "Block tunnel (waived)", 100));
            report.Stages.Add(PassStage("geometry_integrity", "Geometry (layout only)", 96));
            report.Stages.Add(WaivedStage("organic_mesh", "Organic mesh (waived)", 100));
            report.Stages.Add(WaivedStage("enclosure", "Occlusion (waived)", 100));
            report.Stages.Add(GradeWalkways(caveRoot));
            report.Stages.Add(GradeNavMesh(buildReport, relaxed: true));
            report.Stages.Add(GradePortalSpawn(caveRoot));
            report.Stages.Add(GradeSpawnReachability(caveRoot));
            report.Stages.Add(GradeMobSpawns(caveRoot));
            report.Stages.Add(GradePlayability(caveRoot, relaxed: true));
            report.Stages.Add(GradePerformance(caveRoot));
            report.Stages.Add(GradeLighting(caveRoot));
            report.Stages.Add(GradeAtmosphere(caveRoot, relaxed: true));
            report.Stages.Add(GradeCaveMouthSeal(caveRoot, ground));
            report.Stages.Add(GradeExportArtifacts(report));
            report.Stages.Add(GradeModeConsistency(caveRoot, layoutPrototype: true));

            return report;
        }

        static CaveBuildStageGrade WaivedStage(string id, string name, int score) =>
            new() { StageId = id, StageName = name, Weight = 0, Score = score, Passed = true };

        static CaveBuildStageGrade PassStage(string id, string name, int score) =>
            new() { StageId = id, StageName = name, Weight = 4, Score = score };

        static CaveBuildStageGrade GradeLayoutPath(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "path",
                StageName = "Layout course path",
                Weight = 14
            };

            var authoring = caveRoot?.GetComponent<CaveSplinePathAuthoring>();
            if (authoring?.Knots == null || authoring.Knots.Count < 8)
            {
                g.Score = 40;
                g.AddIssue("Layout path too short — increase CaveTunnelSegments in build settings.");
                return g;
            }

            g.Score = 98;
            var markers = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveLayoutPrototypeGenerator.MarkersRootName}");
            if (markers == null)
            {
                g.Score -= 10;
                g.AddIssue("Missing CaveLayoutMarkers — rebuild layout prototype.");
            }

            return g;
        }

        static CaveBuildStageGrade GradeOrganicTube(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "organic_mesh",
                StageName = "Organic tube mesh",
                Weight = 12
            };

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                g.StageName = "Adventure shell (grid floors + blocks)";
                var compact = IsCompactRouteBuild(caveRoot, out var platforms, out var blockCount);
                var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
                var shell = caveRoot.Find(
                    $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}");
                var floors = 0;
                var pathCeilings = 0;
                if (shell != null)
                {
                    foreach (Transform child in shell)
                    {
                        if (child.name.StartsWith("Floor_"))
                            floors++;
                        if (child.name.StartsWith("PathCeiling_"))
                            pathCeilings++;
                    }
                }

                var skyCap = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/SkyRockCap");
                if (skyCap != null)
                    pathCeilings += skyCap.childCount;

                g.Score = audit.ComputeScore(compact, layoutPrototype: false);
                if (audit.StackedCeilingSlabCount > 0)
                    g.AddIssue($"{audit.StackedCeilingSlabCount} stacked ceiling slab(s) — purge layered shells.");
                if (audit.HasAdventureShell || audit.LayeredSlabCount > 0 || audit.LegacySplineLayerCount > 0)
                    g.Score = Mathf.Min(g.Score, audit.ComputeScore(compact, layoutPrototype: false));

                if (compact)
                {
                    g.StageName = "Compact route (platforms + block tunnel)";
                    if (platforms < 6)
                    {
                        g.Score -= 50;
                        g.AddIssue($"Only {platforms} walk platform(s) on route — rebuild CaveGeometry.");
                    }
                    else if (platforms < 10)
                    {
                        g.Score -= 12;
                        g.AddIssue($"Route has {platforms} platforms — short course.");
                    }

                    if (blockCount < 20)
                    {
                        g.Score -= 40;
                        g.AddIssue($"Block tunnel sparse ({blockCount} blocks) — rebuild with UseBlockTunnel.");
                    }
                    else if (blockCount < 40)
                    {
                        g.Score -= 8;
                        g.AddIssue($"Block tunnel light ({blockCount} blocks) along route.");
                    }

                    if (pathCeilings > 0)
                    {
                        g.Score -= 35;
                        g.AddIssue($"{pathCeilings} PathCeiling slab(s) — use single RouteTerrainCeiling mesh only.");
                    }

                    if (!audit.HasSingleRouteCeiling && blockCount >= 20)
                    {
                        g.Score -= 12;
                        g.AddIssue("Missing RouteTerrainCeiling — rebuild for one continuous ceiling mesh.");
                    }
                }
                else
                {
                    if (floors < 6)
                    {
                        g.Score -= 45;
                        g.AddIssue($"Adventure shell has only {floors} floor slab(s) — rebuild CaveGeometry.");
                    }

                    if (blockCount < 120)
                    {
                        g.Score -= 35;
                        g.AddIssue($"Block tunnel sparse ({blockCount} blocks) — rebuild with UseBlockTunnel.");
                    }
                    else if (blockCount < 180)
                    {
                        g.Score -= 12;
                        g.AddIssue($"Block tunnel light ({blockCount} blocks).");
                    }

                    if (pathCeilings < 3)
                    {
                        g.Score -= 10;
                        g.AddIssue($"Few path ceiling segments ({pathCeilings}) — may look open overhead.");
                    }

                    if (shell == null && platforms < 6)
                    {
                        g.Score = 0;
                        g.AddIssue("Missing CaveGeometry/AdventureShell — run Build Complete Cave Level.");
                        g.AddFix("Enable UseTrue3DCaveSystem + UseBlockTunnel in generation request.");
                    }
                }

                g.Score = Mathf.Clamp(g.Score, 0, 100);
                return g;
            }

            var maze = caveRoot != null ? caveRoot.Find("SplineMesh/CaveMazeVolume") : null;
            if (maze != null)
            {
                var walls = maze.GetComponentsInChildren<MeshRenderer>(true).Length;
                g.Score = walls >= 40 ? 100 : walls >= 24 ? 88 : walls > 0 ? 65 : 15;
                if (g.Score < 100)
                    g.AddIssue($"Maze volume only has {walls} wall renderers — enclosure incomplete.");
                return g;
            }

            var main = caveRoot != null ? caveRoot.Find("SplineMesh/MainCaveTube") : null;
            if (main == null)
            {
                g.Score = 0;
                g.AddIssue("Missing cave geometry (CaveMazeVolume or MainCaveTube).");
                g.AddFix("Run Build Complete Cave Level (UseSplineMesh must be true).");
                return g;
            }

            var mf = main.GetComponent<MeshFilter>();
            var mr = main.GetComponent<MeshRenderer>();
            if (mf == null || mf.sharedMesh == null)
            {
                g.Score = 5;
                g.AddIssue("MainCaveTube has no mesh.");
                return g;
            }

            var mesh = mf.sharedMesh;
            var verts = mesh.vertexCount;
            var tris = mesh.triangles.Length / 3;
            var bounds = mesh.bounds;
            var size = bounds.size;

            g.Score = 50;
            if (verts >= 800)
                g.Score += 20;
            if (verts >= 2000)
                g.Score += 10;
            if (tris >= 400)
                g.Score += 10;
            if (size.magnitude >= 12f)
                g.Score += 10;

            var blockRoot = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            var hasBlocks = blockRoot != null && blockRoot.childCount > 0;
            if (mr == null || (!mr.enabled && !hasBlocks))
            {
                g.Score -= 25;
                g.AddIssue("Tube renderer disabled or missing.");
            }
            else if (hasBlocks && mr != null && !mr.enabled)
            {
                g.Score += 15;
            }

            if (mr != null && (mr.sharedMaterial == null || mr.sharedMaterial.shader.name.Contains("Hidden")))
            {
                g.Score -= 30;
                g.AddIssue("Tube material missing or broken (magenta).");
            }

            if (verts < 400)
                g.AddIssue($"Tube too low-poly ({verts} verts). Rebuild with higher CaveTunnelSegments.");

            if (size.magnitude < 8f)
                g.AddIssue("Tube bounds very small — likely empty shell.");

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeBlockTunnel(Transform caveRoot, WorldGenerationRequest request)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "block_tunnel",
                StageName = "Block tunnel shell",
                Weight = 18
            };

            if (request != null && request.UseTrue3DCaveSystem)
            {
                var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
                var hybridBlockCount = CountCaveBlocks(caveRoot);
                var compact = IsCompactRouteBuild(caveRoot, out _, out _);
                var layoutProto = request.UseLayoutPrototype ||
                                  CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot);

                if (audit.StackedCeilingSlabCount > 0 || audit.HasAdventureShell)
                {
                    g.Score = 20;
                    g.AddIssue("Stacked ceiling onion layers — run Remove Cave Layered Shells then rebuild.");
                    return g;
                }

                if (audit.LayeredSlabCount > 2 || audit.LegacySplineLayerCount > 0)
                {
                    g.Score = Mathf.Min(25, audit.ComputeScore(compact, layoutProto));
                    audit.CollectIssues(compact, layoutProto);
                    foreach (var issue in audit.Issues)
                        g.AddIssue(issue);
                    g.AddFix("Purge layered shells before scoring block tunnel.");
                    return g;
                }

                if (compact)
                {
                    g.Score = hybridBlockCount >= 25 && hybridBlockCount <= 220 ? 100 :
                        hybridBlockCount < 25 ? 35 : 55;
                    if (audit.BlocksPerRingAvg > 48f)
                    {
                        g.Score -= 40;
                        g.AddIssue($"Onion block rings ({audit.BlocksPerRingAvg:F0} blocks/ring) — rebuild with WallThickness=1.");
                    }

                    var pathSteps = CountSolutionPathSteps(caveRoot);
                    if (pathSteps > 0 && audit.BlockRingCount > pathSteps + 4)
                    {
                        g.Score -= 20;
                        g.AddIssue(
                            $"Too many block rings ({audit.BlockRingCount}) for path length ({pathSteps}) — one ring per route cell only.");
                    }

                    g.Score = Mathf.Clamp(g.Score, 0, 100);
                    return g;
                }

                if (request.UseBlockTunnel && hybridBlockCount >= 180 && audit.BlocksPerRingAvg <= 55f)
                {
                    g.Score = hybridBlockCount >= 350 ? 100 : hybridBlockCount >= 250 ? 96 : 90;
                    return g;
                }

                var maze = caveRoot != null ? caveRoot.Find("SplineMesh/CaveMazeVolume") : null;
                var wallCount = maze != null ? maze.GetComponentsInChildren<MeshRenderer>(true).Length : 0;
                g.Score = wallCount >= 24 ? 100 : wallCount >= 12 ? 85 : wallCount > 0 ? 60 : 20;
                if (g.Score < 100)
                    g.AddIssue($"Maze volume has only {wallCount} wall renderers — rebuild with UseBlockTunnel for minable walls.");
                return g;
            }

            var root = caveRoot != null ? CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot) : null;
            if (root == null)
            {
                g.Score = 0;
                g.AddIssue("Missing BlockTunnel — enable UseBlockTunnel and rebuild.");
                return g;
            }

            var blocks = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("CaveBlock_"))
                    blocks++;
            }

            g.Score = blocks >= 350 ? 100 : blocks >= 250 ? 96 : blocks >= 180 ? 90 : blocks >= 80 ? 72 : blocks >= 30 ? 50 : blocks;
            if (blocks < 180)
                g.AddIssue($"Only {blocks} cave blocks — AAA needs ~250+. Increase CaveTunnelSegments or run another quality pass.");
            return g;
        }

        static CaveBuildStageGrade GradeGeometryIntegrity(Transform caveRoot, WorldGenerationRequest request)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "geometry_integrity",
                StageName = "Geometry integrity (closed walls, no invisible solids)",
                Weight = 24
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                g.AddIssue("Missing cave root.");
                return g;
            }

            var mainTube = caveRoot.Find("SplineMesh/MainCaveTube");
            var outerShell = caveRoot.Find("SplineMesh/MainCaveOuterShell");
            var seamlessRoot = caveRoot.Find("SeamlessTunnel");
            var occlusionShell = caveRoot.Find("OcclusionShell");
            var true3DMode = (request != null && request.UseTrue3DCaveSystem) ||
                             (seamlessRoot != null && seamlessRoot.childCount == 0);
            var blockRoot = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            var adventureShell = caveRoot.Find(
                $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}");

            var sealedRenderers = 0;
            if (mainTube != null && mainTube.GetComponent<MeshRenderer>() is { enabled: true })
                sealedRenderers++;
            if (outerShell != null && outerShell.GetComponent<MeshRenderer>() is { enabled: true })
                sealedRenderers++;
            if (seamlessRoot != null)
                sealedRenderers += seamlessRoot.GetComponentsInChildren<MeshRenderer>(true).Length;
            if (blockRoot != null)
                sealedRenderers += blockRoot.GetComponentsInChildren<MeshRenderer>(true).Length;

            var invisibleSolidColliders = CavePlayabilityValidator.CountInvisibleSolidColliders(caveRoot);
            var openCeilingSamples = CavePlayabilityValidator.CountOpenCeilingSamples(caveRoot, samples: 16);

            var score = 100;
            if (true3DMode)
            {
                var mazeRoot = caveRoot.Find("SplineMesh/CaveMazeVolume");
                var mazeWalls = mazeRoot != null ? mazeRoot.GetComponentsInChildren<MeshRenderer>(true).Length : 0;
                if (adventureShell != null)
                    mazeWalls += adventureShell.GetComponentsInChildren<MeshRenderer>(true).Length;

                var mazeColliders = mazeRoot != null ? mazeRoot.GetComponentsInChildren<Collider>(true).Length : 0;
                if (adventureShell != null)
                    mazeColliders += adventureShell.GetComponentsInChildren<Collider>(true).Length;

                var hybridBlocks = CountCaveBlocks(caveRoot);
                var walkFloors = CaveAdventurePlayabilityPipeline.CountWalkFloors(caveRoot);
                var pathPlatforms = CountPathPlatforms(caveRoot);
                var compactRoute = IsCompactRouteBuild(caveRoot, out _, out _);
                var hybridEnclosed = (compactRoute && pathPlatforms >= 6 && hybridBlocks >= 20) ||
                                     (hybridBlocks >= 120 && (mazeWalls >= 8 || walkFloors >= 8));
                if (mazeWalls >= 24 || hybridEnclosed)
                {
                    sealedRenderers += mazeWalls;
                    if (mazeColliders < 12 && !hybridEnclosed)
                    {
                        score -= 40;
                        g.AddIssue($"Maze volume missing walk colliders ({mazeColliders} colliders on {mazeWalls} walls).");
                        g.AddFix("Rebuild cave or run Fix Cave Colliders — playability pass was stripping SplineMesh colliders.");
                    }
                }
                else
                {
                    var mainMesh = mainTube != null ? mainTube.GetComponent<MeshFilter>()?.sharedMesh : null;
                    var outerMesh = outerShell != null ? outerShell.GetComponent<MeshFilter>()?.sharedMesh : null;
                    var mainTris = mainMesh != null ? mainMesh.triangles.Length / 3 : 0;
                    var outerTris = outerMesh != null ? outerMesh.triangles.Length / 3 : 0;
                    if (mazeWalls < 12 && (mainTris < 600 || outerTris < 600))
                    {
                        score -= 55;
                        g.AddIssue($"True3D/maze shell insufficient (maze walls {mazeWalls}, inner {mainTris} tris, outer {outerTris} tris).");
                        g.AddFix("Rebuild maze volume cave or increase shell fidelity.");
                    }
                }
            }
            else if (sealedRenderers < 80)
            {
                score -= 55;
                g.AddIssue($"Too few visible tunnel wall renderers ({sealedRenderers}). Cave appears open.");
                g.AddFix("Build SeamlessTunnel + BlockTunnel layers and keep MainCaveTube visible.");
            }

            if (invisibleSolidColliders > 0)
            {
                score -= Mathf.Min(70, invisibleSolidColliders * 3);
                g.AddIssue($"{invisibleSolidColliders} invisible solid collider(s) found.");
                g.AddFix("Remove hidden colliders except WalkFloor and SpawnGroundPad.");
            }

            if (openCeilingSamples > 2)
            {
                score -= Mathf.Min(40, openCeilingSamples * 4);
                g.AddIssue($"{openCeilingSamples}/16 upward samples are open to void/sky.");
                g.AddFix("Increase seamless closure + sky seal coverage along the spline.");
            }

            var mazeVolume = caveRoot.Find("SplineMesh/CaveMazeVolume");
            var routePlatforms = CountPathPlatforms(caveRoot);
            if (mazeVolume == null && mainTube == null && adventureShell == null && routePlatforms < 6)
            {
                score -= 30;
                g.AddIssue("Missing cave geometry (AdventureShell, CaveMazeVolume, or MainCaveTube).");
            }

            if (true3DMode && mazeVolume == null && outerShell == null && adventureShell == null &&
                routePlatforms < 6)
            {
                score -= 35;
                g.AddIssue("True3D mode missing enclosed volume (AdventureShell, CaveMazeVolume, or MainCaveOuterShell).");
                g.AddFix("Rebuild maze volume or inner + outer cave shell.");
            }

            if (true3DMode && ((seamlessRoot != null && seamlessRoot.childCount > 0) ||
                               (occlusionShell != null && occlusionShell.childCount > 0)))
            {
                score -= 35;
                g.AddIssue("True3D mode still contains layered fallback closure geometry.");
                g.AddFix("Remove SeamlessTunnel/OcclusionShell geometry in true 3D mode.");
            }

            if (!true3DMode && (seamlessRoot == null || seamlessRoot.childCount < 4))
            {
                score -= 25;
                g.AddIssue("SeamlessTunnel closure modules missing/too sparse.");
            }

            var shellAudit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            if (shellAudit.HasAdventureShell)
                score = 0;
            else
            {
                if (shellAudit.LayeredSlabCount > 0)
                    score -= Mathf.Min(60, shellAudit.LayeredSlabCount * 12);
                if (shellAudit.LegacySplineLayerCount > 0)
                    score -= Mathf.Min(50, shellAudit.LegacySplineLayerCount * 15);
                if (shellAudit.VisibleFlatPlatformCount > 4)
                    score -= Mathf.Min(35, shellAudit.VisibleFlatPlatformCount * 5);
            }

            g.Score = Mathf.Clamp(score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeLayoutIntegrity(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "layout_integrity",
                StageName = "Layout solution path",
                Weight = 10
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                g.AddIssue("Missing cave root.");
                return g;
            }

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
            {
                g.Score = 50;
                g.AddIssue("Missing CaveBuildMetadata — cannot verify maze solution path.");
                return g;
            }

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            if (layout?.SolutionPath == null || layout.SolutionPath.Count < 6)
            {
                g.Score = 20;
                g.AddIssue("Solution path too short or missing.");
                return g;
            }

            g.Score = 100;
            var prev = layout.SolutionPath[0];
            for (var i = 1; i < layout.SolutionPath.Count; i++)
            {
                var cur = layout.SolutionPath[i];
                var dist = Mathf.Abs(cur.x - prev.x) + Mathf.Abs(cur.y - prev.y);
                if (dist > 1)
                {
                    g.Score -= 25;
                    g.AddIssue($"Solution path gap between ({prev.x},{prev.y}) and ({cur.x},{cur.y}).");
                    break;
                }

                prev = cur;
            }

            if (layout.JumpGapCells != null)
            {
                foreach (var gap in layout.JumpGapCells)
                {
                    var onPath = false;
                    foreach (var cell in layout.SolutionPath)
                    {
                        if (cell.x == gap.x && cell.y == gap.y)
                        {
                            onPath = true;
                            break;
                        }
                    }

                    if (!onPath)
                    {
                        g.Score -= 15;
                        g.AddIssue($"Jump gap ({gap.x},{gap.y}) not on solution path.");
                    }
                }
            }

            var finish = layout.SolutionPath[layout.SolutionPath.Count - 1];
            var markers = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveLayoutPrototypeGenerator.MarkersRootName}");
            if (markers != null)
            {
                var finishMarker = markers.Find($"Marker_Finish_{finish.x}_{finish.y}");
                if (finishMarker == null)
                {
                    g.Score -= 8;
                    g.AddIssue("Finish marker missing for solution endpoint.");
                }
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var routeFloor = geometry != null ? geometry.Find(CaveEnclosureShellBuilder.FloorRootName) : null;
            var floorMesh = routeFloor != null ? routeFloor.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (floorMesh != null)
            {
                var bounds = floorMesh.bounds;
                var worldLen = Mathf.Max(bounds.size.x, bounds.size.z);
                var uvs = floorMesh.uv;
                if (uvs != null && uvs.Length > 0 && worldLen > 8f)
                {
                    var minV = float.MaxValue;
                    var maxV = float.MinValue;
                    var minU = float.MaxValue;
                    var maxU = float.MinValue;
                    foreach (var uv in uvs)
                    {
                        minV = Mathf.Min(minV, uv.y);
                        maxV = Mathf.Max(maxV, uv.y);
                        minU = Mathf.Min(minU, uv.x);
                        maxU = Mathf.Max(maxU, uv.x);
                    }

                    var spanV = maxV - minV;
                    var spanU = maxU - minU;
                    var tilesAlong = worldLen * 0.25f;
                    if (tilesAlong > spanV * 2f && spanV < 2f)
                    {
                        g.Score -= 12;
                        g.AddIssue(
                            $"RouteTerrainFloor UV span {spanV:F2} along path vs ~{tilesAlong:F0} tiles for {worldLen:F0}m mesh (stretched floor texture).");
                        g.AddFix("Rebuild RouteTerrainFloor (arc-length UVs) via Build Complete Cave or visual_shell meat fix.");
                    }

                    var worldWidth = Mathf.Min(bounds.size.x, bounds.size.z);
                    if (worldWidth > 2f && spanU < 0.35f)
                    {
                        g.Score -= 6;
                        g.AddIssue(
                            $"RouteTerrainFloor UV span {spanU:F2} across width vs ~{worldWidth * 0.25f:F1} tiles for {worldWidth:F1}m (narrow UV strip).");
                    }
                }
            }

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeEnclosurePolicy(Transform caveRoot, bool layoutPrototype = false)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "enclosure_policy",
                StageName = "Floor + ceiling policy",
                Weight = 10
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                return g;
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
            {
                g.Score = layoutPrototype ? 70 : 30;
                g.AddIssue("Missing CaveGeometry root.");
                return g;
            }

            var floorRoots = 0;
            var ceilingRoots = 0;
            var extraBands = 0;
            foreach (Transform child in geometry)
            {
                if (child.name == CaveEnclosureShellBuilder.FloorRootName ||
                    child.name == CaveLayoutPrototypeGenerator.FlatFloorRootName)
                    floorRoots++;
                if (child.name == CaveEnclosureShellBuilder.CeilingRootName)
                    ceilingRoots++;
                if (child.name.Contains("Ceiling") && child.name != CaveEnclosureShellBuilder.CeilingRootName)
                    extraBands++;
            }

            if (layoutPrototype)
            {
                g.Score = floorRoots == 1 && ceilingRoots == 0 && extraBands == 0 ? 100 : 55;
                if (ceilingRoots > 0)
                    g.AddIssue("Layout prototype must not include RouteTerrainCeiling — sculpt in Terrain.");
                if (floorRoots > 1)
                    g.AddIssue("Multiple floor roots — horizontal onion risk.");
                return g;
            }

            g.Score = 100;
            if (floorRoots != 1)
            {
                g.Score -= floorRoots == 0 ? 40 : 25;
                g.AddIssue($"Expected 1 floor root, found {floorRoots}.");
            }

            if (ceilingRoots != 1)
            {
                g.Score -= ceilingRoots == 0 ? 35 : 25;
                g.AddIssue($"Expected 1 ceiling root, found {ceilingRoots}.");
            }

            if (extraBands > 0)
            {
                g.Score -= Mathf.Min(40, extraBands * 15);
                g.AddIssue($"{extraBands} extra horizontal ceiling band(s) under CaveGeometry.");
            }

            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);
            if (audit.StackedCeilingSlabCount > 0)
            {
                g.Score = Mathf.Min(g.Score, 25);
                g.AddIssue($"{audit.StackedCeilingSlabCount} stacked ceiling renderer(s).");
            }

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradePlayability(Transform caveRoot, bool relaxed = false)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "playability",
                StageName = "Jump gaps traversable",
                Weight = 8
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                return g;
            }

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
            {
                g.Score = relaxed ? 85 : 60;
                g.AddIssue("No metadata — skipped jump raycasts.");
                return g;
            }

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            if (layout?.JumpGapCells == null || layout.JumpGapCells.Count == 0)
            {
                g.Score = 95;
                return g;
            }

            var failed = 0;
            foreach (var gap in layout.JumpGapCells)
            {
                var world = caveRoot.TransformPoint(layout.GetFloorSurfaceLocal(gap.x, gap.y));
                var from = world + Vector3.up * 0.4f;
                var to = from + Vector3.down * 6f;
                if (!Physics.Raycast(from, Vector3.down, out var hit, 6f))
                {
                    failed++;
                    continue;
                }

                var clearance = world.y - hit.point.y;
                if (clearance > 2.8f)
                    failed++;
            }

            g.Score = failed == 0 ? 100 : failed == 1 ? 78 : Mathf.Max(20, 100 - failed * 22);
            if (failed > 0)
                g.AddIssue($"{failed} jump gap(s) lack walkable landing (raycast).");
            if (!relaxed && failed > 1)
                g.AddFix("Run Fix Cave Playability or rebuild walkways along jump cells.");

            return g;
        }

        static CaveBuildStageGrade GradePerformance(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "performance",
                StageName = "Renderer / triangle budget",
                Weight = 5
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                return g;
            }

            var renderers = 0;
            var tris = 0;
            foreach (var mf in caveRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null)
                    continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled)
                    continue;
                renderers++;
                tris += mf.sharedMesh.triangles.Length / 3;
            }

            g.Score = 100;
            if (tris > 180_000)
            {
                g.Score -= 35;
                g.AddIssue($"Cave mesh budget high ({tris} triangles).");
            }
            else if (tris > 120_000)
            {
                g.Score -= 15;
                g.AddIssue($"Cave mesh budget elevated ({tris} triangles).");
            }

            if (renderers > 600)
            {
                g.Score -= 20;
                g.AddIssue($"{renderers} enabled renderers — trim shell blocks or hidden slabs.");
            }
            else if (renderers > 350)
            {
                g.Score -= 8;
                g.AddIssue($"{renderers} enabled renderers — consider fewer minable blocks.");
            }

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradePackagingReadiness(
            Transform caveRoot,
            SceneGroundInfo ground,
            LavaTubeCaveBuildReport buildReport)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "packaging_readiness",
                StageName = "Packaging / first-minute play",
                Weight = 6
            };

            g.Score = 100;
            if (caveRoot == null)
            {
                g.Score = 0;
                g.AddIssue("No cave root.");
                return g;
            }

            if (!CaveAdventurePlayabilityPipeline.CheckSpawnReachability(caveRoot))
            {
                g.Score -= 35;
                g.AddIssue("Spawn reachability failed — first minute not trustworthy.");
            }

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
            {
                g.Score -= 30;
                g.AddIssue("CaveEntrance_SpawnPoint missing.");
            }
            else if (spawn.Find(CaveSpawnPadUtility.PadName) == null)
            {
                g.Score -= 20;
                g.AddIssue("SpawnGroundPad missing under entrance spawn.");
            }

            if (CaveBuildCompileGate.HasBlockingErrors())
            {
                g.Score -= 40;
                g.AddIssue("Compile errors block commercial ship.");
            }

            var surfaceSpawn = GameObject.Find("PlayerSpawnPoint");
            if (surfaceSpawn == null && GameObject.FindWithTag("PlayerSpawn") == null)
            {
                g.Score -= 15;
                g.AddIssue("No surface PlayerSpawnPoint — packaged app needs surface respawn.");
            }

            if (buildReport != null && !buildReport.NavMeshBuilt)
            {
                g.Score -= 15;
                g.AddIssue("NavMesh not built — AI/mobs may fail in ship build.");
            }

            if (ground != null && ground.HasAnchor &&
                Mathf.Abs(CaveGroundPlacementUtility.MeasureRootDepthError(caveRoot, ground)) >
                CaveGroundPlacementUtility.MaxVerticalErrorMeters)
            {
                g.Score -= 25;
                g.AddIssue("Cave root depth error — world integration not ship-ready.");
            }

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeExportArtifacts(CaveBuildQualityReport report)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "export_artifacts",
                StageName = "Blueprint + quality JSON",
                Weight = 4
            };

            g.Score = 50;
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var qualityPath = Path.Combine(projectRoot, report.ExportPath);
            if (File.Exists(qualityPath))
                g.Score += 25;

            var blueprint = Path.Combine(projectRoot, CaveLayoutPrototypeGenerator.ExportPath);
            if (File.Exists(blueprint) || !report.LayoutPrototypeMode)
                g.Score += report.LayoutPrototypeMode ? 25 : 25;

            if (!File.Exists(qualityPath))
                g.AddIssue("CaveBuildQualityReport.json not written yet.");
            if (report.LayoutPrototypeMode && !File.Exists(blueprint))
                g.AddIssue("CaveLayoutBlueprint.json missing for layout prototype.");

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeModeConsistency(Transform caveRoot, bool layoutPrototype)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "mode_consistency",
                StageName = "Build mode consistency",
                Weight = 8
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                return g;
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var hasLayoutFloor = geometry != null &&
                                 geometry.Find(CaveLayoutPrototypeGenerator.FlatFloorRootName) != null;
            var hasRouteCeiling = geometry != null &&
                                  geometry.Find(CaveEnclosureShellBuilder.CeilingRootName) != null;
            var blockTunnel = CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot);
            var hasBlocks = blockTunnel != null && blockTunnel.childCount > 0;
            var audit = CaveBuildVisualShellAuditor.Audit(caveRoot);

            if (layoutPrototype)
            {
                g.Score = 100;
                if (hasRouteCeiling || audit.StackedCeilingSlabCount > 0)
                {
                    g.Score = 20;
                    g.AddIssue("Layout prototype must NOT include RouteTerrainCeiling / ceiling slabs.");
                }

                if (hasBlocks)
                {
                    g.Score = Mathf.Min(g.Score, 30);
                    g.AddIssue("Layout prototype must NOT include BlockTunnel blocks.");
                }

                if (!hasLayoutFloor)
                {
                    g.Score = Mathf.Min(g.Score, 40);
                    g.AddIssue("Missing LayoutWalkFloor — not a layout prototype build.");
                }

                return g;
            }

            g.Score = 100;
            if (hasLayoutFloor && !hasRouteCeiling && !hasBlocks)
            {
                g.Score = 25;
                g.AddIssue("Full build still has LayoutWalkFloor-only — run Build Complete Cave Level.");
                g.AddFix("Remove layout prototype floor and rebuild full enclosure (floor + single ceiling).");
            }

            if (audit.HasAdventureShell)
            {
                g.Score = 0;
                g.AddIssue("AdventureShell present in full build — forbidden onion stack.");
            }

            return g;
        }

        /// <summary>Underground mouth seal only — above-ground terrain is graded by <see cref="SurfaceTerrainBuildLadder"/>.</summary>
        static CaveBuildStageGrade GradeCaveMouthSeal(Transform caveRoot, SceneGroundInfo ground)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "cave_mouth_seal",
                StageName = "Underground mouth seal",
                Weight = 10
            };

            if (caveRoot == null || !CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                g.Score = 40;
                g.AddIssue("Missing UndergroundCaveSystem root for mouth seal grading.");
                return g;
            }

            if (ground == null || !ground.HasAnchor)
            {
                g.Score = 55;
                g.AddIssue("Missing ground anchor for underground mouth placement.");
                return g;
            }

            var mouthErr = CaveGroundPlacementUtility.MeasureEntranceMouthSurfaceError(caveRoot, ground);
            if (CaveGroundPlacementUtility.IsGroundPlacementAcceptable(caveRoot, ground))
            {
                g.Score = 96;
                return g;
            }

            if (Mathf.Abs(mouthErr) > CaveGroundPlacementUtility.MaxEntranceMouthSurfaceErrorMeters)
            {
                var abs = Mathf.Abs(mouthErr);
                g.Score = abs <= 5f
                    ? Mathf.Max(55, 92 - Mathf.RoundToInt(abs * 8f))
                    : Mathf.Max(20, 50 - Mathf.RoundToInt((abs - 5f) * 6f));
                g.AddIssue(
                    $"Underground mouth {mouthErr:F1}m from expected seal — snap depth-only (XZ locked).");
                g.AddFix(
                    "Cave meat-loop: CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly (not terrain sculpt).");
            }
            else
            {
                var placementErr = CaveGroundPlacementUtility.MeasureRootPlacementError(caveRoot, ground);
                g.Score = Mathf.Max(50, 88 - Mathf.RoundToInt(placementErr.magnitude * 6f));
                g.AddIssue($"Cave root placement residual {placementErr.magnitude:F1}m (mouth within tolerance).");
            }

            return g;
        }

        static CaveBuildStageGrade GradeInteriorRibs(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "interior_ribs",
                StageName = "Interior rock ribs",
                Weight = 10
            };

            var blockRoot = caveRoot != null ? CaveAdventureCaveGenerator.FindBlockTunnel(caveRoot) : null;
            var blockSections = blockRoot != null ? blockRoot.childCount : 0;
            if (blockSections >= 1)
            {
                g.Score = 95;
                return g;
            }

            var ribs = caveRoot != null ? caveRoot.Find("SplineMesh/InteriorRibs") : null;
            var count = ribs != null ? ribs.childCount : 0;
            g.Score = count >= 4 ? Mathf.Min(100, 55 + count * 3) : count * 12;
            if (count < 4)
                g.AddIssue($"Only {count} interior ribs — tube may look like empty void.");
            return g;
        }

        static CaveBuildStageGrade GradeWalkways(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "walkways",
                StageName = "Walk colliders",
                Weight = 12
            };

            if (CaveFloorSafetyUtility.UsesRouteTerrainFloor(caveRoot))
            {
                var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
                var floor = geometry != null
                    ? geometry.Find(CaveEnclosureShellBuilder.FloorRootName)
                    : null;
                var mr = floor != null ? floor.GetComponent<MeshRenderer>() : null;
                var col = floor != null ? floor.GetComponent<Collider>() : null;
                if (col != null && !col.isTrigger && mr != null && mr.enabled)
                {
                    g.Score = 100;
                    return g;
                }

                g.Score = 75;
                g.AddIssue("RouteTerrainFloor missing enabled renderer or collider.");
                return g;
            }

            var count = CaveAdventurePlayabilityPipeline.CountWalkFloors(caveRoot);

            g.Score = count >= 14 ? 100 : count >= 10 ? 94 : count >= 8 ? 88 : count * 8;
            if (count < 10)
                g.AddIssue($"Only {count} walk floors — AAA needs ~10+.");

            var visibleWalkways = CountVisibleWalkFloors(caveRoot);

            if (visibleWalkways < Mathf.Max(1, count / 2))
            {
                g.Score = Mathf.Min(g.Score, 40);
                g.AddIssue("Most walk floors are invisible; this creates 'invisible ground' feel.");
            }

            return g;
        }

        static CaveBuildStageGrade GradePlayerFloor(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "player_floor",
                StageName = "Player floor (no fall-through)",
                Weight = 14
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                g.AddIssue("Missing cave root.");
                return g;
            }

            var score = 100;
            Transform walkSurface = null;
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
            {
                walkSurface = geometry.Find(CaveEnclosureShellBuilder.FloorRootName) ??
                              geometry.Find(CaveLayoutPrototypeGenerator.FlatFloorRootName);
            }

            if (walkSurface == null)
            {
                score = 25;
                g.AddIssue("No RouteTerrainFloor or LayoutWalkFloor under CaveGeometry.");
                g.AddFix("Run Build Complete Cave — ensure CaveEnclosureShellBuilder builds floor mesh.");
            }
            else
            {
                var col = walkSurface.GetComponent<Collider>();
                var mr = walkSurface.GetComponent<MeshRenderer>();
                if (col == null || col.isTrigger)
                {
                    score -= 45;
                    g.AddIssue("Walk surface missing non-trigger collider (player falls through).");
                    g.AddFix("Run CaveFloorSafetyUtility / Fix player_floor — MeshCollider on RouteTerrainFloor.");
                }

                if (mr == null || !mr.enabled)
                {
                    score -= 20;
                    g.AddIssue("Walk surface renderer disabled — colliders may be stripped.");
                }
            }

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            if (spawn == null)
            {
                score = Mathf.Min(score, 30);
                g.AddIssue("Missing CaveEntrance_SpawnPoint.");
            }
            else
            {
                var pad = spawn.Find(CaveSpawnPadUtility.PadName);
                if (pad == null || pad.GetComponent<Collider>() == null)
                {
                    score -= 25;
                    g.AddIssue("Missing SpawnGroundPad box collider under entrance spawn.");
                    g.AddFix("CaveSpawnPadUtility.EnsureUnderSpawn on build.");
                }

                if (!CavePlayabilityValidator.CheckEntranceSpawnGrounded(caveRoot))
                {
                    score = Mathf.Min(score, 35);
                    g.AddIssue("Entrance spawn has no walkable ground within raycast range (fall-through in Play Mode).");
                    g.AddFix("Align spawn to maze start + SnapSpawnToWalkSurface; protect RouteTerrainFloor collider.");
                }
            }

            g.Score = Mathf.Clamp(score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeEnclosure(Transform caveRoot, LavaTubeCaveBuildReport build, WorldGenerationRequest request)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "enclosure",
                StageName = "Occlusion shell",
                Weight = 8
            };

            if (request != null && request.UseTrue3DCaveSystem)
            {
                var enclosureShell = caveRoot != null
                    ? caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}")
                    : null;
                var blocks = CountCaveBlocks(caveRoot);
                if (enclosureShell != null && blocks >= 80)
                {
                    g.Score = 100;
                    return g;
                }

                if (IsCompactRouteBuild(caveRoot, out var platforms, out _) && platforms >= 6 && blocks >= 20)
                {
                    g.Score = 100;
                    return g;
                }

                var maze = caveRoot != null ? caveRoot.Find("SplineMesh/CaveMazeVolume") : null;
                var mazeWalls = maze != null ? maze.GetComponentsInChildren<MeshRenderer>(true).Length : 0;
                if (mazeWalls >= 24)
                {
                    g.Score = 100;
                    return g;
                }

                var main = caveRoot != null ? caveRoot.Find("SplineMesh/MainCaveTube") : null;
                var outer = caveRoot != null ? caveRoot.Find("SplineMesh/MainCaveOuterShell") : null;
                g.Score = (main != null && outer != null) ? 100 : 35;
                if (g.Score < 100)
                    g.AddIssue("True3D enclosure incomplete (missing adventure shell, maze volume, or inner/outer shell).");
                return g;
            }

            var shell = caveRoot != null ? caveRoot.Find("OcclusionShell") : null;
            var shellCount = shell != null ? shell.childCount : build?.ShellPieceCount ?? 0;
            g.Score = shellCount >= 12 ? 100 : shellCount >= 8 ? 92 : shellCount >= 4 ? 75 : shellCount * 12;
            if (shellCount < 8)
                g.AddIssue("Thin occlusion shell — sky may leak through.");
            return g;
        }

        static CaveBuildStageGrade GradeMaterials(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "materials",
                StageName = "URP materials",
                Weight = 8
            };

            var broken = 0;
            var total = 0;
            foreach (var r in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                total++;
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name.Contains("Hidden/InternalError"))
                    broken++;
            }

            g.Score = total == 0 ? 0 : Mathf.RoundToInt(100f * (1f - broken / (float)total));
            if (broken > 0)
                g.AddIssue($"{broken} renderer(s) with missing/broken materials.");
            return g;
        }

        static CaveBuildStageGrade GradeWater(Transform caveRoot, WorldGenerationRequest request)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "water",
                StageName = "Underground water",
                Weight = 8
            };

            if (request != null && !request.IncludeCaveWater)
            {
                g.Score = 100;
                return g;
            }

            var pool = caveRoot != null ? caveRoot.Find("Water/UndergroundRiver_Pool") : null;
            if (pool == null)
            {
                g.Score = 0;
                g.AddIssue("Missing UndergroundRiver_Pool.");
                return g;
            }

            var mr = pool.GetComponent<MeshRenderer>();
            var isLava = pool.GetComponent<CaveLavaGlow>() != null ||
                         (mr != null && mr.sharedMaterial != null &&
                          CaveWaterMaterialFactory.IsCaveLavaMaterial(mr.sharedMaterial));

            if (isLava)
            {
                g.Score = 100;
                return g;
            }

            if (pool.GetComponent<CaveUndergroundWaterPool>() == null)
            {
                g.Score = 25;
                g.AddIssue("Pool missing CaveUndergroundWaterPool component.");
            }
            else
                g.Score = 70;

            if (mr != null && mr.sharedMaterial != null)
            {
                if (CaveWaterMaterialFactory.IsCaveWaterMaterial(mr.sharedMaterial))
                    g.Score += 30;
                else
                    g.AddIssue("Pool not using Ignite Simple Water Shader.");
            }

            var legacyChildren = 0;
            foreach (Transform child in pool)
            {
                if (child.name == "LavaPoolLight")
                    continue;
                legacyChildren++;
            }

            if (legacyChildren > 0)
            {
                g.Score -= 20;
                g.AddIssue("Legacy floor meshes parented under pool — purge and rebuild.");
            }

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeLighting(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "lighting",
                StageName = "Cave lighting",
                Weight = 8
            };

            var lights = caveRoot.GetComponentsInChildren<Light>(true);
            var pointCount = 0;
            foreach (var l in lights)
            {
                if (l != null && l.enabled && l.type == LightType.Point)
                    pointCount++;
            }

            g.Score = pointCount >= 8 ? 100 : pointCount >= 5 ? 94 : pointCount >= 3 ? 85 : pointCount * 20;
            if (pointCount < 5)
                g.AddIssue("Very few point lights — cave will look flat/black.");
            return g;
        }

        static CaveBuildStageGrade GradeAtmosphere(Transform caveRoot, bool relaxed = false)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "atmosphere",
                StageName = "Underground atmosphere",
                Weight = 8
            };

            var zone = caveRoot.GetComponentInChildren<CaveUndergroundAtmosphere>(true);

            if (relaxed)
            {
                g.Score = zone != null ? 100 : 80;
                if (zone == null)
                    g.AddIssue("Atmosphere zone optional for layout prototype.");
                return g;
            }

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                g.Score = zone != null ? 100 : 35;
                if (zone == null)
                {
                    g.AddIssue("Missing CaveUndergroundAtmosphere trigger.");
                    g.AddFix("Run Fix Cave Playability or rebuild — adventure uses a single atmosphere zone (no stacked URP Volume).");
                }

                return g;
            }

            var volume = caveRoot.GetComponentInChildren<Volume>(true);
            g.Score = 30;
            if (zone != null)
                g.Score += 40;
            if (volume != null)
                g.Score += 30;
            if (zone == null)
                g.AddIssue("Missing CaveUndergroundAtmosphere trigger.");
            return g;
        }

        static CaveBuildStageGrade GradeNavMesh(LavaTubeCaveBuildReport build, bool relaxed = false)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "navmesh",
                StageName = "NavMesh bake",
                Weight = 6
            };

            var baked = build != null && build.NavMeshBuilt;
            if (!baked)
                baked = NavMeshHasWalkableData();

            g.Score = baked ? 100 : relaxed ? 70 : 30;
            if (g.Score < 100)
                g.AddIssue("NavMesh bake failed — check walk floors (RouteTerrainFloor / LayoutWalkFloor).");
            return g;
        }

        static bool NavMeshHasWalkableData()
        {
            var tri = NavMesh.CalculateTriangulation();
            return tri.vertices != null && tri.vertices.Length >= 3;
        }

        static CaveBuildStageGrade GradePortalSpawn(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "portal",
                StageName = "Portal + spawn",
                Weight = 8
            };

            var spawn = caveRoot.Find("Entrance/CaveEntrance_SpawnPoint");
            var marker = caveRoot.Find("Entrance/CaveEntrance_Marker");
            var portal = GameObject.Find("PortalFive") ?? GameObject.Find("MainScene_CavePortal");
            g.Score = 30;
            if (spawn != null)
                g.Score += 35;
            if (marker != null)
                g.Score += 20;
            if (portal != null)
                g.Score += 15;
            if (spawn == null)
                g.AddIssue("Missing CaveEntrance_SpawnPoint.");
            if (portal == null)
                g.AddIssue("PortalFive / MainScene_CavePortal not found in scene.");
            return g;
        }

        static CaveBuildStageGrade GradeMobSpawns(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "mob_spawns",
                StageName = "Enemy spawn coverage",
                Weight = 8
            };

            if (caveRoot == null)
            {
                g.Score = 0;
                g.AddIssue("Missing cave root.");
                return g;
            }

            var spawners = caveRoot.GetComponentsInChildren<CaveMobSpawner>(true);
            if (spawners == null || spawners.Length == 0)
            {
                g.Score = 0;
                g.AddIssue("No CaveMobSpawner objects found.");
                return g;
            }

            var fallbackReady = 0;
            var totalSpawnCount = 0;
            foreach (var s in spawners)
            {
                if (s == null)
                    continue;
                totalSpawnCount += Mathf.Max(0, s.spawnCount);
                if (s.enemyPrefab != null || s.spawnCount > 0)
                    fallbackReady++;
            }

            g.Score = 40;
            if (spawners.Length >= 3)
                g.Score += 25;
            else if (spawners.Length >= 1)
                g.Score += 12;

            if (totalSpawnCount >= 8)
                g.Score += 20;
            else if (totalSpawnCount >= 3)
                g.Score += 18;
            else if (totalSpawnCount >= 1)
                g.Score += 8;
            if (fallbackReady == spawners.Length)
                g.Score += 15;
            if (spawners.Length < 3)
                g.AddIssue($"Only {spawners.Length} spawn point(s); target 3+.");

            g.Score = Mathf.Clamp(g.Score, 0, 100);
            return g;
        }

        static CaveBuildStageGrade GradeSpawnReachability(Transform caveRoot)
        {
            var g = new CaveBuildStageGrade
            {
                StageId = "spawn_reachability",
                StageName = "Spawn reachability",
                Weight = 8
            };

            var reachable = CavePlayabilityValidator.CheckSpawnReachability(caveRoot);
            g.Score = reachable ? 100 : 20;
            if (!reachable)
            {
                g.AddIssue("Entrance spawn cannot reliably path to cave walk floors.");
                g.AddFix("Rebuild walkways and align entrance spawn onto nearest cave walk floor.");
            }

            return g;
        }

        static int CountVisibleWalkFloors(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var visible = 0;
            var walk = caveRoot.Find("Walkways");
            if (walk != null)
            {
                foreach (Transform c in walk)
                {
                    if (!c.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                        continue;
                    var mr = c.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                        visible++;
                }
            }

            var shell = caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}");
            if (shell != null)
            {
                foreach (var mr in shell.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr == null || !mr.enabled)
                        continue;
                    var n = mr.gameObject.name;
                    if (n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix) || n.Contains("Floor") ||
                        n.Contains("Entrance_Floor"))
                        visible++;
                }
            }

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry != null)
            {
                var routeFloor = geometry.Find(CaveEnclosureShellBuilder.FloorRootName);
                if (routeFloor != null)
                {
                    var mr = routeFloor.GetComponent<MeshRenderer>();
                    if (mr != null && mr.enabled)
                        visible += 12;
                }

                var platforms = geometry.Find(CaveAdventureBlockBuilder.PlatformsRootName);
                if (platforms != null)
                {
                    foreach (var mr in platforms.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (mr != null && mr.enabled &&
                            mr.gameObject.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                            visible++;
                    }
                }

            }

            var features = caveRoot.Find(CaveAdventureFeaturesBuilder.RootName);
            if (features != null)
            {
                foreach (var mr in features.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != null && mr.enabled &&
                        mr.gameObject.name.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                        visible++;
                }
            }

            return visible;
        }

        static bool IsCompactRouteBuild(Transform caveRoot, out int platforms, out int blocks)
        {
            platforms = CountPathPlatforms(caveRoot);
            blocks = CountCaveBlocks(caveRoot);
            var shell = caveRoot != null
                ? caveRoot.Find($"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureShellBuilder.ShellRootName}")
                : null;
            return platforms >= 6 && shell == null;
        }

        static int CountPathPlatforms(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var root = caveRoot.Find(
                $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureBlockBuilder.PlatformsRootName}");
            return root != null ? root.childCount : 0;
        }

        static int CountSolutionPathSteps(Transform caveRoot)
        {
            var meta = caveRoot?.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return 0;

            var layout = CaveMazeLayoutGenerator.Generate(
                meta.seed, meta.tunnelSegments, meta.chamberCount);
            return layout?.SolutionPath?.Count ?? 0;
        }

        static float MeasureRouteVerticalDrop(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0f;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                if (layout?.SolutionPath != null && layout.SolutionPath.Count >= 2)
                {
                    var start = layout.SolutionPath[0];
                    var end = layout.SolutionPath[layout.SolutionPath.Count - 1];
                    var startY = layout.GetFloorSurfaceLocal(start.x, start.y).y;
                    var endY = layout.GetFloorSurfaceLocal(end.x, end.y).y;
                    return startY - endY;
                }
            }

            var platforms = caveRoot.Find(
                $"{CaveGeometryPaths.GeometryRoot}/{CaveAdventureBlockBuilder.PlatformsRootName}");
            if (platforms == null || platforms.childCount < 2)
                return 0f;

            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            foreach (Transform child in platforms)
            {
                var y = child.localPosition.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }

            return maxY - minY;
        }

        static int CountCaveBlocks(Transform caveRoot)
        {
            if (caveRoot == null)
                return 0;

            var count = 0;
            foreach (var t in caveRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.StartsWith("CaveBlock_"))
                    count++;
            }

            return count;
        }

        public const int MeatLoopGradeBatchCount = 4;

        internal static CaveBuildQualityReport BeginMeatLoopGradeReport(
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request)
        {
            return new CaveBuildQualityReport
            {
                Seed = request != null ? request.Seed : 0,
                SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                AdventureMode = CaveGeometryPaths.IsAdventureCave(caveRoot),
            };
        }

        /// <summary>One editor-queue tick of stage grading (avoids stack overflow from monolithic Grade).</summary>
        internal static void AppendMeatLoopGradeBatch(
            CaveBuildQualityReport report,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            int batchIndex)
        {
            if (report == null)
                return;

            if (request != null && (request.UseLayoutPrototype ||
                                    CaveLayoutPrototypeGenerator.IsLayoutPrototypeRoot(caveRoot)))
                return;

            switch (batchIndex)
            {
                case 0:
                    report.Stages.Add(GradeSceneGround(ground));
                    report.Stages.Add(GradePath(caveRoot));
                    report.Stages.Add(GradeLayoutIntegrity(caveRoot));
                    report.Stages.Add(GradeVisualShell(caveRoot));
                    report.Stages.Add(GradeEnclosurePolicy(caveRoot));
                    break;
                case 1:
                    report.Stages.Add(GradeOrganicTube(caveRoot));
                    report.Stages.Add(GradeBlockTunnel(caveRoot, request));
                    report.Stages.Add(GradeGeometryIntegrity(caveRoot, request));
                    report.Stages.Add(GradeCaveMouthSeal(caveRoot, ground));
                    report.Stages.Add(GradeInteriorRibs(caveRoot));
                    report.Stages.Add(GradeWalkways(caveRoot));
                    report.Stages.Add(GradePlayerFloor(caveRoot));
                    break;
                case 2:
                    report.Stages.Add(GradeEnclosure(caveRoot, buildReport, request));
                    report.Stages.Add(GradeMaterials(caveRoot));
                    report.Stages.Add(GradeWater(caveRoot, request));
                    report.Stages.Add(GradeLighting(caveRoot));
                    report.Stages.Add(GradeAtmosphere(caveRoot));
                    break;
                case 3:
                    report.Stages.Add(GradeNavMesh(buildReport));
                    report.Stages.Add(GradePortalSpawn(caveRoot));
                    report.Stages.Add(GradeSpawnReachability(caveRoot));
                    report.Stages.Add(GradeMobSpawns(caveRoot));
                    report.Stages.Add(GradePlayability(caveRoot));
                    report.Stages.Add(GradePerformance(caveRoot));
                    report.Stages.Add(GradeExportArtifacts(report));
                    report.Stages.Add(GradePackagingReadiness(caveRoot, ground, buildReport));
                    report.Stages.Add(GradeModeConsistency(caveRoot, layoutPrototype: false));
                    break;
            }
        }

        internal static CaveBuildStageGrade GradeStageById(
            string stageId,
            Transform caveRoot,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            LavaTubeCaveBuildReport buildReport,
            CaveBuildQualityReport report) =>
            stageId switch
            {
                "ground" => GradeSceneGround(ground),
                "path" => GradePath(caveRoot),
                "layout_integrity" => GradeLayoutIntegrity(caveRoot),
                "visual_shell" => GradeVisualShell(caveRoot),
                "enclosure_policy" => GradeEnclosurePolicy(caveRoot),
                "organic_mesh" => GradeOrganicTube(caveRoot),
                "block_tunnel" => GradeBlockTunnel(caveRoot, request),
                "geometry_integrity" => GradeGeometryIntegrity(caveRoot, request),
                "cave_mouth_seal" => GradeCaveMouthSeal(caveRoot, ground),
                "terrain_integration" => GradeCaveMouthSeal(caveRoot, ground),
                "interior_ribs" => GradeInteriorRibs(caveRoot),
                "walkways" => GradeWalkways(caveRoot),
                "player_floor" => GradePlayerFloor(caveRoot),
                "enclosure" => GradeEnclosure(caveRoot, buildReport, request),
                "materials" => GradeMaterials(caveRoot),
                "water" => GradeWater(caveRoot, request),
                "lighting" => GradeLighting(caveRoot),
                "atmosphere" => GradeAtmosphere(caveRoot),
                "navmesh" => GradeNavMesh(buildReport),
                "portal" => GradePortalSpawn(caveRoot),
                "spawn_reachability" => GradeSpawnReachability(caveRoot),
                "mob_spawns" => GradeMobSpawns(caveRoot),
                "playability" => GradePlayability(caveRoot),
                "performance" => GradePerformance(caveRoot),
                "export_artifacts" => GradeExportArtifacts(report),
                "packaging_readiness" => GradePackagingReadiness(caveRoot, ground, buildReport),
                "mode_consistency" => GradeModeConsistency(caveRoot, layoutPrototype: false),
                _ => null,
            };
    }
}
