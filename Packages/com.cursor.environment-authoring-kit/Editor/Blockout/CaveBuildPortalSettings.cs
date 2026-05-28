#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Which surface portal links to the cave spawn when building in the active scene.</summary>
    public static class CaveBuildPortalSettings
    {
        const string PrefsPrefix = "EnvironmentKit_CavePortal_";

        public static GameObject PortalForBuild
        {
            get => LoadPortalForScene(SceneManager.GetActiveScene().name);
            set => SavePortalForScene(SceneManager.GetActiveScene().name, value);
        }

        [MenuItem("Window/Environment Kit/Cave Build/Advanced/Assign Cave Portal (Selected)")]
        public static void AssignFromSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null || !IsPortalCandidate(go))
            {
                EditorUtility.DisplayDialog(
                    "Assign Cave Portal",
                    "Select a GameObject with CaveEntrancePortal (e.g. PortalFive) in the Hierarchy.",
                    "OK");
                return;
            }

            PortalForBuild = go;
            EditorUtility.DisplayDialog(
                "Assign Cave Portal",
                $"Build will link '{go.name}' to the underground spawn in scene '{SceneManager.GetActiveScene().name}'.",
                "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Set Cave Portal…")]
        public static void PickPortalFromCandidates()
        {
            var picked = PromptIfNeeded(showDialog: true);
            if (picked == null)
            {
                EditorUtility.DisplayDialog(
                    "Set Cave Portal",
                    "No CaveEntrancePortal candidates found in this scene.",
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Set Cave Portal",
                $"Current cave portal: '{picked.name}' for scene '{SceneManager.GetActiveScene().name}'.",
                "OK");
        }

        public static GameObject PromptIfNeeded(bool showDialog)
        {
            var scene = SceneManager.GetActiveScene().name;
            var current = LoadPortalForScene(scene);
            var candidates = FindPortalCandidates();

            if (candidates.Length == 0)
                return null;

            if (current != null && System.Array.IndexOf(candidates, current) >= 0)
                return current;

            if (!showDialog)
                return candidates[0];

            var menu = new GenericMenu();
            foreach (var c in candidates)
            {
                var captured = c;
                menu.AddItem(new GUIContent(c.name), current == c, () =>
                {
                    SavePortalForScene(scene, captured);
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Use first (PortalFive if present)"), false, () =>
            {
                SavePortalForScene(scene, candidates[0]);
            });

            menu.ShowAsContext();
            return LoadPortalForScene(scene) ?? candidates[0];
        }

        public static GameObject[] FindPortalCandidates()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour == null || behaviour.GetType().Name != "CaveEntrancePortal")
                    continue;
                if (IsPortalCandidate(behaviour.gameObject))
                    list.Add(behaviour.gameObject);
            }

            foreach (var name in new[] { "PortalFive", "MainScene_CavePortal" })
            {
                var go = GameObject.Find(name);
                if (go != null && IsPortalCandidate(go) && !list.Contains(go))
                    list.Add(go);
            }

            return list.ToArray();
        }

        public static bool IsPortalCandidate(GameObject go)
        {
            if (go == null || go.name.Contains("(1)"))
                return false;

            return go.GetComponent("CaveEntrancePortal") != null
                   || go.name == "PortalFive"
                   || go.name == "MainScene_CavePortal";
        }

        static GameObject LoadPortalForScene(string sceneName)
        {
            var stored = EditorPrefs.GetString(PrefsPrefix + sceneName, string.Empty);
            if (string.IsNullOrEmpty(stored) || !GlobalObjectId.TryParse(stored, out var id))
                return null;

            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
        }

        static void SavePortalForScene(string sceneName, GameObject portal)
        {
            if (portal == null)
            {
                EditorPrefs.DeleteKey(PrefsPrefix + sceneName);
                return;
            }

            var id = GlobalObjectId.GetGlobalObjectIdSlow(portal);
            EditorPrefs.SetString(PrefsPrefix + sceneName, id.ToString());
        }
    }
}
#endif
