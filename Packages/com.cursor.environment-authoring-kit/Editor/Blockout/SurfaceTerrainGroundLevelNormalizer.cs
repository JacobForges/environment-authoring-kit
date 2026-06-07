#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Treats the highest walkable terrain peak in the play region as ground level; lowers the rest so the cave sits under the surface contour.
    /// Peak scan runs entirely in memory; terrain is written once after evaluation (never per-row during apply).
    /// </summary>
    public static class SurfaceTerrainGroundLevelNormalizer
    {
        public const float MaxSlopeClampDegrees = 38f;
        const float SkipShiftWorldEpsilonMeters = 0.35f;

        sealed class NormalizeSession
        {
            public Terrain Terrain;
            public SceneGroundInfo Ground;
            public Vector3 CenterWorld;
            public float ExtentMeters;
            public float[,] Heights;
            public int Res;
            public int RowY;
            public Vector3 Size;
            public Vector3 Origin;
            public float Outer;
            public float TargetPeakY;
            public float MaxWorldY = float.MinValue;
            public float MinWorldY = float.MaxValue;
            public float DeltaNorm;
            public int Changed;
            public System.Action<string> OnComplete;
        }

        enum NormalizeStep
        {
            ScanRows,
            ApplyInMemory,
            CommitHeights,
        }

        static int NormalizeRowChunk(int res) =>
            res >= 1025 ? 8 : res >= 513 ? 12 : 24;

        /// <summary>Non-blocking peak normalize — scan → in-memory shift → single terrain commit.</summary>
        public static void QueueNormalizePeakToGroundLevel(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            SceneGroundInfo ground,
            System.Action<string> onComplete)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke("No terrain to normalize.");
                return;
            }

            var data = terrain.terrainData;
            var session = new NormalizeSession
            {
                Terrain = terrain,
                Ground = ground,
                CenterWorld = centerWorld,
                ExtentMeters = extentMeters,
                Heights = new float[data.heightmapResolution, data.heightmapResolution],
                Res = data.heightmapResolution,
                Size = data.size,
                Origin = terrain.transform.position,
                Outer = extentMeters * 1.05f,
                TargetPeakY = ground != null && ground.HasAnchor
                    ? ground.SurfaceY
                    : terrain.transform.position.y + data.size.y * 0.5f,
                OnComplete = onComplete,
            };
            ScheduleNormalizeStep(session, NormalizeStep.ScanRows);
        }

        static void ScheduleNormalizeStep(NormalizeSession session, NormalizeStep step) =>
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunNormalizeStep(session, step));

        static void RunNormalizeStep(NormalizeSession session, NormalizeStep step)
        {
            var chunk = NormalizeRowChunk(session.Res);
            var yEnd = Mathf.Min(session.Res, session.RowY + chunk);
            var resM1 = Mathf.Max(1, session.Res - 1);
            var cx = session.CenterWorld.x;
            var cz = session.CenterWorld.z;
            var outerSq = session.Outer * session.Outer;

            switch (step)
            {
                case NormalizeStep.ScanRows:
                {
                    var rowCount = yEnd - session.RowY;
                    if (rowCount > 0)
                    {
                        var slice = session.Terrain.terrainData.GetHeights(0, session.RowY, session.Res, rowCount);
                        for (var y = 0; y < rowCount; y++)
                        {
                            var gy = session.RowY + y;
                            var wz = session.Origin.z + gy / (float)resM1 * session.Size.z;
                            var dz = wz - cz;

                            for (var x = 0; x < session.Res; x++)
                            {
                                var h = slice[y, x];
                                session.Heights[gy, x] = h;

                                if (dz * dz > outerSq)
                                    continue;

                                var wx = session.Origin.x + x / (float)resM1 * session.Size.x;
                                var dx = wx - cx;
                                if (dx * dx + dz * dz > outerSq)
                                    continue;

                                var wy = session.Origin.y + h * session.Size.y;
                                if (wy > session.MaxWorldY)
                                    session.MaxWorldY = wy;
                                if (wy < session.MinWorldY)
                                    session.MinWorldY = wy;
                            }
                        }
                    }

                    session.RowY = yEnd;
                    if (session.RowY < session.Res)
                    {
                        CaveBuildRunStatusPublisher.PulseSubOperation(
                            "surface finish",
                            $"peak scan rows {session.RowY}/{session.Res}");
                        ScheduleNormalizeStep(session, NormalizeStep.ScanRows);
                        return;
                    }

                    if (session.MaxWorldY < float.MinValue + 1f)
                    {
                        session.OnComplete?.Invoke("No height samples in play region.");
                        return;
                    }

                    session.DeltaNorm = (session.TargetPeakY - session.MaxWorldY) / session.Size.y;
                    var shiftMeters = session.DeltaNorm * session.Size.y;
                    if (Mathf.Abs(shiftMeters) < SkipShiftWorldEpsilonMeters)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] Peak normalize — shift {shiftMeters:F2}m within epsilon; no heightmap write.",
                            forceUnityConsole: true);
                        FinishNormalizeOnNextFrame(session, "Peak already at ground level (no shift).");
                        return;
                    }

                    session.RowY = 0;
                    CaveBuildEditorLog.LogSurface(
                        $"[Surface] Peak evaluation done (max {session.MaxWorldY:F1}m → target {session.TargetPeakY:F1}m, shift {shiftMeters:F1}m) — applying in memory…",
                        forceUnityConsole: true);
                    ScheduleNormalizeStep(session, NormalizeStep.ApplyInMemory);
                    return;
                }

                case NormalizeStep.ApplyInMemory:
                {
                    for (var y = session.RowY; y < yEnd; y++)
                    {
                        var wz = session.Origin.z + y / (float)resM1 * session.Size.z;
                        var dz = wz - cz;
                        if (dz * dz > outerSq)
                            continue;

                        for (var x = 0; x < session.Res; x++)
                        {
                            var wx = session.Origin.x + x / (float)resM1 * session.Size.x;
                            var dx = wx - cx;
                            if (dx * dx + dz * dz > outerSq)
                                continue;

                            var before = session.Heights[y, x];
                            session.Heights[y, x] = Mathf.Clamp01(before + session.DeltaNorm);
                            if (Mathf.Abs(session.Heights[y, x] - before) > 0.00005f)
                                session.Changed++;
                        }
                    }

                    session.RowY = yEnd;
                    if (session.RowY < session.Res)
                    {
                        CaveBuildRunStatusPublisher.PulseSubOperation(
                            "surface finish",
                            $"peak shift (memory) rows {session.RowY}/{session.Res}");
                        ScheduleNormalizeStep(session, NormalizeStep.ApplyInMemory);
                        return;
                    }

                    CaveBuildRunStatusPublisher.PulseSubOperation("surface finish", "peak commit heightmap");
                    ScheduleNormalizeStep(session, NormalizeStep.CommitHeights);
                    return;
                }

                case NormalizeStep.CommitHeights:
                {
                    CaveBuildMicroTerrainHeightmap.QueueWriteFull(
                        session.Terrain,
                        session.Heights,
                        "surface finish",
                        "peak commit",
                        () =>
                        {
                            var message =
                                $"Peak normalized to ground Y={session.TargetPeakY:F1} (shift {session.DeltaNorm * session.Size.y:F1}m, {session.Changed} cells, " +
                                $"range {session.MinWorldY:F1}–{session.MaxWorldY:F1}m → peak at surface).";
                            CaveBuildPipelineLog.Info(message, "Surface-Terrain");
                            FinishNormalizeOnNextFrame(session, message);
                        });
                    break;
                }
            }
        }

        static void FinishNormalizeOnNextFrame(NormalizeSession session, string message)
        {
            CaveBuildActionPacing.ScheduleNextEditorFrame(() =>
            {
                EditorUtility.ClearProgressBar();
                if (session?.Terrain != null)
                {
                    session.Terrain.Flush();
                    if (session.Ground != null)
                        session.Ground.SurfaceY = session.TargetPeakY;
                }

                CaveBuildEditorLog.LogSurface(
                    "[Surface] Peak normalize done — continuing surface finish.",
                    forceUnityConsole: true);
                session?.OnComplete?.Invoke(message);
            });
        }

        public static bool NormalizePeakToGroundLevel(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (terrain == null || terrain.terrainData == null)
            {
                message = "No terrain to normalize.";
                return false;
            }

            var targetPeakY = ground != null && ground.HasAnchor
                ? ground.SurfaceY
                : terrain.transform.position.y + terrain.terrainData.size.y * 0.5f;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Normalize terrain peak to ground level");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var outer = extentMeters * 1.05f;
            var maxWorldY = float.MinValue;
            var minWorldY = float.MaxValue;

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    if (Vector2.Distance(new Vector2(wx, wz), new Vector2(centerWorld.x, centerWorld.z)) > outer)
                        continue;

                    var wy = origin.y + heights[y, x] * size.y;
                    if (wy > maxWorldY)
                        maxWorldY = wy;
                    if (wy < minWorldY)
                        minWorldY = wy;
                }
            }

            if (maxWorldY < float.MinValue + 1f)
            {
                message = "No height samples in play region.";
                return false;
            }

            var deltaNorm = (targetPeakY - maxWorldY) / size.y;
            if (Mathf.Abs(deltaNorm * size.y) < SkipShiftWorldEpsilonMeters)
            {
                message = "Peak already at ground level (no shift).";
                return true;
            }

            var changed = 0;
            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    if (Vector2.Distance(new Vector2(wx, wz), new Vector2(centerWorld.x, centerWorld.z)) > outer)
                        continue;

                    var before = heights[y, x];
                    heights[y, x] = Mathf.Clamp01(before + deltaNorm);
                    if (Mathf.Abs(heights[y, x] - before) > 0.00005f)
                        changed++;
                }
            }

            SmoothSteepCells(heights, res, centerWorld, origin, size, outer, passes: 2);
            CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(terrain, 0, 0, heights, requestDelayLod: true);
            CaveBuildTerrainHeightmapMemory.FlushAllSurfaceTerrains(terrain);

            if (ground != null)
                ground.SurfaceY = targetPeakY;

            message =
                $"Peak normalized to ground Y={targetPeakY:F1} (shift {deltaNorm * size.y:F1}m, {changed} cells, " +
                $"range {minWorldY:F1}–{maxWorldY:F1}m → peak at surface).";
            CaveBuildPipelineLog.Info(message, "Surface-Terrain");
            return true;
        }

        static void SmoothSteepCells(
            float[,] heights,
            int res,
            Vector3 centerWorld,
            Vector3 origin,
            Vector3 size,
            float outer,
            int passes)
        {
            for (var pass = 0; pass < passes; pass++)
            {
                var copy = (float[,])heights.Clone();
                for (var y = 1; y < res - 1; y++)
                {
                    for (var x = 1; x < res - 1; x++)
                    {
                        var wx = origin.x + x / (float)(res - 1) * size.x;
                        var wz = origin.z + y / (float)(res - 1) * size.z;
                        if (Vector2.Distance(new Vector2(wx, wz), new Vector2(centerWorld.x, centerWorld.z)) > outer)
                            continue;

                        var avg = (copy[y - 1, x] + copy[y + 1, x] + copy[y, x - 1] + copy[y, x + 1]) * 0.25f;
                        var slope = Mathf.Abs(copy[y, x] - avg) * size.y / (size.x / res);
                        if (slope > 0.08f)
                            heights[y, x] = Mathf.Lerp(copy[y, x], avg, 0.45f);
                    }
                }
            }
        }
    }
}
#endif
