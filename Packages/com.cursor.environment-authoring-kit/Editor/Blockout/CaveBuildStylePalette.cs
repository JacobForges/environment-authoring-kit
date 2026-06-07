#if UNITY_EDITOR
using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Per-build visual and atmosphere palettes — keeps each generation visually distinct.</summary>
    public static class CaveBuildStylePalette
    {
        public const string Classic = "classic_organic";
        public const string TombExplorer = "tomb_explorer";
        public const string ZeldaRuin = "zelda_ruin";
        public const string DiabloCatacomb = "diablo_catacomb";
        public const string FloodedGrotto = "flooded_grotto";

        public static string PickVisualStyle(System.Random rng)
        {
            var roll = rng.NextDouble();
            if (roll < 0.22)
                return TombExplorer;
            if (roll < 0.44)
                return ZeldaRuin;
            if (roll < 0.66)
                return DiabloCatacomb;
            if (roll < 0.82)
                return FloodedGrotto;
            return Classic;
        }

        public static void ApplyRockTint(Material mat, string styleId, System.Random rng)
        {
            if (mat == null)
                return;

            var baseTint = styleId switch
            {
                TombExplorer => new Color(0.58f, 0.52f, 0.46f),
                ZeldaRuin => new Color(0.52f, 0.56f, 0.48f),
                DiabloCatacomb => new Color(0.48f, 0.4f, 0.38f),
                FloodedGrotto => new Color(0.42f, 0.5f, 0.55f),
                _ => new Color(0.62f, 0.5f, 0.42f),
            };

            var jitter = (float)(rng.NextDouble() * 0.08 - 0.04);
            baseTint.r = Mathf.Clamp01(baseTint.r + jitter);
            baseTint.g = Mathf.Clamp01(baseTint.g + jitter * 0.7f);
            baseTint.b = Mathf.Clamp01(baseTint.b + jitter * 0.5f);

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", baseTint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", baseTint);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", styleId == DiabloCatacomb ? 0.22f : 0.1f);
        }

        public static void ApplyFloorTint(Material mat, string styleId, System.Random rng)
        {
            if (mat == null)
                return;

            var tint = styleId switch
            {
                TombExplorer => new Color(0.5f, 0.44f, 0.36f),
                ZeldaRuin => new Color(0.46f, 0.5f, 0.4f),
                DiabloCatacomb => new Color(0.4f, 0.34f, 0.32f),
                FloodedGrotto => new Color(0.38f, 0.46f, 0.5f),
                _ => new Color(0.48f, 0.4f, 0.34f),
            };

            var jitter = (float)(rng.NextDouble() * 0.06 - 0.03);
            tint.r = Mathf.Clamp01(tint.r + jitter);
            tint.g = Mathf.Clamp01(tint.g + jitter);
            tint.b = Mathf.Clamp01(tint.b + jitter);

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
        }

        public static void GetShrineColors(string styleId, System.Random rng, out Color rock, out Color accent)
        {
            switch (styleId)
            {
                case TombExplorer:
                    rock = new Color(0.45f, 0.4f, 0.36f);
                    accent = new Color(0.62f, 0.55f, 0.38f);
                    break;
                case ZeldaRuin:
                    rock = new Color(0.5f, 0.48f, 0.42f);
                    accent = new Color(0.38f, 0.55f, 0.48f);
                    break;
                case DiabloCatacomb:
                    rock = new Color(0.38f, 0.32f, 0.3f);
                    accent = new Color(0.55f, 0.28f, 0.22f);
                    break;
                case FloodedGrotto:
                    rock = new Color(0.4f, 0.44f, 0.48f);
                    accent = new Color(0.35f, 0.52f, 0.58f);
                    break;
                default:
                    rock = new Color(0.42f, 0.38f, 0.34f);
                    accent = new Color(0.55f, 0.48f, 0.32f);
                    break;
            }

            var j = (float)(rng.NextDouble() * 0.06 - 0.03);
            rock = new Color(
                Mathf.Clamp01(rock.r + j),
                Mathf.Clamp01(rock.g + j),
                Mathf.Clamp01(rock.b + j));
            accent = new Color(
                Mathf.Clamp01(accent.r + j),
                Mathf.Clamp01(accent.g + j),
                Mathf.Clamp01(accent.b + j));
        }

        public static string ShrineArchetypeLabel(string styleId, System.Random rng) =>
            styleId switch
            {
                TombExplorer => rng.NextDouble() < 0.5 ? "tomb_arch" : "tomb_steps",
                ZeldaRuin => rng.NextDouble() < 0.5 ? "zelda_ring" : "zelda_pillar_gate",
                DiabloCatacomb => rng.NextDouble() < 0.5 ? "diablo_blood_gate" : "diablo_skull_steps",
                FloodedGrotto => "flooded_basin_gate",
                _ => "classic_arch",
            };
    }
}
#endif
