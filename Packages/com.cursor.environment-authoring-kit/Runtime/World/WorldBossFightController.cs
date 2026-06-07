using EnvironmentAuthoringKit.Cave;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Minimal boss fight loop — spawns BOSS_Stage, tracks HP, awards victory flag.</summary>
    public sealed class WorldBossFightController : MonoBehaviour
    {
        public static WorldBossFightController Active { get; private set; }

        [SerializeField] int bossMaxHp = 420;
        [SerializeField] string bossSlotId = "BOSS_Stage";

        GameObject _boss;
        WorldSimpleHealth _bossHealth;
        Transform _player;
        bool _won;

        public static void SpawnInlineArena(Transform player, Vector3 offset)
        {
            var root = new GameObject("BossStage_Inline");
            root.transform.position = player != null ? player.position + offset : offset;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "BossArena_Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(24f, 0.4f, 24f);

            var controller = root.AddComponent<WorldBossFightController>();
            controller.BeginFight(player, Vector3.zero);
        }

        public void BeginFight(Transform player, Vector3 localOffset)
        {
            Active = this;
            _player = player;
            var spawn = transform.position + localOffset + Vector3.up * 0.5f;
            SpawnBoss(spawn);
        }

        void SpawnBoss(Vector3 position)
        {
            if (_boss != null)
                Destroy(_boss);

            var prefab = WorldBossPrefabResolver.Load(bossSlotId);
            if (prefab != null)
                _boss = Instantiate(prefab, position, Quaternion.Euler(0f, 180f, 0f));
            else
            {
                _boss = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _boss.name = "BOSS_Stage_Proxy";
                _boss.transform.position = position + Vector3.up * 1f;
                _boss.transform.localScale = new Vector3(2f, 2.5f, 2f);
            }

            _bossHealth = _boss.GetComponent<WorldSimpleHealth>();
            if (_bossHealth == null)
                _bossHealth = _boss.AddComponent<WorldSimpleHealth>();
            _bossHealth.maxHp = bossMaxHp;
            _bossHealth.currentHp = bossMaxHp;
            _bossHealth.lootBiome = WorldSurfaceBiomeId.BossThreshold;
            _bossHealth.Died += OnBossDied;

            HumanoidCombatSpawner.EnsureCombatReady(_boss, seed: 9001, CaveMobAggression.Aggressive);
            var hubStats = _boss.GetComponent("CombatStats");
            if (hubStats != null)
            {
                var type = hubStats.GetType();
                type.GetField("maxHp")?.SetValue(hubStats, bossMaxHp);
                type.GetField("currentHp")?.SetValue(hubStats, bossMaxHp);
            }
            if (_boss.GetComponent<WorldEnemyLootDropper>() == null)
            {
                var drop = _boss.AddComponent<WorldEnemyLootDropper>();
                drop.biome = WorldSurfaceBiomeId.BossThreshold;
            }
        }

        void OnBossDied(WorldSimpleHealth _)
        {
            if (_won)
                return;
            _won = true;

            var persistence = _player != null
                ? _player.GetComponentInParent<PlayerPersistence>()
                : FindAnyObjectByType<PlayerPersistence>();
            if (persistence?.Card != null)
            {
                persistence.Card.bossDefeated = true;
                WorldCurrencyService.AddTier(persistence.Card, CurrencyType.Platinum, 1);
                persistence.Save();
            }

            Debug.Log("[Boss] Stage cleared — bossDefeated saved.");
            Invoke(nameof(UnloadBossScene), 3f);
        }

        void UnloadBossScene()
        {
            var scene = SceneManager.GetSceneByName(WorldBossStagePortal.BossSceneName);
            if (scene.IsValid() && scene.isLoaded)
                SceneManager.UnloadSceneAsync(scene);
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
            if (_bossHealth != null)
                _bossHealth.Died -= OnBossDied;
        }
    }

    /// <summary>Loads boss prefab from Resources when CC0 registry unavailable at runtime.</summary>
    static class WorldBossPrefabResolver
    {
        public static GameObject Load(string slot) => Cc0RuntimePrefabResolver.LoadCharacter(slot);
    }
}
