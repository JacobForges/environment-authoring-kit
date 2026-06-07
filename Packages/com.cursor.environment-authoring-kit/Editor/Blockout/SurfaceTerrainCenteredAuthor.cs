#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// World-space FBM sculpt from Ground center — lerps toward one fixed height field (no per-pass noise offset / strata).
    /// </summary>
    static class SurfaceTerrainCenteredAuthor
    {
        /// <summary>Paced blend steps toward the target field (not 50 additive noise layers).</summary>
        public const int DefaultPassCount = 8;
        public const int RefinementPassCountAfterFloridaDem = 0;
        public const int NeighborTilePassCount = 6;
        public const int MaxPassCount = 24;
        const float SculptMacroAmplitude = 0.34f;
        const float SculptMacroAmplitudeAfterDem = 0.30f;
        /// <summary>Push sculpted rows to Terrain after each pass (manual/debug only — very expensive).</summary>
        static bool PreviewEachPassOnTerrain =>
            !CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive && !Application.isBatchMode;

        /// <summary>Upload each row band during a pass (manual only). Hub builds commit once at end.</summary>
        static bool LivePreviewEachRowChunk =>
            !CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive && !Application.isBatchMode;

        /// <summary>Hub / batch builds — one heightmap commit + paced post-sculpt (no multi-minute sync freeze).</summary>
        static bool FastAutomatedSculptCommit =>
            CaveBuildSurfaceCompletionGate.IsSurfaceBuildActive || Application.isBatchMode;
        /// <summary>Outer-band hydro only — kept outside playable grader annulus (0.08–0.72× extent).</summary>
        const float SculptWaterBowlAmplitude = 0.00035f;
        const float SculptRoadCutAmplitude = 0.025f;
        /// <summary>Post-sculpt heightmap blur is optional; FBM lerp + commit-time polish is enough.</summary>
        const int ProgressBarEveryNChunks = 6;
        const int AutomatedHeightsLoadBandRows = 2;
        const int AutomatedCommitBandRows = 2;
        const int LivePreviewRowInterval = 48;

        enum MicroSculptPhase
        {
            LoadHeights,
            Sculpt,
            Commit,
            Finish,
        }

        static QueuedPassState _microSculptState;
        static bool _microSculptHooked;
        static int _microUpdateTicks;

        static int _queuedPassesActive;
        static int _progressThrottle;
        static double _sculptWatchdogAt;
        static double _sculptWatchdogLastWarnAt;
        static int _sculptWatchdogPass;
        static int _sculptWatchdogRow;
        static bool _sculptWatchdogKickoffDone;

        static double SculptWatchdogStallSeconds =>
            EnvironmentKitHardwareBudget.Active.ConserveGpuMemory ? 120.0 : 90.0;

        public static int ResolvePassCount(int requestedPassCount)
        {
            if (requestedPassCount <= 0)
                return DefaultPassCount;
            return Mathf.Clamp(requestedPassCount, 1, MaxPassCount);
        }

        public static int ResolvePassCountAfterFloridaDem(int requestedPassCount, bool demStamped)
        {
            if (!demStamped)
                return ResolvePassCount(requestedPassCount);
            // LiDAR sets macro structure; paced sculpt passes carve/rise walkable relief at character scale.
            return Mathf.Clamp(ResolvePassCount(requestedPassCount), 3, MaxPassCount);
        }

        public static int PassCountForNeighborTile(int mainPassCount)
        {
            if (SurfaceFloridaDemBuildState.AuthoritativeStampCompletedThisBuild || mainPassCount <= 0)
                return 0;
            return Mathf.Clamp(Mathf.Min(NeighborTilePassCount, mainPassCount), 1, MaxPassCount);
        }

        static int RowChunkSizeFor(int res, bool microSculpt = false)
        {
            if (microSculpt)
                return 1;

            if (res >= 1025)
                return 4;
            if (res >= 513)
                return 4;
            return EnvironmentKitHardwareBudget.Active.ConserveGpuMemory ? 8 : 12;
        }

        /// <summary>Flush only — SyncHeightmap can freeze the editor 30–120s on 513² maps.</summary>
        public static void QueueDeferredHeightmapSync(Terrain terrain, System.Action onComplete)
        {
            CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
            {
                if (terrain != null)
                    terrain.Flush();
                onComplete?.Invoke();
            });
        }

        public static bool IsQueuedPassesActive => _queuedPassesActive > 0;

        public static void ResetQueuedPassesState()
        {
            _queuedPassesActive = 0;
            StopMicroSculpt(null);
        }

        static void StopMicroSculpt(QueuedPassState completed)
        {
            if (_microSculptState != null && completed != null && !ReferenceEquals(_microSculptState, completed))
                return;

            _microSculptState = null;
            if (!_microSculptHooked)
                return;

            EditorApplication.update -= OnMicroSculptUpdate;
            _microSculptHooked = false;
        }

        static void EnsureMicroSculptHook()
        {
            if (_microSculptHooked)
                return;

            _microSculptHooked = true;
            EditorApplication.update += OnMicroSculptUpdate;
        }

        static void BeginMicroSculpt(QueuedPassState state)
        {
            state.MicroSculpt = true;
            state.MicroPhase = MicroSculptPhase.LoadHeights;
            _microSculptState = state;
            _microUpdateTicks = 0;
            _queuedPassesActive++;
            _sculptWatchdogAt = EditorApplication.timeSinceStartup;
            _sculptWatchdogPass = 0;
            _sculptWatchdogRow = 0;
            if (state.Terrain != null)
                SurfaceTerrainTileExpansion.SetLiveTerrainFocus(state.Terrain);
            EditorApplication.update -= SculptPassWatchdog;
            EditorApplication.update += SculptPassWatchdog;
            CaveBuildEditorLog.LogSurface(
                "[Surface] Terrain sculpt — paced queue micro pipeline (1 sculpt row / step).",
                forceUnityConsole: true);
            CaveBuildActionPacing.ScheduleLight(
                OnMicroSculptUpdate,
                CaveBuildPipelineDomains.QueueLabel("terrain sculpt micro"));
        }

        static void OnMicroSculptUpdate()
        {
            var state = _microSculptState;
            if (state?.Terrain == null)
            {
                StopMicroSculpt(null);
                return;
            }

            CaveBuildActionPacing.TouchQueueActivity();
            _microUpdateTicks++;

            switch (state.MicroPhase)
            {
                case MicroSculptPhase.LoadHeights:
                    if (!AdvanceMicroHeightsLoad(state))
                    {
                        ScheduleMicroSculptContinue();
                        return;
                    }

                    state.MicroPhase = MicroSculptPhase.Sculpt;
                    state.PassRowStart = 0;
                    state.PassIndex = 0;
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Heightmap loaded ({state.Res}×{state.Res}) — sculpt starting.",
                        forceUnityConsole: true);
                    break;

                case MicroSculptPhase.Sculpt:
                    var passBefore = state.PassIndex;
                    var rowBefore = state.PassRowStart;
                    PulseMicroSculptProgress(state);
                    state.RunRowChunk();
                    if (!FastAutomatedSculptCommit)
                    {
                        TryLivePreviewSculptRows(state, rowBefore);
                        if (state.PassIndex > passBefore)
                            PreviewPassOnTerrain(state);
                    }
                    if (state.PassIndex < state.PassCount)
                    {
                        ScheduleMicroSculptContinue();
                        return;
                    }

                    state.MicroPhase = MicroSculptPhase.Finish;
                    state.HeightsAppliedToTerrain = false;
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Sculpt passes done — single heightmap apply next (no row-band upload).",
                        forceUnityConsole: true);
                    break;

                case MicroSculptPhase.Commit:
                    state.MicroPhase = MicroSculptPhase.Finish;
                    break;

                case MicroSculptPhase.Finish:
                    if (!state.HeightsAppliedToTerrain && state.Heights != null)
                    {
                        state.HeightsAppliedToTerrain = true;
                        CaveBuildMicroTerrainHeightmap.QueueWriteFull(
                            state.Terrain,
                            state.Heights,
                            "terrain sculpt",
                            "apply",
                            () => CaveBuildActionPacing.ScheduleLight(() =>
                            {
                                state.Terrain?.Flush();
                                CaveBuildRunStatusPublisher.PulseSubOperation("terrain sculpt", "done");
                                CaveBuildEditorLog.LogSurface(
                                    "[Surface] Terrain heightmap committed (micro pipeline).",
                                    forceUnityConsole: true);
                                StopMicroSculpt(state);
                                CompleteSculptSession(state, 0);
                            }, CaveBuildPipelineDomains.QueueLabel("terrain sculpt commit")));
                        return;
                    }

                    state.Terrain?.Flush();
                    CaveBuildRunStatusPublisher.PulseSubOperation("terrain sculpt", "done");
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Terrain heightmap committed (micro pipeline).",
                        forceUnityConsole: true);
                    StopMicroSculpt(state);
                    CompleteSculptSession(state, 0);
                    break;
            }

            ScheduleMicroSculptContinue();
        }

        static void ScheduleMicroSculptContinue()
        {
            if (_microSculptState == null)
                return;

            CaveBuildActionPacing.ScheduleLight(
                OnMicroSculptUpdate,
                CaveBuildPipelineDomains.QueueLabel("terrain sculpt micro"));
        }

        static bool AdvanceMicroHeightsLoad(QueuedPassState state)
        {
            var res = state.Res > 0 ? state.Res : state.Terrain.terrainData.heightmapResolution;
            state.Res = res;
            if (state.Heights == null)
            {
                state.Heights = new float[res, res];
                state.HeightsLoadRow = 0;
            }

            var y0 = state.HeightsLoadRow;
            var y1 = Mathf.Min(res, y0 + AutomatedHeightsLoadBandRows);
            if (y1 > y0)
            {
                var slice = state.Terrain.terrainData.GetHeights(0, y0, res, y1 - y0);
                for (var y = 0; y < y1 - y0; y++)
                {
                    for (var x = 0; x < res; x++)
                        state.Heights[y0 + y, x] = slice[y, x];
                }

                state.HeightsLoadRow = y1;
            }

            if (_microUpdateTicks % 4 == 0 || state.HeightsLoadRow >= res)
            {
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "terrain sculpt",
                    $"load rows {state.HeightsLoadRow}/{res}");
            }

            return state.HeightsLoadRow >= res;
        }

        static void PulseMicroSculptProgress(QueuedPassState state)
        {
            if (_microUpdateTicks % 8 != 0 && state.PassRowStart != 0)
                return;

            var passNum = state.PassIndex + 1;
            CaveBuildRunStatusPublisher.PulseSubOperation(
                "terrain sculpt",
                $"pass {passNum}/{state.PassCount} rows {state.PassRowStart}/{state.Res}");
            PulseSculptLiveScene(state, passNum);
            _sculptWatchdogPass = state.PassIndex;
            _sculptWatchdogRow = state.PassRowStart;
            _sculptWatchdogAt = EditorApplication.timeSinceStartup;
        }

        static void PulseSculptLiveScene(QueuedPassState state, int passNum)
        {
            if (state?.Terrain == null)
                return;

            SurfaceTerrainTileExpansion.SetLiveTerrainFocus(state.Terrain);
            CaveBuildStepCounter.PulseFlatGridTerraformSculptProgress(
                SurfaceTerrainTileExpansion.LiveTerraformStep,
                SurfaceTerrainTileExpansion.LiveTerraformTotal,
                passNum,
                state.PassCount,
                state.PassRowStart,
                state.Res);
            var sculptLiveBeat = !FastAutomatedSculptCommit &&
                                 !CaveBuildEditorResponsiveness.IsLongBuildActive;
            CaveBuildLiveSceneFeedback.NotifyTerrainTile(
                state.Terrain,
                $"terrain sculpt — pass {passNum}/{state.PassCount} rows {state.PassRowStart}/{state.Res}",
                ping: false,
                forceCamera: sculptLiveBeat);
        }

        static void TryLivePreviewSculptRows(QueuedPassState state, int rowBefore)
        {
            if (FastAutomatedSculptCommit || CaveBuildEditorResponsiveness.IsLongBuildActive)
                return;

            if (state?.Terrain == null || state.Heights == null || state.PassRowStart <= rowBefore)
                return;

            if (state.PassRowStart % LivePreviewRowInterval != 0 &&
                state.PassRowStart < state.Res)
                return;

            var chunk = RowChunkSizeFor(state.Res, state.MicroSculpt);
            var yStart = Mathf.Max(0, state.PassRowStart - chunk);
            state.FlushCommitRows(yStart, state.PassRowStart, delayLod: true);
            CaveBuildLiveSceneFlushUtility.FlushWorldView(state.Terrain, forceRepaint: false);
        }

        public static void ApplyCenteredPasses(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            int seed,
            bool mountains,
            bool water,
            bool roads,
            float preserveInnerRadiusMeters = -1f,
            int passCount = DefaultPassCount,
            System.Action<int, int> onPass = null)
        {
            if (terrain == null || terrain.terrainData == null || passCount < 1)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface centered terrain passes");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var rng = new System.Random(seed);
            var inner = preserveInnerRadiusMeters > 0f ? preserveInnerRadiusMeters : extentMeters * 0.04f;
            var outer = extentMeters * 1.12f;
            var sculptSeed = seed;

            for (var pass = 0; pass < passCount; pass++)
            {
                onPass?.Invoke(pass + 1, passCount);
                ApplySculptRows(
                    heights,
                    res,
                    size,
                    origin,
                    centerWorld,
                    inner,
                    outer,
                    extentMeters,
                    sculptSeed,
                    rng,
                    mountains,
                    water,
                    roads,
                    pass,
                    passCount,
                    0,
                    res,
                    refinementAfterDem: false);

                SoftenSculptTerraces(heights, res, size, origin, centerWorld, inner, outer);
                EditorApplication.QueuePlayerLoopUpdate();
            }

            SoftenSculptTerraces(heights, res, size, origin, centerWorld, inner, outer);
            data.SetHeights(0, 0, heights);
        }

        public static void QueueCenteredPasses(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            int seed,
            bool mountains,
            bool water,
            bool roads,
            float preserveInnerRadiusMeters,
            int passCount,
            bool refinementAfterAuthoritativeDem = false,
            System.Action onComplete = null)
        {
            if (terrain == null || onComplete == null)
            {
                onComplete?.Invoke();
                return;
            }

            if (passCount <= 0)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Terrain sculpt skipped (0 passes — pre-plan / LiDAR authoritative).",
                    forceUnityConsole: true);
                onComplete.Invoke();
                return;
            }

            var userComplete = onComplete;
            var state = new QueuedPassState
            {
                Terrain = terrain,
                Center = centerWorld,
                Extent = extentMeters,
                Seed = seed,
                Mountains = mountains,
                Water = water,
                Roads = roads,
                PreserveInner = preserveInnerRadiusMeters,
                PassCount = Mathf.Max(1, passCount),
                PassIndex = 0,
                RefinementAfterDem = refinementAfterAuthoritativeDem,
                OnComplete = userComplete,
            };
            state.Prepare(microSculpt: true);
            _progressThrottle = 0;
            BeginMicroSculpt(state);
        }

        static void SchedulePacedHeightsLoad(QueuedPassState state, System.Action onComplete) =>
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunPacedHeightsLoadBand(state, onComplete));

        static void RunPacedHeightsLoadBand(QueuedPassState state, System.Action onComplete)
        {
            if (state?.Terrain?.terrainData == null)
            {
                onComplete?.Invoke();
                return;
            }

            var res = state.Res > 0 ? state.Res : state.Terrain.terrainData.heightmapResolution;
            state.Res = res;
            if (state.Heights == null)
            {
                state.Heights = new float[res, res];
                state.HeightsLoadRow = 0;
            }

            var y0 = state.HeightsLoadRow;
            var y1 = Mathf.Min(res, y0 + AutomatedHeightsLoadBandRows);
            if (y1 > y0)
            {
                var slice = state.Terrain.terrainData.GetHeights(0, y0, res, y1 - y0);
                for (var y = 0; y < y1 - y0; y++)
                {
                    for (var x = 0; x < res; x++)
                        state.Heights[y0 + y, x] = slice[y, x];
                }

                state.HeightsLoadRow = y1;
            }

            CaveBuildRunStatusPublisher.PulseSubOperation(
                "terrain sculpt",
                $"load rows {state.HeightsLoadRow}/{res}");

            if (state.HeightsLoadRow < res)
            {
                SchedulePacedHeightsLoad(state, onComplete);
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] Heightmap loaded in {Mathf.CeilToInt(res / (float)AutomatedHeightsLoadBandRows)} band(s).",
                forceUnityConsole: true);
            onComplete?.Invoke();
        }

        sealed class QueuedPassState
        {
            public Terrain Terrain;
            public Vector3 Center;
            public float Extent;
            public int Seed;
            public bool Mountains;
            public bool Water;
            public bool Roads;
            public float PreserveInner;
            public int PassCount;
            public int PassIndex;
            public float[,] Heights;
            public int Res;
            public Vector3 Size;
            public Vector3 Origin;
            public System.Random Rng;
            public float Inner;
            public float Outer;
            public int PassRowStart;
            public int CommitRowStart;
            public int HeightsLoadRow;
            public int PolishRowY = 1;
            public float[,] PolishSource;
            public bool HeightsFlushedIncremental;
            public bool RefinementAfterDem;
            public bool MicroSculpt;
            public bool HeightsAppliedToTerrain;
            public MicroSculptPhase MicroPhase;
            public System.Action OnComplete;

            public void Prepare(bool microSculpt = false)
            {
                MicroSculpt = microSculpt;
                var data = Terrain.terrainData;
                Res = data.heightmapResolution;
                Heights = null;
                Size = data.size;
                Origin = Terrain.transform.position;
                Rng = new System.Random(Seed);
                Inner = PreserveInner > 0f ? PreserveInner : Extent * 0.04f;
                Outer = Extent * 1.12f;
                PassRowStart = 0;
                CommitRowStart = 0;
                HeightsLoadRow = 0;
                PolishRowY = 1;
                PolishSource = null;
                HeightsFlushedIncremental = false;
                if (!MicroSculpt)
                    Undo.RecordObject(data, "Surface centered terrain passes");
            }

            public void EnsureHeightsLoaded()
            {
                if (Heights != null || Terrain?.terrainData == null)
                    return;

                if (FastAutomatedSculptCommit)
                    return;

                Heights = Terrain.terrainData.GetHeights(0, 0, Res, Res);
            }

            /// <returns>True when the current sculpt pass (all row chunks) is finished.</returns>
            public bool RunRowChunk()
            {
                if (Heights == null)
                    return false;

                EnsureHeightsLoaded();
                var chunk = RowChunkSizeFor(Res, MicroSculpt);
                var yEnd = Mathf.Min(Res, PassRowStart + chunk);
                ApplySculptRows(
                    Heights,
                    Res,
                    Size,
                    Origin,
                    Center,
                    Inner,
                    Outer,
                    Extent,
                    Seed,
                    Rng,
                    Mountains,
                    Water,
                    Roads,
                    PassIndex,
                    PassCount,
                    PassRowStart,
                    yEnd,
                    RefinementAfterDem);
                PassRowStart = yEnd;
                if (PassRowStart < Res)
                    return false;

                PassRowStart = 0;
                PassIndex++;
                return true;
            }

            public void Commit()
            {
                if (Terrain == null)
                    return;

                Terrain.terrainData.SetHeights(0, 0, Heights);
                HeightsFlushedIncremental = true;
                Terrain.Flush();
            }

            public void FlushCommitRows(int yStart, int yEnd, bool delayLod = false)
            {
                if (Terrain == null || Heights == null || yEnd <= yStart)
                    return;

                var rowCount = yEnd - yStart;
                var slice = new float[rowCount, Res];
                for (var y = 0; y < rowCount; y++)
                {
                    for (var x = 0; x < Res; x++)
                        slice[y, x] = Heights[yStart + y, x];
                }

                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(Terrain, 0, yStart, slice, delayLod);
                HeightsFlushedIncremental = true;
            }

            public void SyncHeightmapIfNeeded()
            {
                if (Terrain?.terrainData == null)
                    return;
                Terrain.terrainData.SyncHeightmap();
                Terrain.Flush();
            }
        }

        static bool SculptFinished(QueuedPassState state) => state.PassIndex >= state.PassCount;

        /// <summary>Upload in-memory sculpt to the terrain so each pass is visible in the editor.</summary>
        static void PreviewPassOnTerrain(QueuedPassState state)
        {
            if (state?.Terrain?.terrainData == null || state.Heights == null)
                return;

            state.Terrain.terrainData.SetHeights(0, 0, state.Heights);
            state.HeightsFlushedIncremental = true;
            state.Terrain.Flush();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static void ScheduleQueuedPass(QueuedPassState state)
        {
            if (state.PassIndex == 0 && state.PassRowStart == 0)
            {
                _queuedPassesActive++;
                _sculptWatchdogAt = EditorApplication.timeSinceStartup;
                _sculptWatchdogLastWarnAt = 0;
                _sculptWatchdogKickoffDone = false;
                _sculptWatchdogPass = 0;
                _sculptWatchdogRow = 0;
                EditorApplication.update -= SculptPassWatchdog;
                EditorApplication.update += SculptPassWatchdog;
            }

            var label = CaveBuildPipelineDomains.SurfaceQueueLabel(
                $"terrain sculpt pass {state.PassIndex + 1}/{state.PassCount}");
            if (FastAutomatedSculptCommit || CaveBuildActionPacing.QueuedCount <= 8)
            {
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunQueuedPass(state));
            }
            else
            {
                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    () => RunQueuedPass(state),
                    label,
                    CaveBuildActionPacing.ActionWeight.Light);
            }
        }

        static void SculptPassWatchdog()
        {
            if (_queuedPassesActive <= 0)
            {
                EditorApplication.update -= SculptPassWatchdog;
                return;
            }

            // Paced sculpt waits on the editor queue — not a stall.
            if (_microSculptState != null || CaveBuildActionPacing.IsBusy || CaveBuildActionPacing.QueuedCount > 0)
            {
                _sculptWatchdogAt = EditorApplication.timeSinceStartup;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now - _sculptWatchdogAt < SculptWatchdogStallSeconds)
                return;

            if (now - _sculptWatchdogLastWarnAt < SculptWatchdogStallSeconds)
                return;

            _sculptWatchdogLastWarnAt = now;
            _sculptWatchdogAt = now;
            CaveBuildEditorLog.LogSurfaceWarning(
                $"[Surface] Sculpt queue idle >{SculptWatchdogStallSeconds:F0}s with empty editor queue — nudging pipeline once.");
            if (!_sculptWatchdogKickoffDone)
            {
                _sculptWatchdogKickoffDone = true;
                CaveBuildActionPacing.PreparePipelineChainKickoff();
            }
        }

        static void RunQueuedPass(QueuedPassState state)
        {
            if (state?.Terrain == null)
            {
                EditorUtility.ClearProgressBar();
                _queuedPassesActive = Mathf.Max(0, _queuedPassesActive - 1);
                state?.OnComplete?.Invoke();
                return;
            }

            var passNum = state.PassIndex + 1;
            if (state.PassRowStart == 0 || state.PassIndex != _sculptWatchdogPass)
            {
                _sculptWatchdogPass = state.PassIndex;
                _sculptWatchdogRow = state.PassRowStart;
                _sculptWatchdogAt = EditorApplication.timeSinceStartup;
            }
            else if (state.PassRowStart > _sculptWatchdogRow)
            {
                _sculptWatchdogRow = state.PassRowStart;
                _sculptWatchdogAt = EditorApplication.timeSinceStartup;
            }

            if (++_progressThrottle % ProgressBarEveryNChunks == 0 || state.PassRowStart == 0)
            {
                CaveBuildProgressUI.ShowThrottled(
                    "Environment Kit",
                    $"[Surface] sculpt pass {passNum}/{state.PassCount} rows {state.PassRowStart}/{state.Res}",
                    0.2f + 0.55f * ((state.PassIndex + state.PassRowStart / (float)Mathf.Max(1, state.Res)) /
                                     state.PassCount));
            }

            if (state.PassRowStart == 0 || state.PassRowStart % Mathf.Max(16, state.Res / 8) == 0)
            {
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "terrain sculpt",
                    $"pass {passNum}/{state.PassCount} rows {state.PassRowStart}/{state.Res}");
                PulseSculptLiveScene(state, passNum);
            }

            var chunk = RowChunkSizeFor(state.Res);
            var rowBandStart = Mathf.Max(0, state.PassRowStart - chunk);
            var rowBefore = state.PassRowStart;
            var passBefore = state.PassIndex;
            var passFinished = state.RunRowChunk();
            if (!FastAutomatedSculptCommit)
            {
                TryLivePreviewSculptRows(state, rowBefore);
                if (state.PassIndex > passBefore)
                    PreviewPassOnTerrain(state);
            }
            if (state.Heights == null)
            {
                SchedulePacedHeightsLoad(state, () => ScheduleQueuedPass(state));
                return;
            }

            if (!passFinished && LivePreviewEachRowChunk && state.PassRowStart > 0)
            {
                state.FlushCommitRows(rowBandStart, state.PassRowStart, delayLod: true);
                CaveBuildLiveSceneFlushUtility.FlushWorldView(state.Terrain, forceRepaint: true);
            }

            if (passFinished)
            {
                if (PreviewEachPassOnTerrain || FastAutomatedSculptCommit)
                    PreviewPassOnTerrain(state);

                if (state.PassIndex % 2 == 0 || state.PassIndex + 1 >= state.PassCount)
                {
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] terrain pass {state.PassIndex}/{state.PassCount} applied (live preview).",
                        forceUnityConsole: true);
                }
            }

            if (!SculptFinished(state))
            {
                ScheduleQueuedPass(state);
                return;
            }

            if (FastAutomatedSculptCommit)
            {
                CaveBuildRunStatusPublisher.PulseSubOperation("terrain sculpt", "commit heightmap");
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => BeginHeightmapUpload(state));
                return;
            }

            BeginHeightmapUpload(state);
        }

        static void BeginHeightmapUpload(QueuedPassState state)
        {
            state.PolishSource = null;
            state.CommitRowStart = 0;

            if (FastAutomatedSculptCommit)
            {
                CaveBuildEditorLog.LogSurface(
                    "[Surface] Sculpt complete — committing heightmap (fast path)…",
                    forceUnityConsole: true);
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunFastHeightmapCommit(state));
                return;
            }

            CaveBuildEditorLog.LogSurface(
                "[Surface] Sculpt complete — uploading heightmap row bands…",
                forceUnityConsole: true);
            ScheduleCommitRows(state);
        }

        static void RunFastHeightmapCommit(QueuedPassState state)
        {
            if (state?.Terrain == null || state.Heights == null)
            {
                CompleteSculptSession(state, 0);
                return;
            }

            CaveBuildMicroTerrainHeightmap.QueueWriteFull(
                state.Terrain,
                state.Heights,
                "terrain sculpt",
                "apply",
                () =>
                {
                    CaveBuildEditorLog.LogSurface(
                        "[Surface] Heightmap committed (paced row bands).",
                        forceUnityConsole: true);
                    CaveBuildActionPacing.ScheduleNextEditorFrame(() => CompleteSculptSession(state, 0));
                });
        }

        static void CompleteSculptSession(QueuedPassState state, int repaired)
        {
            if (repaired > 0)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Post-sculpt heightfield repair — {repaired} cell operation(s).",
                    forceUnityConsole: true);
            }

            EditorUtility.ClearProgressBar();
            _queuedPassesActive = Mathf.Max(0, _queuedPassesActive - 1);
            if (_queuedPassesActive <= 0)
                EditorApplication.update -= SculptPassWatchdog;

            var done = state?.OnComplete;
            StopMicroSculpt(state);
            done?.Invoke();
        }

        static void SchedulePostPolish(QueuedPassState state) =>
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunPostPolish(state));

        static void RunPostPolish(QueuedPassState state)
        {
            if (state?.Terrain == null)
            {
                EditorUtility.ClearProgressBar();
                _queuedPassesActive = Mathf.Max(0, _queuedPassesActive - 1);
                state?.OnComplete?.Invoke();
                return;
            }

            state.EnsureHeightsLoaded();
            if (state.PolishSource == null)
                state.PolishSource = (float[,])state.Heights.Clone();

            var polishYMax = state.Res - 2;
            var chunk = Mathf.Max(1, RowChunkSizeFor(state.Res) * 2);
            var yEnd = Mathf.Min(polishYMax, state.PolishRowY + chunk - 1);
            CaveBuildProgressUI.ShowThrottled(
                "Environment Kit",
                $"[Surface] sculpt polish rows {state.PolishRowY}/{polishYMax}",
                0.78f);

            SoftenSculptTerraceRows(
                state.Heights,
                state.PolishSource,
                state.Res,
                state.Size,
                state.Origin,
                state.Center,
                state.Inner,
                state.Outer,
                state.PolishRowY,
                yEnd);

            state.PolishRowY = yEnd + 1;
            if (state.PolishRowY <= polishYMax)
            {
                SchedulePostPolish(state);
                return;
            }

            state.PolishSource = null;
            state.CommitRowStart = 0;
            CaveBuildEditorLog.LogSurface(
                "[Surface] Sculpt polish done — uploading heightmap row bands…",
                forceUnityConsole: true);
            ScheduleCommitRows(state);
        }

        static void ScheduleCommitRows(QueuedPassState state)
        {
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunCommitRows(state));
        }

        static void RunCommitRows(QueuedPassState state)
        {
            if (state?.Terrain == null)
            {
                EditorUtility.ClearProgressBar();
                _queuedPassesActive = Mathf.Max(0, _queuedPassesActive - 1);
                state?.OnComplete?.Invoke();
                return;
            }

            if (state.CommitRowStart == 0)
            {
                CaveBuildProgressUI.ShowThrottled(
                    "Environment Kit",
                    "[Surface] uploading sculpted heightmap…",
                    0.82f);
            }

            var chunk = RowChunkSizeFor(state.Res) * 12;
            var yEnd = Mathf.Min(state.Res, state.CommitRowStart + chunk);
            var delayLod = !PreviewEachPassOnTerrain;
            state.FlushCommitRows(state.CommitRowStart, yEnd, delayLod);
            state.CommitRowStart = yEnd;
            if (!delayLod)
                CaveBuildLiveSceneFlushUtility.FlushWorldView(state.Terrain);

            if (state.CommitRowStart < state.Res)
            {
                ScheduleCommitRows(state);
                return;
            }

            if (delayLod)
            {
                CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
                {
                    QueueDeferredHeightmapSync(state.Terrain, () => FinishCommitPolish(state));
                });
                return;
            }

            CaveBuildActionPacing.ScheduleNextEditorFrame(() => FinishCommitPolish(state));
        }

        static void FinishCommitPolish(QueuedPassState state)
        {
            if (state?.Terrain == null)
            {
                CompleteSculptSession(state, 0);
                return;
            }

            if (FastAutomatedSculptCommit)
            {
                CompleteSculptSession(state, 0);
                return;
            }

            try
            {
                SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                    state.Terrain,
                    state.Center,
                    state.Extent,
                    strength: state.RefinementAfterDem ? 0.22f : 0.36f);

                var repaired = SurfaceTerrainCraterRepair.RepairHeightfieldPlayable(
                    state.Terrain,
                    state.Center,
                    state.Extent,
                    maxPasses: 22);
                if (repaired > 0)
                {
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Post-sculpt heightfield repair — {repaired} cell operation(s).",
                        forceUnityConsole: true);
                }

                if (!state.RefinementAfterDem)
                {
                    SurfaceTerrainRefinement.SmoothGraderSampleBandPublic(
                        state.Terrain,
                        state.Center,
                        state.Extent,
                        strength: 0.14f);
                }

                state.Terrain.Flush();
                CaveBuildEditorLog.LogSurface(
                    state.RefinementAfterDem
                        ? "[Surface] Terrain heightmap committed (sculpt + de-checkerboard; radial grader skipped after DEM)."
                        : "[Surface] Terrain heightmap committed (sculpt + de-checkerboard + grader polish).",
                    forceUnityConsole: true);
            }
            finally
            {
                CompleteSculptSession(state, 0);
            }
        }

        static void ApplySculptRows(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 centerWorld,
            float inner,
            float outer,
            float extentMeters,
            int sculptSeed,
            System.Random rng,
            bool mountains,
            bool water,
            bool roads,
            int passIndex,
            int passCount,
            int yStart,
            int yEnd,
            bool refinementAfterDem)
        {
            var cx = centerWorld.x;
            var cz = centerWorld.z;
            var innerSq = inner * inner;
            var outerSq = outer * outer;
            var resM1 = Mathf.Max(1, res - 1);
            var invResM1X = size.x / resM1;
            var invResM1Z = size.z / resM1;
            var ox = origin.x;
            var oz = origin.z;
            var passT0 = passIndex <= 0
                ? 0f
                : Mathf.SmoothStep(0f, 1f, passIndex / (float)Mathf.Max(1, passCount));
            var passT1 = Mathf.SmoothStep(0f, 1f, (passIndex + 1f) / Mathf.Max(1, passCount));
            var passStep = Mathf.Max(0.0001f, passT1 - passT0);

            for (var y = yStart; y < yEnd; y++)
            {
                var wz = oz + y * invResM1Z;
                var dz = wz - cz;
                var dzSq = dz * dz;
                if (dzSq > outerSq)
                    continue;

                var maxDx = outerSq - dzSq;
                if (maxDx <= 0f)
                    continue;
                maxDx = Mathf.Sqrt(maxDx);
                var xMin = Mathf.Clamp(Mathf.FloorToInt((cx - maxDx - ox) / invResM1X), 0, res - 1);
                var xMax = Mathf.Clamp(Mathf.CeilToInt((cx + maxDx - ox) / invResM1X), 0, res - 1);

                for (var x = xMin; x <= xMax; x++)
                {
                    var wx = ox + x * invResM1X;
                    var dx = wx - cx;
                    var distSq = dx * dx + dzSq;
                    var dist = Mathf.Sqrt(distSq);
                    var radialT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, dist));
                    var radialMask = radialT * radialT;
                    if (dist < inner)
                        radialMask *= Mathf.SmoothStep(0.4f, 1f, dist / Mathf.Max(inner, 0.5f));
                    if (radialMask < 0.0005f)
                        continue;

                    var normDist = Mathf.Clamp01(dist / extentMeters);
                    var targetNorm = ComputeSculptTargetNorm(
                        wx,
                        wz,
                        sculptSeed,
                        normDist,
                        radialMask,
                        mountains,
                        water,
                        roads,
                        refinementAfterDem);

                    var stepWeight = passStep * radialMask;
                    if (stepWeight < 0.0003f)
                        continue;

                    heights[y, x] = Mathf.Clamp01(Mathf.Lerp(heights[y, x], targetNorm, stepWeight));
                }
            }
        }

        /// <summary>Fixed world-space FBM target (Unity Terrain Tools: world-space noise, FBM not Strata).</summary>
        static float ComputeSculptTargetNorm(
            float wx,
            float wz,
            int sculptSeed,
            float normDist,
            float radialMask,
            bool mountains,
            bool water,
            bool roads,
            bool refinementAfterDem)
        {
            var macroAmp = refinementAfterDem ? SculptMacroAmplitudeAfterDem : SculptMacroAmplitude;
            var fbm = SampleWorldFbm(wx, wz, sculptSeed);
            var h = 0.5f + fbm * macroAmp * Mathf.Lerp(0.55f, 1f, radialMask);

            if (mountains)
            {
                var mountainGate = Mathf.SmoothStep(0.08f, 0.42f, normDist) *
                                   (1f - Mathf.SmoothStep(0.82f, 1f, normDist));
                var ridged = SurfaceMountainReliefSampler.SampleRidgedMassif(wx, wz, sculptSeed + 4403);
                h += (fbm * 0.45f + ridged * 0.55f) * macroAmp * radialMask * mountainGate * 0.95f;
            }

            // Outer rim only (normDist &gt; 0.96) — grader annulus is 0.08–1.05×; hydro bowls inside that band fail heightfield_no_craters.
            if (water && !refinementAfterDem && normDist > 0.96f)
            {
                var hydro = Mathf.PerlinNoise(
                    wx * 0.00173f + sculptSeed * 0.19f + 4.2f,
                    wz * 0.00171f - sculptSeed * 0.13f + 1.8f);
                var bowlGate = Mathf.SmoothStep(0.97f, 0.985f, normDist) *
                               (1f - Mathf.SmoothStep(0.995f, 1.02f, normDist));
                if (hydro > 0.58f)
                {
                    var bowl = (1f - Mathf.Abs(normDist - 0.985f) / 0.02f) * bowlGate;
                    h -= SculptWaterBowlAmplitude * bowl * radialMask * Mathf.Max(0f, 0.55f - fbm);
                }
            }

            if (roads)
            {
                var roadGate = 1f - Mathf.SmoothStep(0f, 0.5f, normDist);
                h -= SculptRoadCutAmplitude * roadGate * radialMask;
            }

            return Mathf.Clamp01(h);
        }

        /// <summary>Fractal Brownian motion in world meters — stable across all blend passes.</summary>
        public static float SampleWorldFbm(float wx, float wz, int seed)
        {
            var ox = seed * 0.173f + 2.1f;
            var oz = seed * 0.091f + 5.7f;
            var amplitude = 1f;
            var frequency = 0.00185f;
            var sum = 0f;
            var norm = 0f;

            for (var octave = 0; octave < 3; octave++)
            {
                var u = wx * frequency + ox + octave * 3.7f;
                var v = wz * frequency + oz + octave * 2.9f;
                var n = Mathf.PerlinNoise(u, v) * 2f - 1f;
                sum += n * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.05f;
            }

            return norm > 0.0001f ? sum / norm : 0f;
        }

        /// <summary>Light isotropic blur in the play disk — removes row/chunk and periodic sculpt seams.</summary>
        static void SoftenSculptTerraces(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 centerWorld,
            float inner,
            float outer,
            int iterations = 1)
        {
            if (res < 5 || iterations <= 0)
                return;

            for (var iter = 0; iter < iterations; iter++)
            {
                var source = (float[,])heights.Clone();
                SoftenSculptTerraceRows(
                    heights,
                    source,
                    res,
                    size,
                    origin,
                    centerWorld,
                    inner,
                    outer,
                    1,
                    res - 2);
            }
        }

        /// <summary>One blur pass on a row band (source = frozen input for this pass).</summary>
        static void SoftenSculptTerraceRows(
            float[,] heights,
            float[,] source,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 centerWorld,
            float inner,
            float outer,
            int yStart,
            int yEnd)
        {
            if (res < 5 || source == null)
                return;

            var cx = centerWorld.x;
            var cz = centerWorld.z;
            var innerSq = inner * inner;
            var outerSq = outer * outer;
            var resM1 = Mathf.Max(1, res - 1);
            var yMax = res - 2;
            yStart = Mathf.Clamp(yStart, 1, yMax);
            yEnd = Mathf.Clamp(yEnd, yStart, yMax);

            for (var y = yStart; y <= yEnd; y++)
            {
                var wz = origin.z + y / (float)resM1 * size.z;
                var dz = wz - cz;
                var dzSq = dz * dz;
                if (dzSq > outerSq)
                    continue;

                for (var x = 1; x < res - 1; x++)
                {
                    var wx = origin.x + x / (float)resM1 * size.x;
                    var dx = wx - cx;
                    var distSq = dx * dx + dzSq;
                    if (distSq < innerSq || distSq > outerSq)
                        continue;

                    var sum = source[y, x]
                        + source[y - 1, x] + source[y + 1, x]
                        + source[y, x - 1] + source[y, x + 1]
                        + source[y - 1, x - 1] + source[y - 1, x + 1]
                        + source[y + 1, x - 1] + source[y + 1, x + 1];
                    heights[y, x] = Mathf.Lerp(source[y, x], sum / 9f, 0.38f);
                }
            }
        }
    }
}
#endif
