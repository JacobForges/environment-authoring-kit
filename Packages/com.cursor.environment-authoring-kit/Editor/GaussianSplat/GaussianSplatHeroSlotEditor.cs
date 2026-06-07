#if UNITY_EDITOR
using EnvironmentAuthoringKit.GaussianSplat;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.GaussianSplat
{
    [CustomEditor(typeof(GaussianSplatHeroSlot))]
    sealed class GaussianSplatHeroSlotEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var slot = (GaussianSplatHeroSlot)target;
            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Refresh rendering policy"))
                slot.ApplyRenderingPolicy();
            if (GUILayout.Button("Re-link UnityGaussianSplatting renderer"))
            {
                EnvironmentKitGaussianSplatBridge.TryAttachRenderer(slot, slot.splatAsset, out var msg);
                EditorUtility.DisplayDialog("Gaussian splat", msg ?? "Done.", "OK");
            }
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawGizmo(GaussianSplatHeroSlot slot, GizmoType type)
        {
            if (slot == null)
                return;

            Gizmos.color = slot.allowRendering
                ? new Color(0.4f, 0.85f, 1f, 0.35f)
                : new Color(0.5f, 0.5f, 0.5f, 0.2f);
            Gizmos.DrawWireSphere(slot.transform.position + Vector3.up * 1.5f, slot.heroRadiusMeters * 0.35f);
        }
    }
}
#endif
