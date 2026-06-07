using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Places mob spawners on the route with editor capsule markers and default enemy wiring.</summary>
    public static class CaveMobSpawnerPlacement
    {
        const string SpawnsRootName = "Spawns";

        static readonly CaveMobAggression[] AggressionPattern =
        {
            CaveMobAggression.Aggressive,
            CaveMobAggression.Aggressive,
            CaveMobAggression.Defensive,
            CaveMobAggression.Passive,
        };

        public static int PlaceAlongRoute(Transform cavesRoot, CaveMazeLayout layout)
        {
            if (cavesRoot == null || layout?.SolutionPath == null || layout.SolutionPath.Count < 2)
                return 0;

            var spawnsRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, SpawnsRootName);
            CaveBuildSceneUtility.ClearChildrenFast(spawnsRoot);

            var prefab = CaveCombatSetupUtility.EnsureEnemyPrefab();
            var positions = CollectSpawnPositions(layout);
            var placed = 0;

            for (var idx = 0; idx < positions.Count; idx++)
            {
                var aggression = AggressionPattern[idx % AggressionPattern.Length];
                var go = new GameObject($"MobSpawn_{idx:D2}_{aggression}");
                CaveEditorUndo.RegisterCreated(go, "Mob Spawn");
                go.transform.SetParent(spawnsRoot, false);
                go.transform.localPosition = positions[idx];

                AddEditorCapsuleMarker(go, aggression);

                var spawner = go.AddComponent<CaveMobSpawner>();
                spawner.enemyPrefab = prefab;
                spawner.mobAggression = aggression;
                spawner.spawnCount = aggression == CaveMobAggression.Passive ? 1 : 2;
                spawner.radius = aggression == CaveMobAggression.Aggressive ? 8f : 6f;
                spawner.spawnSeed = UnityObjectCompat.ReferenceId(cavesRoot) + idx * 131;
                spawner.spawnOnStart = true;
                placed++;
            }

            CaveCombatSetupUtility.WireSceneCombat(cavesRoot);
            return placed;
        }

        public static int PlaceAlongRoute(
            Transform cavesRoot,
            System.Collections.Generic.IReadOnlyList<CavePathKnot> knots)
        {
            if (cavesRoot == null || knots == null || knots.Count < 2)
                return 0;

            var meta = cavesRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                return PlaceAlongRoute(cavesRoot, layout);
            }

            var spawnsRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, SpawnsRootName);
            CaveBuildSceneUtility.ClearChildrenFast(spawnsRoot);

            var prefab = CaveCombatSetupUtility.EnsureEnemyPrefab();
            var positions = new System.Collections.Generic.List<Vector3>();
            var chamberPositions = new System.Collections.Generic.List<Vector3>();

            foreach (var k in knots)
            {
                if (k.IsChamber)
                    chamberPositions.Add(k.Position);
            }

            const int minSpawnerPoints = 4;
            if (chamberPositions.Count >= minSpawnerPoints)
            {
                for (var i = 0; i < minSpawnerPoints; i++)
                    positions.Add(chamberPositions[i]);
            }
            else
            {
                positions.AddRange(chamberPositions);
                for (var i = 0; i < knots.Count && positions.Count < minSpawnerPoints; i++)
                {
                    if (i == 0)
                        continue;
                    var p = knots[i].Position;
                    if (!positions.Exists(v => (v - p).sqrMagnitude < 36f))
                        positions.Add(p);
                }

                while (positions.Count < minSpawnerPoints && knots.Count > 1)
                {
                    var t = positions.Count / (float)minSpawnerPoints;
                    var idx = Mathf.Clamp(Mathf.RoundToInt(t * (knots.Count - 1)), 1, knots.Count - 1);
                    positions.Add(knots[idx].Position);
                }
            }

            var placed = 0;
            for (var idx = 0; idx < positions.Count; idx++)
            {
                var aggression = AggressionPattern[idx % AggressionPattern.Length];
                var go = new GameObject($"MobSpawn_{idx:D2}_{aggression}");
                CaveEditorUndo.RegisterCreated(go, "Mob Spawn");
                go.transform.SetParent(spawnsRoot, false);
                go.transform.localPosition = positions[idx];
                AddEditorCapsuleMarker(go, aggression);

                var spawner = go.AddComponent<CaveMobSpawner>();
                spawner.enemyPrefab = prefab;
                spawner.mobAggression = aggression;
                spawner.spawnCount = aggression == CaveMobAggression.Passive ? 1 : 2;
                spawner.radius = 7f;
                spawner.spawnSeed = UnityObjectCompat.ReferenceId(cavesRoot) + idx * 131;
                spawner.spawnOnStart = true;
                placed++;
            }

            CaveCombatSetupUtility.WireSceneCombat(cavesRoot);
            return placed;
        }

        static System.Collections.Generic.List<Vector3> CollectSpawnPositions(CaveMazeLayout layout)
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            var path = layout.SolutionPath;
            var step = Mathf.Max(2, path.Count / 5);

            for (var i = 0; i < path.Count; i += step)
            {
                var cell = path[i];
                if (layout.IsJumpGap(cell.x, cell.y))
                    continue;

                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                positions.Add(floor + Vector3.up * 0.35f);
            }

            if (layout.IsCavernCell(layout.CavernCenter.x, layout.CavernCenter.y))
            {
                var cavern = layout.GetFloorSurfaceLocal(layout.CavernCenter.x, layout.CavernCenter.y);
                if (!positions.Exists(v => (v - cavern).sqrMagnitude < 16f))
                    positions.Add(cavern + Vector3.up * 0.35f);
            }

            while (positions.Count < 4 && path.Count > 0)
            {
                var cell = path[positions.Count % path.Count];
                var floor = layout.GetFloorSurfaceLocal(cell.x, cell.y);
                positions.Add(floor + Vector3.up * 0.35f);
            }

            return positions;
        }

        static void AddEditorCapsuleMarker(GameObject spawner, CaveMobAggression aggression)
        {
            var existing = spawner.transform.Find("SpawnMarker_Capsule");
            if (existing != null)
                return;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            CaveEditorUndo.RegisterCreated(marker, "Spawn Marker");
            marker.name = "SpawnMarker_Capsule";
            marker.transform.SetParent(spawner.transform, false);
            marker.transform.localPosition = Vector3.up * 0.9f;
            marker.transform.localScale = new Vector3(0.45f, 0.55f, 0.45f);

            var col = marker.GetComponent<Collider>();
            if (col != null)
                Object.DestroyImmediate(col);

            var mr = marker.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    var color = aggression switch
                    {
                        CaveMobAggression.Passive => new Color(0.35f, 0.55f, 0.85f, 0.85f),
                        CaveMobAggression.Defensive => new Color(0.85f, 0.55f, 0.15f, 0.85f),
                        _ => new Color(0.85f, 0.18f, 0.12f, 0.9f),
                    };
                    if (mat.HasProperty("_BaseColor"))
                        mat.SetColor("_BaseColor", color);
                    mr.sharedMaterial = mat;
                }
            }

            if (marker.GetComponent<CaveSpawnEditorMarker>() == null)
                marker.AddComponent<CaveSpawnEditorMarker>();
        }
    }
}
