using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.TerrainAuthoring
{
    static class TerrainDressingApplier
    {
        public static void Apply(UnityEngine.Terrain terrain, TerrainDressingPreset preset)
        {
            if (terrain == null || preset == null)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Apply Terrain Dressing");

            var layers = data.terrainLayers;
            if (layers == null || layers.Length == 0)
            {
                var fromProject = ProjectTerrainLayerResolver.TryResolveTerrainLayers();
                if (fromProject != null && fromProject.Length > 0)
                {
                    layers = fromProject;
                }
                else
                {
                    layers = new[]
                    {
                        CreateLayer(preset.groundTint, "Ground"),
                        CreateLayer(preset.secondaryTint, "Secondary")
                    };
                }

                data.terrainLayers = layers;
            }

            var map = new float[data.alphamapWidth, data.alphamapHeight, layers.Length];
            for (var y = 0; y < data.alphamapHeight; y++)
            {
                for (var x = 0; x < data.alphamapWidth; x++)
                {
                    map[x, y, 0] = 0.7f;
                    map[x, y, 1] = layers.Length > 1 ? 0.3f : 0f;
                }
            }

            data.SetAlphamaps(0, 0, map);
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        public static void ApplyHeightStyle(UnityEngine.Terrain terrain, TerrainHeightStyle style, TerrainDressingPreset preset, int seed)
        {
            if (terrain == null)
                return;

            var data = terrain.terrainData;
            Undo.RecordObject(data, "Apply Terrain Height");

            var res = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, res, res);
            var rng = new System.Random(seed);

            var origin = terrain.transform.position;
            var size = data.size;
            var resM1 = Mathf.Max(1, res - 1);
            var ox = seed * 0.173f + 2.1f;
            var oz = seed * 0.091f + 5.7f;

            for (var y = 0; y < res; y++)
            {
                var wz = origin.z + y / (float)resM1 * size.z;
                for (var x = 0; x < res; x++)
                {
                    var wx = origin.x + x / (float)resM1 * size.x;
                    var fbm = SampleWorldFbm(wx, wz, ox, oz);
                    var h = 0.5f + fbm * 0.12f;

                    switch (style)
                    {
                        case TerrainHeightStyle.Flat:
                            h = 0.5f + fbm * 0.02f;
                            break;
                        case TerrainHeightStyle.Mountains:
                            h = 0.5f + fbm * 0.18f;
                            break;
                        default:
                            h = 0.5f + fbm * 0.1f;
                            break;
                    }

                    if (preset?.heightmapStamp != null)
                    {
                        var u = x / (float)resM1;
                        var v = y / (float)resM1;
                        var stamp = preset.heightmapStamp.GetPixelBilinear(u, v).grayscale;
                        h = Mathf.Lerp(h, stamp, preset.heightmapStrength);
                    }

                    heights[y, x] = Mathf.Clamp01(h + (float)(rng.NextDouble() - 0.5) * 0.004f);
                }
            }

            data.SetHeights(0, 0, heights);
        }

        static float SampleWorldFbm(float wx, float wz, float ox, float oz)
        {
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

        static TerrainLayer CreateLayer(Color tint, string name)
        {
            var layer = new TerrainLayer
            {
                name = name,
                diffuseTexture = CreateSolidTexture(tint, name)
            };
            return layer;
        }

        static Texture2D CreateSolidTexture(Color color, string name)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = name + "_Tint",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
