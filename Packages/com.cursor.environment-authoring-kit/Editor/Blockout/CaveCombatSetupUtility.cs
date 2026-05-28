#if UNITY_EDITOR
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Ensures CaveEnemy prefab, player combat, and spawner wiring for cave builds.</summary>
    public static class CaveCombatSetupUtility
    {
        public const string EnemyPrefabPath = "Assets/GameData/Prefabs/CaveEnemy.prefab";
        public const string EnemyAnimatorPath = "Assets/GameData/Animators/CaveEnemyAnimator.controller";
        public const string BashSkillPath = "Assets/GameData/Skills/Skill_Bash.asset";
        public const string HealSkillPath = "Assets/GameData/Skills/Skill_Heal.asset";

        public static GameObject EnsureEnemyPrefab()
        {
            CaveCombatGameTypes.WarnIfMissing();

            if (!AssetDatabase.IsValidFolder("Assets/GameData/Prefabs"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Prefabs");
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            if (existing != null)
            {
                UpgradePrefabIfNeeded(existing);
                return existing;
            }

            var go = BuildEnemyRoot();
            if (go == null)
                return null;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, EnemyPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"[CaveCombat] Created {EnemyPrefabPath}");
            return prefab;
        }

        static GameObject BuildEnemyRoot()
        {
            if (!CaveCombatGameTypes.IsGameplayAvailable)
                return null;

            HumanoidDimensions.Resolve(out var height, out var radius, out _);
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "CaveEnemy";
            go.transform.localScale = HumanoidDimensions.CapsuleLocalScale(height, radius);

            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            var stats = CaveCombatGameTypes.AddComponent(go, CaveCombatGameTypes.CombatStats);
            CaveCombatGameTypes.SetField(stats, "maxHp", 32);
            CaveCombatGameTypes.SetField(stats, "currentHp", 32);
            CaveCombatGameTypes.SetField(stats, "attackPower", 7);
            CaveCombatGameTypes.SetField(stats, "defense", 2);

            CaveCombatGameTypes.AddComponent(go, CaveCombatGameTypes.NpcEnemy);
            var agent = go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.height = height;
            agent.radius = radius;
            agent.speed = 3.6f;
            CaveCombatGameTypes.AddComponent(go, CaveCombatGameTypes.EnemyController);
            EnsureAnimatorChild(go);
            return go;
        }

        static void UpgradePrefabIfNeeded(GameObject prefabRoot)
        {
            if (!CaveCombatGameTypes.IsGameplayAvailable)
                return;

            var path = AssetDatabase.GetAssetPath(prefabRoot);
            if (string.IsNullOrEmpty(path))
                return;

            var instance = PrefabUtility.LoadPrefabContents(path);
            var changed = false;

            if (!CaveCombatGameTypes.HasComponent(instance, CaveCombatGameTypes.CombatStats))
            {
                var stats = CaveCombatGameTypes.AddComponent(instance, CaveCombatGameTypes.CombatStats);
                CaveCombatGameTypes.SetField(stats, "maxHp", 32);
                CaveCombatGameTypes.SetField(stats, "currentHp", 32);
                changed = true;
            }

            if (!CaveCombatGameTypes.HasComponent(instance, CaveCombatGameTypes.NpcEnemy))
            {
                CaveCombatGameTypes.AddComponent(instance, CaveCombatGameTypes.NpcEnemy);
                changed = true;
            }

            if (instance.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
            {
                instance.AddComponent<UnityEngine.AI.NavMeshAgent>();
                changed = true;
            }

            if (instance.GetComponentInChildren<Animator>() == null)
            {
                EnsureAnimatorChild(instance);
                changed = true;
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log($"[CaveCombat] Upgraded {path} (animator + combat components).");
            }

            PrefabUtility.UnloadPrefabContents(instance);
        }

        static void EnsureAnimatorChild(GameObject root)
        {
            var visual = root.transform.Find("Visual");
            if (visual == null)
            {
                var visualGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visualGo.name = "Visual";
                visualGo.transform.SetParent(root.transform, false);
                visualGo.transform.localPosition = Vector3.zero;
                visualGo.transform.localScale = Vector3.one;
                var visualCol = visualGo.GetComponent<Collider>();
                if (visualCol != null)
                    Object.DestroyImmediate(visualCol);
                visual = visualGo.transform;
            }

            var animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = visual.gameObject.AddComponent<Animator>();

            animator.runtimeAnimatorController = EnsureEnemyAnimatorController();
        }

        public static RuntimeAnimatorController EnsureEnemyAnimatorController()
        {
            if (!AssetDatabase.IsValidFolder("Assets/GameData/Animators"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Animators");
            }

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(EnemyAnimatorPath);
            if (existing != null)
                return existing;

            var controller = AnimatorController.CreateAnimatorControllerAtPath(EnemyAnimatorPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            return controller;
        }

        public static int WireSceneCombat(Transform caveRoot = null)
        {
            CaveCombatGameTypes.WarnIfMissing();
            EnsureSkillAssets();
            var bash = LoadSkill(BashSkillPath);
            var heal = LoadSkill(HealSkillPath);
            var prefab = EnsureEnemyPrefab();
            var wired = 0;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && CaveCombatGameTypes.IsGameplayAvailable)
            {
                if (!CaveCombatGameTypes.HasComponent(player, CaveCombatGameTypes.CombatStats))
                    CaveCombatGameTypes.AddComponent(player, CaveCombatGameTypes.CombatStats);

                var combat = CaveCombatGameTypes.GetComponent(player, CaveCombatGameTypes.PlayerCombat);
                if (combat == null)
                    combat = CaveCombatGameTypes.AddComponent(player, CaveCombatGameTypes.PlayerCombat);

                CaveCombatGameTypes.SetField(combat, "bashSkill", bash);
                CaveCombatGameTypes.SetField(combat, "healSkill", heal);

                if (CaveCombatGameTypes.GetField(combat, "aimOrigin") == null)
                {
                    var cam = player.GetComponentInChildren<Camera>();
                    CaveCombatGameTypes.SetField(
                        combat,
                        "aimOrigin",
                        cam != null ? cam.transform : player.transform);
                }

                EditorUtility.SetDirty(player);
                wired++;
            }

            CaveMobSpawner[] spawners;
            if (caveRoot != null)
                spawners = caveRoot.GetComponentsInChildren<CaveMobSpawner>(true);
            else
                spawners = Object.FindObjectsByType<CaveMobSpawner>(FindObjectsInactive.Include);

            foreach (var spawner in spawners)
            {
                if (spawner == null)
                    continue;
                spawner.enemyPrefab = prefab;
                if (spawner.spawnCount < 1)
                    spawner.spawnCount = 1;
                EditorUtility.SetDirty(spawner);
                wired++;
            }

            return wired;
        }

        static ScriptableObject LoadSkill(string path) =>
            CaveCombatGameTypes.SkillDefinition == null
                ? null
                : AssetDatabase.LoadAssetAtPath(path, CaveCombatGameTypes.SkillDefinition) as ScriptableObject;

        static void EnsureSkillAssets()
        {
            if (CaveCombatGameTypes.SkillDefinition == null)
                return;

            if (!AssetDatabase.IsValidFolder("Assets/GameData/Skills"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameData"))
                    AssetDatabase.CreateFolder("Assets", "GameData");
                AssetDatabase.CreateFolder("Assets/GameData", "Skills");
            }

            if (LoadSkill(BashSkillPath) == null)
            {
                var bash = CaveCombatGameTypes.CreateSkillInstance();
                if (bash != null)
                {
                    CaveCombatGameTypes.SetField(bash, "skillName", "Bash");
                    CaveCombatGameTypes.SetField(bash, "mpCost", 4);
                    CaveCombatGameTypes.SetField(bash, "cooldown", 2f);
                    CaveCombatGameTypes.SetField(bash, "range", 4f);
                    CaveCombatGameTypes.SetField(bash, "damage", 18);
                    AssetDatabase.CreateAsset(bash, BashSkillPath);
                }
            }

            if (LoadSkill(HealSkillPath) == null)
            {
                var heal = CaveCombatGameTypes.CreateSkillInstance();
                if (heal != null)
                {
                    CaveCombatGameTypes.SetField(heal, "skillName", "Heal");
                    CaveCombatGameTypes.SetField(heal, "mpCost", 8);
                    CaveCombatGameTypes.SetField(heal, "cooldown", 4f);
                    CaveCombatGameTypes.SetField(heal, "healAmount", 20);
                    AssetDatabase.CreateAsset(heal, HealSkillPath);
                }
            }
        }
    }
}
#endif
