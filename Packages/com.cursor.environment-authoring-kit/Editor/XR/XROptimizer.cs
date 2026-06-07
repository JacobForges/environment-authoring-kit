using System.Collections.Generic;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EnvironmentAuthoringKit.Editor.XR
{
    public sealed class XROptimizationReport
    {
        public readonly List<string> Applied = new();
        public readonly List<string> Skipped = new();
        public readonly List<string> Warnings = new();
        public string Summary = string.Empty;
    }

    public static class XROptimizer
    {
        public static XROptimizationReport LastReport { get; private set; }

        public static XROptimizationReport Apply(
            XROptimizationProfile profile,
            Transform environmentRoot,
            bool skipLodGroups = false)
        {
            var report = new XROptimizationReport();
            if (profile == null)
            {
                report.Skipped.Add("No XR profile assigned.");
                LastReport = report;
                return report;
            }

            if (environmentRoot != null &&
                (environmentRoot.name == "LavaTubeCaveSystem" || environmentRoot.GetComponentInChildren<CaveSplinePathAuthoring>(true) != null))
                skipLodGroups = true;

            ApplyUrPSettings(profile, report);
            OptimizeSceneContent(environmentRoot, profile, report, skipLodGroups);
            ApplyAndroidXrHints(report);
            VitureIntegration.TryApplyVitureSettings();

            Application.targetFrameRate = profile.targetFrameRate;
            report.Applied.Add($"Target frame rate set to {profile.targetFrameRate}.");

            var sb = new StringBuilder();
            sb.AppendLine($"XR optimization complete. Applied {report.Applied.Count}, skipped {report.Skipped.Count}.");
            if (report.Warnings.Count > 0)
                sb.AppendLine("Warnings: " + string.Join("; ", report.Warnings));
            report.Summary = sb.ToString().Trim();
            LastReport = report;
            return report;
        }

        static void ApplyUrPSettings(XROptimizationProfile profile, XROptimizationReport report)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                report.Skipped.Add("URP asset not active.");
                return;
            }

            Undo.RecordObject(urp, "XR Optimize URP");
            urp.renderScale = profile.renderScale;
            urp.shadowDistance = profile.shadowDistance;
            urp.shadowCascadeCount = profile.shadowCascades;
            urp.msaaSampleCount = profile.msaa;
            report.Applied.Add($"URP render scale {profile.renderScale}, shadows {profile.shadowDistance}m.");

            if (profile.disableHdr)
                report.Applied.Add("HDR should remain disabled on mobile XR URP asset (verify in Graphics settings).");
        }

        static void OptimizeSceneContent(
            Transform root,
            XROptimizationProfile profile,
            XROptimizationReport report,
            bool skipLodGroups)
        {
            if (root == null)
            {
                report.Skipped.Add("No environment root.");
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var tris = 0;
            var drawCalls = renderers.Length;

            foreach (var renderer in renderers)
            {
                if (profile.markEnvironmentStatic)
                {
                    Undo.RecordObject(renderer.gameObject, "XR Mark Static");
                    renderer.gameObject.isStatic = true;
                }

                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null)
                        continue;
                    if (profile.enableGpuInstancing)
                        mat.enableInstancing = true;
                    if (profile.maxScatterTextureSize <= 512)
                        DownscaleMaterialTextures(mat, profile.maxScatterTextureSize);
                }

                if (renderer is MeshRenderer meshRenderer)
                {
                    var mf = meshRenderer.GetComponent<MeshFilter>();
                    if (mf?.sharedMesh != null)
                        tris += mf.sharedMesh.triangles.Length / 3;

                    if (!skipLodGroups)
                        EnsureLod(meshRenderer.gameObject, profile.lodCullDistance);
                }
            }

            if (skipLodGroups)
                report.Skipped.Add("LOD groups skipped for cave content (blocks use distance culling).");

            report.Applied.Add($"Processed {renderers.Length} renderers.");
            if (drawCalls > 120)
                report.Warnings.Add($"High draw call estimate (~{drawCalls}). Consider lowering scatter density.");
            if (tris > 300000)
                report.Warnings.Add($"High triangle count (~{tris}). Reduce terrain size or scatter density.");
        }

        const int MinTrianglesForLod = 8000;
        const float Lod0ScreenSize = 0.55f;
        const float Lod1ScreenSize = 0.28f;
        const float Lod2ScreenSizeMax = 0.22f;

        static void EnsureLod(GameObject go, float cullDistance)
        {
            if (go == null)
                return;

            if (go.GetComponent<LODGroup>() != null)
                return;

            if (ShouldSkipLod(go))
                return;

            if (go.GetComponentInParent<CaveSplinePathAuthoring>() != null)
                return;

            var mf = go.GetComponent<MeshFilter>();
            if (mf?.sharedMesh == null)
                return;

            var triCount = mf.sharedMesh.triangles.Length / 3;
            if (triCount < MinTrianglesForLod)
                return;

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return;

            var lod0 = Lod0ScreenSize;
            var lod1 = Mathf.Min(Lod1ScreenSize, lod0 - 0.05f);
            var lod2 = Mathf.Clamp(cullDistance / 100f, 0.05f, Lod2ScreenSizeMax);
            if (lod2 >= lod1)
                lod2 = Mathf.Max(0.01f, lod1 - 0.08f);

            var group = go.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(lod0, new[] { renderer }),
                new LOD(lod1, new[] { renderer }),
                new LOD(lod2, new[] { renderer })
            });
            group.RecalculateBounds();
        }

        static bool ShouldSkipLod(GameObject go)
        {
            if (go.GetComponent<CaveTunnelBlock>() != null)
                return true;
            if (go.GetComponent<MinableRock>() != null)
                return true;
            if (go.GetComponent<CaveUndergroundWaterPool>() != null)
                return true;

            var n = go.name;
            if (n.StartsWith("CaveBlock_") || n.StartsWith("BlockRing_") || n.StartsWith("WalkFloor_"))
                return true;
            if (n.Contains("UndergroundRiver") || n.Contains("WaterBranchTube") || n.Contains("MainCaveTube"))
                return true;

            return false;
        }

        static void DownscaleMaterialTextures(Material mat, int maxSize)
        {
            if (mat == null || maxSize <= 0)
                return;

            var shader = mat.shader;
            if (shader == null)
                return;

            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
                    continue;

                var prop = shader.GetPropertyName(i);
                var tex = mat.GetTexture(prop) as Texture2D;
                if (tex == null)
                    continue;

                var max = Mathf.Max(tex.width, tex.height);
                if (max <= maxSize)
                    continue;

                var scale = maxSize / (float)max;
                var w = Mathf.Max(4, Mathf.RoundToInt(tex.width * scale));
                var h = Mathf.Max(4, Mathf.RoundToInt(tex.height * scale));
                var resized = ResizeTextureCpu(tex, w, h);
                if (resized == null)
                    continue;

                mat.SetTexture(prop, resized);
            }
        }

        static Texture2D ResizeTextureCpu(Texture2D source, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        static void ApplyAndroidXrHints(XROptimizationReport report)
        {
            report.Applied.Add("Android XR: use Vulkan, OpenXR loader, Space Warp when supported.");
            report.Applied.Add(VitureIntegration.GetStatusMessage());
        }
    }
}
