#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Hillshade / satellite-style classification: dark canopy → trees (props), not height bumps.
    /// Macro terrain changes use elevation grid or mid-tone hillshade only.
    /// </summary>
    public static class SurfaceHillshadeObjectMask
    {
        public enum SurfaceClass
        {
            Unknown = 0,
            OpenGround = 1,
            TreeCanopy = 2,
            Water = 3,
            MacroSlope = 4,
            Understory = 5,
            GrassMeadow = 6,
        }

        const float TreeLuminanceMax = 0.42f;
        const float WaterLuminanceMax = 0.28f;
        const float TerrainHillshadeMin = 0.34f;
        const float TerrainHillshadeMax = 0.76f;

        sealed class HillshadeCache
        {
            public Texture2D Texture;
            public int Seed;
            public string Path;
        }

        static HillshadeCache _cache;

        public static void ClearCache() => _cache = null;

        public static bool TrySampleWorld(
            float wx,
            float wz,
            Vector3 groundCenter,
            float extentMeters,
            int seed,
            out SurfaceClass surfaceClass,
            out float luminance,
            out Color rgb)
        {
            surfaceClass = SurfaceClass.Unknown;
            luminance = 0.5f;
            rgb = Color.gray;

            if (!TryEnsureTexture(seed, out var tex))
                return false;

            var u = (wx - groundCenter.x) / Mathf.Max(extentMeters, 1f) * 0.5f + 0.5f;
            var v = (wz - groundCenter.z) / Mathf.Max(extentMeters, 1f) * 0.5f + 0.5f;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            if (SurfaceDemGeoreferenceAuthor.TryLoadGeorefForSeed(seed, out var georef, out _))
            {
                var uv = SurfaceDemGeoreferenceAuthor.WorldToHillshadeUv(
                    new Vector3(wx, groundCenter.y, wz),
                    groundCenter,
                    georef,
                    extentMeters);
                uv = SurfaceTerrainLidarCreativeGuide.WarpDemUv(uv.x, uv.y, wx, wz, seed, extentMeters);
                u = uv.x;
                v = uv.y;
            }

            rgb = tex.GetPixelBilinear(u, v);
            luminance = rgb.grayscale;
            surfaceClass = Classify(rgb, luminance);
            return true;
        }

        public static bool IsTreeCanopyAtWorld(
            float wx,
            float wz,
            Vector3 groundCenter,
            float extentMeters,
            int seed) =>
            TrySampleWorld(wx, wz, groundCenter, extentMeters, seed, out var cls, out _, out _) &&
            cls == SurfaceClass.TreeCanopy;

        /// <summary>Dark / green canopy pixels must not become heightmap spikes or pits.</summary>
        public static bool UseHillshadeForTerrainHeight(float luminance, Color rgb)
        {
            if (rgb != Color.gray)
            {
                var cls = Classify(rgb, luminance);
                if (cls == SurfaceClass.TreeCanopy || cls == SurfaceClass.Water)
                    return false;
            }
            else if (luminance < TreeLuminanceMax)
            {
                return false;
            }

            return luminance >= TerrainHillshadeMin && luminance <= TerrainHillshadeMax;
        }

        public static bool PreferTreePropPlacement(float luminance, Color rgb) =>
            Classify(rgb, luminance) == SurfaceClass.TreeCanopy;

        public static SurfaceClass Classify(Color rgb, float luminance)
        {
            var r = rgb.r;
            var g = rgb.g;
            var b = rgb.b;
            var maxC = Mathf.Max(r, Mathf.Max(g, b));
            var minC = Mathf.Min(r, Mathf.Min(g, b));

            if (luminance < WaterLuminanceMax && b >= r * 0.95f && b >= g * 0.85f)
                return SurfaceClass.Water;

            var greenShift = g > r * 1.05f && g > b * 0.92f && luminance < 0.58f;
            var darkBlob = luminance < TreeLuminanceMax;
            var canopyTexture = (maxC - minC) < 0.22f && luminance < 0.48f;

            if (greenShift || darkBlob || canopyTexture)
                return SurfaceClass.TreeCanopy;

            var brightGreen = g > r * 1.12f && g > b * 0.88f && luminance >= 0.52f && luminance < 0.82f;
            if (brightGreen)
                return SurfaceClass.GrassMeadow;

            var midGreen = g > r && luminance >= 0.38f && luminance < 0.55f;
            if (midGreen)
                return SurfaceClass.Understory;

            if (luminance >= TerrainHillshadeMin && luminance <= TerrainHillshadeMax)
                return SurfaceClass.MacroSlope;

            return SurfaceClass.OpenGround;
        }

        public static bool MatchesPropCategory(SurfaceClass cls, SurfacePropCategory category) =>
            category switch
            {
                SurfacePropCategory.Trees => cls == SurfaceClass.TreeCanopy,
                SurfacePropCategory.Bushes => cls == SurfaceClass.Understory,
                SurfacePropCategory.Grass => cls == SurfaceClass.GrassMeadow,
                SurfacePropCategory.GroundCover => cls == SurfaceClass.GrassMeadow,
                _ => false,
            };

        static bool TryEnsureTexture(int seed, out Texture2D tex)
        {
            tex = null;
            if (_cache != null && _cache.Seed == seed && _cache.Texture != null)
            {
                tex = _cache.Texture;
                return true;
            }

            if (!SurfaceTerrainRefinement.TryLoadHillshade(seed, out tex) || tex == null)
                return false;

            _cache = new HillshadeCache { Texture = tex, Seed = seed };
            return true;
        }
    }
}
#endif
