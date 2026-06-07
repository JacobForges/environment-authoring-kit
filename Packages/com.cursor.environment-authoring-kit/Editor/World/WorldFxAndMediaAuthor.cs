#if UNITY_EDITOR
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.Editor.Generation;
using EnvironmentAuthoringKit.World;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Surface water meshes, CC0 music, and cinematic trigger volumes.</summary>
    public static class WorldFxAndMediaAuthor
    {
        public const string AudioRootName = "WorldAmbientAudio";
        public const string CinematicRootName = "WorldCinematicTriggers";

        [MenuItem("Window/Environment Kit/World/Apply FX + Music + Cinematics")]
        public static void ApplyFromMenu()
        {
            var terrain = Object.FindAnyObjectByType<Terrain>();
            Apply(terrain, null);
        }

        public static void Apply(Terrain mainTerrain, WorldGenerationRequest request)
        {
            UpgradeSurfaceWaterMeshes();
            AddWaterFoamParticles();
            PlaceCinematicTriggers(mainTerrain, request);
            EnsureDirector();
            WorldPlanV4RuntimeSystemsAuthor.EnsureInScene();
        }

        static void AddWaterFoamParticles()
        {
            foreach (var go in Object.FindObjectsByType<Transform>())
            {
                if (go == null || !go.name.StartsWith("Water_"))
                    continue;
                if (go.GetComponentInChildren<ParticleSystem>() != null)
                    continue;

                var foam = new GameObject("WaterFoam");
                foam.transform.SetParent(go, false);
                foam.transform.localPosition = Vector3.up * 0.05f;
                var ps = foam.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startSize = 0.15f;
                main.startLifetime = 1.2f;
                main.startSpeed = 0.08f;
                main.maxParticles = 48;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission;
                emission.rateOverTime = 12f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 1.5f;
            }
        }

        static void EnsureDirector()
        {
            if (Object.FindAnyObjectByType<WorldCinematicDirector>() != null)
                return;

            var go = new GameObject("WorldCinematicDirector");
            go.AddComponent<WorldCinematicDirector>();
        }

        public static void UpgradeSurfaceWaterMeshes()
        {
            var waterMat = WorldSurfaceWaterUtility.GetOrCreateSurfaceWater();
            var lavaMat = CaveWaterMaterialFactory.GetOrCreateLava();
            if (waterMat == null && lavaMat == null)
                return;

            foreach (var go in Object.FindObjectsByType<Transform>())
            {
                if (go == null || !go.name.StartsWith("Water_"))
                    continue;

                var mr = go.GetComponent<MeshRenderer>();
                if (mr == null)
                    mr = go.gameObject.AddComponent<MeshRenderer>();
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null)
                {
                    mf = go.gameObject.AddComponent<MeshFilter>();
                    mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                }

                mr.sharedMaterial = go.name.Contains("Lava", System.StringComparison.OrdinalIgnoreCase)
                    ? lavaMat
                    : waterMat;
            }
        }

        static void ApplyAmbientAudio(int seed)
        {
            var existing = GameObject.Find(AudioRootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(AudioRootName, out existing, "ambient audio"))
                return;

            var explore = LoadClip("adventure_explore");
            var battle = LoadClip("battle_loop");
            var theme = LoadClip("theme_loop");
            if (explore == null && battle == null && theme == null)
            {
                Debug.LogWarning("[World] No CC0 music in CC0Imports/Audio — run PlanV4-AssetReview/download-cc0-media.mjs");
                return;
            }

            var root = new GameObject(AudioRootName);
            var exploreSrc = root.AddComponent<AudioSource>();
            exploreSrc.clip = explore ?? theme;
            exploreSrc.loop = true;
            exploreSrc.volume = 0.35f;
            exploreSrc.spatialBlend = 0f;
            exploreSrc.playOnAwake = true;
            exploreSrc.Play();

            if (battle != null)
            {
                var battleSrc = root.AddComponent<AudioSource>();
                battleSrc.clip = battle;
                battleSrc.loop = false;
                battleSrc.volume = 0f;
                battleSrc.spatialBlend = 0f;
                battleSrc.playOnAwake = false;
            }

            Debug.Log("[World] Ambient CC0 music started.", root);
        }

        static AudioClip LoadClip(string fileNameNoExt)
        {
            var path = $"{Cc0ContentImportUtility.Cc0Root}/Audio/{fileNameNoExt}";
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{path}.ogg")
                   ?? AssetDatabase.LoadAssetAtPath<AudioClip>($"{path}.mp3");
        }

        static void PlaceCinematicTriggers(Terrain mainTerrain, WorldGenerationRequest request)
        {
            var existing = GameObject.Find(CinematicRootName);
            if (PipelineContentPreservePolicy.TryPreserveSceneRoot(CinematicRootName, out existing, "cinematic triggers"))
                return;

            if (mainTerrain == null)
                return;

            var root = new GameObject(CinematicRootName);
            var center = mainTerrain.transform.position + mainTerrain.terrainData.size * 0.5f;

            var intro = AddTrigger(root.transform, "intro_guide", center + Vector3.forward * 12f, new Vector3(16f, 6f, 16f));
            intro.biomeHint = WorldSurfaceBiomeId.PlayKarst;

            var portal = WorldProjectTagSetup.FindBossStagePortalObject();
            if (portal != null)
            {
                var t = portal.GetComponent<WorldCinematicTrigger>();
                if (t == null)
                {
                    t = portal.AddComponent<WorldCinematicTrigger>();
                    t.cinematicId = "boss_portal";
                    t.playOnce = true;
                    t.duration = 8f;
                }
            }
            else
            {
                AddTrigger(root.transform, "boss_portal", center + Vector3.up * 40f, new Vector3(10f, 8f, 10f));
            }

            if (request != null)
            {
                var fh = AddTrigger(
                    root.transform,
                    "biome_foothill",
                    center + new Vector3(-80f, 0f, 40f),
                    new Vector3(24f, 10f, 24f));
                fh.biomeHint = WorldSurfaceBiomeId.FoothillGreen;
                var pk = AddTrigger(
                    root.transform,
                    "biome_peak",
                    center + new Vector3(60f, 0f, -70f),
                    new Vector3(24f, 10f, 24f));
                pk.biomeHint = WorldSurfaceBiomeId.PeakStone;
                var ax = AddTrigger(
                    root.transform,
                    "biome_annex",
                    center + new Vector3(-120f, 0f, -90f),
                    new Vector3(20f, 10f, 20f));
                ax.biomeHint = WorldSurfaceBiomeId.AnnexLabyrinth;
            }
        }

        static WorldCinematicTrigger AddTrigger(Transform parent, string id, Vector3 world, Vector3 size)
        {
            var go = new GameObject($"Cinematic_{id}");
            go.transform.SetParent(parent, false);
            go.transform.position = world;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            box.center = Vector3.up * (size.y * 0.25f);
            var trigger = go.AddComponent<WorldCinematicTrigger>();
            trigger.cinematicId = id;
            trigger.playOnce = true;
            trigger.duration = id == "boss_portal" ? 8f : 6f;
            return trigger;
        }
    }
}
#endif
