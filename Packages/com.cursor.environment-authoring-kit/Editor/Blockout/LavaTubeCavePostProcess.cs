using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.Editor.XR;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public sealed class LavaTubeCaveBuildReport
    {
        public int PieceCount;
        public int ShellPieceCount;
        public int DrawCallEstimate;
        public int TriangleEstimate;
        public int MinableCount;
        public bool NavMeshBuilt;
        public int TunnelRingCount;
        public int QualityScore;
        public string QualityLetter = string.Empty;
        public bool QualityAcceptable;
        public string SeamlessQuality = string.Empty;
        public string Message = string.Empty;
        public System.Collections.Generic.List<Vector3> PathNodes = new();
    }

    public static class LavaTubeCavePostProcess
    {
        const int MaxTrianglesPerChunk = 50000;
        const int TargetMaxDrawCalls = 100;
        const float NavClearanceMeters = 2f;

        public static LavaTubeCaveBuildReport Apply(
            Transform caveRoot,
            XROptimizationProfile xrProfile,
            bool bakeNavMesh,
            bool bakeGiHints,
            bool setupGlobalFog = true)
        {
            var report = new LavaTubeCaveBuildReport();
            if (caveRoot == null)
                return report;

            var renderers = caveRoot.GetComponentsInChildren<Renderer>(true);
            report.PieceCount = renderers.Length;
            report.DrawCallEstimate = CountDrawCalls(renderers);
            report.TriangleEstimate = CountUniqueMeshTriangles(renderers);
            report.MinableCount = caveRoot.GetComponentsInChildren<MinableRock>(true).Length;

            MarkStaticForGi(caveRoot, bakeGiHints);
            EnsurePhysicsColliders(caveRoot);
            SetupLighting(caveRoot, setupGlobalFog);
            EnsureRegistry(caveRoot);

            if (xrProfile != null)
                XROptimizer.Apply(xrProfile, caveRoot, skipLodGroups: true);

            if (bakeNavMesh)
                report.NavMeshBuilt = BakeNavMesh(caveRoot);

            if (report.DrawCallEstimate > TargetMaxDrawCalls)
                report.Message += $" Draw calls ~{report.DrawCallEstimate} (target <{TargetMaxDrawCalls}).";
            if (report.TriangleEstimate > MaxTrianglesPerChunk * 2)
                report.Message += $" Triangles ~{report.TriangleEstimate}.";

            return report;
        }

        static int CountDrawCalls(Renderer[] renderers)
        {
            var mats = new HashSet<Material>();
            var count = 0;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled)
                    continue;
                count++;
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null)
                        mats.Add(m);
                }
            }

            return count;
        }

        static int CountUniqueMeshTriangles(Renderer[] renderers)
        {
            var seen = new HashSet<Mesh>();
            var tris = 0;
            foreach (var r in renderers)
            {
                if (r is not MeshRenderer)
                    continue;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf?.sharedMesh;
                if (mesh == null || !seen.Add(mesh))
                    continue;
                tris += mesh.triangles.Length / 3;
            }

            return tris;
        }

        static void MarkStaticForGi(Transform root, bool contributeGi)
        {
            if (!contributeGi)
                return;

            var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                        StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccludeeStatic;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<CaveFeatureMarker>() != null)
                    continue;
                if (t.GetComponent<MinableRock>() != null)
                    continue;

                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
        }

        static void AddLodGroups(Transform root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                if (go.GetComponent<LODGroup>() != null)
                    continue;

                var mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;

                var triCount = mesh.triangles.Length / 3;
                if (triCount < 8000)
                    continue;

                var renderer = mf.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                var group = go.AddComponent<LODGroup>();
                group.SetLODs(new[]
                {
                    new LOD(0.55f, new[] { renderer }),
                    new LOD(0.28f, new[] { renderer }),
                    new LOD(0.08f, new[] { renderer })
                });
                group.RecalculateBounds();
            }
        }

        static void EnsurePhysicsColliders(Transform root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                if (!ShouldAddPhysicsCollider(go))
                    continue;
                if (go.GetComponent<Collider>() != null)
                    continue;

                var mesh = mf.sharedMesh;
                if (mesh == null)
                    continue;

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = false;
            }
        }

        static bool ShouldAddPhysicsCollider(GameObject go)
        {
            if (CaveColliderUtility.IsMazeVolumeCollider(go))
                return false;

            if (go.GetComponent<MinableRock>() != null)
                return true;

            var n = go.name;
            if (n.Contains("NavClearance") || n.Contains("Nav "))
                return false;
            if (n.Contains("Ceiling") || n.Contains("Cap_") || n.Contains("Seal_"))
                return false;
            if (n.Contains("Light") || n.Contains("Probe") || n.Contains("FX") || n.Contains("Mote"))
                return false;
            if (go.GetComponent<CaveFeatureMarker>() != null)
                return false;

            if (n.Contains("SpawnGroundPad"))
                return true;

            if (n.Contains(CaveWalkwayBuilder.WalkFloorPrefix))
                return true;

            if (n.Contains(CaveRouteTerrainMeshBuilder.FloorRootName))
                return true;

            if (n.Contains("MinableWall") || n.Contains("MinableWallBlock"))
                return true;

            // Decorative shell geometry should not get mesh colliders (major runtime cost and invisible collisions).
            return false;
        }

        static bool ShouldContributeNavMesh(GameObject go)
        {
            if (go.GetComponent<MinableRock>() != null)
                return false;

            var n = go.name;
            if (n.StartsWith(CaveWalkwayBuilder.WalkFloorPrefix))
                return true;
            if (CaveColliderUtility.IsMazeVolumeCollider(go) && n.Contains("Floor"))
                return true;

            if (n.Contains("NavClearance") || n.Contains("Nav "))
                return false;
            if (n.Contains("Ceiling") || n.Contains("Wall") || n.Contains("Cap_") || n.Contains("Seal_"))
                return false;
            if (n.Contains("Shell") || n.Contains("Stalactite"))
                return false;
            if (n.Contains("Light") || n.Contains("Probe") || n.Contains("FX"))
                return false;

            if (n.Contains("NavFloor") || n.Contains(CaveWalkwayBuilder.WalkFloorPrefix))
                return true;

            if (n.Contains(CaveRouteTerrainMeshBuilder.FloorRootName))
                return true;

            return n.Contains("Floor") || (n.Contains("SM_") && n.Contains("Floor"));
        }

        static void SetupLighting(Transform caveRoot, bool setupGlobalFog)
        {
            var lightingRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "Lighting");

            if (lightingRoot.Find("CaveAmbientFill") == null)
            {
                var fill = new GameObject("CaveAmbientFill");
                CaveEditorUndo.RegisterCreated(fill, "Cave Fill Light");
                fill.transform.SetParent(lightingRoot, false);
                fill.transform.localPosition = new Vector3(0f, 2.5f, 18f);
                var light = fill.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = CaveLightingSettings.MaxAmbientFillRange;
                light.intensity = 0.42f;
                light.color = new Color(0.55f, 0.68f, 0.9f);
                fill.AddComponent<CaveLightRangeClamp>();
                CaveLightingSettings.ApplyCaveLight(light);
            }

            foreach (var light in caveRoot.GetComponentsInChildren<Light>(true))
                CaveLightingSettings.ApplyCaveLight(light, light.name.Contains("Chamber"));

            var chambers = caveRoot.Find("Chambers");
            if (chambers == null)
                return;

            var gpu = EnvironmentKitHardwareBudget.Active;
            if (gpu.SkipReflectionProbes)
                return;

            foreach (Transform chamber in chambers)
            {
                var probeName = $"ReflectionProbe_{chamber.name}";
                if (chamber.Find(probeName) != null)
                    continue;

                var probeGo = new GameObject(probeName);
                CaveEditorUndo.RegisterCreated(probeGo, "Cave Reflection Probe");
                probeGo.transform.SetParent(chamber, false);
                probeGo.transform.localPosition = Vector3.up * 2f;
                var probe = probeGo.AddComponent<ReflectionProbe>();
                probe.size = new Vector3(18f, 10f, 18f);
                probe.resolution = gpu.ReflectionProbeResolution;
                probe.hdr = !gpu.ConserveGpuMemory;
                probe.shadowDistance = 12f;
                probe.mode = ReflectionProbeMode.Baked;
            }
        }

        public static void ApplyLightingOnly(Transform caveRoot)
        {
            if (caveRoot != null)
                SetupLighting(caveRoot, setupGlobalFog: false);
        }

        public static LavaTubeCaveBuildReport ApplyPhysicsAndLod(
            Transform caveRoot,
            XROptimizationProfile xrProfile,
            bool bakeGiHints)
        {
            var report = new LavaTubeCaveBuildReport();
            if (caveRoot == null)
                return report;

            var renderers = caveRoot.GetComponentsInChildren<Renderer>(true);
            report.PieceCount = renderers.Length;
            report.DrawCallEstimate = CountDrawCalls(renderers);
            report.TriangleEstimate = CountUniqueMeshTriangles(renderers);

            MarkStaticForGi(caveRoot, bakeGiHints);
            EnsurePhysicsColliders(caveRoot);

            if (xrProfile != null)
                XROptimizer.Apply(xrProfile, caveRoot, skipLodGroups: true);

            return report;
        }

        public static bool BakeNavMeshOnly(Transform caveRoot, bool force = false)
        {
            if (!force && !CaveBuildWorkflowCoordinator.TryConsumeNavMeshBake())
                return false;

            return BakeNavMesh(caveRoot);
        }

        public static bool BakeNavMeshOnly(Transform caveRoot, WorldGenerationRequest request, bool force = false) =>
            BakeNavMeshOnly(caveRoot, force);

        public static void EnsureRegistry(Transform caveRoot)
        {
            if (caveRoot.GetComponent<CaveSystemRegistry>() == null)
                caveRoot.gameObject.AddComponent<CaveSystemRegistry>();
        }

        static bool BakeNavMesh(Transform caveRoot)
        {
            var buildSettings = NavMesh.GetSettingsByID(0);
            buildSettings.agentRadius = 0.35f;
            buildSettings.agentHeight = NavClearanceMeters;
            buildSettings.agentClimb = 0.45f;
            buildSettings.agentSlope = 42f;

            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(caveRoot.position, Vector3.one * 8f);
            var hasGeometry = false;

            foreach (var meshFilter in caveRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                var go = meshFilter.gameObject;
                if (!ShouldContributeNavMesh(go))
                    continue;

                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = meshFilter.sharedMesh,
                    transform = meshFilter.transform.localToWorldMatrix,
                    area = 0
                });

                bounds.Encapsulate(meshFilter.GetComponent<Renderer>() != null
                    ? meshFilter.GetComponent<Renderer>().bounds
                    : new Bounds(meshFilter.transform.position, Vector3.one * 2f));
                hasGeometry = true;
            }

            var surfaceCount = SurfaceNavMeshBaker.AppendWalkableSources(sources, ref bounds);
            if (surfaceCount > 0)
                hasGeometry = true;

            if (!hasGeometry)
            {
                Debug.LogWarning("[LavaTubeCave] NavMesh bake skipped — no floor geometry found.");
                return false;
            }

            bounds.Expand(surfaceCount > 0 ? 24f : 6f);
            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(
                buildSettings,
                sources,
                bounds,
                caveRoot.position,
                caveRoot.rotation);

            if (data == null)
                return false;

            NavMesh.RemoveAllNavMeshData();
            NavMesh.AddNavMeshData(data);
            Debug.Log($"[LavaTubeCave] NavMesh baked from {sources.Count} floor mesh(es).");
            return true;
        }
    }
}
