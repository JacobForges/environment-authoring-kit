using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Runtime hook: signals when a generated cave is present and play-mode grading should run.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveLiveBuildMonitor : MonoBehaviour
    {
        public static event Action<Transform> OnCaveReady;
        public static event Action<Transform> OnGradeRequired;

        public static Transform ActiveCaveRoot { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded += (_, _) => ScanActiveScene();
            ScanActiveScene();
        }

        static void ScanActiveScene()
        {
            var cave = FindCaveInScene();
            if (cave == null)
                return;

            ActiveCaveRoot = cave;
            if (cave.GetComponent<CaveLiveBuildMonitor>() == null)
                cave.gameObject.AddComponent<CaveLiveBuildMonitor>();
        }

        static Transform FindCaveInScene()
        {
            var grid = GameObject.Find("Grid");
            if (grid != null)
            {
                var t = grid.transform.Find(CaveGeometryPaths.CaveSystemRootName);
                if (t != null)
                    return t;
                t = grid.transform.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
                if (t != null)
                    return t;
            }

            var legacy = GameObject.Find(CaveGeometryPaths.LegacyCaveSystemRootName);
            return legacy != null ? legacy.transform : null;
        }

        void Awake()
        {
            ActiveCaveRoot = transform;
            OnCaveReady?.Invoke(transform);
        }

        public void RequestGrade(string reason)
        {
            OnGradeRequired?.Invoke(transform);
            Debug.Log($"[CaveLive] Grade required: {reason}");
        }
    }
}
