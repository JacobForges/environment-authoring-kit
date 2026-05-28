#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Stamps ResearchCache Florida LiDAR close-up segment relief (meters-accurate; not county-wide satellite stretch).
    /// </summary>
    public static class SurfaceLidarTerrainAuthor
    {
        public const string HillshadesIndexRel =
            "Assets/EnvironmentKit/ResearchCache/images/florida-hillshades-index.json";

        [Serializable]
        public class HillshadesIndexFile
        {
            public CountyEntry[] counties;
            public string dataNote;
        }

        [Serializable]
        public class CountyEntry
        {
            public string countyId;
            public string path;
            public string source;
        }

        /// <summary>Fraction of extent around ground anchor where existing land heights are preserved (no overwrite).</summary>
        public const float MainLandPreserveRadiusFraction = 0.28f;

        /// <summary>Max vertical relief from hillshade / elev guide (meters) — must read as walkable hills, not ripples.</summary>
        public const float MaxReliefMeters = 24f;

        public static bool ApplyFromResearchCache(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            out string message)
        {
            message = null;
            if (terrain == null || terrain.terrainData == null)
            {
                message = "No terrain to stamp.";
                return false;
            }

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var indexPath = Path.Combine(hub, HillshadesIndexRel);
            if (!File.Exists(indexPath))
            {
                message =
                    "No florida-hillshades-index.json — run research pull or npm run sync-florida-hillshades in Tools/cave-grader.";
                return false;
            }

            HillshadesIndexFile index;
            try
            {
                index = JsonUtility.FromJson<HillshadesIndexFile>(File.ReadAllText(indexPath));
            }
            catch (Exception ex)
            {
                message = "Invalid hillshades index: " + ex.Message;
                return false;
            }

            if (index?.counties == null || index.counties.Length == 0)
            {
                message = "Hillshades index has no counties.";
                return false;
            }

            var pick = index.counties[Math.Abs(seed) % index.counties.Length];
            var rel = pick.path?.Replace('\\', '/');
            if (string.IsNullOrEmpty(rel))
            {
                message = "Hillshade entry missing path.";
                return false;
            }

            var pngPath = Path.Combine(hub, rel);
            if (!File.Exists(pngPath))
            {
                message = "Missing hillshade PNG: " + rel;
                return false;
            }

            var tex = LoadPng(pngPath);
            if (tex == null)
            {
                message = "Could not load hillshade: " + rel;
                return false;
            }

            try
            {
                if (SurfaceDemGeoreferenceAuthor.TryLoadGeorefForSeed(seed, out var georef, out _))
                {
                    var stamped = StampSegmentHeights(terrain, tex, groundCenter, extentMeters, georef, seed);
                    var scale = georef.HasCloseUpSegment
                        ? $"{georef.SegmentSizeMeters:F0}m segment"
                        : "local UV (re-run sync-florida-hillshades)";
                    message =
                        $"LiDAR segment stamp ({pick.countyId}, {scale}) — {stamped} cells.";
                    CaveBuildPipelineLog.Info(message, "Surface-LiDAR");
                    return stamped > 0;
                }

                var legacy = StampHeightsLegacyStretch(terrain, tex, groundCenter, extentMeters);
                message =
                    $"LiDAR legacy stretch ({pick.countyId}) — {legacy} cells. Run: npm run sync-florida-hillshades in Tools/cave-grader.";
                CaveBuildPipelineLog.Warn(message, "Surface-LiDAR");
                return legacy > 0;
            }
            finally
            {
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        static Texture2D LoadPng(string absolutePath)
        {
            var bytes = File.ReadAllBytes(absolutePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }

            return tex;
        }

        static int StampSegmentHeights(
            Terrain terrain,
            Texture2D hillshade,
            Vector3 groundCenter,
            float extentMeters,
            SurfaceDemGeoreferenceAuthor.GeorefContext georef,
            int seed)
        {
            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface LiDAR segment stamp");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var preserveR = extentMeters * MainLandPreserveRadiusFraction;
            var blendOuter = extentMeters * 0.55f;
            var changed = 0;

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var dist = Vector2.Distance(
                        new Vector2(wx, wz),
                        new Vector2(groundCenter.x, groundCenter.z));
                    if (dist > extentMeters * 1.1f)
                        continue;

                    var preserve = dist < preserveR
                        ? 0f
                        : Mathf.Clamp01((dist - preserveR) / (blendOuter - preserveR));
                    if (preserve <= 0.001f)
                        continue;

                    var uv = SurfaceDemGeoreferenceAuthor.WorldToHillshadeUv(
                        new Vector3(wx, groundCenter.y, wz), groundCenter, georef, extentMeters);
                    uv = SurfaceTerrainLidarCreativeGuide.WarpDemUv(uv.x, uv.y, wx, wz, seed, extentMeters);
                    var lum = hillshade.GetPixelBilinear(uv.x, uv.y).grayscale;
                    var creative = SurfaceTerrainLidarCreativeGuide.SampleCreativeHeightNorm(
                        wx, wz, seed, heights[y, x], size.y, mountains: true);
                    var guide = SurfaceTerrainLidarCreativeGuide.SampleStructuralGuideNormFromHillshade(
                        lum, heights[y, x], size.y, MaxReliefMeters * 0.5f);
                    var target = SurfaceTerrainLidarCreativeGuide.ComposeTargetHeightNorm(
                        creative, guide, preserve);
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Clamp01(
                        Mathf.Lerp(before, target, preserve * SurfaceTerrainLidarCreativeGuide.PlayDiskBlendStrength * 0.5f));
                    if (Mathf.Abs(heights[y, x] - before) > 0.0001f)
                        changed++;
                }
            }

            data.SetHeights(0, 0, heights);
            terrain.Flush();
            return changed;
        }

        /// <summary>Old county-wide stretch (satellite look) — avoid when segment manifest exists.</summary>
        static int StampHeightsLegacyStretch(Terrain terrain, Texture2D hillshade, Vector3 groundCenter, float extentMeters)
        {
            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface LiDAR height stamp");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var preserveR = extentMeters * MainLandPreserveRadiusFraction;
            var blendOuter = extentMeters * 0.55f;
            var changed = 0;

            for (var y = 0; y < res; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)(res - 1) * size.x;
                    var wz = origin.z + y / (float)(res - 1) * size.z;
                    var dx = wx - groundCenter.x;
                    var dz = wz - groundCenter.z;
                    var dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > extentMeters * 1.1f)
                        continue;

                    var preserve = dist < preserveR ? 0f : Mathf.Clamp01((dist - preserveR) / (blendOuter - preserveR));
                    if (preserve <= 0.001f)
                        continue;

                    var u = x / (float)(res - 1);
                    var v = y / (float)(res - 1);
                    var lum = hillshade.GetPixelBilinear(u, v).grayscale;
                    var reliefNorm = (lum - 0.5f) * (MaxReliefMeters / size.y);
                    var before = heights[y, x];
                    heights[y, x] = Mathf.Clamp01(before + reliefNorm * preserve);
                    if (Mathf.Abs(heights[y, x] - before) > 0.0001f)
                        changed++;
                }
            }

            data.SetHeights(0, 0, heights);
            terrain.Flush();
            return changed;
        }
    }
}
#endif
