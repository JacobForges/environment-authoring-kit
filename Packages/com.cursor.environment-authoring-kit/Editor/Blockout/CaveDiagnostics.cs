using System.Text;
using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// One-click cave diagnostics + a guaranteed-visible debug room. Use these to verify
    /// teleport and geometry independently of the procedural spline generator.
    /// </summary>
    public static class CaveDiagnostics
    {
        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Remove Cave Water")]
        public static void RemoveCaveWaterFromMenu()
        {
            var caveRoot = GameObject.Find(CaveEntranceTeleport.CaveSystemObjectName)?.transform;
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog("Remove Cave Water", "LavaTubeCaveSystem not found.", "OK");
                return;
            }

            CaveWaterUtility.ClearAllWater(caveRoot);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Remove Cave Water", "All cave water pools, branch tubes, and anchors removed.", "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Restore Route + Mob Spawns")]
        public static void RestoreRouteAndSpawns()
        {
            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog("Restore Route", "UndergroundCaveSystem not found.", "OK");
                return;
            }

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
            {
                EditorUtility.DisplayDialog("Restore Route", "Missing CaveBuildMetadata — run Build Complete Cave Level first.", "OK");
                return;
            }

            var layout = CaveMazeLayoutGenerator.Generate(meta.seed, meta.tunnelSegments, meta.chamberCount);
            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            if (geometry == null)
            {
                EditorUtility.DisplayDialog("Restore Route", "Missing CaveGeometry.", "OK");
                return;
            }

            var floorMat = CaveSplineMaterialFactory.GetOrCreateCaveFloorMaterial();
            var rockMat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();
            CaveEnclosureShellBuilder.PurgeLayerOffenders(geometry);
            var terrain = CaveEnclosureShellBuilder.Build(geometry, layout, floorMat, rockMat, meta.seed);
            var mobs = CaveMobSpawnerPlacement.PlaceAlongRoute(caveRoot, layout);
            var nodes = new System.Collections.Generic.List<Vector3>();
            foreach (var cell in layout.SolutionPath)
                nodes.Add(layout.CellToLocal(cell.x, cell.y));
            LavaTubeCaveEnclosureBuilder.EnsureAtmosphereZone(caveRoot, nodes);
            CaveAdventureCaveLighting.Apply(caveRoot, layout);
            LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog(
                "Restore Route",
                $"Terrain surfaces: {terrain}\nMob spawners: {mobs}\nNavMesh rebaked.\nSave the scene.",
                "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Remove Layered Shells")]
        public static void RemoveLayeredShells()
        {
            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Remove Layered Shells",
                    "UndergroundCaveSystem / LavaTubeCaveSystem not found under Grid.",
                    "OK");
                return;
            }

            var removed = CaveCompactLayerPurge.Purge(caveRoot);
            var rebuilt = 0;
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                var layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
                rebuilt = CaveCompactRouteUtility.RebuildCompactRouteShell(caveRoot, layout, meta.seed);
            }

            CaveAdventureVisualPass.Apply(caveRoot);
            CaveEnclosureShellBuilder.HideRoutePlatformSlabs(caveRoot);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog(
                "Remove Layered Shells",
                $"Removed {removed} layered slab/shell object(s).\n" +
                $"Rebuilt {rebuilt} route terrain surface(s) (RouteTerrainFloor + RouteTerrainCeiling).\n\n" +
                "PathPlatforms stay hidden when route floor exists. Run Build Complete Cave Level if walls look sparse.",
                "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Fix Cave Colliders")]
        public static void FixCaveColliders()
        {
            var caveRoot = GameObject.Find(CaveEntranceTeleport.CaveSystemObjectName)?.transform;
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog("Fix Cave Colliders", "LavaTubeCaveSystem not found.", "OK");
                return;
            }

            var fixedMaze = CaveColliderUtility.EnsureMazeVolumeColliders(caveRoot);
            var walkRebuilt = CaveWalkwayBuilder.RebuildFromAuthoring(caveRoot);

            LavaTubeCavePostProcess.BakeNavMeshOnly(caveRoot);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog(
                "Fix Cave Colliders",
                $"Restored {fixedMaze} maze colliders.\nRebuilt {walkRebuilt} walk floors.\nNavMesh re-baked.",
                "OK");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Diagnostics/Diagnose Cave State")]
        public static void DiagnoseCaveState()
        {
            var report = new StringBuilder();
            report.AppendLine("=== Cave Diagnostic Report ===");

            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot == null)
            {
                report.AppendLine("✗ LavaTubeCaveSystem NOT FOUND in scene.");
                Debug.LogWarning(report.ToString());
                EditorUtility.DisplayDialog("Cave Diagnostic", report.ToString(), "OK");
                return;
            }

            report.AppendLine($"✓ LavaTubeCaveSystem world position = {caveRoot.transform.position}");
            report.AppendLine($"  Rotation = {caveRoot.transform.eulerAngles}");

            var isHybrid = CaveGeometryPaths.IsAdventureCave(caveRoot.transform);
            report.AppendLine(isHybrid
                ? "✓ Mode: HYBRID (grid floors + minable block walls)"
                : "✗ Mode: LEGACY spline (Walkways/MainCaveTube) — NOT the hybrid cave you want.");

            var geometry = caveRoot.transform.Find(CaveGeometryPaths.GeometryRoot);
            var shell = geometry != null ? geometry.Find(CaveGeometryPaths.AdventureShell) : null;
            var blockTunnel = CaveGeometryPaths.FindBlockTunnel(caveRoot.transform);
            if (geometry == null)
                report.AppendLine("  ✗ Missing CaveGeometry — run Build Complete Cave Level.");
            if (shell == null)
                report.AppendLine("  ✗ Missing AdventureShell (walkable floors) — full rebuild required.");
            else
            {
                var walkFloors = 0;
                foreach (var col in shell.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null && !col.isTrigger && col.gameObject.name.Contains("WalkFloor_"))
                        walkFloors++;
                }

                report.AppendLine($"  AdventureShell walk floors = {walkFloors} (need ≥ 6)");
                if (walkFloors < 6)
                    report.AppendLine("  ✗ Too few floors — rebuild or Window → Fix Cave Playability.");
            }

            if (blockTunnel == null)
                report.AppendLine("  ✗ Missing BlockTunnel — no minable walls.");
            else
            {
                var minable = 0;
                foreach (var t in blockTunnel.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name.StartsWith("CaveBlock_Minable"))
                        minable++;
                }

                report.AppendLine($"  Minable wall blocks = {minable} (need ≥ 80)");
                if (minable < 80)
                    report.AppendLine("  ✗ Too few blocks — full rebuild required.");
            }

            var legacyWalk = caveRoot.transform.Find("Walkways");
            if (legacyWalk != null && legacyWalk.childCount > 0 && !isHybrid)
                report.AppendLine("  ⚠ Legacy Walkways present — these are thin spline floors, not grid cave floors.");

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                report.AppendLine("  ✗ No CaveBuildMetadata — build never finished hybrid generator.");
            else
                report.AppendLine($"  Build metadata: seed={meta.seed}, hybrid={meta.adventureHybrid}");

            if (!isHybrid)
            {
                report.AppendLine();
                report.AppendLine("FIX: Window → Environment Kit → Build Complete Cave Level (Active Scene)");
                report.AppendLine("Wait for 0 compile errors first. Save scene after build.");
                report.AppendLine("Then confirm: Grid/LavaTubeCaveSystem/CaveGeometry/AdventureShell + BlockTunnel.");
            }

            var combinedBounds = new Bounds(caveRoot.transform.position, Vector3.zero);
            var hasBounds = false;
            foreach (var r in caveRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (!hasBounds) { combinedBounds = r.bounds; hasBounds = true; }
                else combinedBounds.Encapsulate(r.bounds);
            }
            if (hasBounds)
            {
                report.AppendLine($"  Combined renderer bounds center = {combinedBounds.center}");
                report.AppendLine($"  Combined renderer bounds size = {combinedBounds.size}");
            }
            else
            {
                report.AppendLine("  ✗ No enabled renderers found under cave root.");
            }

            var mazeVolume = caveRoot.transform.Find("SplineMesh/CaveMazeVolume");
            if (mazeVolume != null)
            {
                var walls = mazeVolume.GetComponentsInChildren<MeshRenderer>(true).Length;
                var cols = mazeVolume.GetComponentsInChildren<Collider>(true).Length;
                report.AppendLine($"✓ CaveMazeVolume exists, wall renderers = {walls}, colliders = {cols}");
                if (cols < 12)
                    report.AppendLine("  ✗ Too few maze colliders — run Fix Cave Colliders or rebuild.");
            }

            var mainTube = caveRoot.transform.Find("SplineMesh/MainCaveTube");
            if (mainTube != null)
            {
                var mf = mainTube.GetComponent<MeshFilter>();
                var mr = mainTube.GetComponent<MeshRenderer>();
                var tris = mf != null && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0;
                report.AppendLine($"✓ MainCaveTube exists, tris = {tris}, renderer enabled = {(mr != null && mr.enabled)}");
                report.AppendLine($"  MainCaveTube world center = {(mr != null ? mr.bounds.center.ToString() : "n/a")}");
            }
            else if (mazeVolume == null)
            {
                report.AppendLine("✗ SplineMesh/MainCaveTube and CaveMazeVolume NOT FOUND.");
            }

            var outer = caveRoot.transform.Find("SplineMesh/MainCaveOuterShell");
            if (outer != null)
            {
                var mf = outer.GetComponent<MeshFilter>();
                var tris = mf != null && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0;
                report.AppendLine($"✓ MainCaveOuterShell exists, tris = {tris}");
            }

            var spawn = caveRoot.transform.Find($"Entrance/{CaveEntranceTeleport.SpawnPointObjectName}");
            if (spawn != null)
            {
                report.AppendLine($"✓ CaveEntrance_SpawnPoint world position = {spawn.position}");
                if (hasBounds)
                {
                    var inside = combinedBounds.Contains(spawn.position);
                    var dist = Vector3.Distance(spawn.position, combinedBounds.center);
                    report.AppendLine($"  Spawn inside cave bounds = {inside}, distance from center = {dist:F2}m");
                }
            }
            else
            {
                report.AppendLine("✗ CaveEntrance_SpawnPoint NOT FOUND.");
            }

            var atmosphere = caveRoot.GetComponentInChildren<CaveUndergroundAtmosphere>(true);
            if (atmosphere != null)
            {
                var col = atmosphere.GetComponent<Collider>();
                report.AppendLine($"✓ CaveUndergroundAtmosphere on '{atmosphere.gameObject.name}'");
                report.AppendLine($"  Trigger collider = {(col != null && col.isTrigger)}, bounds = {(col != null ? col.bounds.ToString() : "n/a")}");
                report.AppendLine($"  fogDensity = {atmosphere.fogDensity}, cameraBackground = {atmosphere.cameraBackground}");
            }
            else
            {
                report.AppendLine("✗ CaveUndergroundAtmosphere NOT FOUND.");
            }

            var torchCount = 0;
            var lightCount = 0;
            foreach (var l in caveRoot.GetComponentsInChildren<Light>(true))
            {
                if (!l.enabled) continue;
                lightCount++;
                if (l.name.Contains("Torch") || l.name.Contains("Spline")) torchCount++;
            }
            report.AppendLine($"  Lights enabled = {lightCount} (torches/spline = {torchCount})");

            var controller = Object.FindAnyObjectByType<CharacterController>();
            if (controller != null)
            {
                report.AppendLine($"  Player CharacterController world position = {controller.transform.position}");
                report.AppendLine($"  Controller enabled = {controller.enabled}");
                if (spawn != null)
                {
                    var spawnDist = Vector3.Distance(controller.transform.position, spawn.position);
                    report.AppendLine($"  Distance from spawn point = {spawnDist:F2}m");
                }
            }
            else
            {
                report.AppendLine("  ✗ No CharacterController found in scene.");
            }

            report.AppendLine($"  RenderSettings.fog = {RenderSettings.fog}, fogColor = {RenderSettings.fogColor}, fogDensity = {RenderSettings.fogDensity}");
            report.AppendLine($"  RenderSettings.ambientLight = {RenderSettings.ambientLight}, intensity = {RenderSettings.ambientIntensity}");

            var output = report.ToString();
            Debug.Log(output);
            EditorUtility.DisplayDialog("Cave Diagnostic", output, "Copy to Console");
        }

        [MenuItem("Window/Environment Kit/Cave Build/Diagnostics/Build Debug Cave Room")]
        public static void BuildDebugCaveRoom()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Debug Cave Room",
                    "This builds a simple 20×20×8m enclosed box room as the cave system, " +
                    "with a spawn point at center and 4 torches. " +
                    "Use this to verify teleport works independently of procedural cave geometry.\n\n" +
                    "Existing LavaTubeCaveSystem will be replaced.",
                    "Build Room",
                    "Cancel"))
                return;

            var grid = GameObject.Find("Grid") ?? GameObject.Find("Environment/Grid");
            Transform parent = grid != null ? grid.transform : null;
            if (parent == null)
            {
                var envRoot = Object.FindAnyObjectByType<EnvironmentAuthoringKit.EnvironmentRoot>();
                parent = envRoot != null ? envRoot.transform : null;
            }

            var existing = GameObject.Find(CaveEntranceTeleport.CaveSystemObjectName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var caveRoot = new GameObject(CaveEntranceTeleport.CaveSystemObjectName);
            if (parent != null) caveRoot.transform.SetParent(parent, false);

            // Place the room near origin underground so it's predictable.
            caveRoot.transform.position = new Vector3(0f, -25f, 0f);

            const float width = 20f;
            const float depth = 20f;
            const float height = 8f;
            var halfW = width * 0.5f;
            var halfD = depth * 0.5f;

            var mat = CaveSplineMaterialFactory.GetOrCreateCaveRockMaterial();

            // 6 inward-facing wall slabs (floor, ceiling, 4 walls). Using inverted-scale cubes so
            // back-face culling shows the interior surface.
            CreateInvertedWall(caveRoot.transform, "Floor", new Vector3(0f, 0f, 0f), new Vector3(width, 0.5f, depth), mat);
            CreateInvertedWall(caveRoot.transform, "Ceiling", new Vector3(0f, height, 0f), new Vector3(width, 0.5f, depth), mat);
            CreateInvertedWall(caveRoot.transform, "Wall_North", new Vector3(0f, height * 0.5f, halfD), new Vector3(width, height, 0.5f), mat);
            CreateInvertedWall(caveRoot.transform, "Wall_South", new Vector3(0f, height * 0.5f, -halfD), new Vector3(width, height, 0.5f), mat);
            CreateInvertedWall(caveRoot.transform, "Wall_East", new Vector3(halfW, height * 0.5f, 0f), new Vector3(0.5f, height, depth), mat);
            CreateInvertedWall(caveRoot.transform, "Wall_West", new Vector3(-halfW, height * 0.5f, 0f), new Vector3(0.5f, height, depth), mat);

            // 4 torch lights at corners.
            CreateTorch(caveRoot.transform, "Torch_NE", new Vector3(halfW - 2f, height * 0.6f, halfD - 2f));
            CreateTorch(caveRoot.transform, "Torch_NW", new Vector3(-halfW + 2f, height * 0.6f, halfD - 2f));
            CreateTorch(caveRoot.transform, "Torch_SE", new Vector3(halfW - 2f, height * 0.6f, -halfD + 2f));
            CreateTorch(caveRoot.transform, "Torch_SW", new Vector3(-halfW + 2f, height * 0.6f, -halfD + 2f));

            // Center bonfire light for the spawn area.
            CreateTorch(caveRoot.transform, "Bonfire_Center", new Vector3(0f, 2f, 0f), intensity: 8f, range: 18f);

            // Entrance / spawn.
            var entrance = new GameObject("Entrance");
            entrance.transform.SetParent(caveRoot.transform, false);
            var spawn = new GameObject(CaveEntranceTeleport.SpawnPointObjectName);
            spawn.transform.SetParent(entrance.transform, false);
            spawn.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            spawn.transform.localRotation = Quaternion.identity;
            spawn.AddComponent<CaveEntranceSpawnPoint>();
            spawn.tag = CaveTags.Entrance;

            // Atmosphere trigger covering the full room.
            var atmoGo = new GameObject("CaveAtmosphereZone");
            atmoGo.transform.SetParent(caveRoot.transform, false);
            atmoGo.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            var box = atmoGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width - 1f, height - 1f, depth - 1f);
            var atmo = atmoGo.AddComponent<CaveUndergroundAtmosphere>();
            atmo.cameraBackground = new Color(0.018f, 0.012f, 0.008f, 1f);
            atmo.overrideFog = true;
            atmo.fogColor = new Color(0.05f, 0.035f, 0.025f, 1f);
            atmo.fogDensity = 0.035f;
            atmo.fogMode = FogMode.ExponentialSquared;
            atmo.overrideAmbient = true;
            atmo.ambientSky = new Color(0.07f, 0.055f, 0.04f, 1f);
            atmo.ambientEquator = new Color(0.05f, 0.038f, 0.028f, 1f);
            atmo.ambientGround = new Color(0.03f, 0.022f, 0.016f, 1f);
            atmo.ambientIntensity = 0.55f;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[DebugRoom] Built at {caveRoot.transform.position}. Spawn at {spawn.transform.position}.");
            EditorUtility.DisplayDialog(
                "Debug Cave Room",
                $"Built 20×20×8m debug room at {caveRoot.transform.position}.\n" +
                $"Spawn point at {spawn.transform.position}.\n\n" +
                "Save the scene then portal in. If you see this enclosed room with 5 torches, " +
                "the teleport works and the procedural cave gen is the problem to focus on. " +
                "If you still see open sky/ocean, the teleport itself is broken.",
                "OK");
        }

        static void CreateInvertedWall(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            // Make the cube wall RENDER from the inside: instantiate a mesh with reversed triangles.
            // Simpler approach: keep the cube, set the material's cull mode to Off so both sides render.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.TwoSided;
                mr.receiveShadows = true;
            }

            var col = go.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.isTrigger = false;
            }
        }

        static void CreateTorch(Transform parent, string name, Vector3 localPos, float intensity = 4.5f, float range = 14f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.62f, 0.28f);
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.bounceIntensity = 0f;
        }
    }
}
