#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Heightmap analysis for terrain ladder grading (craters, spikes, playable slope).</summary>
    public static class SurfaceTerrainHeightAnalyzer
    {
        public struct HeightfieldReport
        {
            public int resolution;
            public int craterCellCount;
            public int spikeCellCount;
            public float maxCraterDepthNorm;
            public float maxSpikeHeightNorm;
            public float meanAbsSlope;
            public int sampleCells;
        }

        const float CraterDepthThreshold = 0.018f;
        const float SpikeRaiseThreshold = 0.022f;
        const int RingInner = 2;
        const int RingOuter = 6;

        public static HeightfieldReport Analyze(Terrain terrain, Vector3 centerWorld, float extentMeters)
        {
            var report = new HeightfieldReport();
            if (terrain == null || terrain.terrainData == null)
                return report;

            var data = terrain.terrainData;
            var res = data.heightmapResolution;
            report.resolution = res;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;

            for (var y = RingOuter; y < res - RingOuter; y++)
            {
                for (var x = RingOuter; x < res - RingOuter; x++)
                {
                    if (!SurfaceTerrainPlayRegion.InPlayAnnulusOnTerrain(
                            terrain,
                            x,
                            y,
                            res,
                            centerWorld,
                            extentMeters,
                            innerFraction: 0.08f,
                            outerFraction: 1.05f))
                        continue;

                    report.sampleCells++;
                    var centerH = heights[y, x];
                    var ringSum = 0f;
                    var ringN = 0;
                    for (var dy = -RingOuter; dy <= RingOuter; dy++)
                    {
                        for (var dx = -RingOuter; dx <= RingOuter; dx++)
                        {
                            var r2 = dx * dx + dy * dy;
                            if (r2 < RingInner * RingInner || r2 > RingOuter * RingOuter)
                                continue;
                            ringSum += heights[y + dy, x + dx];
                            ringN++;
                        }
                    }

                    if (ringN < 4)
                        continue;

                    var ringAvg = ringSum / ringN;
                    var delta = centerH - ringAvg;
                    if (delta < -CraterDepthThreshold)
                    {
                        report.craterCellCount++;
                        report.maxCraterDepthNorm = Mathf.Max(report.maxCraterDepthNorm, -delta);
                    }
                    else if (delta > SpikeRaiseThreshold)
                    {
                        report.spikeCellCount++;
                        report.maxSpikeHeightNorm = Mathf.Max(report.maxSpikeHeightNorm, delta);
                    }

                    if (x > RingOuter && y > RingOuter)
                    {
                        var slope = Mathf.Abs(heights[y, x] - heights[y, x - 1]) +
                                      Mathf.Abs(heights[y, x] - heights[y, y - 1]);
                        report.meanAbsSlope += slope;
                    }
                }
            }

            if (report.sampleCells > 0)
                report.meanAbsSlope /= report.sampleCells;

            return report;
        }

        public static int ScoreHeightfield(HeightfieldReport r)
        {
            if (r.sampleCells < 100)
                return 40;

            var score = 100;
            var craterClusters = r.craterCellCount / 12;
            score -= Mathf.Min(45, craterClusters * 8);
            score -= Mathf.Min(20, (int)(r.maxCraterDepthNorm * 800f));
            score -= Mathf.Min(15, r.spikeCellCount / 20);
            if (r.meanAbsSlope > 0.035f)
                score -= Mathf.Min(12, (int)((r.meanAbsSlope - 0.035f) * 400f));

            return Mathf.Clamp(score, 0, 100);
        }
    }
}
#endif
