using EnvironmentAuthoringKit.Editor.Generation;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public static class CaveSystemGenerator
    {
        const float TunnelLength = 10f;
        const float TunnelWidth = 5f;
        const float TunnelHeight = 4f;
        const float WallThickness = 0.35f;

        public static void Generate(Transform environmentRoot, WorldGenerationRequest request, SceneGroundInfo ground)
        {
            var cavesRoot = EnvironmentSceneUtility.GetOrCreateChild(environmentRoot, "Caves");
            var rng = new System.Random(request.Seed);

            var entranceRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Entrance");
            var entrancePos = GetEntrancePosition(ground);
            GenerateEntrance(entranceRoot, entrancePos, rng, ground);

            if (request.CaveMode == CaveGenerationMode.FullSystem)
            {
                var tunnelsRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Tunnels");
                var chambersRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Chambers");
                GenerateTunnelNetwork(tunnelsRoot, chambersRoot, entrancePos, request, rng, ground);
            }

            ScatterCaveDetails(cavesRoot, request, rng, ground);
            EnvironmentSceneUtility.MarkSceneDirty();
        }

        public static void GenerateEntranceOnly(Transform environmentRoot, WorldGenerationRequest request, SceneGroundInfo ground)
        {
            var cavesRoot = EnvironmentSceneUtility.GetOrCreateChild(environmentRoot, "Caves");
            var entranceRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Entrance");
            var entrancePos = GetEntrancePosition(ground);
            GenerateEntrance(entranceRoot, entrancePos, new System.Random(request.Seed), ground);
            ScatterCaveDetails(cavesRoot, request, new System.Random(request.Seed + 7), ground);
        }

        static Vector3 GetEntrancePosition(SceneGroundInfo ground)
        {
            if (ground == null || !ground.HasAnchor)
                return new Vector3(0f, -2f, 0f);

            var center = ground.Bounds.center;
            var edge = center - ground.HorizontalForward * Mathf.Max(8f, ground.Bounds.extents.z * 0.35f);
            var below = EnvironmentKitSettings.PlaceUnderGroundSurface ? ground.Down * 2f : Vector3.zero;
            edge.y = ground.SurfaceY;
            return edge + below;
        }

        static void GenerateEntrance(Transform parent, Vector3 mouthPosition, System.Random rng, SceneGroundInfo ground)
        {
            var forward = ground != null && ground.HasAnchor ? ground.HorizontalForward : Vector3.forward;
            var yaw = Quaternion.LookRotation(forward, Vector3.up);
            var floorY = mouthPosition.y;

            CreateCavePart(parent, "EntranceFloor", BlockoutPrimitiveKind.Plane,
                mouthPosition + new Vector3(2f, floorY, 0f), yaw,
                new Vector3(14f, 0.15f, 10f), BlockoutSettings.CaveFloorColor);

            CreateCavePart(parent, "EntranceRamp", BlockoutPrimitiveKind.Ramp,
                mouthPosition + new Vector3(8f, floorY + 0.5f, 0f), yaw,
                new Vector3(12f, 0.4f, 8f), BlockoutSettings.CaveRockColor);

            CreateCavePart(parent, "ArchLeft", BlockoutPrimitiveKind.Wall,
                mouthPosition + new Vector3(0f, floorY + 2.5f, -3.5f), yaw,
                new Vector3(WallThickness, 5f, 2f), BlockoutSettings.CaveWallColor);

            CreateCavePart(parent, "ArchRight", BlockoutPrimitiveKind.Wall,
                mouthPosition + new Vector3(0f, floorY + 2.5f, 3.5f), yaw,
                new Vector3(WallThickness, 5f, 2f), BlockoutSettings.CaveWallColor);

            CreateCavePart(parent, "ArchLinte", BlockoutPrimitiveKind.Wall,
                mouthPosition + new Vector3(0f, floorY + 5.2f, 0f), yaw,
                new Vector3(WallThickness, 1.2f, 8.5f), BlockoutSettings.CaveWallColor);

            CreateCavePart(parent, "BermLeft", BlockoutPrimitiveKind.Cube,
                mouthPosition + new Vector3(-4f, floorY + 2f, -6f), yaw,
                new Vector3(8f, 4f, 3f), BlockoutSettings.CaveRockColor);

            CreateCavePart(parent, "BermRight", BlockoutPrimitiveKind.Cube,
                mouthPosition + new Vector3(-4f, floorY + 2f, 6f), yaw,
                new Vector3(8f, 4f, 3f), BlockoutSettings.CaveRockColor);

            CreateEntranceLight(mouthPosition + new Vector3(3f, floorY + 3f, 0f));
            CreateEntranceLight(mouthPosition + new Vector3(-6f, floorY + 4f, 0f), intensity: 0.35f, warm: false);

            if (rng.NextDouble() > 0.4)
            {
                CreateCavePart(parent, "MouthRockA", BlockoutPrimitiveKind.Cylinder,
                    mouthPosition + new Vector3(-2f, floorY + 1f, 4.5f), Quaternion.identity,
                    new Vector3(1.2f, 2.5f, 1.2f), BlockoutSettings.CaveRockColor);
            }
        }

        static void GenerateTunnelNetwork(
            Transform tunnelsRoot,
            Transform chambersRoot,
            Vector3 start,
            WorldGenerationRequest request,
            System.Random rng,
            SceneGroundInfo ground)
        {
            var down = ground != null ? ground.Down : Vector3.down;
            var pos = start + down * 1.5f;
            var direction = ground != null && ground.HasAnchor ? -ground.HorizontalForward : Vector3.left;
            var segments = Mathf.Clamp(request.CaveTunnelSegments, 4, 24);
            var chamberEvery = Mathf.Max(3, segments / Mathf.Max(1, request.CaveChamberCount));

            for (var i = 0; i < segments; i++)
            {
                BuildTunnelSegment(tunnelsRoot, pos, direction);

                if (i > 0 && i % chamberEvery == 0)
                    BuildChamber(chambersRoot, pos, rng, i);

                if (rng.NextDouble() < 0.35)
                {
                    var branchDir = Quaternion.Euler(0f, rng.NextDouble() > 0.5 ? 90f : -90f, 0f) * direction;
                    BuildTunnelSegment(tunnelsRoot, pos, branchDir.normalized);
                }

                pos += direction * TunnelLength + down * 0.35f;
                if (i % 2 == 0 && rng.NextDouble() > 0.45)
                    direction = Quaternion.Euler(0f, (float)(rng.NextDouble() * 90 - 45), 0f) * direction;
            }
        }

        static void BuildTunnelSegment(Transform parent, Vector3 center, Vector3 forward)
        {
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            var halfW = TunnelWidth * 0.5f;
            var halfH = TunnelHeight * 0.5f;

            CreateCavePart(parent, "TunnelFloor", BlockoutPrimitiveKind.Plane, center, rot,
                new Vector3(TunnelLength, 0.12f, TunnelWidth), BlockoutSettings.CaveFloorColor);
            CreateCavePart(parent, "TunnelCeiling", BlockoutPrimitiveKind.Plane, center + Vector3.up * TunnelHeight, rot,
                new Vector3(TunnelLength, 0.12f, TunnelWidth), BlockoutSettings.CaveWallColor);
            CreateCavePart(parent, "TunnelWallL", BlockoutPrimitiveKind.Wall,
                center + rot * new Vector3(0f, halfH, -halfW), rot,
                new Vector3(TunnelLength, TunnelHeight, WallThickness), BlockoutSettings.CaveWallColor);
            CreateCavePart(parent, "TunnelWallR", BlockoutPrimitiveKind.Wall,
                center + rot * new Vector3(0f, halfH, halfW), rot,
                new Vector3(TunnelLength, TunnelHeight, WallThickness), BlockoutSettings.CaveWallColor);
        }

        static void BuildChamber(Transform parent, Vector3 center, System.Random rng, int index)
        {
            var size = 8f + (float)rng.NextDouble() * 6f;
            var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            CreateCavePart(parent, $"Chamber_{index}_Floor", BlockoutPrimitiveKind.Plane, center, rot,
                new Vector3(size, 0.15f, size), BlockoutSettings.CaveFloorColor);

            var half = size * 0.5f;
            CreateCavePart(parent, $"Chamber_{index}_N", BlockoutPrimitiveKind.Wall,
                center + rot * new Vector3(0f, 2.5f, half), rot,
                new Vector3(size, 5f, WallThickness), BlockoutSettings.CaveWallColor);
            CreateCavePart(parent, $"Chamber_{index}_S", BlockoutPrimitiveKind.Wall,
                center + rot * new Vector3(0f, 2.5f, -half), rot,
                new Vector3(size, 5f, WallThickness), BlockoutSettings.CaveWallColor);
            CreateCavePart(parent, $"Chamber_{index}_E", BlockoutPrimitiveKind.Wall,
                center + rot * new Vector3(half, 2.5f, 0f), rot * Quaternion.Euler(0f, 90f, 0f),
                new Vector3(size, 5f, WallThickness), BlockoutSettings.CaveWallColor);
            CreateCavePart(parent, $"Chamber_{index}_W", BlockoutPrimitiveKind.Wall,
                center + rot * new Vector3(-half, 2.5f, 0f), rot * Quaternion.Euler(0f, 90f, 0f),
                new Vector3(size, 5f, WallThickness), BlockoutSettings.CaveWallColor);
        }

        static void ScatterCaveDetails(Transform cavesRoot, WorldGenerationRequest request, System.Random rng, SceneGroundInfo ground)
        {
            if (request.Density == ScatterDensityLevel.Empty)
                return;

            var detailsRoot = EnvironmentSceneUtility.GetOrCreateChild(cavesRoot, "Details");
            var count = request.Density switch
            {
                ScatterDensityLevel.Sparse => 6,
                ScatterDensityLevel.Dense => 24,
                _ => 12
            };

            var center = ground != null ? ground.Bounds.center : Vector3.zero;
            var down = ground != null ? ground.Down : Vector3.down;

            for (var i = 0; i < count; i++)
            {
                var pos = center + new Vector3(
                    (float)(rng.NextDouble() * 40 - 20),
                    0f,
                    (float)(rng.NextDouble() * 40 - 20));
                pos = pos + down * (2f + (float)rng.NextDouble() * 8f);
                var scale = new Vector3(0.3f, 1.5f + (float)rng.NextDouble() * 2f, 0.3f);
                CreateCavePart(detailsRoot, "Stalactite", BlockoutPrimitiveKind.Cylinder, pos, Quaternion.identity,
                    scale, BlockoutSettings.StalactiteColor);
            }
        }

        static void CreateEntranceLight(Vector3 position, float intensity = 1.1f, bool warm = true)
        {
            var go = new GameObject(warm ? "EntranceLight" : "CaveFillLight");
            Undo.RegisterCreatedObjectUndo(go, "Cave Light");
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = warm ? 18f : 25f;
            light.intensity = intensity;
            light.color = warm ? new Color(1f, 0.75f, 0.45f) : new Color(0.55f, 0.65f, 0.85f);
            light.shadows = LightShadows.Soft;
        }

        static GameObject CreateCavePart(
            Transform parent,
            string name,
            BlockoutPrimitiveKind kind,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Color color)
        {
            var go = GameObject.CreatePrimitive(kind switch
            {
                BlockoutPrimitiveKind.Cylinder => PrimitiveType.Cylinder,
                BlockoutPrimitiveKind.Plane => PrimitiveType.Cube,
                _ => PrimitiveType.Cube
            });
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Cave " + name);
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                renderer.sharedMaterial = new Material(shader) { color = color };
            }

            return go;
        }
    }
}
