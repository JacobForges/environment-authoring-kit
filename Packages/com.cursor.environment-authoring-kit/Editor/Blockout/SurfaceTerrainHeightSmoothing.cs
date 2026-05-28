#if UNITY_EDITOR
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
    }
}
#endif
