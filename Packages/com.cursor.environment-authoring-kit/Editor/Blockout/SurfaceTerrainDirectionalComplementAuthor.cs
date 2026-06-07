#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Eight (or 4–16) axis-aligned complement passes after authoritative Florida DEM.
    /// Each pass smooths and adds light detail along one direction so prior passes are preserved
    /// and later passes hide seams — no radial FBM lerp or sector wedges.
    /// </summary>
    static class SurfaceTerrainDirectionalComplementAuthor
    {
        public const int DefaultDirectionCount = 8;

        static int _queuedActive;

        public static bool IsQueuedActive => _queuedActive > 0;

        public static int ResolveDirectionCount(WorldGenerationRequest request)
        {
            var count = request?.SurfaceDirectionCount ?? 0;
            if (count <= 0)
                count = DefaultDirectionCount;
            return Mathf.Clamp(count, 4, 16);
        }

        public static void QueuePasses(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            int seed,
            int directionCount,
            float preserveInnerRadiusMeters,
            bool mountains,
            bool water,
            bool roads,
            System.Action onComplete)
        {
            if (terrain == null || onComplete == null)
            {
                onComplete?.Invoke();
                return;
            }

            directionCount = Mathf.Clamp(directionCount, 4, 16);
            var state = new QueuedState
            {
                Terrain = terrain,
                Center = centerWorld,
                Extent = extentMeters,
                Seed = seed,
                DirectionCount = directionCount,
                DirectionIndex = 0,
                PreserveInner = preserveInnerRadiusMeters,
                Mountains = mountains,
                Water = water,
                Roads = roads,
                OnComplete = onComplete,
            };
            state.Prepare();
            _queuedActive++;
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunQueuedDirection(state));
        }

        sealed class QueuedState
        {
            public Terrain Terrain;
            public Vector3 Center;
            public float Extent;
            public int Seed;
            public int DirectionCount;
            public int DirectionIndex;
            public float PreserveInner;
            public bool Mountains;
            public bool Water;
            public bool Roads;
            public float[,] Heights;
            public int Res;
            public Vector3 Size;
            public Vector3 Origin;
            public System.Action OnComplete;

            public void Prepare()
            {
                var data = Terrain.terrainData;
                Res = data.heightmapResolution;
                Heights = data.GetHeights(0, 0, Res, Res);
                Size = data.size;
                Origin = Terrain.transform.position;
                Undo.RecordObject(data, "Surface directional complement passes");
            }
        }

        static void RunQueuedDirection(QueuedState state)
        {
            if (state?.Terrain == null)
            {
                FinishQueue(state);
                return;
            }

            var passNum = state.DirectionIndex + 1;
            EditorUtility.DisplayProgressBar(
                "Environment Kit",
                $"[Surface] directional complement {passNum}/{state.DirectionCount}…",
                0.2f + 0.55f * (state.DirectionIndex / (float)Mathf.Max(1, state.DirectionCount)));

            if (passNum == 1 || passNum == state.DirectionCount)
            {
                CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                    $"[Surface] directional pass {passNum}/{state.DirectionCount} (axis complement, full heightmap)");
            }

            ApplyDirectionalComplementPass(
                state.Heights,
                state.Res,
                state.Size,
                state.Origin,
                state.Center,
                state.Extent,
                state.PreserveInner,
                state.Seed,
                state.DirectionIndex,
                state.DirectionCount,
                state.Mountains,
                state.Water,
                state.Roads);

            state.Terrain.terrainData.SetHeights(0, 0, state.Heights);
            state.Terrain.Flush();
            EditorApplication.QueuePlayerLoopUpdate();

            if (passNum == 1 || passNum == state.DirectionCount)
            {
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] directional complement {passNum}/{state.DirectionCount} committed.",
                    forceUnityConsole: true);
            }

            state.DirectionIndex++;
            if (state.DirectionIndex < state.DirectionCount)
            {
                CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunQueuedDirection(state));
                return;
            }

            FinishTerrain(state);
            FinishQueue(state);
        }

        static void FinishTerrain(QueuedState state)
        {
            try
            {
                SurfaceTerrainHeightSmoothing.DeCheckerboardOnTerrain(
                    state.Terrain,
                    state.Center,
                    state.Extent,
                    strength: 0.28f);
                state.Terrain.Flush();
                CaveBuildEditorLog.LogSurface(
                    $"[Surface] Directional complement complete ({state.DirectionCount}-axis chain + de-checkerboard; no radial FBM).",
                    forceUnityConsole: true);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static void FinishQueue(QueuedState state)
        {
            EditorUtility.ClearProgressBar();
            _queuedActive = Mathf.Max(0, _queuedActive - 1);
            state?.OnComplete?.Invoke();
        }

        static void ApplyDirectionalComplementPass(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 centerWorld,
            float extentMeters,
            float preserveInner,
            int seed,
            int directionIndex,
            int directionCount,
            bool mountains,
            bool water,
            bool roads)
        {
            if (res < 5)
                return;

            var angle = (directionIndex + 0.5f) / directionCount * Mathf.PI * 2f;
            var dirX = Mathf.Cos(angle);
            var dirZ = Mathf.Sin(angle);
            var perpX = -dirZ;
            var perpZ = dirX;

            var passT = (directionIndex + 1f) / Mathf.Max(1, directionCount);
            var smoothStrength = Mathf.Lerp(0.09f, 0.16f, passT);
            var detailStrength = Mathf.Lerp(0.008f, 0.018f, passT);

            var inner = preserveInner > 0f ? preserveInner : extentMeters * 0.12f;
            var outer = extentMeters * 0.98f;
            var cx = centerWorld.x;
            var cz = centerWorld.z;
            var resM1 = Mathf.Max(1, res - 1);
            var invX = size.x / resM1;
            var invZ = size.z / resM1;
            var ox = origin.x;
            var oz = origin.z;

            var source = (float[,])heights.Clone();
            var stepCells = Mathf.Max(1, Mathf.RoundToInt(2f / Mathf.Min(invX, invZ)));

            for (var y = 1; y < res - 1; y++)
            {
                var wz = oz + y * invZ;
                var dz = Mathf.Abs(wz - cz);
                if (dz > outer)
                    continue;

                for (var x = 1; x < res - 1; x++)
                {
                    var wx = ox + x * invX;
                    var dx = Mathf.Abs(wx - cx);
                    if (Mathf.Max(dx, dz) > outer)
                        continue;
                    if (Mathf.Max(dx, dz) < inner)
                        continue;

                    var sum = source[y, x];
                    var count = 1;
                    for (var s = -stepCells; s <= stepCells; s++)
                    {
                        if (s == 0)
                            continue;
                        var nx = x + Mathf.RoundToInt(dirX * s);
                        var ny = y + Mathf.RoundToInt(dirZ * s);
                        if (nx < 0 || nx >= res || ny < 0 || ny >= res)
                            continue;
                        sum += source[ny, nx];
                        count++;
                    }

                    var smoothed = sum / count;
                    heights[y, x] = Mathf.Clamp01(Mathf.Lerp(source[y, x], smoothed, smoothStrength));

                    var along = (wx - cx) * dirX + (wz - cz) * dirZ;
                    var across = (wx - cx) * perpX + (wz - cz) * perpZ;
                    var nAlong = Mathf.PerlinNoise(
                        along * 0.0038f + seed * 0.17f + directionIndex * 1.3f,
                        across * 0.011f + seed * 0.09f);
                    var nAcross = Mathf.PerlinNoise(
                        along * 0.0092f + directionIndex * 2.7f,
                        across * 0.0046f + seed * 0.31f);
                    var detail = (nAlong * 0.55f + nAcross * 0.45f - 0.5f) * detailStrength;
                    heights[y, x] = Mathf.Clamp01(heights[y, x] + detail);

                    if (mountains)
                    {
                        var normAlong = normAlongFor(along, extentMeters);
                        var ridgeAcross = Mathf.Exp(-across * across / (extentMeters * extentMeters * 0.08f));
                        if (normAlong > 0.28f && normAlong < 0.92f)
                        {
                            var ridge = Mathf.SmoothStep(0f, 1f, (normAlong - 0.28f) / 0.55f) * ridgeAcross;
                            heights[y, x] = Mathf.Clamp01(heights[y, x] + ridge * 0.006f * passT);
                        }
                    }

                    if (water && directionIndex % 2 == 0)
                    {
                        var normAlong = normAlongFor(along, extentMeters);
                        if (normAlong > 0.96f)
                        {
                            var bowlAlong = 1f - Mathf.Abs(normAlong - 0.98f) / 0.05f;
                            if (bowlAlong > 0.35f)
                                heights[y, x] = Mathf.Max(0f, heights[y, x] - bowlAlong * 0.00055f * passT);
                        }
                    }

                    if (roads)
                    {
                        var roadAcross = Mathf.Exp(-across * across / (extentMeters * 0.06f));
                        var roadAlong = 1f - Mathf.SmoothStep(0f, 0.45f, Mathf.Abs(along) / extentMeters);
                        heights[y, x] = Mathf.Max(0f, heights[y, x] - roadAcross * roadAlong * 0.003f * passT);
                    }
                }
            }
        }

        static float normAlongFor(float along, float extent) =>
            Mathf.Clamp01((along + extent) / (2f * extent));
    }
}
#endif
