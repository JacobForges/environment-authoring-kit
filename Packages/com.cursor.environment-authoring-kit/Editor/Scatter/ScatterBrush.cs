using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Scatter
{
    [InitializeOnLoad]
    public static class ScatterBrush
    {
        public static bool Active;
        public static ScatterProfile Profile;

        static ScatterBrush()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        public static void OnSceneGUI(SceneView view)
        {
            if (!Active || Profile == null)
                return;

            var e = Event.current;
            if (e == null)
                return;

            Handles.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
            if (e.type == EventType.Repaint && e.mousePosition != Vector2.zero)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (Physics.Raycast(ray, out var hit, 5000f, Profile.surfaceMask))
                    Handles.DrawSolidDisc(hit.point, hit.normal, Profile.brushRadius);
            }

            if (e.type != EventType.MouseDrag && e.type != EventType.MouseDown)
                return;

            if (e.button != 0)
                return;

            e.Use();
            var paintRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(paintRay, out var paintHit, 5000f, Profile.surfaceMask))
                return;

            var root = EnvironmentSceneUtility.GetOrCreateRoot().transform;
            var scatterRoot = EnvironmentSceneUtility.GetOrCreateChild(root, "Scatter");
            var rng = new System.Random(EnvironmentKitSettings.GenerationSeed + paintHit.point.GetHashCode());

            if (e.shift)
            {
                Erase(scatterRoot, paintHit.point);
                return;
            }

            if (!ScatterUtility.SlopeOk(paintHit.normal, Profile) || !ScatterUtility.HeightOk(paintHit.point, Profile))
                return;

            var prefab = Profile.PickWeighted(rng);
            Profile.TryGetEntryForPrefab(prefab, out var entry);
            ScatterPlacer.Place(Profile, entry, prefab, paintHit.point, paintHit.normal, scatterRoot, rng, "Scatter Paint");
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        static void Erase(Transform scatterRoot, Vector3 center)
        {
            var radius = Profile.brushRadius;
            for (var i = scatterRoot.childCount - 1; i >= 0; i--)
            {
                var child = scatterRoot.GetChild(i);
                if (Vector3.Distance(child.position, center) <= radius)
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
        }
    }
}
