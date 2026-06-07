using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Loads BossStage_01 additively and starts the fight loop when the player enters.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldBossStagePortal : MonoBehaviour
    {
        public const string BossSceneName = "BossStage_01";

        [SerializeField] bool playOnce = true;
        [SerializeField] Vector3 arenaOffset = new(0f, 120f, 0f);

        static bool _entered;

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            if (playOnce && _entered)
                return;

            _entered = true;
            var persistence = other.GetComponentInParent<PlayerPersistence>();
            if (persistence?.Card != null)
            {
                persistence.Card.hollowTreeEntered = true;
                persistence.Save();
            }

            StartCoroutine(LoadBossRoutine(other.transform));
        }

        IEnumerator LoadBossRoutine(Transform player)
        {
            if (!Application.CanStreamedLevelBeLoaded(BossSceneName))
            {
                Debug.LogWarning(
                    $"[Boss] Scene '{BossSceneName}' not in build settings — using inline arena. " +
                    "Run Window/Environment Kit/World/Create Boss Stage Scene.");
                WorldBossFightController.SpawnInlineArena(player, arenaOffset);
                yield break;
            }

            var load = SceneManager.LoadSceneAsync(BossSceneName, LoadSceneMode.Additive);
            while (load != null && !load.isDone)
                yield return null;

            var bossScene = SceneManager.GetSceneByName(BossSceneName);
            if (bossScene.IsValid())
                SceneManager.SetActiveScene(bossScene);

            var controller = FindAnyObjectByType<WorldBossFightController>();
            if (controller == null)
            {
                var go = new GameObject("BossFightController");
                controller = go.AddComponent<WorldBossFightController>();
            }

            controller.BeginFight(player, arenaOffset);
        }
    }
}
