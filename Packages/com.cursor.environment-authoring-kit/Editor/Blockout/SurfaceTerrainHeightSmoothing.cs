#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Removes blocky / checkerboard height artifacts from coarse DEM grids or aligned sculpt noise.
    /// </summary>
    static class SurfaceTerrainHeightSmoothing
    {
        public static int DeCheckerboardOnTerrain(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float strength = 0.38f)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "De-checkerboard terrain");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var changed = DeCheckerboardHeights(
                heights,
                res,
                terrain,
                centerWorld,
                extentMeters,
                strength);
            if (changed > 0)
                data.SetHeights(0, 0, heights);

            return changed;
        }

        public static int DeCheckerboardHeights(
            float[,] heights,
            int res,
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float strength)
        {
            if (heights == null || res < 5)
                return 0;

            strength = Mathf.Clamp(strength, 0.1f, 0.65f);
            var inner = extentMeters * 0.08f;
            var outer = extentMeters * 1.05f;
            var changed = 0;

            for (var pass = 0; pass < 2; pass++)
            {
                var copy = (float[,])heights.Clone();
                for (var y = 1; y < res - 1; y++)
                {
                    for (var x = 1; x < res - 1; x++)
                    {
                        if (!InPlayDisk(terrain, x, y, res, centerWorld, inner, outer))
                            continue;

                        var sum = copy[y, x]
                            + copy[y - 1, x] + copy[y + 1, x]
                            + copy[y, x - 1] + copy[y, x + 1]
                            + copy[y - 1, x - 1] + copy[y - 1, x + 1]
                            + copy[y + 1, x - 1] + copy[y + 1, x + 1];
                        var before = heights[y, x];
                        heights[y, x] = Mathf.Lerp(before, sum / 9f, strength);
                        if (Mathf.Abs(heights[y, x] - before) > 0.00004f)
                            changed++;
                    }
                }
            }

            return changed;
        }

        static bool InPlayDisk(
            Terrain terrain,
            int x,
            int y,
            int res,
            Vector3 centerWorld,
            float innerMeters,
            float outerMeters)
        {
            var extent = Mathf.Max(outerMeters / 1.05f, 1f);
            return SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                terrain,
                x,
                y,
                res,
                centerWorld,
                extent,
                innerMeters / extent,
                outerMeters / extent);
        }

        sealed class DeCheckQueueSession
        {
            public Terrain Terrain;
            public Vector3 CenterWorld;
            public float ExtentMeters;
            public float Strength;
            public float[,] Heights;
            public float[,] Copy;
            public int Res;
            public int Pass;
            public int RowY;
            public int Changed;
            public Action<int> OnComplete;
        }

        static int DeCheckRowChunk(int res)
        {
            if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                return res >= 1025 ? 32 : res >= 513 ? 96 : 128;
            return res >= 1025 ? 8 : res >= 513 ? 16 : 48;
        }

        /// <summary>Row-banded de-checkerboard for terrain ladder (avoids 513² sync freeze).</summary>
        public static void QueueDeCheckerboardOnTerrain(
            Terrain terrain,
            Vector3 centerWorld,
            float extentMeters,
            float strength,
            Action<int> onComplete)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                onComplete?.Invoke(0);
                return;
            }

            var data = terrain.terrainData;
            Undo.RecordObject(data, "De-checkerboard terrain");
            var res = data.heightmapResolution;
            var session = new DeCheckQueueSession
            {
                Terrain = terrain,
                CenterWorld = centerWorld,
                ExtentMeters = extentMeters,
                Strength = strength,
                Heights = data.GetHeights(0, 0, res, res),
                Res = res,
                Pass = 0,
                RowY = 1,
                OnComplete = onComplete,
            };
            session.Copy = (float[,])session.Heights.Clone();
            CaveBuildActionPacing.ScheduleLight(
                () => RunDeCheckRowBand(session),
                CaveBuildPipelineDomains.QueueLabel("de-checkerboard rows"));
        }

        static void RunDeCheckRowBand(DeCheckQueueSession session)
        {
            if (session?.Heights == null || session.Terrain == null)
            {
                session?.OnComplete?.Invoke(0);
                return;
            }

            var res = session.Res;
            var strength = Mathf.Clamp(session.Strength, 0.1f, 0.65f);
            var inner = session.ExtentMeters * 0.08f;
            var outer = session.ExtentMeters * 1.05f;
            var yEnd = Mathf.Min(res - 1, session.RowY + DeCheckRowChunk(res));

            for (var y = session.RowY; y < yEnd; y++)
            {
                for (var x = 1; x < res - 1; x++)
                {
                    if (!InPlayDisk(session.Terrain, x, y, res, session.CenterWorld, inner, outer))
                        continue;

                    var sum = session.Copy[y, x]
                        + session.Copy[y - 1, x] + session.Copy[y + 1, x]
                        + session.Copy[y, x - 1] + session.Copy[y, x + 1]
                        + session.Copy[y - 1, x - 1] + session.Copy[y - 1, x + 1]
                        + session.Copy[y + 1, x - 1] + session.Copy[y + 1, x + 1];
                    var before = session.Heights[y, x];
                    session.Heights[y, x] = Mathf.Lerp(before, sum / 9f, strength);
                    if (Mathf.Abs(session.Heights[y, x] - before) > 0.00004f)
                        session.Changed++;
                }
            }

            session.RowY = yEnd;
            CaveBuildActionPacing.TouchQueueActivity();
            if (session.RowY < res - 1)
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => RunDeCheckRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel("de-checkerboard rows"));
                return;
            }

            session.Pass++;
            if (session.Pass < 2)
            {
                session.Copy = (float[,])session.Heights.Clone();
                session.RowY = 1;
                CaveBuildActionPacing.ScheduleLight(
                    () => RunDeCheckRowBand(session),
                    CaveBuildPipelineDomains.QueueLabel("de-checkerboard pass"));
                return;
            }

            if (session.Changed > 0)
                CaveBuildTerrainHeightmapMemory.ApplyHeightSlice(
                    session.Terrain,
                    0,
                    0,
                    session.Heights,
                    requestDelayLod: false);

            CaveBuildTerrainHeightmapMemory.Release(ref session.Heights);
            CaveBuildTerrainHeightmapMemory.Release(ref session.Copy);
            session.OnComplete?.Invoke(session.Changed);
        }
    }
}
#endif
