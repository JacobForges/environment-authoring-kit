#if UNITY_EDITOR
using System;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Single build-session contract — wizard finalizes once per new build; checkpoints read this until cleared.
    /// Replaces opaque concept presets for bots and humans.
    /// </summary>
    public static class CaveBuildSessionConfig
    {
        public const int CurrentVersion = 2;
        /// <summary>Minimum FullWorld scope — 3×3 play disk only (no outer rings).</summary>
        public const int PlayDiskTerrainTileCount = 9;
        public const string ActiveRelPath = "Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json";
        public const string WizardStateRelPath = "Assets/EnvironmentKit/Generated/CaveBuildWizardState.json";
        public const string PlannerBriefRelPath = CaveBuildPlannerLayoutBridge.PlannerBriefRelPath;

        [Serializable]
        public sealed class Doc
        {
            public int version = CurrentVersion;
            public string finalizedUtc;
            public string label = "Custom build";
            public int tileCount = 9;
            public bool randomSeedEachBuild = true;
            public int seed;
            public bool playDiskProps = true;
            public bool hollowTitan;
            public bool enhancementPhases;
            public bool prePlacementResearch = true;
            public bool sequentialTerrain;
            public int surfaceTerrainPasses = 6;
            public bool outerRingMountains = true;
            public bool mountainLabyrinth;
            public bool surfaceTrails = true;
            public bool surfaceWater;
            public bool agentInvokes = true;
            public bool runPostBuildResearch = true;
            public bool preBuildReloop = true;
            /// <summary>Seed-driven wilderness / mouth / maze variation (outer rings only).</summary>
            public bool randomLandPlacement = true;
            /// <summary>Allow wilderness tiles at varying Y without grid consolidation (play disk stays flat 3×3).</summary>
            public bool floatingTiles;
            /// <summary>CC0 prefabs + biome 3D scatter (trees, rocks, grass meshes).</summary>
            public bool import3DObjects = true;
            /// <summary>AAA content tier — full NPC/loot/landmarks pipeline.</summary>
            public bool proLevelWorld;
            /// <summary>Enclosed spline/block true 3D cave geometry (recommended).</summary>
            public bool use3DCaveSystem = true;
        }

        [Serializable]
        sealed class WizardStateDoc
        {
            public string phase = "idle";
            public string hubRoot;
            public string openedUtc;
            public bool cancelled;
        }

        static Doc _active;

        public static Doc Active => _active ??= LoadActive() ?? CreateDefaults();

        public static bool HasFinalizedActive => LoadActive() != null;

        /// <summary>
        /// Planner handoff on disk — <see cref="Doc.finalizedUtc"/> set by wizard approve/finalize.
        /// Survives session scratch reset (wizard state JSON is ephemeral).
        /// </summary>
        public static bool HasApprovedPlannerSession()
        {
            var doc = LoadActive();
            return doc != null && !string.IsNullOrWhiteSpace(doc.finalizedUtc);
        }

        public static int PreviewTileCount()
        {
            if (!HasFinalizedActive)
                return CreateDefaults().tileCount;

            var doc = LoadActive();
            if (doc == null)
                return CreateDefaults().tileCount;

            if (IsPlayDiskOnlyDemo(doc))
                return PlayDiskTerrainTileCount;

            if (IsFloatingIslandsDemo(doc))
                return SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount;

            return doc.tileCount;
        }

        public static Doc CreateDefaults() =>
            new Doc
            {
                label = "Speed walkable demo",
                tileCount = 9,
                randomSeedEachBuild = true,
                playDiskProps = true,
                hollowTitan = false,
                enhancementPhases = false,
                prePlacementResearch = false,
                sequentialTerrain = false,
                surfaceTerrainPasses = 4,
                outerRingMountains = true,
                mountainLabyrinth = false,
                surfaceTrails = true,
                surfaceWater = false,
                agentInvokes = false,
                runPostBuildResearch = false,
                preBuildReloop = false,
                randomLandPlacement = true,
                floatingTiles = false,
                import3DObjects = true,
                proLevelWorld = false,
                use3DCaveSystem = true,
            };

        /// <summary>Hub scratch path — skip browser planner and use <see cref="CreateDefaults"/> (9-tile speed demo).</summary>
        public static bool UseScratchDefaultsWithoutPlanner =>
            string.Equals(
                Environment.GetEnvironmentVariable("CAVE_USE_BUILD_DEFAULTS"),
                "1",
                StringComparison.Ordinal);

        public static Doc LoadActive()
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, ActiveRelPath);
            if (!File.Exists(abs))
                return null;

            try
            {
                var raw = JsonUtility.FromJson<Doc>(File.ReadAllText(abs));
                if (raw == null || raw.version <= 0)
                    return null;

                var beforeTiles = raw.tileCount;
                var beforeFloating = raw.floatingTiles;
                var doc = NormalizeDoc(raw);
                if (doc.tileCount != beforeTiles || doc.floatingTiles != beforeFloating)
                {
                    Debug.LogWarning(
                        $"[CaveBuild] Normalized planner session scope — tileCount {beforeTiles}→{doc.tileCount}, " +
                        $"floatingTiles={beforeFloating}→{doc.floatingTiles}.");
                    SaveActive(doc);
                }

                return doc;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Repair inconsistent planner JSON (e.g. tileCount=81 + floatingTiles=true → 13).</summary>
        public static Doc NormalizeDoc(Doc doc)
        {
            if (doc == null)
                return null;

            doc.tileCount = NormalizeTileCount(doc.tileCount, doc.floatingTiles);
            if (doc.tileCount == PlayDiskTerrainTileCount)
                doc.floatingTiles = false;
            else if (doc.tileCount == SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount)
                doc.floatingTiles = true;
            else
                doc.floatingTiles = false;

            doc.surfaceTerrainPasses = Mathf.Clamp(doc.surfaceTerrainPasses, 2, 12);

            // Floating-islands social demo — match build_planner.py _normalize_config (8–16 GB safe pacing).
            if (doc.tileCount == SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount &&
                doc.floatingTiles &&
                !doc.proLevelWorld)
            {
                doc.sequentialTerrain = true;
                doc.enhancementPhases = false;
                doc.prePlacementResearch = false;
                doc.preBuildReloop = false;
                doc.outerRingMountains = false;
                doc.mountainLabyrinth = false;
                doc.agentInvokes = false;
                doc.runPostBuildResearch = false;
                doc.hollowTitan = false;
                doc.surfaceTerrainPasses = Mathf.Min(4, doc.surfaceTerrainPasses);
            }

            return doc;
        }

        public static void SaveActive(Doc doc)
        {
            if (doc == null)
                return;

            doc.version = CurrentVersion;
            if (string.IsNullOrEmpty(doc.finalizedUtc))
                doc.finalizedUtc = DateTime.UtcNow.ToString("o");

            NormalizeDoc(doc);
            _active = doc;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var abs = Path.Combine(hub, ActiveRelPath);
            File.WriteAllText(abs, JsonUtility.ToJson(doc, true) + "\n");
        }

        public static void ClearActive()
        {
            _active = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, ActiveRelPath);
            if (File.Exists(abs))
            {
                try
                {
                    File.Delete(abs);
                }
                catch
                {
                    // advisory
                }
            }
        }

        public static void ApplyToRequest(WorldGenerationRequest request, Doc doc = null)
        {
            if (request == null)
                return;

            doc ??= Active;
            request.SurfaceScope = SurfaceBuildScope.FullWorld;
            request.ConceptLayoutIndex = -1;
            request.GenerationStyleId = "session_config";
            request.ForceNineTileSquareGrid = true;
            request.UseExtendedOpenWorldGrid = doc.tileCount > 81;
            request.RunEnhancementPhases = doc.enhancementPhases || doc.proLevelWorld;
            request.ContentTier = doc.proLevelWorld
                ? WorldBuildContentTier.Aaa
                : doc.tileCount <= 81
                    ? WorldBuildContentTier.Light
                    : WorldBuildContentTier.Standard;
            request.SurfaceTerrainBuildPasses = Mathf.Clamp(doc.surfaceTerrainPasses, 2, 12);
            request.SurfaceIncludeTrails = doc.surfaceTrails;
            request.SurfaceIncludeWater = doc.surfaceWater;
            request.SurfaceIncludeMountains = true;
            request.UseOuterRingMountains = doc.outerRingMountains;
            request.SurfaceIncludeMountainLabyrinth = doc.mountainLabyrinth || doc.proLevelWorld;
            request.UseTombRaiderLabyrinthCadence = false;
            request.UsePeakSummitCap = doc.tileCount > 81 || doc.proLevelWorld;
            request.UseMountainWildernessCaveMouth = true;
            request.UseTrue3DCaveSystem = doc.use3DCaveSystem;
            request.SatelliteCaveCount = doc.proLevelWorld ? 3 : doc.tileCount <= 81 ? 1 : 3;
            request.PropEmphasis = doc.playDiskProps ? "speed_playable_demo" : string.Empty;

            if (IsPlayDiskOnlyDemo(doc) || IsFloatingIslandsDemo(doc))
            {
                request.UseOuterRingMountains = false;
                request.SurfaceIncludeMountains = false;
                request.SurfaceIncludeMountainLabyrinth = false;
                request.UsePeakSummitCap = false;
                request.UseMountainWildernessCaveMouth = false;
                request.RunEnhancementPhases = false;
                request.SatelliteCaveCount = 0;
            }

            if (doc.randomLandPlacement)
            {
                request.SurfaceTileLayoutVariant = -1;
                request.PreferredCaveOpeningSector = -1;
                request.MazeGenFlavor = -1;
            }
            else
            {
                var s = Mathf.Max(1, doc.seed > 0 ? doc.seed : 424242);
                request.SurfaceTileLayoutVariant = Mathf.Abs(s * 1103515245 + 12345) % 48;
                request.PreferredCaveOpeningSector = Mathf.Abs(s * 22695477 + 1) % 8;
                request.MazeGenFlavor = Mathf.Abs(s * 69069 + 7) % 6;
            }

            request.EnsureFullWorldSurfaceContract();
        }

        public static bool AllowsFloatingWildernessTiles(Doc doc = null)
        {
            doc ??= Active;
            return doc.floatingTiles;
        }

        public static bool ShouldImport3DObjects(Doc doc = null)
        {
            doc ??= Active;
            return doc.import3DObjects || doc.proLevelWorld;
        }

        public static bool ShouldScatterBiomeProps(Doc doc = null)
        {
            doc ??= Active;
            return doc.playDiskProps || doc.import3DObjects || doc.proLevelWorld;
        }

        public static bool IsFastDemo(Doc doc = null)
        {
            doc ??= Active;
            return doc.tileCount <= 81;
        }

        /// <summary>Planner minimum — 3×3 play disk only (tileCount=9, no outer rings).</summary>
        public static bool IsPlayDiskOnlyDemo(Doc doc = null)
        {
            doc ??= Active;
            return HasFinalizedActive && doc.tileCount == PlayDiskTerrainTileCount;
        }

        public static bool IsPlayDiskOnlyDemo(WorldGenerationRequest request)
        {
            if (request == null || !IsSessionRequest(request))
                return IsPlayDiskOnlyDemo();
            return HasFinalizedActive && Active.tileCount == PlayDiskTerrainTileCount;
        }

        /// <summary>Planner fast path: centered 3×3 play disk + four cardinal floating wilderness tiles.</summary>
        public static bool IsFloatingIslandsDemo(Doc doc = null)
        {
            doc ??= Active;
            return HasFinalizedActive &&
                   doc.tileCount == SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount &&
                   doc.floatingTiles;
        }

        public static bool IsFloatingIslandsDemo(WorldGenerationRequest request)
        {
            if (request == null || !IsSessionRequest(request))
                return IsFloatingIslandsDemo();
            return HasFinalizedActive &&
                   Active.tileCount == SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount &&
                   Active.floatingTiles;
        }

        public static int NormalizeTileCount(int tileCount, bool floatingTiles = false)
        {
            if (floatingTiles && tileCount < 289)
                return SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount;

            if (tileCount <= PlayDiskTerrainTileCount)
                return PlayDiskTerrainTileCount;
            if (tileCount == SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount)
                return SurfaceTerrainTileExpansion.FloatingIslandsTerrainTileCount;
            if (tileCount == 81)
                return 81;
            if (tileCount >= 289)
                return 289;
            return PlayDiskTerrainTileCount;
        }

        public static bool IsSessionRequest(WorldGenerationRequest request) =>
            request != null &&
            string.Equals(request.GenerationStyleId, "session_config", StringComparison.Ordinal);

        /// <summary>Planner sessions with agentInvokes off — skip blocking tsx meat-loop exports.</summary>
        public static bool SkipTerrainHelperScripts(WorldGenerationRequest request)
        {
            if (!HasFinalizedActive)
                return false;

            var doc = LoadActive();
            if (doc == null || doc.agentInvokes)
                return false;

            return request == null || IsSessionRequest(request);
        }

        public static void BindFullWorldRequest(WorldGenerationRequest request)
        {
            if (request == null || request.SurfaceScope != SurfaceBuildScope.FullWorld)
                return;

            if (HasFinalizedActive)
            {
                ApplyToRequest(request);
                ApplyToEditorSettings();
                return;
            }

            FullWorldGenerationStylePreset.ApplyTo(request);
        }

        public static int EstimatePlannedSteps(Doc doc = null)
        {
            doc ??= Active;
            var request = new WorldGenerationRequest { SurfaceScope = SurfaceBuildScope.FullWorld, Seed = doc.seed };
            ApplyToRequest(request, doc);
            return CaveBuildPlannedStepBudget.ComputeForRequest(request);
        }

        public static void ApplyToEditorSettings(Doc doc = null)
        {
            doc ??= Active;
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            settings.requireHollowTitanOnSurface = doc.hollowTitan || doc.proLevelWorld;
            settings.enableEnhancementPhases = doc.enhancementPhases || doc.proLevelWorld;
            settings.runPostBuildResearchPhase = doc.runPostBuildResearch;
            settings.autoInvokePreBuildWorkflow = doc.agentInvokes;
            settings.autoInvokeEachMeatLoopPass = doc.agentInvokes;
            settings.autoInvokeTerrainAfterSurfaceBuild = doc.agentInvokes;
            settings.preBuildReloopUntilPass = doc.preBuildReloop;
            settings.invokeCursorOnResearchPhase = doc.agentInvokes;
            settings.editorQueueBatchSize = CaveBuildLoadAwareBatching.Clamp(settings.editorQueueBatchSize);
            if (doc.sequentialTerrain)
                settings.showLiveScenePlacement = true;
            SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain = doc.sequentialTerrain;
            SurfaceTerrainTileExpansion.PreferLightweightSeamsOnly =
                IsPlayDiskOnlyDemo(doc) ||
                IsFloatingIslandsDemo(doc) ||
                (IsFastDemo(doc) && !doc.outerRingMountains);
            if (IsPlayDiskOnlyDemo(doc) || IsFloatingIslandsDemo(doc))
            {
                CaveBuildSpeedDemoPolicy.ApplySessionSettings(settings, savePrefs: false);
                settings.enableEnhancementPhases = false;
                settings.runPostBuildResearchPhase = false;
                settings.autoInvokePreBuildWorkflow = false;
                settings.preBuildReloopUntilPass = false;
                settings.invokeCursorOnResearchPhase = false;
                settings.lidarGuidedSculptOnly = IsFloatingIslandsDemo(doc);
                SurfaceLidarGuidedSculptPolicy.PreferSculptOverStamp = IsFloatingIslandsDemo(doc);
                var lowRam = CaveBuildMemoryGuard.SystemRamGb() is > 0f and <= 17f;
                if (doc.sequentialTerrain || lowRam)
                    SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain = true;
                else
                    SurfaceTerrainTileExpansion.PreferSequentialFullWorldTerrain = false;
            }

            settings.SaveToPrefs();
            EditorUtility.SetDirty(settings);
        }

        public static void PrepareFreshBuild(Doc doc)
        {
            if (doc == null)
            {
                _active = null;
                doc = LoadActive();
            }

            if (doc == null)
                return;

            doc = NormalizeDoc(doc);
            SaveActive(doc);
            ApplyToEditorSettings(doc);
            FullWorldConceptLayoutCatalog.SetRandomOnBuild(doc.randomSeedEachBuild);
            CaveBuildConceptSession.ClearLock();
            CaveBuildPersistedSessionReset.ClearForNewBuild("planner fresh build");
            CaveBuildPlannerLayoutAuthor.ResetBuildSession();
            CaveBuildPlannerMeshLandscapeAuthor.ResetBuildSession();
            CaveBuildPlannerMarkerPropScatter.ResetBuildSession();
            CaveBuildPlannerContentAuthor.ResetBuildSession();
            CaveBuildPlannerTrailAuthor.ResetBuildSession();
            CaveBuildPlannerTerrainGuide.ClearSession();

            if (doc.randomSeedEachBuild)
            {
                CaveBuildSeedDefaults.ForceNewSeedBeforeLayoutRoll();
                CaveBuildDeterminism.Unpin();
                CaveBuildLayoutRollSession.ClearPreserveRequest();
                CaveBuildFreshBuildSession.ClearIncrementalArtifacts();
            }
            else if (doc.seed > 0)
            {
                CaveBuildDeterminism.SetPinnedSeed(doc.seed, enabled: true);
                CaveBuildFreshBuildSession.ClearIncrementalArtifacts();
            }
        }

        public static void WriteWizardState(string phase, bool cancelled = false)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            CaveBuildAgentContextExporter.EnsureFolderPublic();
            var doc = new WizardStateDoc
            {
                phase = phase ?? "idle",
                hubRoot = hub,
                openedUtc = DateTime.UtcNow.ToString("o"),
                cancelled = cancelled,
            };
            File.WriteAllText(
                Path.Combine(hub, WizardStateRelPath),
                JsonUtility.ToJson(doc, true) + "\n");
        }

        public static bool TryReadWizardPhase(out string phase)
        {
            phase = "idle";
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var abs = Path.Combine(hub, WizardStateRelPath);
            if (!File.Exists(abs))
                return false;

            try
            {
                var doc = JsonUtility.FromJson<WizardStateDoc>(File.ReadAllText(abs));
                if (doc == null)
                    return false;
                phase = doc.phase ?? "idle";
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Re-read planner JSON from disk and push flags into editor settings + step budget.</summary>
        public static bool ReloadAndApplyPlannerSession(out Doc doc)
        {
            _active = null;
            doc = LoadActive();
            if (doc == null)
                return false;

            _active = doc;
            ApplyToEditorSettings(doc);

            var request = new WorldGenerationRequest { SurfaceScope = SurfaceBuildScope.FullWorld, Seed = doc.seed };
            ApplyToRequest(request, doc);
            CaveBuildStepCounter.ConfigureForRequest(request);

            var tilePlan = FullWorldConceptLayoutCatalog.ExpectedTerrainTileCount(request);
            var steps = CaveBuildPlannedStepBudget.ComputeForRequest(request);
            var layout = IsPlayDiskOnlyDemo(doc)
                ? "play disk only (3×3)"
                : IsFloatingIslandsDemo(doc)
                    ? "floating islands (3×3 + 4 cardinal)"
                    : request.UseExtendedOpenWorldGrid
                        ? "extended ~289"
                        : "81 core grid";
            Debug.Log(
                $"[CaveBuild] Planner approved — \"{doc.label}\" · {layout} · {tilePlan} terrains · ~{steps:N0} steps · " +
                $"props={(doc.playDiskProps ? "on" : "off")} · trails={(doc.surfaceTrails ? "on" : "off")} · " +
                $"caves={(doc.use3DCaveSystem ? "on" : "off")}.");
            return true;
        }

        public static string DescribeActiveTilePlan()
        {
            if (!HasFinalizedActive)
                return "no approved plan";

            var doc = Active;
            if (IsPlayDiskOnlyDemo(doc))
                return "play disk only — 3×3 (9 terrains)";

            if (IsFloatingIslandsDemo(doc))
                return "floating islands — 3×3 play disk + 4 cardinal tiles (13 terrains)";

            if (doc.tileCount > 81)
                return $"~{doc.tileCount} terrain tiles (17×17 extended grid)";

            return "81-tile core grid (9×9)";
        }
    }
}
#endif
