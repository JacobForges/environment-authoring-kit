using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Spawns player-sized combat humanoids — capsule default or optional animated prefab.
    /// </summary>
    public static class HumanoidCombatSpawner
    {
        static Material _fallbackEnemyMaterial;
        static Type _npcEnemyType;
        static Type _combatStatsType;
        static Type _enemyControllerType;
        static Type _caveEnemyType;

        public static GameObject SpawnEnemy(
            Vector3 position,
            Quaternion rotation,
            Transform parent,
            GameObject enemyPrefab,
            int seed,
            CaveMobAggression aggression,
            Color? capsuleTint = null)
        {
            HumanoidDimensions.Resolve(out var height, out var radius, out var stepOffset);

            GameObject instance;
            if (enemyPrefab != null)
            {
                instance = UnityEngine.Object.Instantiate(enemyPrefab, position, rotation, parent);
                instance.name = enemyPrefab.name;
                StripPhysicsCollidersOnRoot(instance);
                EnsureCombatComponents(instance, height, radius, stepOffset);
                HideBuiltinPrimitiveMeshIfAnimated(instance);
            }
            else
            {
                instance = CreateCapsuleEnemy(position, rotation, height, radius, stepOffset, capsuleTint);
                instance.transform.SetParent(parent, true);
            }

            ConfigureAggression(instance, aggression);
            ConfigureEnemyBrain(instance, seed);
            return instance;
        }

        public static GameObject CreateCapsuleEnemy(
            Vector3 position,
            Quaternion rotation,
            float? height = null,
            float? radius = null,
            float? stepOffset = null,
            Color? tint = null)
        {
            HumanoidDimensions.Resolve(out var h, out var r, out var step);
            h = height ?? h;
            r = radius ?? r;
            step = stepOffset ?? step;

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "CombatHumanoid";
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = HumanoidDimensions.CapsuleLocalScale(h, r);

            var meshCol = go.GetComponent<Collider>();
            if (meshCol != null)
                UnityEngine.Object.Destroy(meshCol);

            ApplyCapsuleMaterial(go, tint ?? new Color(0.68f, 0.22f, 0.22f, 1f));
            EnsureCombatComponents(go, h, r, step);
            return go;
        }

        static void EnsureCombatComponents(GameObject go, float height, float radius, float stepOffset)
        {
            ResolveTypes();

            if (_combatStatsType != null && go.GetComponent(_combatStatsType) == null)
            {
                var stats = go.AddComponent(_combatStatsType);
                SetField(stats, "maxHp", 36);
                SetField(stats, "currentHp", 36);
                SetField(stats, "attackPower", 7);
                SetField(stats, "defense", 2);
            }

            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null && NavMesh.SamplePosition(go.transform.position, out _, 12f, NavMesh.AllAreas))
                agent = go.AddComponent<NavMeshAgent>();

            if (agent != null)
            {
                agent.height = height;
                agent.radius = radius;
                agent.speed = 3.6f;
                agent.stoppingDistance = 1.2f;
                agent.autoBraking = true;
            }

            if (_npcEnemyType != null && go.GetComponent(_npcEnemyType) == null)
                go.AddComponent(_npcEnemyType);
            else if (_caveEnemyType != null && go.GetComponent(_caveEnemyType) == null)
                go.AddComponent(_caveEnemyType);

            if (_enemyControllerType != null && go.GetComponent(_enemyControllerType) == null)
                go.AddComponent(_enemyControllerType);

            var cc = go.GetComponent<CharacterController>();
            if (cc == null)
                cc = go.AddComponent<CharacterController>();

            cc.height = height;
            cc.radius = radius;
            cc.center = new Vector3(0f, height * 0.5f, 0f);
            cc.stepOffset = stepOffset;
            cc.skinWidth = 0.05f;
            cc.enabled = false;
        }

        static void HideBuiltinPrimitiveMeshIfAnimated(GameObject root)
        {
            var anim = root.GetComponentInChildren<Animator>();
            if (anim == null)
                return;

            var rootRenderer = root.GetComponent<Renderer>();
            if (rootRenderer != null)
                rootRenderer.enabled = false;
        }

        static void StripPhysicsCollidersOnRoot(GameObject root)
        {
            foreach (var col in root.GetComponents<Collider>())
            {
                if (col is CharacterController)
                    continue;
                UnityEngine.Object.Destroy(col);
            }
        }

        static void ApplyCapsuleMaterial(GameObject go, Color color)
        {
            var mr = go.GetComponent<Renderer>();
            if (mr == null)
                return;

            if (_fallbackEnemyMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    _fallbackEnemyMaterial = new Material(shader);
                    if (_fallbackEnemyMaterial.HasProperty("_BaseColor"))
                        _fallbackEnemyMaterial.SetColor("_BaseColor", color);
                    if (_fallbackEnemyMaterial.HasProperty("_Color"))
                        _fallbackEnemyMaterial.SetColor("_Color", color);
                }
            }

            if (_fallbackEnemyMaterial != null)
                mr.sharedMaterial = _fallbackEnemyMaterial;
        }

        static void ConfigureAggression(GameObject instance, CaveMobAggression aggression)
        {
            ResolveTypes();
            if (_npcEnemyType == null)
                return;

            var comp = instance.GetComponent(_npcEnemyType);
            if (comp == null)
                return;

            var aggressionField = _npcEnemyType.GetField("aggression");
            if (aggressionField != null && aggressionField.FieldType.IsEnum)
                aggressionField.SetValue(comp, aggression);

            _npcEnemyType.GetMethod("SetAggression", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(comp, new object[] { aggression });
        }

        static void ConfigureEnemyBrain(GameObject instance, int seed)
        {
            var configurer = instance.GetComponent<ICaveSpawnedEnemy>();
            if (configurer != null)
            {
                configurer.Configure(seed);
                return;
            }

            ResolveTypes();
            if (_npcEnemyType == null)
                return;

            var comp = instance.GetComponent(_npcEnemyType);
            if (comp == null)
                comp = instance.AddComponent(_npcEnemyType);

            _npcEnemyType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(comp, new object[] { seed });
        }

        static void ResolveTypes()
        {
            _npcEnemyType ??= Type.GetType("NpcEnemy, Assembly-CSharp");
            _combatStatsType ??= Type.GetType("CombatStats, Assembly-CSharp");
            _enemyControllerType ??= Type.GetType("EnemyController, Assembly-CSharp");
            _caveEnemyType ??= Type.GetType("CaveEnemy, Assembly-CSharp");
        }

        static void SetField(Component comp, string name, object value)
        {
            if (comp == null)
                return;
            var field = comp.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            field?.SetValue(comp, value);
        }
    }
}
