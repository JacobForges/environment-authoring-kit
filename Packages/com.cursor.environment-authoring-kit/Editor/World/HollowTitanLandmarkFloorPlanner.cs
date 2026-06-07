#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.World
{
    /// <summary>Interior floor rings, spiral stair nodes, and entrance framing for the hollow tree.</summary>
    public static class HollowTitanLandmarkFloorPlanner
    {
        /// <summary>Playtest character height (~1.8m).</summary>
        public const float CharacterHeightMeters = 1.85f;

        /// <summary>Max vertical gap jumpable without grappling hook.</summary>
        public const float MaxJumpVerticalMeters = 2.5f;

        /// <summary>Target per-step rise for comfortable jump traversal.</summary>
        public const float TargetStepRiseMeters = 2.0f;

        /// <summary>Minimum tread depth on spiral steps.</summary>
        public const float TreadDepthMeters = 1.6f;

        /// <summary>Visual tread thickness.</summary>
        public const float TreadHeightMeters = 0.32f;

        public sealed class FloorRing
        {
            public int Index;
            public float LocalY;
            public float Radius;
            public Vector3 WorldCenter;
        }

        public sealed class StairNode
        {
            public int FromFloor;
            public int ToFloor;
            public Vector3 LocalPosition;
            public float YawDegrees;
            public float StepRiseMeters;
        }

        public sealed class BoulderPlatform
        {
            public int FloorIndex;
            public Vector3 LocalPosition;
            public float Scale;
        }

        public sealed class StairMetrics
        {
            public float FloorVerticalGapMeters;
            public int StepsPerFloorTransition;
            public float StepRiseMeters;
            public float StepRunMeters;
            public bool HookRequiredForVerticalGaps;
        }

        public sealed class Plan
        {
            public int FloorCount;
            public float FloorHeightMeters;
            public float TrunkRadius;
            public float TrunkHeight;
            public float EntranceYawDegrees;
            public List<FloorRing> Floors = new();
            public List<StairNode> Stairs = new();
            public List<BoulderPlatform> BoulderPlatforms = new();
            public StairMetrics Metrics = new();
            public Vector3 EntranceLocal;
            public Vector3 EntranceForward;
        }

        public static Plan Build(
            int seed,
            int floorCount,
            float trunkRadius,
            float trunkHeight,
            float floorHeightMeters)
        {
            floorCount = Mathf.Clamp(floorCount, 2, 8);
            floorHeightMeters = Mathf.Max(4f, floorHeightMeters);
            trunkRadius = Mathf.Max(8f, trunkRadius);
            trunkHeight = Mathf.Max(trunkRadius * 2f, trunkHeight);

            var rng = new System.Random(seed + 0x464C4F52); // "FLOR"
            var plan = new Plan
            {
                FloorCount = floorCount,
                FloorHeightMeters = floorHeightMeters,
                TrunkRadius = trunkRadius,
                TrunkHeight = trunkHeight,
                EntranceYawDegrees = (float)rng.NextDouble() * 360f,
            };

            var usableHeight = trunkHeight - floorHeightMeters * 0.6f;
            var floorVerticalGap = usableHeight / floorCount;

            for (var f = 0; f < floorCount; f++)
            {
                var t = floorCount <= 1 ? 0f : f / (float)(floorCount - 1);
                var ringRadius = Mathf.Lerp(trunkRadius * 0.55f, trunkRadius * 0.28f, t);
                plan.Floors.Add(new FloorRing
                {
                    Index = f,
                    LocalY = floorHeightMeters * 0.5f + floorVerticalGap * f,
                    Radius = ringRadius,
                });
            }

            var entranceRad = plan.EntranceYawDegrees * Mathf.Deg2Rad;
            var entranceRadius = trunkRadius * 0.92f;
            plan.EntranceLocal = new Vector3(
                Mathf.Sin(entranceRad) * entranceRadius,
                plan.Floors[0].LocalY * 0.35f,
                Mathf.Cos(entranceRad) * entranceRadius);
            plan.EntranceForward = new Vector3(Mathf.Sin(entranceRad), 0f, Mathf.Cos(entranceRad));

            var stairStepsPerFloor = ComputeStepsPerTransition(floorVerticalGap);
            var stepRise = floorVerticalGap / (stairStepsPerFloor + 1);
            var spiralSweep = 300f;
            var stepRun = EstimateStepRun(plan.Floors[0].Radius * 0.12f, spiralSweep, stairStepsPerFloor);

            plan.Metrics = new StairMetrics
            {
                FloorVerticalGapMeters = floorVerticalGap,
                StepsPerFloorTransition = stairStepsPerFloor,
                StepRiseMeters = stepRise,
                StepRunMeters = stepRun,
                HookRequiredForVerticalGaps = stepRise > MaxJumpVerticalMeters,
            };

            for (var f = 0; f < floorCount - 1; f++)
            {
                var from = plan.Floors[f];
                var to = plan.Floors[f + 1];
                for (var s = 0; s < stairStepsPerFloor; s++)
                {
                    var t = (s + 1) / (float)(stairStepsPerFloor + 1);
                    var angle = plan.EntranceYawDegrees + f * 84f + t * spiralSweep;
                    var rad = angle * Mathf.Deg2Rad;
                    var r = Mathf.Lerp(from.Radius * 0.1f, to.Radius * 0.14f, t);
                    var y = Mathf.Lerp(from.LocalY, to.LocalY, t);
                    plan.Stairs.Add(new StairNode
                    {
                        FromFloor = f,
                        ToFloor = f + 1,
                        LocalPosition = new Vector3(Mathf.Sin(rad) * r, y, Mathf.Cos(rad) * r),
                        YawDegrees = angle + 90f,
                        StepRiseMeters = stepRise,
                    });
                }
            }

            for (var f = 1; f < floorCount; f++)
            {
                var floor = plan.Floors[f];
                var platformAngle = plan.EntranceYawDegrees + 180f + f * 17f;
                var platformRad = platformAngle * Mathf.Deg2Rad;
                var reach = floor.Radius * 0.68f;
                plan.BoulderPlatforms.Add(new BoulderPlatform
                {
                    FloorIndex = f,
                    LocalPosition = new Vector3(
                        Mathf.Sin(platformRad) * reach,
                        floor.LocalY + floorHeightMeters * 0.52f,
                        Mathf.Cos(platformRad) * reach),
                    Scale = Mathf.Lerp(trunkRadius * 0.072f, trunkRadius * 0.117f,
                        f / (float)Mathf.Max(1, floorCount - 1)),
                });
            }

            return plan;
        }

        public static int ComputeStepsPerTransition(float floorVerticalGap)
        {
            var minSteps = Mathf.CeilToInt(floorVerticalGap / TargetStepRiseMeters);
            var steps = Mathf.Max(8, minSteps - 1);
            var rise = floorVerticalGap / (steps + 1);
            while (rise > MaxJumpVerticalMeters)
            {
                steps++;
                rise = floorVerticalGap / (steps + 1);
            }

            return steps;
        }

        static float EstimateStepRun(float radius, float spiralSweepDegrees, int stepCount)
        {
            var anglePerStep = spiralSweepDegrees / (stepCount + 1);
            return radius * anglePerStep * Mathf.Deg2Rad;
        }

        public static void Worldify(Plan plan, Transform root)
        {
            if (plan == null || root == null)
                return;

            foreach (var floor in plan.Floors)
                floor.WorldCenter = root.TransformPoint(new Vector3(0f, floor.LocalY, 0f));
        }
    }
}
#endif
