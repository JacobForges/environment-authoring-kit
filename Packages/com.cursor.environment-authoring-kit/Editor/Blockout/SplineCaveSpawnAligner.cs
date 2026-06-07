using EnvironmentAuthoringKit.Cave;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    static class SplineCaveSpawnAligner
    {
        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Fix Cave Spawn (MainScene)")]
        public static void FixSpawnFromMenu()
        {
            var cavesRoot = GameObject.Find("Grid")?.transform.Find("LavaTubeCaveSystem");
            if (cavesRoot == null)
                cavesRoot = GameObject.Find("LavaTubeCaveSystem")?.transform;
            if (cavesRoot == null)
            {
                EditorUtility.DisplayDialog("Fix Cave Spawn", "LavaTubeCaveSystem not found. Rebuild the cave first.", "OK");
                return;
            }

            var authoring = cavesRoot.GetComponentInChildren<CaveSplinePathAuthoring>(true);
            if (authoring == null || authoring.Knots == null || authoring.Knots.Count < 2)
            {
                EditorUtility.DisplayDialog("Fix Cave Spawn", "No spline path on cave. Run Rebuild Complete Cave.", "OK");
                return;
            }

            var spline = CaveSplinePathSpace.CreateLocalSpline(authoring);
            var entrance = cavesRoot.Find("Entrance");
            CaveMazeLayout layout = null;
            var meta = cavesRoot.GetComponent<CaveBuildMetadata>();
            if (meta != null)
            {
                layout = CaveMazeLayoutGenerator.Generate(
                    meta.seed, meta.tunnelSegments, meta.chamberCount);
            }

            var spawn = layout != null
                ? CaveSpawnTeleportAuthority.ApplyMainAreaTeleportSpawn(cavesRoot)
                : AlignEntranceSpawn(cavesRoot, entrance, spline, keepAtSurfaceMouth: true);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog(
                "Fix Cave Spawn",
                layout != null
                    ? "Spawn set to maze route start. PortalFive relinked."
                    : "Spawn kept at above-ground cave mouth. PortalFive relinked.",
                "OK");
        }

        public static Transform AlignEntranceSpawn(
            Transform cavesRoot,
            Transform entrance,
            CaveSplinePath spline,
            bool keepAtSurfaceMouth = true,
            CaveMazeLayout mazeLayout = null)
        {
            if (cavesRoot == null || entrance == null || spline == null || spline.KnotCount < 2)
                return null;

            if (mazeLayout != null && mazeLayout.SolutionPath != null && mazeLayout.SolutionPath.Count > 0)
            {
                CaveSpawnAlignmentUtility.AlignSpawnToMazeStart(cavesRoot, mazeLayout);
                var mazeSpawn = EnsureSpawnTransform(entrance);
                EnsureSpawnGroundPad(mazeSpawn, mazeSpawn.rotation);
                EnsureSpawnTorch(mazeSpawn);
                Debug.Log(
                    $"[SplineCave] Spawn aligned to maze route start (world {mazeSpawn.position}).",
                    mazeSpawn);
                return mazeSpawn;
            }

            var spawn = EnsureSpawnTransform(entrance);
            if (keepAtSurfaceMouth && HasSurfaceEntranceMouth(entrance))
            {
                AlignSpawnAtSurfaceMouth(cavesRoot, entrance, spline, spawn);
                EnsureSpawnGroundPad(spawn, spawn.rotation);
                return spawn;
            }

            var spawnDist = Mathf.Clamp(6f, 2f, spline.TotalLength * 0.18f);
            var entranceFallback = entrance.TransformPoint(new Vector3(1f, 2.1f, 2f));
            var entranceRot = entrance.rotation * Quaternion.LookRotation(Vector3.forward, Vector3.up);

            var sample = spline.SampleAtDistance(spawnDist);
            var localFloor = sample.Position - sample.Up * (sample.RadiusY * 0.55f);
            var localPos = localFloor + sample.Up * 0.95f;
            var localRot = Quaternion.LookRotation(sample.Tangent, Vector3.up);

            var worldPos = cavesRoot.TransformPoint(localPos);
            var worldRot = cavesRoot.rotation * localRot;

            if (Vector3.Distance(worldPos, entranceFallback) > 25f)
            {
                worldPos = entranceFallback;
                worldRot = entranceRot;
            }

            spawn = EnsureSpawnTransform(entrance);
            CaveEditorUndo.RecordObject(spawn, "Align Cave Spawn");
            spawn.SetPositionAndRotation(worldPos, worldRot);

            // Keep CaveEntrance_Marker at the shaft mouth (local lift). Moving it to underground
            // spawn world coords breaks mouth→surface grounding and drags the cave root far below terrain.
            RestoreEntranceMarkerAtShaftMouth(entrance);

            var markerComp = spawn.GetComponent<CaveEntranceSpawnPoint>();
            if (markerComp == null)
                markerComp = spawn.gameObject.AddComponent<CaveEntranceSpawnPoint>();
            markerComp.positionOffset = Vector3.zero;
            markerComp.applyRotation = true;

            EnsureSpawnGroundPad(spawn, worldRot);
            EnsureSpawnTorch(spawn);

            Debug.Log($"[SplineCave] Spawn @ {spawnDist:F1}m along path, world {worldPos}.", spawn);
            return spawn;
        }

        static void EnsureSpawnTorch(Transform spawn)
        {
            const string torchName = "SpawnTorchLight";
            var torch = spawn.Find(torchName);
            if (torch == null)
            {
                var torchGo = new GameObject(torchName);
                CaveEditorUndo.RegisterCreated(torchGo, "Spawn Torch");
                torchGo.transform.SetParent(spawn, false);
                torch = torchGo.transform;
            }

            torch.localPosition = new Vector3(0f, 1.6f, 0.4f);
            torch.localRotation = Quaternion.identity;

            var light = torch.GetComponent<Light>();
            if (light == null)
                light = torch.gameObject.AddComponent<Light>();

            CaveEditorUndo.RecordObject(light, "Spawn Torch Light");
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.intensity = 6f;
            light.range = 24f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
            light.bounceIntensity = 0f;
        }

        /// <summary>Re-seat walk-in marker at shaft mouth so ground placement uses a stable offset.</summary>
        public static void RestoreEntranceMarkerAtShaftMouth(Transform entrance)
        {
            if (entrance == null)
                return;

            var marker = entrance.Find(CaveEntranceTeleport.EntranceMarkerObjectName);
            if (marker == null)
                return;

            var targetLocal = new Vector3(
                1f,
                CaveGroundPlacementUtility.DefaultMarkerLiftAboveShaftMeters,
                0f);
            if ((marker.localPosition - targetLocal).sqrMagnitude < 0.01f &&
                marker.localRotation == Quaternion.identity)
                return;

            CaveEditorUndo.RecordObject(marker, "Restore Cave Entrance Marker");
            marker.localPosition = targetLocal;
            marker.localRotation = Quaternion.identity;
        }

        static bool HasSurfaceEntranceMouth(Transform entrance)
        {
            var marker = entrance.Find(CaveEntranceTeleport.EntranceMarkerObjectName);
            return marker != null && marker.localPosition.y < 4f;
        }

        static void AlignSpawnAtSurfaceMouth(
            Transform cavesRoot,
            Transform entrance,
            CaveSplinePath spline,
            Transform spawn)
        {
            var marker = entrance.Find(CaveEntranceTeleport.EntranceMarkerObjectName);
            var mouthLocal = marker != null
                ? marker.localPosition
                : new Vector3(1f, 1.6f, 0f);

            var sample = spline.SampleAtDistance(Mathf.Min(2.5f, spline.TotalLength));
            var localRot = Quaternion.LookRotation(sample.Tangent, Vector3.up);
            spawn.localPosition = mouthLocal + Vector3.up * 0.15f;
            spawn.localRotation = localRot;

            if (marker != null)
            {
                CaveEditorUndo.RecordObject(marker, "Align Cave Marker");
                marker.localRotation = localRot;
            }
        }

        static Transform EnsureSpawnTransform(Transform entrance)
        {
            var spawn = entrance.Find(CaveEntranceTeleport.SpawnPointObjectName);
            if (spawn != null)
                return spawn;

            var spawnGo = new GameObject(CaveEntranceTeleport.SpawnPointObjectName);
            CaveEditorUndo.RegisterCreated(spawnGo, "Cave Spawn");
            spawnGo.transform.SetParent(entrance, false);
            spawnGo.tag = CaveTags.Entrance;
            spawnGo.AddComponent<CaveEntranceSpawnPoint>();
            return spawnGo.transform;
        }

        static void EnsureSpawnGroundPad(Transform spawn, Quaternion worldRot)
        {
            CaveSpawnPadUtility.EnsureUnderSpawn(spawn, new Vector3(8.5f, 0.85f, 8.5f));
        }
    }
}
