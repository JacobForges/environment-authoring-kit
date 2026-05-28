using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Terrain = UnityEngine.Terrain;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Builds underground cave water: Ignite shader pool + optional waterfall particles (no SUIMONO scripts).</summary>
    public static class CaveWaterBuilder
    {
        public const string IgniteWaterPath = CaveWaterMaterialFactory.CaveWaterMatPath;
        const string WaterfallFxPrefab =
            "Assets/SUIMONO - WATER SYSTEM 2/PREFABS/fx_object.prefab";

        public static int Build(
            Transform waterRoot,
            Vector3 poolLocalPosition,
            Vector3 waterfallLocalPosition,
            float poolExtentMeters = 12f,
            Transform caveRoot = null)
        {
            if (waterRoot == null)
                return 0;

            PurgeLegacySuimono(waterRoot);

            var placed = 0;
            var poolPos = AlignPoolToBasin(caveRoot ?? waterRoot.parent, poolLocalPosition);
            placed += BuildPoolPlane(waterRoot, poolPos, poolExtentMeters);
            placed += PlaceWaterfallFx(waterRoot, waterfallLocalPosition + Vector3.up * 1.2f);
            PlaceFeatureMarkers(waterRoot, poolLocalPosition, waterfallLocalPosition);
            FixParticleMaterials(waterRoot);

            return placed;
        }

        public static void RebuildForCave(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var water = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "Water");
            PurgeLegacySuimono(water);

            var anchor = caveRoot.GetComponent<CaveWaterBranchAnchor>();
            if (anchor == null)
                return;

            Build(water, anchor.poolLocalPosition, anchor.waterfallLocalPosition, poolExtentMeters: 6f, caveRoot);
        }

        /// <summary>Places pool surface on carved terrain basin or spline floor (not floating in void).</summary>
        public static Vector3 AlignPoolToBasin(Transform caveRoot, Vector3 poolLocal)
        {
            if (caveRoot == null)
                return poolLocal + Vector3.up * 0.2f;

            var world = caveRoot.TransformPoint(poolLocal);
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain != null)
            {
                var floorY = CaveTerrainCarveUtility.SampleCarvedFloorY(terrain, caveRoot, poolLocal);
                world.y = floorY + 0.28f;
            }
            else
            {
                world.y += 0.22f;
            }

            return caveRoot.InverseTransformPoint(world);
        }

        public static void PurgeLegacySuimono(Transform root)
        {
            if (root == null)
                return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n.Contains("SUIMONO_Surface") || n.Contains("Suimono_ObjectScale") ||
                    n == "SUIMONO_Module" || n == "SUIMONO_System" || n == "Suimono_Object")
                {
                    if (t.name == "UndergroundRiver_Pool")
                        continue;
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                var ns = behaviour.GetType().Namespace;
                if (ns == "Suimono.Core" || behaviour.GetType().FullName == "Suimono.Core.SuimonoObject")
                    Object.DestroyImmediate(behaviour);
            }
        }

        static int BuildPoolPlane(Transform waterRoot, Vector3 localPos, float extentMeters)
        {
            var existing = waterRoot.Find("UndergroundRiver_Pool");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var useLava = extentMeters <= 8f;
            var mat = useLava ? CaveWaterMaterialFactory.GetOrCreateLava() : CaveWaterMaterialFactory.GetOrCreate();
            if (mat == null)
                return 0;

            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
            CaveEditorUndo.RegisterCreated(pool, "Cave Water Pool");
            pool.name = "UndergroundRiver_Pool";
            pool.tag = CaveTags.Water;
            pool.transform.SetParent(waterRoot, false);
            pool.transform.localPosition = localPos;
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var size = Mathf.Clamp(extentMeters, 2.5f, 18f);
            pool.transform.localScale = new Vector3(size, size, 1f);

            var col = pool.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            var renderer = pool.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
                if (!useLava)
                    CaveWaterMaterialFactory.ForceCaveWaterMaterial(renderer);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            if (useLava)
            {
                if (pool.GetComponent<CaveLavaGlow>() == null)
                    pool.AddComponent<CaveLavaGlow>();
                var lightGo = new GameObject("LavaPoolLight");
                CaveEditorUndo.RegisterCreated(lightGo, "Lava Pool Light");
                lightGo.transform.SetParent(pool.transform, false);
                lightGo.transform.localPosition = Vector3.up * 0.5f;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.38f, 0.1f);
                light.intensity = 6f;
                light.range = size * 2.5f;
            }
            else
            {
                if (pool.GetComponent<CaveUndergroundWaterPool>() == null)
                    pool.AddComponent<CaveUndergroundWaterPool>();
                if (pool.GetComponent<CaveWaterSurfaceAnimator>() == null)
                    pool.AddComponent<CaveWaterSurfaceAnimator>();
            }

            var feat = pool.GetComponent<CaveFeatureMarker>();
            if (feat == null)
                feat = pool.AddComponent<CaveFeatureMarker>();
            feat.featureKind = CaveFeatureKind.UndergroundWater;

            return 1;
        }

        static int PlaceWaterfallFx(Transform waterRoot, Vector3 localPos)
        {
            var existing = waterRoot.Find("HiddenWaterfall_Fx");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterfallFxPrefab);
            if (prefab == null)
                return 0;

            var go = PrefabUtility.InstantiatePrefab(prefab, waterRoot) as GameObject;
            if (go == null)
                return 0;

            CaveEditorUndo.RegisterCreated(go, "Waterfall FX");
            go.name = "HiddenWaterfall_Fx";
            go.tag = CaveTags.HiddenWaterfall;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
            go.transform.localScale = Vector3.one * 0.75f;

            PurgeLegacySuimono(go.transform);

            if (go.GetComponent<CaveWaterFxPlayer>() == null)
                go.AddComponent<CaveWaterFxPlayer>();

            var feat = go.GetComponent<CaveFeatureMarker>();
            if (feat == null)
                feat = go.AddComponent<CaveFeatureMarker>();
            feat.featureKind = CaveFeatureKind.HiddenWaterfall;

            return 1;
        }

        static void PlaceFeatureMarkers(Transform waterRoot, Vector3 poolPos, Vector3 fallPos)
        {
            var poolMarker = EnvironmentSceneUtility.GetOrCreateChild(waterRoot, "UndergroundRiver_PoolMarker");
            poolMarker.localPosition = poolPos;
            poolMarker.tag = CaveTags.Water;

            var fallMarker = EnvironmentSceneUtility.GetOrCreateChild(waterRoot, "HiddenWaterfall_Marker");
            fallMarker.localPosition = fallPos;
            fallMarker.tag = CaveTags.HiddenWaterfall;
        }

        static void FixParticleMaterials(Transform waterRoot)
        {
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                 ?? Shader.Find("Particles/Standard Unlit");
            if (particleShader == null)
                return;

            foreach (var psr in waterRoot.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var mats = psr.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || (m.shader != null && m.shader.name.Contains("Universal")))
                        continue;

                    var copy = new Material(particleShader) { name = "CaveWaterFx_URP" };
                    if (copy.HasProperty("_BaseColor"))
                        copy.SetColor("_BaseColor", new Color(0.7f, 0.88f, 1f, 0.6f));
                    if (m.HasProperty("_MainTex") && copy.HasProperty("_BaseMap"))
                        copy.SetTexture("_BaseMap", m.GetTexture("_MainTex"));
                    mats[i] = copy;
                }

                psr.sharedMaterials = mats;
            }
        }
    }
}
