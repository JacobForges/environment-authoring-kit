using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Editor
{
    static class ActiveSceneUtility
    {
        public static Scene ActiveScene => SceneManager.GetActiveScene();

        public static bool HasValidActiveScene => ActiveScene.IsValid() && ActiveScene.isLoaded;

        public static T FindInActiveScene<T>() where T : Object
        {
            if (!HasValidActiveScene)
                return null;

            foreach (var root in ActiveScene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }

        public static bool IsInActiveScene(GameObject go)
        {
            return go != null && go.scene == ActiveScene;
        }
    }
}
