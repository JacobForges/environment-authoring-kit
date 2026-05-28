using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Single source of truth for one cave build session — prevents scripts from undoing each other's work.
    /// </summary>
    public static class CaveBuildWorkflowCoordinator
    {
        public enum Phase
        {
            Idle = 0,
            Geometry = 1,
            Playability = 2,
            World = 3,
            MeatLoop = 4,
            PostMeat = 5,
            Research = 6,
            GradingOnly = 7,
        }

        static Phase _phase = Phase.Idle;
        static bool _walkFloorsCommitted;
        static bool _navMeshBaked;
        static bool _worldPropsScattered;
        static int _meatPropScatterPasses;
        static int _meatLightingPasses;
        static int _meatAtmospherePasses;
        static int _meatMobPasses;
        static int _meatVisualPolishPasses;
        static int _meatDecalPasses;
        static int _meatAudioPasses;
        static int _meatPerfPasses;
        static int _meatSurfaceTerrainPasses;
        static int _meatSurfaceVegetationPasses;
        static bool _mouthGrounded;
        static bool _groundPlacementLocked;

        public static Phase CurrentPhase => _phase;

        public static void BeginSession()
        {
            _phase = Phase.Geometry;
            _walkFloorsCommitted = false;
            _navMeshBaked = false;
            _worldPropsScattered = false;
            _meatPropScatterPasses = 0;
            _meatLightingPasses = 0;
            _meatAtmospherePasses = 0;
            _meatMobPasses = 0;
            _meatVisualPolishPasses = 0;
            _meatDecalPasses = 0;
            _meatAudioPasses = 0;
            _meatPerfPasses = 0;
            _meatSurfaceTerrainPasses = 0;
            _meatSurfaceVegetationPasses = 0;
            _mouthGrounded = false;
            _groundPlacementLocked = false;
        }

        public static void EndSession() => _phase = Phase.Idle;

        public static void EnterPhase(Phase phase) => _phase = phase;

        public static void MarkWalkFloorsCommitted() => _walkFloorsCommitted = true;

        public static void MarkMouthGrounded() => _mouthGrounded = true;

        public static bool MouthIsGrounded => _mouthGrounded;

        /// <summary>
        /// After world grounding, meat loop must not re-seat the cave root.
        /// Always records world XZ on <see cref="CaveBuildMetadata"/> so grading cannot drift the layout.
        /// </summary>
        public static void LockGroundPlacement(Transform caveRoot = null)
        {
            _groundPlacementLocked = true;
            caveRoot ??= CaveRouteProbeRunner.FindCaveRoot();
            if (caveRoot != null)
                CaveGroundPlacementUtility.EnsureRootWorldXZLock(caveRoot);
        }

        /// <summary>When mouth/root placement is already good, lock automatically (no manual step).</summary>
        public static void TryAutoLockIfPlacementReady(Transform caveRoot, SceneGroundInfo ground)
        {
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return;
            if (!CaveGroundPlacementUtility.IsGroundPlacementAcceptable(caveRoot, ground))
                return;

            MarkMouthGrounded();
            LockGroundPlacement(caveRoot);
        }

        public static bool IsGroundPlacementLocked => _groundPlacementLocked;

        /// <summary>After playability marks floors, do not delete Walkways children during shell purges.</summary>
        public static bool ShouldPreserveWalkways =>
            _walkFloorsCommitted && _phase >= Phase.Playability;

        /// <summary>NavMesh bake at most once per build unless a fix explicitly invalidates it.</summary>
        public static bool TryConsumeNavMeshBake()
        {
            if (_navMeshBaked)
                return false;

            _navMeshBaked = true;
            return true;
        }

        public static void InvalidateNavMesh() => _navMeshBaked = false;

        /// <summary>World scatter stage runs once; meat loop uses separate capped enrichment passes.</summary>
        public static bool TryConsumeWorldPropScatter()
        {
            if (_worldPropsScattered)
                return false;

            _worldPropsScattered = true;
            return true;
        }

        public const int MaxMeatPropScatterPasses = 5;

        public static bool TryConsumeMeatPropScatter()
        {
            if (_meatPropScatterPasses >= MaxMeatPropScatterPasses)
                return false;

            _meatPropScatterPasses++;
            return true;
        }

        public static bool TryConsumeMeatLightingPass()
        {
            if (_meatLightingPasses >= 3)
                return false;
            _meatLightingPasses++;
            return true;
        }

        public static bool TryConsumeMeatAtmospherePass()
        {
            if (_meatAtmospherePasses >= 2)
                return false;
            _meatAtmospherePasses++;
            return true;
        }

        public static bool TryConsumeMeatMobPass()
        {
            if (_meatMobPasses >= 2)
                return false;
            _meatMobPasses++;
            return true;
        }

        public static bool TryConsumeMeatVisualPolishPass()
        {
            if (_meatVisualPolishPasses >= 4)
                return false;
            _meatVisualPolishPasses++;
            return true;
        }

        public static bool TryConsumeMeatDecalPass()
        {
            if (_meatDecalPasses >= 2)
                return false;
            _meatDecalPasses++;
            return true;
        }

        public static bool TryConsumeMeatAudioPass()
        {
            if (_meatAudioPasses >= 1)
                return false;
            _meatAudioPasses++;
            return true;
        }

        public static bool TryConsumeMeatPerfPass()
        {
            if (_meatPerfPasses >= 2)
                return false;
            _meatPerfPasses++;
            return true;
        }

        public const int MaxMeatSurfaceTerrainPasses = 5;
        public const int MaxMeatSurfaceVegetationPasses = 8;

        public static bool TryConsumeMeatSurfaceTerrainPass()
        {
            if (_meatSurfaceTerrainPasses >= MaxMeatSurfaceTerrainPasses)
                return false;
            _meatSurfaceTerrainPasses++;
            return true;
        }

        public static bool TryConsumeMeatSurfaceVegetationPass()
        {
            if (_meatSurfaceVegetationPasses >= MaxMeatSurfaceVegetationPasses)
                return false;
            _meatSurfaceVegetationPasses++;
            return true;
        }

        /// <summary>Full purge + walkways destroy — only before walk floors exist or explicit rebuild.</summary>
        public static bool ShouldRunDestructivePurge =>
            !ShouldPreserveWalkways && _phase <= Phase.Geometry;

        /// <summary>Post-grade purge undoes meat fixes — never during queue or meat/post-meat.</summary>
        public static bool ShouldRunPostGradePurge =>
            _phase == Phase.Idle &&
            !CaveBuildActionPacing.IsInsideQueueInvoke &&
            !CaveBuildActionPacing.IsBusy;

        public static bool IsBuildPipelineActive =>
            _phase != Phase.Idle && _phase != Phase.GradingOnly;

        public static bool ShouldDeferAutoRebuild =>
            IsBuildPipelineActive ||
            CaveBuildActionPacing.IsBusy ||
            LavaTubeCaveBuildPipeline.IsPhasedBuildActive ||
            LavaTubeCaveBuilder.IsBuildInProgress ||
            CaveBuildCursorAgentBridge.IsAgentRunning ||
            EditorApplication.isCompiling ||
            CaveBuildPipelineCompletion.ShouldBlockAutoRebuildAfterAgent();

        /// <summary>Last layout seed from the current session or EditorPrefs (route probes, metadata inference).</summary>
        public static bool TryReadLastSeed(out int seed)
        {
            seed = CaveBuildLayoutRollSession.LastRecordedSeed;
            if (seed > 0)
                return true;

            seed = EditorPrefs.GetInt("CaveBuild_LastSeed", 0);
            return seed > 0;
        }
    }
}
