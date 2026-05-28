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
        public const int DefaultPassCount = 12;
        public const int RefinementPassCountAfterFloridaDem = 5;
        public const int NeighborTilePassCount = 8;
        public const int MaxPassCount = 24;
        const float SculptMacroAmplitude = 0.21f;
        const float SculptMacroAmplitudeAfterDem = 0.19f;
        /// <summary>Push sculpted rows to Terrain after each pass so Scene view updates (not only at end).</summary>
        const bool PreviewEachPassOnTerrain = true;
        /// <summary>Upload each row band during a pass so sculpt changes are visible while the pass runs.</summary>
        const bool LivePreviewEachRowChunk = true;
        /// <summary>Outer-band hydro only — kept outside playable grader annulus (0.08–0.72× extent).</summary>
        const float SculptWaterBowlAmplitude = 0.00035f;
        const float SculptRoadCutAmplitude = 0.025f;
        /// <summary>Post-sculpt heightmap blur is optional; FBM lerp + commit-time polish is enough.</summary>
        const int ProgressBarEveryNChunks = 6;

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
            return Mathf.Clamp(
                Mathf.Min(RefinementPassCountAfterFloridaDem, ResolvePassCount(requestedPassCount)),
                3,
                MaxPassCount);
        }

        public static int PassCountForNeighborTile(int mainPassCount) =>
            Mathf.Clamp(Mathf.Min(NeighborTilePassCount, mainPassCount), 1, MaxPassCount);

        static int RowChunkSizeFor(int res)
        {
            if (res >= 1025)
                return 4;
            if (res >= 513)
                return 4;
            return EnvironmentKitHardwareBudget.Active.ConserveGpuMemory ? 8 : 12;
        }

        public static bool IsQueuedPassesActive => _queuedPassesActive > 0;

        public static void ResetQueuedPassesState() => _queuedPassesActive = 0;

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
                OnComplete = onComplete,
            };
            state.Prepare();
            _progressThrottle = 0;
            ScheduleQueuedPass(state);
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
            public int PolishRowY = 1;
            public float[,] PolishSource;
            public bool HeightsFlushedIncremental;
            public bool RefinementAfterDem;
            public System.Action OnComplete;

            public void Prepare()
            {
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
                PolishRowY = 1;
                PolishSource = null;
                HeightsFlushedIncremental = false;
                Undo.RecordObject(data, "Surface centered terrain passes");
            }

            public void EnsureHeightsLoaded()
            {
                if (Heights != null || Terrain?.terrainData == null)
                    return;
                Heights = Terrain.terrainData.GetHeights(0, 0, Res, Res);
            }

            /// <returns>True when the current sculpt pass (all row chunks) is finished.</returns>
            public bool RunRowChunk()
            {
                EnsureHeightsLoaded();
                var chunk = RowChunkSizeFor(Res);
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

            public void FlushCommitRows(int yStart, int yEnd)
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

                Terrain.terrainData.SetHeights(0, yStart, slice);
                HeightsFlushedIncremental = true;
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
            if (CaveBuildActionPacing.QueuedCount > 8)
            {
                CaveBuildActionPacing.SchedulePriorityFirstStep(
                    () => RunQueuedPass(state),
                    label,
                    CaveBuildActionPacing.ActionWeight.Light);
            }
            else
            {
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunQueuedPass(state));
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
            if (CaveBuildActionPacing.IsBusy || CaveBuildActionPacing.QueuedCount > 0)
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

            if (state.PassRowStart == 0 && (passNum == 1 || passNum % 10 == 0))
            {
                CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                    $"[Surface] terrain pass {passNum}/{state.PassCount} (world FBM blend)");
            }

            if (state.PassRowStart == 0 || state.PassRowStart % Mathf.Max(16, state.Res / 8) == 0)
            {
                CaveBuildRunStatusPublisher.PulseSubOperation(
                    "terrain sculpt",
                    $"pass {passNum}/{state.PassCount} rows {state.PassRowStart}/{state.Res}");
            }

            var chunk = RowChunkSizeFor(state.Res);
            var rowBandStart = Mathf.Max(0, state.PassRowStart - chunk);
            var passFinished = state.RunRowChunk();
            if (!passFinished && LivePreviewEachRowChunk && state.PassRowStart > 0)
            {
                state.FlushCommitRows(rowBandStart, state.PassRowStart);
                CaveBuildLiveSceneFlushUtility.FlushWorldView(state.Terrain);
            }

            if (passFinished)
            {
                if (PreviewEachPassOnTerrain)
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

            BeginHeightmapUpload(state);
        }

        static void BeginHeightmapUpload(QueuedPassState state)
        {
            state.PolishSource = null;
            state.CommitRowStart = 0;
            CaveBuildEditorLog.LogSurface(
                "[Surface] Sculpt complete — uploading heightmap row bands…",
                forceUnityConsole: true);
            ScheduleCommitRows(state);
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

            var chunk = RowChunkSizeFor(state.Res) * 8;
            var yEnd = Mathf.Min(state.Res, state.CommitRowStart + chunk);
            state.FlushCommitRows(state.CommitRowStart, yEnd);
            state.CommitRowStart = yEnd;
            CaveBuildLiveSceneFlushUtility.FlushWorldView(state.Terrain);

            if (state.CommitRowStart < state.Res)
            {
                ScheduleCommitRows(state);
                return;
            }

            CaveBuildActionPacing.ScheduleNextEditorFrame(() => FinishCommitPolish(state));
        }

        static void FinishCommitPolish(QueuedPassState state)
        {
            if (state?.Terrain == null)
            {
                EditorUtility.ClearProgressBar();
                _queuedPassesActive = Mathf.Max(0, _queuedPassesActive - 1);
                state?.OnComplete?.Invoke();
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
                EditorUtility.ClearProgressBar();
                _queuedPassesActive = Mathf.Max(0, _queuedPassesActive - 1);
                if (_queuedPassesActive <= 0)
                    EditorApplication.update -= SculptPassWatchdog;
                state.OnComplete?.Invoke();
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
                h += fbm * macroAmp * radialMask * mountainGate * 0.85f;
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
