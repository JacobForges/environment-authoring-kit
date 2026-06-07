#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Editor combat bot: validates enemy prefab, spawner spots, animator wiring, and attack/defend capability.
    /// </summary>
    public static class CaveCombatProbeRunner
    {
        public const string ReportPath = CaveBuildAgentContextExporter.Folder + "/CaveBuildCombatProbe.json";

        public struct CombatIssue
        {
            public string Code;
            public string Message;
            public string SuggestedStageId;
        }

        public sealed class CombatReport
        {
            public bool Passed;
            public int SpawnerCount;
            public int AggressiveSpawners;
            public int PassiveSpawners;
            public bool PlayerCanAttack;
            public bool PlayerCanDefend;
            public bool EnemyCanAttackPlayer;
            public readonly List<CombatIssue> Issues = new();
        }

        public static CombatReport Run(Transform caveRoot)
        {
            var report = new CombatReport();
            CaveCombatGameTypes.WarnIfMissing();

            if (!CaveCombatGameTypes.IsGameplayAvailable)
            {
                report.Issues.Add(Issue(
                    "gameplay_asm",
                    "Hub combat scripts not loaded (CombatStats/NpcEnemy/PlayerCombat).",
                    "mob_spawns"));
                report.Passed = false;
                return report;
            }

            if (caveRoot == null)
            {
                report.Issues.Add(Issue("no_cave", "Cave root missing.", "mob_spawns"));
                return report;
            }

            AuditEnemyPrefab(report);
            AuditSpawners(caveRoot, report);
            AuditPlayerCombat(report);
            AuditAggressionProfiles(report);
            SimulateCombatLoops(report);

            report.Passed = report.Issues.Count == 0;
            return report;
        }

        static void AuditEnemyPrefab(CombatReport report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CaveCombatSetupUtility.EnemyPrefabPath);
            if (prefab == null)
            {
                report.Issues.Add(Issue("missing_prefab", $"Missing {CaveCombatSetupUtility.EnemyPrefabPath}.", "mob_spawns"));
                return;
            }

            var root = prefab;

            if (!CaveCombatGameTypes.HasComponent(root, CaveCombatGameTypes.CombatStats))
                report.Issues.Add(Issue("prefab_no_stats", "CaveEnemy prefab needs CombatStats.", "mob_spawns"));

            if (!CaveCombatGameTypes.HasComponent(root, CaveCombatGameTypes.NpcEnemy) &&
                !CaveCombatGameTypes.HasComponent(root, CaveCombatGameTypes.CaveEnemy))
            {
                report.Issues.Add(Issue("prefab_no_ai", "CaveEnemy prefab needs NpcEnemy or CaveEnemy.", "mob_spawns"));
            }

            if (root.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                report.Issues.Add(Issue("prefab_no_nav", "CaveEnemy prefab needs NavMeshAgent for chase.", "navmesh"));

            var animator = root.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                CaveCombatSetupUtility.EnsureEnemyPrefab();
                report.Issues.Add(Issue(
                    "prefab_no_animator",
                    "CaveEnemy prefab had no Animator — run build again after auto-upgrade.",
                    "mob_spawns"));
            }
            else if (!HasAnimatorParam(animator, "Attack") && !HasAnimatorParam(animator, "Speed"))
            {
                report.Issues.Add(Issue(
                    "animator_params",
                    "Enemy Animator missing Attack/Speed parameters — add or use Humanoid controller.",
                    "mob_spawns"));
            }
        }

        static void AuditSpawners(Transform caveRoot, CombatReport report)
        {
            var spawners = caveRoot.GetComponentsInChildren<CaveMobSpawner>(true);
            report.SpawnerCount = spawners.Length;

            if (spawners.Length < 3)
            {
                report.Issues.Add(Issue(
                    "few_spawners",
                    $"Only {spawners.Length} CaveMobSpawner(s) — need ≥3 along route.",
                    "mob_spawns"));
            }

            foreach (var spawner in spawners)
            {
                if (spawner.enemyPrefab == null)
                    report.Issues.Add(Issue("spawner_no_prefab", $"{spawner.name} missing enemyPrefab.", "mob_spawns"));

                if (spawner.transform.Find("SpawnMarker_Capsule") == null)
                    report.Issues.Add(Issue("spawner_no_marker", $"{spawner.name} missing editor capsule marker.", "mob_spawns"));

                switch (spawner.mobAggression)
                {
                    case CaveMobAggression.Aggressive:
                        report.AggressiveSpawners++;
                        break;
                    case CaveMobAggression.Passive:
                        report.PassiveSpawners++;
                        break;
                }
            }

            if (report.AggressiveSpawners < 1)
            {
                report.Issues.Add(Issue(
                    "no_aggressive",
                    "No aggressive mob spawners — mix at least one Aggressive spot.",
                    "mob_spawns"));
            }

            if (report.PassiveSpawners < 1 && report.SpawnerCount >= 3)
            {
                report.Issues.Add(Issue(
                    "no_passive",
                    "No passive mob spawners — mix at least one Passive spot for variety.",
                    "mob_spawns"));
            }
        }

        static void AuditAggressionProfiles(CombatReport report)
        {
            var aggressive = new GameObject("AggressionProbe_Aggressive");
            var passive = new GameObject("AggressionProbe_Passive");

            try
            {
                var aggNpc = CaveCombatGameTypes.AddComponent(aggressive, CaveCombatGameTypes.NpcEnemy);
                var passNpc = CaveCombatGameTypes.AddComponent(passive, CaveCombatGameTypes.NpcEnemy);
                if (aggNpc == null || passNpc == null)
                    return;

                CaveCombatGameTypes.InvokeSetAggression(aggNpc, CaveMobAggression.Aggressive);
                CaveCombatGameTypes.InvokeConfigureNpc(aggNpc, 101);
                CaveCombatGameTypes.InvokeSetAggression(passNpc, CaveMobAggression.Passive);
                CaveCombatGameTypes.InvokeConfigureNpc(passNpc, 202);

                var aggChase = CaveCombatGameTypes.GetFloatField(aggNpc, "chaseRange");
                var passChase = CaveCombatGameTypes.GetFloatField(passNpc, "chaseRange");

                if (aggChase < 10f)
                {
                    report.Issues.Add(Issue(
                        "aggressive_chase",
                        "Aggressive NpcEnemy chaseRange too low — will not hunt player.",
                        "mob_spawns"));
                }

                if (passChase > aggChase * 0.6f)
                {
                    report.Issues.Add(Issue(
                        "passive_chase",
                        "Passive NpcEnemy chaseRange not reduced vs aggressive.",
                        "mob_spawns"));
                }
            }
            finally
            {
                Object.DestroyImmediate(aggressive);
                Object.DestroyImmediate(passive);
            }
        }

        [MenuItem(CaveBuildMenuPaths.Diagnostics + "Run Cave Combat Probe (Bot)")]
        public static void RunCombatProbeMenu()
        {
            var cave = CaveRouteProbeRunner.FindCaveRoot();
            if (cave == null)
            {
                EditorUtility.DisplayDialog("Cave Combat Probe", "Build a cave first.", "OK");
                return;
            }

            CaveCombatSetupUtility.WireSceneCombat(cave);
            var report = Run(cave);
            Export(report, cave);
        }

        static void AuditPlayerCombat(CombatReport report)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                report.Issues.Add(Issue("no_player", "No Player tag in scene.", "mob_spawns"));
                return;
            }

            if (!CaveCombatGameTypes.HasComponent(player, CaveCombatGameTypes.CombatStats))
                report.Issues.Add(Issue("player_no_stats", "Player needs CombatStats.", "mob_spawns"));

            var combat = CaveCombatGameTypes.GetComponent(player, CaveCombatGameTypes.PlayerCombat);
            if (combat == null)
            {
                report.Issues.Add(Issue("player_no_combat", "Player needs PlayerCombat for attack/skills.", "mob_spawns"));
                return;
            }

            if (CaveCombatGameTypes.GetField(combat, "bashSkill") == null ||
                CaveCombatGameTypes.GetField(combat, "healSkill") == null)
            {
                report.Issues.Add(Issue(
                    "player_skills",
                    "PlayerCombat missing bash/heal SkillDefinition assets.",
                    "mob_spawns"));
            }

            var controller = CaveCombatGameTypes.GetComponent(player, CaveCombatGameTypes.PlayerController);
            if (controller == null)
                report.Issues.Add(Issue("player_no_controller", "Player needs PlayerController for defend input.", "mob_spawns"));
            else
                report.PlayerCanDefend = true;

            var attackDamage = CaveCombatGameTypes.GetIntField(combat, "attackDamage");
            var attackRange = CaveCombatGameTypes.GetFloatField(combat, "attackRange");
            report.PlayerCanAttack = attackDamage > 0 && attackRange > 0.5f;
            if (!report.PlayerCanAttack)
                report.Issues.Add(Issue("player_attack_zero", "Player attack damage/range invalid.", "mob_spawns"));
        }

        static void SimulateCombatLoops(CombatReport report)
        {
            var playerGo = new GameObject("CombatProbe_Player");
            var playerStats = CaveCombatGameTypes.AddComponent(playerGo, CaveCombatGameTypes.CombatStats);
            CaveCombatGameTypes.SetField(playerStats, "maxHp", 50);
            CaveCombatGameTypes.SetField(playerStats, "currentHp", 50);
            CaveCombatGameTypes.SetField(playerStats, "defense", 2);
            var playerController = CaveCombatGameTypes.AddComponent(playerGo, CaveCombatGameTypes.PlayerController);

            var enemyGo = new GameObject("CombatProbe_Enemy");
            var enemyStats = CaveCombatGameTypes.AddComponent(enemyGo, CaveCombatGameTypes.CombatStats);
            CaveCombatGameTypes.SetField(enemyStats, "maxHp", 30);
            CaveCombatGameTypes.SetField(enemyStats, "currentHp", 30);
            CaveCombatGameTypes.SetField(enemyStats, "attackPower", 8);
            CaveCombatGameTypes.SetField(enemyStats, "defense", 1);

            var rawHit = 12;
            CaveCombatGameTypes.InvokeTakeDamage(enemyStats, rawHit);
            var enemyHp = CaveCombatGameTypes.GetIntField(enemyStats, "currentHp", 30);
            if (enemyHp >= 30)
                report.Issues.Add(Issue("sim_player_attack", "Player damage simulation did not reduce enemy HP.", "mob_spawns"));

            CaveCombatGameTypes.SetField(enemyStats, "currentHp", 30);
            CaveCombatGameTypes.SetField(playerStats, "currentHp", 50);
            CaveCombatGameTypes.InvokeDefend(playerController, true);

            CaveCombatGameTypes.InvokeTakeDamage(playerStats, 10);
            var defendedHp = CaveCombatGameTypes.GetIntField(playerStats, "currentHp", 50);
            CaveCombatGameTypes.SetField(playerStats, "currentHp", 50);
            CaveCombatGameTypes.InvokeDefend(playerController, false);
            CaveCombatGameTypes.InvokeTakeDamage(playerStats, 10);
            var undefendedHp = CaveCombatGameTypes.GetIntField(playerStats, "currentHp", 50);
            report.PlayerCanDefend = defendedHp > undefendedHp;
            if (!report.PlayerCanDefend)
            {
                report.Issues.Add(Issue(
                    "sim_player_defend",
                    "Defend did not reduce incoming damage (hold RMB / IsDefending).",
                    "mob_spawns"));
            }

            CaveCombatGameTypes.SetField(playerStats, "currentHp", 50);
            CaveCombatGameTypes.InvokeTakeDamage(enemyStats, 20);
            var attackPower = CaveCombatGameTypes.GetIntField(enemyStats, "attackPower", 8);
            var enemyHits = 8 + attackPower / 3;
            CaveCombatGameTypes.InvokeTakeDamage(playerStats, enemyHits);
            var playerHp = CaveCombatGameTypes.GetIntField(playerStats, "currentHp", 50);
            report.EnemyCanAttackPlayer = playerHp < 50;
            if (!report.EnemyCanAttackPlayer)
                report.Issues.Add(Issue("sim_enemy_attack", "Enemy damage simulation did not harm player.", "mob_spawns"));

            Object.DestroyImmediate(playerGo);
            Object.DestroyImmediate(enemyGo);
        }

        public static void Export(CombatReport report, Transform caveRoot)
        {
            if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit/Generated"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/EnvironmentKit"))
                    AssetDatabase.CreateFolder("Assets", "EnvironmentKit");
                AssetDatabase.CreateFolder("Assets/EnvironmentKit", "Generated");
            }

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"passed\": {(report.Passed ? "true" : "false")},");
            sb.AppendLine($"  \"spawnerCount\": {report.SpawnerCount},");
            sb.AppendLine($"  \"aggressiveSpawners\": {report.AggressiveSpawners},");
            sb.AppendLine($"  \"passiveSpawners\": {report.PassiveSpawners},");
            sb.AppendLine($"  \"playerCanAttack\": {(report.PlayerCanAttack ? "true" : "false")},");
            sb.AppendLine($"  \"playerCanDefend\": {(report.PlayerCanDefend ? "true" : "false")},");
            sb.AppendLine($"  \"enemyCanAttackPlayer\": {(report.EnemyCanAttackPlayer ? "true" : "false")},");
            sb.AppendLine($"  \"caveRoot\": \"{Escape(caveRoot != null ? caveRoot.name : "")}\",");
            sb.AppendLine("  \"issues\": [");
            for (var i = 0; i < report.Issues.Count; i++)
            {
                var issue = report.Issues[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"code\": \"{Escape(issue.Code)}\",");
                sb.AppendLine($"      \"message\": \"{Escape(issue.Message)}\",");
                sb.AppendLine($"      \"suggestedStageId\": \"{Escape(issue.SuggestedStageId)}\"");
                sb.AppendLine(i < report.Issues.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(projectRoot, ReportPath), sb.ToString());
            Debug.Log($"[CaveCombatProbe] Wrote {ReportPath} — {(report.Passed ? "PASS" : $"{report.Issues.Count} issue(s)")}");
        }

        static CombatIssue Issue(string code, string message, string stage) =>
            new() { Code = code, Message = message, SuggestedStageId = stage };

        static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static bool HasAnimatorParam(Animator animator, string name)
        {
            foreach (var p in animator.parameters)
            {
                if (p.name == name)
                    return true;
            }

            return false;
        }
    }
}
#endif
