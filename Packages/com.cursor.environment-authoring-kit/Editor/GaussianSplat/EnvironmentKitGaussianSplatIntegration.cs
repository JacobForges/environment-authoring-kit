#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.GaussianSplat;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.GaussianSplat
{
    /// <summary>
    /// Places an optional hero splat at the primary cave mouth with hardware safeguards.
    /// </summary>
    public static class EnvironmentKitGaussianSplatIntegration
    {
        public const string HeroRootName = "GaussianSplatHero_PrimaryMouth";
        public const string SurfaceSplatsRootName = "GaussianSplats";

        public static void ScheduleAfterSurfaceFinish()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            if (!settings.enableGaussianSplatHeroAtCaveMouth)
                return;

            if (!EnvironmentKitHardwareBudget.Active.AllowGaussianSplats)
            {
                CaveBuildEditorLog.LogSurface(
                    "Gaussian splat hero skipped — hardware budget disables splats (MacBook/demo preset).",
                    forceUnityConsole: false);
                return;
            }

            CaveBuildActionPacing.ScheduleHeavy(
                () => QueuePlaceHeroBatches(0),
                CaveBuildPipelineDomains.SurfaceQueueLabel("gaussian splat hero"));
        }

        static void QueuePlaceHeroBatches(int batch)
        {
            switch (batch)
            {
                case 0:
                    CaveBuildActionPacing.ScheduleHeavy(
                        () =>
                        {
                            var opening = FindPrimaryOpening();
                            if (opening == null)
                            {
                                CaveBuildEditorLog.LogSurface(
                                    "Gaussian splat: No primary cave opening — run surface build first.",
                                    forceUnityConsole: false);
                                return;
                            }

                            QueuePlaceHeroBatches(1);
                        },
                        CaveBuildPipelineDomains.SurfaceQueueLabel("gaussian splat — resolve mouth"));
                    break;
                case 1:
                    CaveBuildActionPacing.ScheduleHeavy(
                        () =>
                        {
                            if (!TryPlaceOrRefreshHeroSlot(out var msg))
                                CaveBuildEditorLog.LogSurface("Gaussian splat: " + msg, forceUnityConsole: false);
                            else
                                CaveBuildEditorLog.LogSurface("Gaussian splat: " + msg, forceUnityConsole: false);
                        },
                        CaveBuildPipelineDomains.SurfaceQueueLabel("gaussian splat — place hero"));
                    break;
            }
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Place Gaussian Splat Hero (Cave Mouth)", false, 40)]
        public static void MenuPlaceHero()
        {
            if (!TryPlaceOrRefreshHeroSlot(out var msg, force: true))
                EditorUtility.DisplayDialog("Gaussian splat hero", msg, "OK");
            else
                EditorUtility.DisplayDialog("Gaussian splat hero", msg, "OK");
        }

        [MenuItem(CaveBuildMenuPaths.Advanced + "Install Gaussian Splat Package Help", false, 41)]
        public static void MenuInstallHelp() => EnvironmentKitGaussianSplatBridge.OpenInstallInstructions();

        [MenuItem(CaveBuildMenuPaths.Advanced + "Disable Gaussian Splat Hero", false, 42)]
        public static void MenuDisableHero()
        {
            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.enableGaussianSplatHeroAtCaveMouth = false;
            settings.SaveToPrefs();
            RemoveExistingHero();
            EditorUtility.DisplayDialog("Gaussian splat hero", "Disabled and removed hero slot from scene.", "OK");
        }

        public static bool TryPlaceOrRefreshHeroSlot(out string message, bool force = false)
        {
            message = "Nothing placed.";
            var opening = FindPrimaryOpening();
            if (opening == null)
            {
                message = "No primary cave opening — run surface build first.";
                return false;
            }

            var surfaceRoot = FindSurfaceRoot(opening.transform);
            if (surfaceRoot == null)
            {
                message = "Surface root not found.";
                return false;
            }

            var settings = CaveBuildCursorSettings.LoadOrCreate();
            settings.LoadFromPrefs();
            var budget = EnvironmentKitHardwareBudget.Active;

            if (!force && !settings.enableGaussianSplatHeroAtCaveMouth)
            {
                message = "Enable enableGaussianSplatHeroAtCaveMouth on CaveBuildCursorSettings.";
                return false;
            }

            if (!budget.AllowGaussianSplats)
            {
                message = "Hardware budget blocks splats — switch off MacBook Air / demo preset.";
                return false;
            }

            var splatsRoot = surfaceRoot.Find(SurfaceSplatsRootName);
            if (splatsRoot == null)
            {
                var go = new GameObject(SurfaceSplatsRootName);
                CaveEditorUndo.RegisterCreated(go, "Gaussian splats root");
                go.transform.SetParent(surfaceRoot, false);
                splatsRoot = go.transform;
            }

            var existing = splatsRoot.Find(HeroRootName);
            Transform heroTransform;
            if (existing != null)
                heroTransform = existing;
            else
            {
                var heroGo = new GameObject(HeroRootName);
                CaveEditorUndo.RegisterCreated(heroGo, "Gaussian splat hero");
                heroGo.transform.SetParent(splatsRoot, false);
                heroTransform = heroGo.transform;
            }

            heroTransform.position = opening.transform.position + Vector3.up * 0.35f;
            heroTransform.rotation = opening.transform.rotation;

            var slot = heroTransform.GetComponent<GaussianSplatHeroSlot>();
            if (slot == null)
                slot = Undo.AddComponent<GaussianSplatHeroSlot>(heroTransform.gameObject);

            ApplyBudgetToSlot(slot, settings, budget);

            var asset = EnvironmentKitGaussianSplatBridge.LoadSplatAsset(settings.gaussianSplatAssetPath);
            if (asset != null)
                slot.splatAsset = asset;

            if (EnvironmentKitGaussianSplatBridge.TryAttachRenderer(slot, slot.splatAsset, out var bridgeMsg))
                message = bridgeMsg;
            else
            {
                message = bridgeMsg ?? "Hero marker placed (install UnityGaussianSplatting + assign PLY asset path).";
                slot.integrationNotes =
                    "Install aras-p/UnityGaussianSplatting. Set gaussianSplatAssetPath on CaveBuildCursorSettings.";
            }

            EnsureProxyCollider(heroTransform, slot.heroRadiusMeters);
            EditorUtility.SetDirty(heroTransform.gameObject);
            return true;
        }

        static void ApplyBudgetToSlot(
            GaussianSplatHeroSlot slot,
            CaveBuildCursorSettings settings,
            EnvironmentKitHardwareBudget.Settings budget)
        {
            var cap = budget.GaussianSplatQualityCap;
            if (cap == EnvironmentKitHardwareBudget.GaussianSplatQualityCap.Off)
            {
                slot.allowRendering = false;
                slot.qualityTier = GaussianSplatHeroSlot.QualityTier.Low;
            }
            else
            {
                slot.qualityTier = (GaussianSplatHeroSlot.QualityTier)Mathf.Min(
                    (int)settings.gaussianSplatHeroQuality,
                    (int)cap - 1);
                slot.allowRendering = budget.AllowGaussianSplats;
            }

            slot.heroRadiusMeters = budget.GaussianSplatHeroRadiusMeters;
            slot.renderInEditMode = settings.gaussianSplatRenderInEditMode && !budget.ConserveGpuMemory;
            slot.renderInPlayMode = settings.gaussianSplatRenderInPlayMode;
            slot.ApplyRenderingPolicy();
        }

        static void EnsureProxyCollider(Transform hero, float radius)
        {
            var col = hero.GetComponent<BoxCollider>();
            if (col == null)
                col = Undo.AddComponent<BoxCollider>(hero.gameObject);

            col.isTrigger = true;
            col.center = Vector3.up * 1.2f;
            col.size = new Vector3(radius * 0.6f, 3.5f, radius * 0.5f);
        }

        static SurfaceCaveOpeningMarker FindPrimaryOpening()
        {
            SurfaceCaveOpeningMarker fallback = null;
            foreach (var m in Object.FindObjectsByType<SurfaceCaveOpeningMarker>(FindObjectsInactive.Exclude))
            {
                if (m == null)
                    continue;
                if (m.isPrimaryEntrance)
                    return m;
                fallback ??= m;
            }

            return fallback;
        }

        static Transform FindSurfaceRoot(Transform from)
        {
            var t = from;
            while (t != null)
            {
                if (t.name == SurfaceWorldPaths.RootName)
                    return t;
                t = t.parent;
            }

            return from != null ? from.root : null;
        }

        static void RemoveExistingHero()
        {
            foreach (var slot in Object.FindObjectsByType<GaussianSplatHeroSlot>(FindObjectsInactive.Include))
            {
                if (slot != null && slot.gameObject != null)
                    CaveEditorUndo.DestroyImmediate(slot.gameObject);
            }
        }
    }
}
#endif
