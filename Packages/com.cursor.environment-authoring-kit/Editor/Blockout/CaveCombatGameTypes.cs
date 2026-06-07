#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Resolves Hub gameplay types without an Assembly-CSharp reference (package editor isolation).</summary>
    static class CaveCombatGameTypes
    {
        const string GameAssembly = "Assembly-CSharp";

        static Type _combatStats;
        static Type _npcEnemy;
        static Type _caveEnemy;
        static Type _enemyController;
        static Type _playerCombat;
        static Type _playerController;
        static Type _skillDefinition;
        static Type _combatPlaytestBot;
        static bool _warnedMissing;

        public static Type CombatStats => _combatStats ??= Get("CombatStats");
        public static Type NpcEnemy => _npcEnemy ??= Get("NpcEnemy");
        public static Type CaveEnemy => _caveEnemy ??= Get("CaveEnemy");
        public static Type EnemyController => _enemyController ??= Get("EnemyController");
        public static Type PlayerCombat => _playerCombat ??= Get("PlayerCombat");
        public static Type PlayerController => _playerController ??= Get("PlayerController");
        public static Type SkillDefinition => _skillDefinition ??= Get("SkillDefinition");
        public static Type CombatPlaytestBot => _combatPlaytestBot ??= Get("CaveCombatPlaytestBot");

        public static bool IsGameplayAvailable =>
            CombatStats != null && NpcEnemy != null && PlayerCombat != null;

        static Type Get(string typeName)
        {
            var direct = Type.GetType($"{typeName}, {GameAssembly}");
            if (direct != null)
                return direct;

            foreach (var type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (type.Name == typeName && type.Assembly.GetName().Name == GameAssembly)
                    return type;
            }

            foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.Name == typeName && type.Assembly.GetName().Name == GameAssembly)
                    return type;
            }

            return null;
        }

        public static void WarnIfMissing()
        {
            if (_warnedMissing || IsGameplayAvailable)
                return;
            _warnedMissing = true;
            Debug.LogWarning(
                "[CaveCombat] Hub gameplay scripts (CombatStats, NpcEnemy, PlayerCombat) not found — " +
                "combat setup/probes will be limited until Assets/Scripts compile.");
        }

        public static Component GetComponent(GameObject go, Type type) =>
            type == null || go == null ? null : go.GetComponent(type);

        public static Component AddComponent(GameObject go, Type type) =>
            type == null || go == null ? null : go.AddComponent(type);

        public static bool HasComponent(GameObject go, Type type) =>
            GetComponent(go, type) != null;

        public static T LoadAsset<T>(string path) where T : UnityEngine.Object =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);

        public static void SetField(object target, string fieldName, object value)
        {
            if (target == null)
                return;
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            field?.SetValue(target, value);
        }

        public static object GetField(object target, string fieldName)
        {
            if (target == null)
                return null;
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            return field?.GetValue(target);
        }

        public static float GetFloatField(object target, string fieldName, float fallback = 0f)
        {
            var value = GetField(target, fieldName);
            return value is float f ? f : fallback;
        }

        public static int GetIntField(object target, string fieldName, int fallback = 0)
        {
            var value = GetField(target, fieldName);
            return value is int i ? i : fallback;
        }

        public static void Invoke(object target, string methodName, params object[] args)
        {
            if (target == null)
                return;
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(target, args);
        }

        public static void InvokeConfigureNpc(Component npc, int seed) =>
            Invoke(npc, "Configure", seed);

        public static void InvokeSetAggression(Component npc, CaveMobAggression aggression) =>
            Invoke(npc, "SetAggression", aggression);

        public static void InvokeTakeDamage(Component stats, int amount) =>
            Invoke(stats, "TakeDamage", amount);

        public static void InvokeDefend(Component controller, bool defending) =>
            Invoke(controller, "Defend", defending);

        public static ScriptableObject CreateSkillInstance()
        {
            if (SkillDefinition == null)
                return null;
            return ScriptableObject.CreateInstance(SkillDefinition);
        }

        public static void EnsureCombatPlaytestBot(Transform cave, bool beginProbe)
        {
            if (cave == null || CombatPlaytestBot == null)
                return;

            var bot = cave.GetComponent(CombatPlaytestBot);
            if (bot == null)
                bot = cave.gameObject.AddComponent(CombatPlaytestBot);

            if (beginProbe)
                Invoke(bot, "BeginCombatProbe");
        }

        public static void CollectCombatPlaytestIssues(Transform cave, System.Collections.Generic.List<string> issues)
        {
            if (cave == null || issues == null || CombatPlaytestBot == null)
                return;

            var bot = cave.GetComponent(CombatPlaytestBot);
            if (bot == null)
                return;

            var prop = CombatPlaytestBot.GetProperty("CombatIssues", BindingFlags.Instance | BindingFlags.Public);
            if (prop?.GetValue(bot) is not IEnumerable list)
                return;

            foreach (var item in list)
            {
                if (item is string line)
                    issues.Add(line);
            }
        }
    }
}
#endif
