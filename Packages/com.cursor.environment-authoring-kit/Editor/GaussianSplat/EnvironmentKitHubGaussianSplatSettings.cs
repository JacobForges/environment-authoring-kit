#if UNITY_EDITOR
using System;
using EnvironmentAuthoringKit.Editor.Blockout;
using EnvironmentAuthoringKit.GaussianSplat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EnvironmentAuthoringKit.Editor.GaussianSplat
{
    /// <summary>
    /// Hub Settings tab — hero splat controls with immediate EditorPrefs persistence and scene actions.
    /// </summary>
    static class EnvironmentKitHubGaussianSplatSettings
    {
        public static void Draw(CaveBuildCursorSettings settings, Action<Action> deferGuiAction)
        {
            if (settings == null)
                return;

            EditorGUILayout.LabelField("3D Gaussian Splat (optional)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "UnityGaussianSplatting package",
                EnvironmentKitGaussianSplatBridge.IsPackagePresent ? "Installed" : "Not installed",
                EditorStyles.miniLabel);

            var budget = EnvironmentKitHardwareBudget.Active;
            if (!budget.AllowGaussianSplats)
            {
                EditorGUILayout.HelpBox(
                    "Current hardware budget blocks Gaussian splats (MacBook Air preset). " +
                    "Switch Hardware budget to Default to enable hero splats.",
                    MessageType.Warning);
            }

            EditorGUI.BeginChangeCheck();
            var enabled = EditorGUILayout.Toggle(
                "Hero splat at cave mouth",
                settings.enableGaussianSplatHeroAtCaveMouth);
            if (EditorGUI.EndChangeCheck())
            {
                settings.enableGaussianSplatHeroAtCaveMouth = enabled;
                Commit(settings);
                if (!enabled)
                    deferGuiAction(EnvironmentKitGaussianSplatIntegration.RemoveHeroFromScene);
            }

            using (new EditorGUI.DisabledScope(!settings.enableGaussianSplatHeroAtCaveMouth))
            {
                DrawAssetPicker(settings);

                EditorGUI.BeginChangeCheck();
                settings.gaussianSplatHeroQuality = (GaussianSplatHeroSlot.QualityTier)EditorGUILayout.EnumPopup(
                    "Quality",
                    settings.gaussianSplatHeroQuality);
                if (EditorGUI.EndChangeCheck())
                    Commit(settings);

                EditorGUI.BeginChangeCheck();
                settings.gaussianSplatRenderInEditMode = EditorGUILayout.Toggle(
                    "Render in Edit Mode (GPU heavy)",
                    settings.gaussianSplatRenderInEditMode);
                if (EditorGUI.EndChangeCheck())
                    Commit(settings);

                EditorGUI.BeginChangeCheck();
                settings.gaussianSplatRenderInPlayMode = EditorGUILayout.Toggle(
                    "Render in Play Mode",
                    settings.gaussianSplatRenderInPlayMode);
                if (EditorGUI.EndChangeCheck())
                    Commit(settings);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Place hero at cave mouth now", GUILayout.Height(24f)))
            {
                deferGuiAction(() =>
                {
                    if (!EnvironmentKitGaussianSplatIntegration.TryPlaceOrRefreshHeroSlot(out var msg, force: true))
                        Debug.LogWarning("[Environment Kit Hub] Gaussian splat: " + msg);
                    else
                        Debug.Log("[Environment Kit Hub] Gaussian splat: " + msg);
                });
            }

            if (GUILayout.Button("Install package help", GUILayout.Height(24f)))
                deferGuiAction(EnvironmentKitGaussianSplatIntegration.MenuInstallHelp);
            EditorGUILayout.EndHorizontal();

            if (settings.enableGaussianSplatHeroAtCaveMouth &&
                string.IsNullOrWhiteSpace(settings.gaussianSplatAssetPath))
            {
                EditorGUILayout.HelpBox(
                    "Assign a GaussianSplatAsset (PLY/SPZ import) above, then Place hero or run a surface build.",
                    MessageType.Info);
            }
        }

        static void DrawAssetPicker(CaveBuildCursorSettings settings)
        {
            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight * 2f);
            var labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
            var fieldRect = new Rect(
                rect.x + EditorGUIUtility.labelWidth,
                rect.y,
                rect.width - EditorGUIUtility.labelWidth,
                EditorGUIUtility.singleLineHeight * 2f);

            EditorGUI.LabelField(labelRect, "Splat asset");

            var current = EnvironmentKitGaussianSplatBridge.LoadSplatAsset(settings.gaussianSplatAssetPath);
            var assetType = EnvironmentKitGaussianSplatBridge.ResolveSplatAssetType();

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(fieldRect, current, assetType, false);
            if (EditorGUI.EndChangeCheck())
            {
                settings.gaussianSplatAssetPath = picked != null
                    ? AssetDatabase.GetAssetPath(picked)
                    : string.Empty;
                Commit(settings);
            }

            HandleAssetDragDrop(fieldRect, settings);

            EditorGUI.BeginChangeCheck();
            var path = EditorGUILayout.TextField("Splat asset path", settings.gaussianSplatAssetPath ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                settings.gaussianSplatAssetPath = path ?? string.Empty;
                Commit(settings);
            }
        }

        static void HandleAssetDragDrop(Rect dropRect, CaveBuildCursorSettings settings)
        {
            var evt = Event.current;
            if (evt == null || !dropRect.Contains(evt.mousePosition))
                return;

            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;

            if (!TryResolveDroppedAsset(out var assetPath))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type != EventType.DragPerform)
                return;

            DragAndDrop.AcceptDrag();
            settings.gaussianSplatAssetPath = assetPath;
            Commit(settings);
            evt.Use();
        }

        static bool TryResolveDroppedAsset(out string assetPath)
        {
            assetPath = null;
            if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0)
                return false;

            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj == null)
                    continue;

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                assetPath = path;
                return true;
            }

            return false;
        }

        static void Commit(CaveBuildCursorSettings settings)
        {
            settings.SaveGaussianSplatToPrefs();
            EditorUtility.SetDirty(settings);
        }
    }
}
#endif
