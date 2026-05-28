#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Georeferenced DEM/hillshade: play-scale close-up segments (meters-accurate), not county-wide satellite stretch.
    /// </summary>
    public static class SurfaceDemGeoreferenceAuthor
    {
        static int _supersampleOverride = -1;

        public static void SetSupersampleTargetDim(int dim) => _supersampleOverride = dim > 0 ? dim : -1;

        public static int ResolveElevGridTargetDim(WorldGenerationRequest request = null)
        {
            if (_supersampleOverride > 0)
                return Mathf.Clamp(_supersampleOverride, 32, 256);
            if (request != null && request.DemSupersampleTargetDim > 0)
                return Mathf.Clamp(request.DemSupersampleTargetDim, 32, 256);
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            return Mathf.Clamp(settings.demSupersampleTargetDim > 0 ? settings.demSupersampleTargetDim : 128, 32, 256);
        }

        [Serializable]
        public class HillshadeManifestFile
        {
            public string countyId;
            public BboxField bbox;
            public CenterField center;
            public SegmentField segment;
            public string source;
            public string relativePath;
        }

        [Serializable]
        public class SegmentField
        {
            public float sizeMeters;
            public int pixels;
            public float metersPerPixel;
            public BboxField bbox;
        }

        [Serializable]
        public class BboxField
        {
            public float west;
            public float south;
            public float east;
            public float north;
        }

        [Serializable]
        public class CenterField
        {
            public float lon;
            public float lat;
        }

        [Serializable]
        public class ElevationGridFile
        {
            public int width;
            public int height;
            public float segmentSizeMeters;
            public float minElevationMeters;
            public float maxElevationMeters;
            public float nodata;
            public float[] values;
        }

        public struct GeorefContext
        {
            public bool Valid;
            public float West;
            public float South;
            public float East;
            public float North;
            public float CenterLon;
            public float CenterLat;
            public float MetersPerDegreeLon;
            public float MetersPerDegreeLat;
            public string CountyId;
            public string ManifestRel;
            public bool HasCloseUpSegment;
            public float SegmentSizeMeters;
            public float SegmentMetersPerPixel;
            public float SegmentWest;
            public float SegmentSouth;
            public float SegmentEast;
            public float SegmentNorth;
        }

        static SurfaceLidarTerrainAuthor.CountyEntry PickCountyForSeed(
            SurfaceLidarTerrainAuthor.HillshadesIndexFile index,
            int seed)
        {
            var counties = index.counties;
            if (counties == null || counties.Length == 0)
                return null;

            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var withSegment = new List<SurfaceLidarTerrainAuthor.CountyEntry>();
            foreach (var c in counties)
            {
                if (c == null || string.IsNullOrEmpty(c.countyId))
                    continue;

                var manifestRel =
                    $"Assets/EnvironmentKit/ResearchCache/images/fl-{c.countyId}-hillshade/manifest.json";
                var manifestPath = Path.Combine(hub, manifestRel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(manifestPath))
                    continue;

                try
                {
                    var manifest = JsonUtility.FromJson<HillshadeManifestFile>(File.ReadAllText(manifestPath));
                    if (manifest?.segment != null && manifest.segment.sizeMeters > 1f)
                        withSegment.Add(c);
                }
                catch
                {
                    // skip invalid manifest
                }
            }

            var pool = withSegment.Count > 0 ? withSegment.ToArray() : counties;
            var idx = Mathf.Abs(seed) % pool.Length;
            return pool[idx];
        }

        public static bool TryLoadGeorefForSeed(int seed, out GeorefContext ctx, out string manifestRel)
        {
            ctx = default;
            manifestRel = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var indexPath = Path.Combine(hub, SurfaceLidarTerrainAuthor.HillshadesIndexRel);
            if (!File.Exists(indexPath))
                return false;

            try
            {
                var index = JsonUtility.FromJson<SurfaceLidarTerrainAuthor.HillshadesIndexFile>(
                    File.ReadAllText(indexPath));
                if (index?.counties == null || index.counties.Length == 0)
                    return false;

                var pick = PickCountyForSeed(index, seed);
                if (pick == null || string.IsNullOrEmpty(pick.countyId))
                    return false;
                var countyId = pick.countyId;
                manifestRel =
                    $"Assets/EnvironmentKit/ResearchCache/images/fl-{countyId}-hillshade/manifest.json";
                var manifestPath = Path.Combine(hub, manifestRel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(manifestPath))
                    return false;

                var manifest = JsonUtility.FromJson<HillshadeManifestFile>(File.ReadAllText(manifestPath));
                if (manifest?.bbox == null || manifest.center == null)
                    return false;

                var latRad = manifest.center.lat * Mathf.Deg2Rad;
                ctx = new GeorefContext
                {
                    Valid = true,
                    West = manifest.bbox.west,
                    South = manifest.bbox.south,
                    East = manifest.bbox.east,
                    North = manifest.bbox.north,
                    CenterLon = manifest.center.lon,
                    CenterLat = manifest.center.lat,
                    MetersPerDegreeLon = 111_320f * Mathf.Cos(latRad),
                    MetersPerDegreeLat = 110_540f,
                    CountyId = countyId,
                    ManifestRel = manifestRel,
                };

                if (manifest.segment != null && manifest.segment.bbox != null && manifest.segment.sizeMeters > 1f)
                {
                    ctx.HasCloseUpSegment = true;
                    ctx.SegmentSizeMeters = manifest.segment.sizeMeters;
                    ctx.SegmentMetersPerPixel = manifest.segment.metersPerPixel > 0f
                        ? manifest.segment.metersPerPixel
                        : manifest.segment.sizeMeters / Mathf.Max(1, manifest.segment.pixels);
                    ctx.SegmentWest = manifest.segment.bbox.west;
                    ctx.SegmentSouth = manifest.segment.bbox.south;
                    ctx.SegmentEast = manifest.segment.bbox.east;
                    ctx.SegmentNorth = manifest.segment.bbox.north;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Maps world XZ around ground center to hillshade UV (0–1) on a close-up segment image (meters to scale).
        /// </summary>
        public static Vector2 WorldToHillshadeUv(
            Vector3 world,
            Vector3 groundCenter,
            GeorefContext georef,
            float extentMeters)
        {
            if (georef.HasCloseUpSegment)
            {
                var segMeters = Mathf.Max(georef.SegmentSizeMeters, extentMeters * 1.02f);
                var half = segMeters * 0.5f;
                var u = (world.x - groundCenter.x + half) / segMeters;
                var v = (world.z - groundCenter.z + half) / segMeters;
                return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
            }

            ComputeSegmentBboxAroundCenter(georef, Mathf.Max(extentMeters, 256f), out var west, out var south, out var east, out var north);
            var dLon = (world.x - groundCenter.x) / georef.MetersPerDegreeLon;
            var dLat = (world.z - groundCenter.z) / georef.MetersPerDegreeLat;
            var lon = georef.CenterLon + dLon;
            var lat = georef.CenterLat + dLat;
            var uLegacy = Mathf.InverseLerp(west, east, lon);
            var vLegacy = Mathf.InverseLerp(south, north, lat);
            return new Vector2(Mathf.Clamp01(uLegacy), Mathf.Clamp01(vLegacy));
        }

        static void ComputeSegmentBboxAroundCenter(
            GeorefContext georef,
            float segmentMeters,
            out float west,
            out float south,
            out float east,
            out float north)
        {
            var halfLon = (segmentMeters * 0.5f) / georef.MetersPerDegreeLon;
            var halfLat = (segmentMeters * 0.5f) / georef.MetersPerDegreeLat;
            west = georef.CenterLon - halfLon;
            east = georef.CenterLon + halfLon;
            south = georef.CenterLat - halfLat;
            north = georef.CenterLat + halfLat;
        }

        public static bool ApplyGeoreferencedStamp(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            out string message)
        {
            message = null;
            if (terrain == null)
            {
                message = "No terrain.";
                return false;
            }

            if (!TryLoadGeorefForSeed(seed, out var georef, out var manifestRel))
            {
                message = "No DEM manifest — falling back to non-georeferenced LiDAR stamp.";
                return SurfaceLidarTerrainAuthor.ApplyFromResearchCache(
                    terrain, groundCenter, extentMeters, seed, out message);
            }

            if (!TryCreateStampSession(
                    terrain,
                    groundCenter,
                    extentMeters,
                    seed,
                    georef,
                    manifestRel,
                    out var session,
                    out message))
                return false;

            try
            {
                StampGeorefRowRange(session, 0, session.Res);
                message = session.BuildResultMessage();
                session.Terrain.terrainData.SetHeights(0, 0, session.Heights);
                session.Terrain.Flush();
                ExportGeorefStatus(georef, manifestRel, session.Changed, seed, extentMeters);
                return session.Changed > 0;
            }
            finally
            {
                session.Dispose();
            }
        }

        const double StampFrameBudgetSeconds = 0.045;
        const int MaxHillshadePixelsForCache = 512 * 512;

        /// <summary>Non-blocking DEM stamp — prep, row bands, and height upload are separate queue steps.</summary>
        public static void QueueApplyGeoreferencedStamp(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            Action<string> onComplete)
        {
            if (terrain == null)
            {
                onComplete?.Invoke("No terrain.");
                return;
            }

            if (!TryLoadGeorefForSeed(seed, out var georef, out var manifestRel))
            {
                CaveBuildActionPacing.ScheduleLightChain(
                    () =>
                    {
                        if (SurfaceLidarTerrainAuthor.ApplyFromResearchCache(
                                terrain, groundCenter, extentMeters, seed, out var fallbackMsg))
                            SurfaceFloridaDemBuildState.MarkAuthoritativeStampCompleted();

                        onComplete?.Invoke(fallbackMsg);
                    },
                    CaveBuildPipelineDomains.SurfaceQueueLabel("surface LiDAR fallback stamp"));
                return;
            }

            var pending = new StampPendingWork
            {
                Terrain = terrain,
                GroundCenter = groundCenter,
                ExtentMeters = extentMeters,
                Seed = seed,
                Georef = georef,
                ManifestRel = manifestRel,
                OnComplete = onComplete,
            };

            CaveBuildActionPacing.ScheduleLightChain(
                () => RunStampPrepare(pending),
                CaveBuildPipelineDomains.SurfaceQueueLabel("LiDAR DEM prep"));
        }

        sealed class StampPendingWork
        {
            public Terrain Terrain;
            public Vector3 GroundCenter;
            public float ExtentMeters;
            public int Seed;
            public GeorefContext Georef;
            public string ManifestRel;
            public Action<string> OnComplete;
        }

        sealed class StampSession : IDisposable
        {
            public Terrain Terrain;
            public Vector3 GroundCenter;
            public float ExtentMeters;
            public int Seed;
            public GeorefContext Georef;
            public string ManifestRel;
            public ElevationGridFile ElevGrid;
            public float[] HillshadeLuminance;
            public int HillshadeWidth;
            public int HillshadeHeight;
            public float[,] Heights;
            public int Res;
            public Vector3 Size;
            public Vector3 Origin;
            public bool Authoritative;
            public float PreserveR;
            public float BlendOuter;
            public float LidarStrength;
            public bool Mountains;
            public bool UseElev;
            public float ElevRange;
            public float TargetRelief;
            public float AnchorNorm;
            public int Changed;
            public int RowY;
            public int CommitRowY;
            public Action<string> OnComplete;

            public string BuildResultMessage()
            {
                var scaleNote = Georef.HasCloseUpSegment
                    ? $"close-up {Georef.SegmentSizeMeters:F0}m @ {Georef.SegmentMetersPerPixel:F2} m/px"
                    : "legacy county UV window (re-run sync-florida-hillshades for segments)";
                var message =
                    $"LiDAR creative guide ({Georef.CountyId}, {scaleNote}) — {Changed} cells " +
                    $"(procedural base + ≤{SurfaceTerrainLidarCreativeGuide.MaxGuideInfluence:P0} structural bias).";
                if (UseElev)
                    message += " Elev grid: macro highs/lows only (smoothed, not photocopy).";
                else if (Georef.HasCloseUpSegment)
                    message +=
                        " (No elevation-grid.json — weak hillshade bias; npm run sync-florida-hillshades -- --elev-grid=128)";

                CaveBuildPipelineLog.Info(message, "Surface-DEM");
                CaveBuildEditorLog.LogSurface(message, forceUnityConsole: true);
                return message;
            }

            public void Dispose()
            {
            }
        }

        static void RunStampPrepare(StampPendingWork pending)
        {
            if (pending?.Terrain == null)
            {
                pending?.OnComplete?.Invoke("LiDAR stamp aborted.");
                return;
            }

            if (!TryCreateStampSession(
                    pending.Terrain,
                    pending.GroundCenter,
                    pending.ExtentMeters,
                    pending.Seed,
                    pending.Georef,
                    pending.ManifestRel,
                    out var session,
                    out var prepMsg))
            {
                pending.OnComplete?.Invoke(prepMsg);
                return;
            }

            session.OnComplete = pending.OnComplete;
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => ScheduleStampRows(session));
        }

        static int StampRowChunkSize(int res) => res >= 1025 ? 4 : res >= 513 ? 6 : 12;

        static void ScheduleStampRows(StampSession session)
        {
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunStampRows(session));
        }

        static void RunStampRows(StampSession session)
        {
            if (session?.Terrain == null)
            {
                session?.OnComplete?.Invoke("LiDAR stamp aborted.");
                return;
            }

            var chunk = StampRowChunkSize(session.Res);
            var yStart = session.RowY;
            var yEnd = Mathf.Min(session.Res, yStart + chunk);
            StampGeorefRowRange(session, yStart, yEnd);

            if (yStart == 0 || yEnd >= session.Res || yEnd % (chunk * 8) < chunk)
            {
                CaveBuildLiveSceneFeedback.NotifySurfacePhase(
                    $"Florida LiDAR DEM rows {yEnd}/{session.Res} ({session.Georef.CountyId})…");
            }

            session.RowY = yEnd;

            if (session.RowY < session.Res)
            {
                ScheduleStampRows(session);
                return;
            }

            CaveBuildActionPacing.ScheduleNextEditorFrame(() => BeginStampHeightmapUpload(session));
        }

        static void BeginStampHeightmapUpload(StampSession session)
        {
            if (session?.Terrain == null)
            {
                session?.OnComplete?.Invoke("LiDAR stamp aborted.");
                return;
            }

            SurfaceTerrainHeightSmoothing.DeCheckerboardHeights(
                session.Heights,
                session.Res,
                session.Terrain,
                session.GroundCenter,
                session.ExtentMeters,
                strength: 0.18f);

            session.CommitRowY = 0;
            ScheduleStampHeightmapUpload(session);
        }

        static void ScheduleStampHeightmapUpload(StampSession session) =>
            CaveBuildActionPacing.ScheduleNextEditorFrame(() => RunStampHeightmapUpload(session));

        static void RunStampHeightmapUpload(StampSession session)
        {
            if (session?.Terrain == null)
            {
                session?.OnComplete?.Invoke("LiDAR stamp aborted.");
                return;
            }

            var chunk = StampRowChunkSize(session.Res) * 4;
            var yEnd = Mathf.Min(session.Res, session.CommitRowY + chunk);
            FlushHeightRows(session, session.CommitRowY, yEnd);
            session.CommitRowY = yEnd;
            CaveBuildLiveSceneFeedback.FlushWorldView(session.Terrain);

            if (session.CommitRowY < session.Res)
            {
                ScheduleStampHeightmapUpload(session);
                return;
            }

            session.Terrain.Flush();
            EditorApplication.QueuePlayerLoopUpdate();

            try
            {
                var message = session.BuildResultMessage();
                if (session.Authoritative && session.Changed > 0)
                    SurfaceFloridaDemBuildState.MarkAuthoritativeStampCompleted();

                ExportGeorefStatus(
                    session.Georef,
                    session.ManifestRel,
                    session.Changed,
                    session.Seed,
                    session.ExtentMeters);
                session.OnComplete?.Invoke(message);
            }
            finally
            {
                session.Dispose();
            }
        }

        static void FlushHeightRows(StampSession session, int yStart, int yEnd)
        {
            var rowCount = yEnd - yStart;
            if (rowCount <= 0)
                return;

            var slice = new float[rowCount, session.Res];
            for (var y = 0; y < rowCount; y++)
            {
                for (var x = 0; x < session.Res; x++)
                    slice[y, x] = session.Heights[yStart + y, x];
            }

            session.Terrain.terrainData.SetHeights(0, yStart, slice);
        }

        static bool TryCreateStampSession(
            Terrain terrain,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            GeorefContext georef,
            string manifestRel,
            out StampSession session,
            out string message)
        {
            session = null;
            message = null;
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var indexPath = Path.Combine(hub, SurfaceLidarTerrainAuthor.HillshadesIndexRel);
            var index = JsonUtility.FromJson<SurfaceLidarTerrainAuthor.HillshadesIndexFile>(File.ReadAllText(indexPath));
            var pick = PickCountyForSeed(index, seed);
            if (pick == null || string.IsNullOrEmpty(pick.path))
            {
                message = "No county in hillshade index.";
                return false;
            }

            var pngPath = Path.Combine(hub, pick.path.Replace('\\', '/'));
            if (!File.Exists(pngPath))
            {
                message = "Missing hillshade PNG.";
                return false;
            }

            ElevationGridFile elevGrid = null;
            var elevPath = Path.Combine(Path.GetDirectoryName(pngPath) ?? hub, "elevation-grid.json");
            if (File.Exists(elevPath))
            {
                try
                {
                    elevGrid = JsonUtility.FromJson<ElevationGridFile>(File.ReadAllText(elevPath));
                    if (elevGrid?.values != null && elevGrid.width > 1 && elevGrid.height > 1)
                    {
                        FillElevationGridNodata(elevGrid);
                        var targetDim = ResolveElevGridTargetDim();
                        if (elevGrid.width < targetDim || elevGrid.height < targetDim)
                        {
                            elevGrid = UpsampleElevationGrid(elevGrid, targetDim);
                            CaveBuildEditorLog.LogSurface(
                                $"[Surface-DEM] Upsampled coarse elevation grid to {elevGrid.width}×{elevGrid.height} (target={targetDim}; sync hillshades with --elev-grid={targetDim} for source quality).",
                                forceUnityConsole: true);
                        }
                    }
                }
                catch
                {
                    elevGrid = null;
                }
            }

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Surface DEM segment stamp");
            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var size = data.size;
            var origin = terrain.transform.position;
            var authoritative = georef.HasCloseUpSegment;
            var useElev = elevGrid != null &&
                          elevGrid.values != null &&
                          elevGrid.width > 1 &&
                          elevGrid.height > 1 &&
                          elevGrid.maxElevationMeters > elevGrid.minElevationMeters + 0.5f;

            float[] luminance = null;
            var hillshadeW = 0;
            var hillshadeH = 0;
            if (!useElev)
            {
                var tex = LoadPng(pngPath);
                if (tex == null)
                {
                    message = "Could not load hillshade.";
                    return false;
                }

                try
                {
                    if (tex.width * tex.height > MaxHillshadePixelsForCache)
                    {
                        message =
                            $"Hillshade too large ({tex.width}×{tex.height}) — add elevation-grid.json beside the PNG for fast stamping.";
                        return false;
                    }

                    luminance = BuildLuminanceCache(tex, out hillshadeW, out hillshadeH);
                }
                finally
                {
                    if (tex != null)
                        UnityEngine.Object.DestroyImmediate(tex);
                }
            }

            session = new StampSession
            {
                Terrain = terrain,
                GroundCenter = groundCenter,
                ExtentMeters = extentMeters,
                Seed = seed,
                Georef = georef,
                ManifestRel = manifestRel,
                HillshadeLuminance = luminance,
                HillshadeWidth = hillshadeW,
                HillshadeHeight = hillshadeH,
                ElevGrid = elevGrid,
                Heights = heights,
                Res = res,
                Size = size,
                Origin = origin,
                Authoritative = authoritative,
                PreserveR = authoritative
                    ? Mathf.Max(2f, extentMeters * 0.02f)
                    : extentMeters * SurfaceLidarTerrainAuthor.MainLandPreserveRadiusFraction * 0.5f,
                BlendOuter = authoritative ? extentMeters * 0.98f : extentMeters * 0.55f,
                LidarStrength = SurfaceTerrainLidarCreativeGuide.MaxGuideInfluence,
                Mountains = Mathf.Abs(seed % 3) != 0,
                UseElev = useElev,
                ElevRange = useElev ? elevGrid.maxElevationMeters - elevGrid.minElevationMeters : 1f,
                TargetRelief = SurfaceLidarTerrainAuthor.MaxReliefMeters,
                AnchorNorm = SampleHeightNormAtWorld(heights, res, size, origin, groundCenter),
            };
            return true;
        }

        static float[] BuildLuminanceCache(Texture2D tex, out int width, out int height)
        {
            width = tex.width;
            height = tex.height;
            var pixels = tex.GetPixels();
            var lum = new float[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
                lum[i] = pixels[i].grayscale;
            return lum;
        }

        static float SampleHillshadeLuminance(StampSession session, float u, float v)
        {
            if (session.HillshadeLuminance == null || session.HillshadeWidth < 2 || session.HillshadeHeight < 2)
                return 0.5f;

            var x = Mathf.Clamp01(u) * (session.HillshadeWidth - 1);
            var y = Mathf.Clamp01(v) * (session.HillshadeHeight - 1);
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var x1 = Mathf.Min(x0 + 1, session.HillshadeWidth - 1);
            var y1 = Mathf.Min(y0 + 1, session.HillshadeHeight - 1);
            var tx = x - x0;
            var ty = y - y0;
            var w = session.HillshadeWidth;

            float At(int ix, int iy) => session.HillshadeLuminance[iy * w + ix];
            var a = Mathf.Lerp(At(x0, y0), At(x1, y0), tx);
            var b = Mathf.Lerp(At(x0, y1), At(x1, y1), tx);
            return Mathf.Lerp(a, b, ty);
        }

        static void StampGeorefRowRange(StampSession session, int yStart, int yEnd)
        {
            var res = session.Res;
            var resM1 = Mathf.Max(1, res - 1);
            for (var y = yStart; y < yEnd; y++)
            {
                for (var x = 0; x < res; x++)
                {
                    var wx = session.Origin.x + x / (float)resM1 * session.Size.x;
                    var wz = session.Origin.z + y / (float)resM1 * session.Size.z;
                    var dx = wx - session.GroundCenter.x;
                    var dz = wz - session.GroundCenter.z;
                    var dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > session.ExtentMeters * 1.1f)
                        continue;

                    // Guide weight ramps from play center — never skip the inner disk (that caused flat quilted cores).
                    var guideWeight = Mathf.Clamp01(
                        Mathf.InverseLerp(session.PreserveR, session.BlendOuter, dist));

                    var uv = WorldToHillshadeUv(
                        new Vector3(wx, session.GroundCenter.y, wz),
                        session.GroundCenter,
                        session.Georef,
                        session.ExtentMeters);
                    uv = SurfaceTerrainLidarCreativeGuide.WarpDemUv(
                        uv.x, uv.y, wx, wz, session.Seed, session.ExtentMeters);

                    var before = session.Heights[y, x];
                    var creativeNorm = SurfaceTerrainLidarCreativeGuide.SampleCreativeHeightNorm(
                        wx,
                        wz,
                        session.Seed,
                        session.AnchorNorm,
                        session.Size.y,
                        session.Mountains);

                    float guideNorm;
                    if (session.UseElev && session.ElevGrid != null)
                    {
                        guideNorm = SurfaceTerrainLidarCreativeGuide.SampleStructuralGuideNormFromElev(
                            session.ElevGrid,
                            uv.x,
                            uv.y,
                            session.AnchorNorm,
                            session.Size.y,
                            session.TargetRelief);
                    }
                    else
                    {
                        var lum = SampleHillshadeLuminance(session, uv.x, uv.y);
                        guideNorm = SurfaceTerrainLidarCreativeGuide.SampleStructuralGuideNormFromHillshade(
                            lum,
                            session.AnchorNorm,
                            session.Size.y,
                            session.TargetRelief * 0.65f);
                    }

                    var targetNorm = SurfaceTerrainLidarCreativeGuide.ComposeTargetHeightNorm(
                        creativeNorm,
                        guideNorm,
                        guideWeight);

                    var blendStrength = session.Authoritative
                        ? Mathf.Lerp(
                            0.72f,
                            SurfaceTerrainLidarCreativeGuide.PlayDiskBlendStrength,
                            guideWeight)
                        : guideWeight * session.LidarStrength;

                    session.Heights[y, x] = Mathf.Clamp01(
                        Mathf.Lerp(before, targetNorm, Mathf.Clamp01(blendStrength)));

                    if (Mathf.Abs(session.Heights[y, x] - before) > 0.0001f)
                        session.Changed++;
                }
            }
        }

        static void ExportGeorefStatus(
            GeorefContext georef,
            string manifestRel,
            int changed,
            int seed,
            float extentMeters)
        {
            var hub = CaveBuildCursorSettings.ResolveHubRoot();
            var path = Path.Combine(hub, CaveBuildAgentContextExporter.Folder, "SurfaceDemGeorefStatus.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? hub);
            var json =
                "{\n" +
                $"  \"seed\": {seed},\n" +
                $"  \"countyId\": \"{georef.CountyId}\",\n" +
                $"  \"manifestRel\": \"{manifestRel}\",\n" +
                $"  \"cellsChanged\": {changed},\n" +
                $"  \"extentMeters\": {extentMeters},\n" +
                $"  \"closeUpSegment\": {(georef.HasCloseUpSegment ? "true" : "false")},\n" +
                $"  \"segmentSizeMeters\": {georef.SegmentSizeMeters},\n" +
                $"  \"metersPerPixel\": {georef.SegmentMetersPerPixel},\n" +
                $"  \"bbox\": {{ \"west\": {georef.West}, \"south\": {georef.South}, \"east\": {georef.East}, \"north\": {georef.North} }},\n" +
                $"  \"centerLonLat\": [{georef.CenterLon}, {georef.CenterLat}],\n" +
                "  \"georeferenced\": true,\n" +
                "  \"creativeGuideMode\": true,\n" +
                $"  \"maxGuideInfluence\": {SurfaceTerrainLidarCreativeGuide.MaxGuideInfluence}\n" +
                "}\n";
            File.WriteAllText(path, json);
        }

        static Texture2D LoadPng(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }

            return tex;
        }

        static float SampleHeightNormAtWorld(
            float[,] heights,
            int res,
            Vector3 size,
            Vector3 origin,
            Vector3 world)
        {
            var lx = Mathf.Clamp01((world.x - origin.x) / Mathf.Max(size.x, 0.01f));
            var lz = Mathf.Clamp01((world.z - origin.z) / Mathf.Max(size.z, 0.01f));
            var hx = Mathf.Clamp(Mathf.RoundToInt(lx * (res - 1)), 0, res - 1);
            var hy = Mathf.Clamp(Mathf.RoundToInt(lz * (res - 1)), 0, res - 1);
            return heights[hy, hx];
        }

        static float HashUvJitter(float wx, float wz, int seed)
        {
            var h = Mathf.Sin(wx * 12.9898f + wz * 78.233f + seed * 0.137f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static float SmootherStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        static void FillElevationGridNodata(ElevationGridFile grid)
        {
            if (grid?.values == null)
                return;

            var w = grid.width;
            var h = grid.height;
            var filled = 0;
            for (var pass = 0; pass < 4; pass++)
            {
                var copy = (float[])grid.values.Clone();
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var idx = y * w + x;
                        if (!IsGridNodata(grid, copy[idx]))
                            continue;

                        var sum = 0f;
                        var count = 0;
                        for (var oy = -1; oy <= 1; oy++)
                        {
                            for (var ox = -1; ox <= 1; ox++)
                            {
                                if (ox == 0 && oy == 0)
                                    continue;
                                var nx = x + ox;
                                var ny = y + oy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                    continue;
                                var n = copy[ny * w + nx];
                                if (IsGridNodata(grid, n))
                                    continue;
                                sum += n;
                                count++;
                            }
                        }

                        if (count > 0)
                        {
                            grid.values[idx] = sum / count;
                            filled++;
                        }
                    }
                }
            }

            if (filled > 0)
                CaveBuildPipelineLog.Info($"Filled {filled} nodata elevation cells.", "Surface-DEM");
        }

        static bool IsGridNodata(ElevationGridFile grid, float value) =>
            Mathf.Abs(value - grid.nodata) < 0.01f;

        static ElevationGridFile UpsampleElevationGrid(ElevationGridFile source, int targetDim)
        {
            var w = source.width;
            var h = source.height;
            var current = source;
            while (w < targetDim || h < targetDim)
            {
                var nw = Mathf.Min(targetDim, w * 2);
                var nh = Mathf.Min(targetDim, h * 2);
                var next = new ElevationGridFile
                {
                    width = nw,
                    height = nh,
                    segmentSizeMeters = source.segmentSizeMeters,
                    minElevationMeters = source.minElevationMeters,
                    maxElevationMeters = source.maxElevationMeters,
                    nodata = source.nodata,
                    values = new float[nw * nh],
                };

                for (var y = 0; y < nh; y++)
                {
                    for (var x = 0; x < nw; x++)
                    {
                        var u = x / (float)(nw - 1);
                        var v = y / (float)(nh - 1);
                        next.values[y * nw + x] = SampleElevationGridBilinear(current, u, v);
                    }
                }

                current = next;
                w = nw;
                h = nh;
            }

            FillElevationGridNodata(current);
            return current;
        }

        static float SampleElevationGridBilinear(ElevationGridFile grid, float u, float v)
        {
            var x = u * (grid.width - 1);
            var y = v * (grid.height - 1);
            var x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, grid.width - 1);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, grid.height - 1);
            var x1 = Mathf.Min(x0 + 1, grid.width - 1);
            var y1 = Mathf.Min(y0 + 1, grid.height - 1);
            var tx = SmootherStep(x - x0);
            var ty = SmootherStep(y - y0);

            float Sample(int ix, int iy)
            {
                var v0 = grid.values[iy * grid.width + ix];
                return IsGridNodata(grid, v0) ? float.NaN : v0;
            }

            var v00 = Sample(x0, y0);
            var v10 = Sample(x1, y0);
            var v01 = Sample(x0, y1);
            var v11 = Sample(x1, y1);
            var sum = 0f;
            var weight = 0f;
            void Acc(float val, float w)
            {
                if (float.IsNaN(val))
                    return;
                sum += val * w;
                weight += w;
            }

            Acc(v00, (1f - tx) * (1f - ty));
            Acc(v10, tx * (1f - ty));
            Acc(v01, (1f - tx) * ty);
            Acc(v11, tx * ty);
            return weight > 0.0001f ? sum / weight : float.NaN;
        }

        static float SampleElevationGrid(ElevationGridFile grid, float u, float v) =>
            SampleElevationGridBilinear(grid, u, v);
    }
}
#endif
