using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Meat-loop presentation passes — capped per type so caves are not overloaded.
    /// Slot chosen from <see cref="CaveBuildMeatLoopPassPlan"/> (16 distinct missions).
    /// </summary>
    static class CaveBuildMeatLoopEnrichment
    {
        public static bool TryApply(
            Transform caveRoot,
            WorldGenerationRequest request,
            SceneGroundInfo ground,
            int pass,
            out string actionTaken)
        {
            actionTaken = string.Empty;
            if (caveRoot == null || request == null)
                return false;

            CaveBuildPhaseResearchGate.EnsureBeforeMeatPass(pass, request.Seed, out _);
            CaveBuildPhaseBotReport.RecordAfterQueuedStep(
                48 + pass,
                caveRoot,
                CaveBuildPhaseResearchGate.PhaseForMeatPass(pass));

            var parts = new System.Collections.Generic.List<string>();

            if (pass == 2)
                TryLayoutPlatformsPass(caveRoot, request, parts);

            var slot = CaveBuildMeatLoopPassPlan.EnrichmentForPass(pass);
            var catalog = LavaTubePrefabCatalog.Load();
            var rng = new System.Random(request.Seed + pass * 7919 + (int)slot * 313);

            var caveOk = false;
            string caveAction = null;
            switch (slot)
            {
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.Props:
                    caveOk = TryScatterProps(caveRoot, request, catalog, rng, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.MaterialsLighting:
                    caveOk = TryMaterialsAndLighting(caveRoot, request, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.AtmosphereFog:
                    caveOk = TryAtmosphereAndFog(caveRoot, request, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.MobsCombat:
                    caveOk = TryMobs(caveRoot, request, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.VisualPolish:
                    caveOk = TryVisualPolish(caveRoot, request, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.DecalsDetails:
                    caveOk = TryDecalsLight(caveRoot, rng, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.AudioAmbience:
                    caveOk = TryAudioAmbience(caveRoot, out caveAction);
                    break;
                case CaveBuildMeatLoopPassPlan.EnrichmentSlot.PerformanceTrim:
                    caveOk = TryPerformanceTrim(caveRoot, out caveAction);
                    break;
            }

            if (!string.IsNullOrEmpty(caveAction))
                parts.Add(caveAction);
            actionTaken = parts.Count > 0 ? string.Join(" | ", parts) : caveAction ?? string.Empty;
            return caveOk || parts.Count > 0;
        }

        static void TryLayoutPlatformsPass(
            Transform caveRoot,
            WorldGenerationRequest request,
            System.Collections.Generic.List<string> parts)
        {
            if (!CaveGeometryPaths.IsAdventureCave(caveRoot))
                return;

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                return;

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null || layout == null)
                return;

            var count = CaveCompactRouteUtility.CountPathPlatformChildren(caveRoot);
            if (count < 6)
            {
                var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
                var added = CaveAdventureBlockBuilder.BuildWalkPlatforms(geometry, layout, floorMat);
                parts.Add($"Layout platforms: added/rebuilt walk platforms ({added} segments, was {count}).");
            }
            else
            {
                parts.Add($"Layout platforms: route has {count} platforms (OK).");
            }
        }

        static bool TryScatterProps(
            Transform caveRoot,
            WorldGenerationRequest request,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            out string action)
        {
            action = string.Empty;
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatPropScatter())
            {
                action = "Prop scatter skipped — cap reached for this build.";
                return false;
            }

            if (catalog == null)
            {
                action = "Prop scatter skipped — no prefab catalog.";
                return false;
            }

            var propsRoot = caveRoot.Find("Details/Props");
            if (propsRoot == null)
            {
                var details = caveRoot.Find("Details") ??
                                EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "Details");
                propsRoot = EnvironmentSceneUtility.GetOrCreateChild(details, "Props");
            }

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                action = "Prop scatter skipped — no spline path.";
                return false;
            }

            var spline = new CaveSplinePath();
            spline.SetKnots(authoring.Knots);
            var lengthBased = Mathf.Clamp(Mathf.CeilToInt(spline.TotalLength / 55f), 8, 20);
            var requestBased = request.CavePropScatterCount > 0
                ? Mathf.Clamp(request.CavePropScatterCount / 2, 8, 24)
                : 12;
            var count = Mathf.Clamp(Mathf.Max(lengthBased, requestBased), 10, 24);
            var placed = 0;
            for (var i = 0; i < count; i++)
            {
                var dist = (float)rng.NextDouble() * spline.TotalLength;
                var sample = spline.SampleAtDistance(dist);
                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var offset = sample.Right * Mathf.Cos(angle) * sample.RadiusX * 0.55f +
                             sample.Up * Mathf.Sin(angle) * sample.RadiusY * 0.35f;
                var before = propsRoot.childCount;
                CavePrefabScatter.PlaceRandomProp(propsRoot, catalog, rng, sample.Position + offset, 0.55f);
                if (propsRoot.childCount > before)
                    placed++;
            }

            EditorUtility.SetDirty(propsRoot.gameObject);
            action = $"Strategic prop scatter: ~{placed} along route (capped, not dense fill).";
            return placed > 0;
        }

        // compile_gate | arXiv:2510.15120 — meat-loop lighting pass threads WorldGenerationRequest through editor validation.
        static bool TryMaterialsAndLighting(
            Transform caveRoot,
            WorldGenerationRequest request,
            out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatLightingPass())
            {
                action = "Lighting pass skipped — cap reached.";
                return false;
            }

            CaveSceneMaterialRepair.RepairCaveRoot(caveRoot);
            CaveCinematicLightingPass.Apply(
                caveRoot,
                SceneGroundResolver.Resolve(),
                request,
                out var cinematicMsg);
            LavaTubeMaterialUpgrader.UpgradeAllPackMaterials();
            action = "Cinematic lighting + materials + URP. " + cinematicMsg;
            return true;
        }

        static bool TryAtmosphereAndFog(Transform caveRoot, WorldGenerationRequest request, out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatAtmospherePass())
            {
                action = "Atmosphere pass skipped — cap reached.";
                return false;
            }

            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance = 220f;

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
                CavePlayabilityFix.RunSilent(caveRoot);

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring != null && authoring.Knots != null && authoring.Knots.Count >= 2)
            {
                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                CaveFogMistBuilder.Build(caveRoot, spline);
            }

            action = "Fog layout: surface open sky (global fog off); cave mouth + interior mist volumes only.";
            return true;
        }

        static bool TryMobs(Transform caveRoot, WorldGenerationRequest request, out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatMobPass())
            {
                action = "Mob placement skipped — cap reached.";
                return false;
            }

            if (!CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                action = "Mob pass skipped — not adventure cave.";
                return false;
            }

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
            }

            action = "Enemy/mob placement along route (capped pass).";
            return true;
        }

        static bool TryVisualPolish(Transform caveRoot, WorldGenerationRequest request, out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatVisualPolishPass())
            {
                action = "Visual polish skipped — cap reached.";
                return false;
            }

            if (CaveGeometryPaths.IsAdventureCave(caveRoot))
            {
                CaveAdventureVisualPass.Apply(caveRoot);
                CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
                CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn(caveRoot, request);
                action = "Adventure visual pass + spawn polish.";
                return true;
            }

            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            action = "Enclosure slab hide polish.";
            return true;
        }

        static bool TryDecalsLight(Transform caveRoot, System.Random rng, out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatDecalPass())
            {
                action = "Detail decals skipped — cap reached.";
                return false;
            }

            var details = caveRoot.Find("Details");
            if (details == null)
            {
                action = "No Details root — skip decals.";
                return false;
            }

            var rubble = details.Find("Rubble");
            if (rubble == null)
                rubble = EnvironmentSceneUtility.GetOrCreateChild(details, "Rubble");

            var added = 0;
            var max = 4;
            for (var i = rubble.childCount; i < max; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"MeatDetail_{passIndex(rng)}";
                cube.transform.SetParent(rubble, false);
                cube.transform.localScale = Vector3.one * (0.4f + (float)rng.NextDouble() * 0.5f);
                cube.transform.localPosition = new Vector3(
                    (float)rng.NextDouble() * 4f - 2f,
                    (float)rng.NextDouble() * 0.5f,
                    (float)rng.NextDouble() * 4f - 2f);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                added++;
            }

            EditorUtility.SetDirty(rubble.gameObject);
            action = added > 0 ? $"Light detail rubble (+{added}, max {max})." : "Detail rubble already at cap.";
            return added > 0;
        }

        static int passIndex(System.Random rng) => rng.Next(1000, 9999);

        static bool TryAudioAmbience(Transform caveRoot, out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatAudioPass())
            {
                action = "Audio ambience skipped — cap reached.";
                return false;
            }

            var audioRoot = caveRoot.Find("Audio");
            if (audioRoot == null)
                audioRoot = EnvironmentSceneUtility.GetOrCreateChild(caveRoot, "Audio").transform;

            if (audioRoot.childCount > 0)
            {
                action = "Audio root already has children — no duplicate beds.";
                return false;
            }

            var bed = new GameObject("MeatAmbience_Bed");
            bed.transform.SetParent(audioRoot, false);
            action = "Placeholder ambience bed (hook AudioSource in kit as needed).";
            return true;
        }

        static bool TryPerformanceTrim(Transform caveRoot, out string action)
        {
            if (!CaveBuildWorkflowCoordinator.TryConsumeMeatPerfPass())
            {
                action = "Performance trim skipped — cap reached.";
                return false;
            }

            var removed = CaveCompactLayerPurge.PurgeShellLayersOnly(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            action = removed > 0
                ? $"Performance trim: purged {removed} extra shell layer(s) + hid route slabs."
                : "Performance trim: hid route slabs (no extra layers).";
            return true;
        }
    }
}
