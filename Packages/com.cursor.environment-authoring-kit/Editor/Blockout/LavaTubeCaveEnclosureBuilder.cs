using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Stage 2: rock occlusion shell + underground atmosphere volume (blocks skybox gaps).
    /// </summary>
    public static class LavaTubeCaveEnclosureBuilder
    {
        const float ShellYOffset = 5.5f;
        const float ShellGrid = 11f;

        public static int Build(Transform caveRoot, LavaTubePrefabCatalog catalog, System.Random rng, IReadOnlyList<Vector3> pathNodes)
        {
            if (caveRoot == null || catalog == null || !catalog.IsValid)
                return 0;

            var shellRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "OcclusionShell");
            ClearChildren(shellRoot);

            var count = 0;
            count += BuildPathCap(shellRoot, catalog, rng, pathNodes);
            count += BuildBoundsCap(shellRoot, catalog, rng, caveRoot, pathNodes);
            SetupAtmosphereZone(caveRoot, pathNodes);
            return count;
        }

        public static void EnsureAtmosphereZone(Transform caveRoot, IReadOnlyList<Vector3> pathNodes)
        {
            if (caveRoot == null)
                return;

            SetupAtmosphereZone(caveRoot, pathNodes);
        }

        static int BuildPathCap(
            Transform shellRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            IReadOnlyList<Vector3> pathNodes)
        {
            if (pathNodes == null || pathNodes.Count == 0)
                return 0;

            var count = 0;
            var ceilingScale = new Vector3(1.8f, 1f, 1.8f);

            foreach (var node in pathNodes)
            {
                var top = node + new Vector3(0f, ShellYOffset, 0f);
                if (PlaceShellPiece(shellRoot, catalog, rng, top, ceilingScale, true))
                    count++;
            }

            return count;
        }

        static int BuildBoundsCap(
            Transform shellRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Transform caveRoot,
            IReadOnlyList<Vector3> pathNodes)
        {
            Bounds localBounds;
            if (pathNodes != null && pathNodes.Count > 0)
            {
                var min = pathNodes[0];
                var max = pathNodes[0];
                foreach (var p in pathNodes)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                localBounds = new Bounds((min + max) * 0.5f, max - min);
                localBounds.Expand(new Vector3(10f, 6f, 10f));
            }
            else
            {
                var renderers = caveRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    return 0;

                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    if (r != null && r.enabled)
                        bounds.Encapsulate(r.bounds);
                }

                localBounds = new Bounds(
                    caveRoot.InverseTransformPoint(bounds.center),
                    bounds.size);
            }

            var localCenter = localBounds.center;
            var localSize = localBounds.size;
            var topY = localCenter.y + localSize.y * 0.5f + ShellYOffset;
            var count = 0;
            var halfX = localSize.x * 0.5f + 4f;
            var halfZ = localSize.z * 0.5f + 4f;

            const int maxShellPieces = 20;
            for (var x = -halfX; x <= halfX; x += ShellGrid)
            {
                for (var z = -halfZ; z <= halfZ; z += ShellGrid)
                {
                    if (count >= maxShellPieces)
                        break;

                    var pos = new Vector3(localCenter.x + x, topY, localCenter.z + z);
                    if (PlaceShellPiece(shellRoot, catalog, rng, pos, new Vector3(2.2f, 1.4f, 2.2f), true))
                        count++;
                }

                if (count >= maxShellPieces)
                    break;
            }

            return count;
        }

        static void SetupAtmosphereZone(Transform caveRoot, IReadOnlyList<Vector3> pathNodes)
        {
            var zoneRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "CaveAtmosphereZone");
            var existingCol = zoneRoot.GetComponent<BoxCollider>();
            if (existingCol == null)
                existingCol = CaveEditorUndo.GetOrAddComponent<BoxCollider>(zoneRoot.gameObject);
            CaveEditorUndo.RecordObject(existingCol, "Cave Atmosphere Bounds");

            var bounds = ComputeLocalBounds(caveRoot, pathNodes);
            zoneRoot.localPosition = bounds.center;
            existingCol.isTrigger = true;
            existingCol.size = bounds.size + new Vector3(6f, 10f, 6f);
            existingCol.center = Vector3.zero;

            var atmosphere = zoneRoot.GetComponent<CaveUndergroundAtmosphere>();
            if (atmosphere == null)
                atmosphere = CaveEditorUndo.GetOrAddComponent<CaveUndergroundAtmosphere>(zoneRoot.gameObject);

            CaveEditorUndo.RecordObject(atmosphere, "Cave Atmosphere");
            // Warm dark cave atmosphere — matches fantasy reference: deep brown-black background,
            // warm amber fog that picks up torch light, ambient with subtle warm undertone so
            // shadowed rock reads earthy instead of cold blue.
            atmosphere.cameraBackground = new Color(0.018f, 0.012f, 0.008f, 1f);
            atmosphere.overrideFieldOfView = true;
            atmosphere.undergroundFieldOfView = 64f;
            atmosphere.overrideFog = true;
            atmosphere.fogColor = new Color(0.05f, 0.035f, 0.025f, 1f);
            atmosphere.fogDensity = 0.03f;
            atmosphere.fogMode = FogMode.ExponentialSquared;
            atmosphere.overrideAmbient = true;
            atmosphere.ambientSky = new Color(0.07f, 0.055f, 0.04f, 1f);
            atmosphere.ambientEquator = new Color(0.05f, 0.038f, 0.028f, 1f);
            atmosphere.ambientGround = new Color(0.03f, 0.022f, 0.016f, 1f);
            atmosphere.ambientIntensity = 0.55f;

            EnsureLocalVolume(zoneRoot, existingCol.size);
        }

        static bool ShouldIncludeRendererForAtmosphereBounds(Transform t, Transform caveRoot)
        {
            while (t != null && t != caveRoot)
            {
                var n = t.name;
                if (n == "Entrance" || n == "Details" || n == "Props" || n == "SplineMesh")
                    return false;
                t = t.parent;
            }

            return true;
        }

        static Bounds ComputeLocalBounds(Transform caveRoot, IReadOnlyList<Vector3> pathNodes)
        {
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            if (pathNodes != null)
            {
                foreach (var p in pathNodes)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }

            foreach (var r in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                // Particle fog systems have enormous world bounds and must not size the atmosphere trigger.
                if (r is ParticleSystemRenderer)
                    continue;
                if (!ShouldIncludeRendererForAtmosphereBounds(r.transform, caveRoot))
                    continue;

                var lp = caveRoot.InverseTransformPoint(r.bounds.center);
                var ext = r.bounds.extents;
                min = Vector3.Min(min, lp - ext);
                max = Vector3.Max(max, lp + ext);
            }

            var size = max - min;
            const float maxExtent = 420f;
            if (size.x > maxExtent || size.y > maxExtent || size.z > maxExtent)
            {
                var center = (min + max) * 0.5f;
                size = Vector3.Min(size, new Vector3(maxExtent, maxExtent, maxExtent));
                min = center - size * 0.5f;
                max = center + size * 0.5f;
            }

            if (float.IsPositiveInfinity(min.x))
                return new Bounds(Vector3.zero, new Vector3(40f, 20f, 80f));

            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        static void EnsureLocalVolume(Transform zoneRoot, Vector3 size)
        {
            var volume = zoneRoot.GetComponent<Volume>();
            if (volume == null)
                volume = CaveEditorUndo.GetOrAddComponent<Volume>(zoneRoot.gameObject);

            CaveEditorUndo.RecordObject(volume, "Cave Volume");
            volume.isGlobal = false;
            volume.priority = 50f;
            volume.blendDistance = 3f;
            volume.weight = 1f;

            if (volume.sharedProfile == null)
                volume.sharedProfile = CreateCaveVolumeProfile();
        }

        static VolumeProfile CreateCaveVolumeProfile()
        {
            const string path = "Assets/EnvironmentKit/Presets/CaveUndergroundVolume.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null)
                return existing;

            System.IO.Directory.CreateDirectory("Assets/EnvironmentKit/Presets");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CaveUndergroundVolume";

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.28f);
            color.saturation.Override(-8f);
            color.contrast.Override(12f);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.22f);
            bloom.threshold.Override(0.85f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.35f);
            vignette.smoothness.Override(0.5f);

            var lift = profile.Add<LiftGammaGain>(true);
            lift.gain.Override(new Vector4(1f, 1f, 1f, 0f));
            lift.lift.Override(new Vector4(0.92f, 0.94f, 1.02f, 0f));

            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static bool PlaceShellPiece(
            Transform shellRoot,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos,
            Vector3 scale,
            bool ceiling)
        {
            var prefab = ceiling
                ? catalog.Pick(catalog.Ceilings, rng)
                : catalog.Pick(catalog.Rockfalls, rng);
            if (prefab == null)
                return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, shellRoot);
            if (instance == null)
                return false;

            CaveEditorUndo.RegisterCreated(instance, "Occlusion Shell");
            instance.name = ceiling ? $"Cap_{prefab.name}" : $"Seal_{prefab.name}";
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.Euler(
                ceiling ? 0f : (float)(rng.NextDouble() * 20 - 10),
                (float)(rng.NextDouble() * 360),
                ceiling ? 0f : (float)(rng.NextDouble() * 12 - 6));
            instance.transform.localScale = scale;
            instance.layer = LayerMask.NameToLayer("Default");
            CaveSceneMaterialRepair.ApplyModuleMaterials(instance, scale);

            var mf = instance.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && instance.GetComponentInChildren<Collider>() == null)
            {
                var mc = instance.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }

            return true;
        }

        static void ClearChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
                CaveEditorUndo.DestroyImmediate(root.GetChild(i).gameObject);
        }
    }
}
