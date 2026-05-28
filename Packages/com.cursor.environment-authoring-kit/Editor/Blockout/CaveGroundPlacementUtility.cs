using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using EnvironmentAuthoringKit.Editor;
using EnvironmentAuthoringKit.Editor.TerrainAuthoring;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Aligns UndergroundCaveSystem world position below SceneGroundInfo surface (not grid Y=0).</summary>
    public static class CaveGroundPlacementUtility
    {
        public const float MaxVerticalErrorMeters = 1.5f;
        public const float MaxHorizontalErrorMeters = 2.5f;
        public const float MaxEntranceMouthSurfaceErrorMeters = 0.75f;
        const float MaxMouthOffsetAboveRootMeters = 14f;
        const float MaxMouthSnapDeltaPerPassMeters = 5.5f;
        const float AlignVerticalToleranceMeters = 0.08f;
        const float AlignHorizontalToleranceMeters = 0.25f;
        const float MouthSurfaceToleranceMeters = 0.12f;
        /// <summary>Reject heightmap spikes above the scene walkable anchor (NVIDIA 3D-GENERALIST 2026 layout grounding).</summary>
        const float MaxWalkableSurfaceDivergenceMeters = 4f;
        /// <summary>
        /// Carved entrance bowl can sit below bare-earth lip (fl-bay-hillshade / unity6-terrain-heightmap) —
        /// mouth snap uses scene walkable anchor, not bowl floor.
        /// </summary>
        const float MaxCarveBowlBelowWalkableMeters = 3.5f;
        const int MaxDepthOnlySnapPasses = 16;

        /// <summary>Marker lift above entrance shaft pivot (LavaTube BuildEntrance layout).</summary>
        public const float DefaultMarkerLiftAboveShaftMeters = 1.6f;

        /// <summary>Mouth world Y − root Y when entrance uses standard shaft depth + marker.</summary>
        public const float DefaultMouthOffsetAboveRootMeters =
            CaveGeometryPaths.UndergroundDepthMeters + DefaultMarkerLiftAboveShaftMeters;

        public static Vector3 ExpectedRootWorldPosition(SceneGroundInfo ground, Transform caveRoot = null) =>
            SplineLavaTubeCaveGenerator.GetEntranceWorldPosition(ground, caveRoot);

        public static float ExpectedRootWorldY(SceneGroundInfo ground, Transform caveRoot = null) =>
            ExpectedRootWorldPosition(ground, caveRoot).y;

        public static Vector3 MeasureRootPlacementError(Transform caveRoot, SceneGroundInfo ground)
        {
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return Vector3.zero;

            return caveRoot.position - ExpectedRootWorldPosition(ground, caveRoot);
        }

        public static float MeasureRootDepthError(Transform caveRoot, SceneGroundInfo ground) =>
            MeasureRootPlacementError(caveRoot, ground).y;

        /// <summary>Positive = mouth above surface; negative = below.</summary>
        public static float MeasureEntranceMouthSurfaceError(Transform caveRoot, SceneGroundInfo ground)
        {
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return 0f;

            var mouth = GetEntranceMouthWorld(caveRoot);
            return mouth.y - SampleWalkableSurfaceWorldY(ground, mouth);
        }

        public static bool IsRootPlacementAcceptable(Transform caveRoot, SceneGroundInfo ground)
        {
            var err = MeasureRootPlacementError(caveRoot, ground);
            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
                return Mathf.Abs(err.y) <= MaxVerticalErrorMeters;
            return Mathf.Abs(err.y) <= MaxVerticalErrorMeters &&
                   new Vector2(err.x, err.z).magnitude <= MaxHorizontalErrorMeters;
        }

        public static bool IsGroundPlacementAcceptable(Transform caveRoot, SceneGroundInfo ground) =>
            IsRootPlacementAcceptable(caveRoot, ground) &&
            Mathf.Abs(MeasureEntranceMouthSurfaceError(caveRoot, ground)) <= MaxEntranceMouthSurfaceErrorMeters;

        /// <summary>Entrance edge XZ on the walkable surface (terrain sample uses this point, not scene bounds center).</summary>
        public static Vector3 ResolveEntranceEdgeXZ(SceneGroundInfo ground, Transform caveRoot = null)
        {
            if (ground == null || !ground.HasAnchor)
                return Vector3.zero;

            if (caveRoot != null)
            {
                var mouth = GetEntranceMouthWorld(caveRoot);
                if (mouth != Vector3.zero)
                {
                    var mouthEdge = new Vector3(mouth.x, 0f, mouth.z);
                    mouthEdge.y = SampleSurfaceWorldY(ground, mouthEdge);
                    return mouthEdge;
                }
            }

            var forward = ground.HorizontalForward;
            if (forward.sqrMagnitude < 0.01f)
                forward = Vector3.forward;
            forward.y = 0f;
            forward.Normalize();

            var bounds = ground.Bounds;
            var depthAlongForward = Mathf.Max(bounds.extents.z, bounds.extents.x * 0.45f);
            var backOffset = Mathf.Max(6f, depthAlongForward * 0.85f);
            var edge = bounds.center - forward * backOffset;
            if (ground.Anchor != null)
            {
                var anchorEdge = ground.Anchor.position - forward * Mathf.Max(4f, backOffset * 0.5f);
                edge = Vector3.Lerp(edge, anchorEdge, 0.35f);
            }

            return edge;
        }

        /// <summary>Uses measured marker offset when present; shaft depth before entrance/marker exist.</summary>
        public static float ResolveMouthOffsetForPlacement(Transform caveRoot)
        {
            if (caveRoot == null)
                return DefaultMouthOffsetAboveRootMeters;

            var mouth = FindEntranceMouthTransform(caveRoot);
            if (mouth != null)
                return ClampMouthOffsetAboveRoot(ResolveMouthOffsetAboveRoot(caveRoot));

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null &&
                Mathf.Abs(entrance.localPosition.y - CaveGeometryPaths.UndergroundDepthMeters) < 0.05f)
                return DefaultMouthOffsetAboveRootMeters;

            return DefaultMouthOffsetAboveRootMeters;
        }

        static float ClampMouthOffsetAboveRoot(float offset) =>
            Mathf.Clamp(offset, 0.5f, MaxMouthOffsetAboveRootMeters);

        /// <summary>
        /// Offset for expected root Y (unity6-terrain-heightmap: 8m shaft + 1.6m marker).
        /// Depth-only snaps can leave a corrupted marker; restore and use canonical lift when measured offset drifts.
        /// </summary>
        public static float ResolveMouthOffsetForExpectedPlacement(Transform caveRoot)
        {
            if (caveRoot == null)
                return DefaultMouthOffsetAboveRootMeters;

            var measured = ResolveMouthOffsetAboveRoot(caveRoot);
            if (Mathf.Abs(measured - DefaultMouthOffsetAboveRootMeters) <= 2f)
                return measured;

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

            return DefaultMouthOffsetAboveRootMeters;
        }

        /// <summary>Terrain SampleHeight at XZ when present; otherwise SceneGroundInfo surface.</summary>
        public static float SampleSurfaceWorldY(SceneGroundInfo ground, Vector3 worldPos) =>
            SampleSurfaceWorldYInternal(ground, worldPos, forMouthWalkableLip: false);

        /// <summary>
        /// Raw heightmap Y at world XZ (all surface tiles). Props/water sit on actual relief — no walkable-anchor clamp.
        /// </summary>
        public static float SampleHeightmapWorldY(SceneGroundInfo ground, Vector3 worldPos)
        {
            if (ground?.Terrain != null && TrySampleTerrainWalkableY(ground.Terrain, worldPos, out var terrainY))
                return terrainY;

            return ground != null ? ground.SurfaceY : float.NaN;
        }

        /// <summary>
        /// Bare-earth walkable lip for mouth snap / packaging (fl-usgs-3dep-elevation-structure).
        /// Ignores carved entrance bowl depressions below the scene anchor surface.
        /// </summary>
        public static float SampleWalkableSurfaceWorldY(SceneGroundInfo ground, Vector3 worldPos) =>
            SampleSurfaceWorldYInternal(ground, worldPos, forMouthWalkableLip: true);

        static float SampleSurfaceWorldYInternal(SceneGroundInfo ground, Vector3 worldPos, bool forMouthWalkableLip)
        {
            if (ground == null)
                return 0f;

            var terrain = ground.Terrain;
            if (terrain == null)
                terrain = Object.FindAnyObjectByType<Terrain>();

            float y;
            if (terrain != null && TrySampleTerrainWalkableY(terrain, worldPos, out var terrainY))
                y = terrainY;
            else
                y = ground.SurfaceY;

            if (ground.HasAnchor)
            {
                var anchorY = ground.SurfaceY;
                if (forMouthWalkableLip && y < anchorY - MaxCarveBowlBelowWalkableMeters)
                    y = anchorY;

                var floor = anchorY - 6f;
                if (y < floor)
                    y = floor;
                // Microsoft EvoTest (2026): iterative repair needs a stable walkable reference, not spurious heightmap peaks.
                if (y > anchorY + MaxWalkableSurfaceDivergenceMeters)
                    y = anchorY;
            }

            return y;
        }

        static bool TrySampleTerrainWalkableY(Terrain mainTerrain, Vector3 worldPos, out float worldY)
        {
            worldY = 0f;
            if (mainTerrain == null)
                return false;

            var tile = mainTerrain;
            if (SurfaceTerrainPlayRegion.TryTerrainAtWorldXZ(mainTerrain, worldPos.x, worldPos.z, out var atTile) &&
                atTile != null)
                tile = atTile;

            if (tile.terrainData == null)
                return false;

            var origin = tile.transform.position;
            var sample = new Vector3(worldPos.x, 0f, worldPos.z);
            worldY = tile.SampleHeight(sample) + origin.y;
            return true;
        }

        /// <summary>
        /// Meat-loop / remediation: move root deeper/shallower only so the entrance mouth meets surface Y.
        /// Preserves world XZ — does not re-seat the cave toward scene bounds (avoids drifting away from the build site).
        /// </summary>
        static void RestoreLockedRootXZ(Transform caveRoot)
        {
            var meta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
            if (meta == null || !meta.preserveRootWorldXZ)
                return;
            var pos = caveRoot.position;
            pos.x = meta.lockedRootWorldPosition.x;
            pos.z = meta.lockedRootWorldPosition.z;
            caveRoot.position = pos;
        }

        /// <summary>Record current world XZ so meat-loop / grading passes cannot re-seat the cave horizontally.</summary>
        public static void EnsureRootWorldXZLock(Transform caveRoot)
        {
            if (caveRoot == null)
                return;
            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            if (meta == null)
                meta = caveRoot.gameObject.AddComponent<CaveBuildMetadata>();
            meta.LockRootWorldXZ(caveRoot);
        }

        public static bool TrySnapMouthToSurfaceDepthOnly(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message) =>
            TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, allowRaise: true, out message);

        /// <param name="allowRaise">When false, only lowers the root so a high mouth meets terrain — never raises after burial.</param>
        public static bool TrySnapMouthToSurfaceDepthOnly(
            Transform caveRoot,
            SceneGroundInfo ground,
            bool allowRaise,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
            {
                message = "Missing cave root or scene ground anchor.";
                return false;
            }

            RestoreLockedRootXZ(caveRoot);
            EnsureEntranceMarkerForPlacement(caveRoot);

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                NormalizeEntranceShaftDepth(entrance);

            var hillshadeNote = ResolveHillshadeCitation(caveRoot);

            var totalDelta = 0f;
            var passes = 0;
            CaveEditorUndo.RecordObject(caveRoot, "Snap Cave Mouth Depth");
            var pos = caveRoot.position;

            for (passes = 0; passes < MaxDepthOnlySnapPasses; passes++)
            {
                var mouthErr = MeasureEntranceMouthSurfaceError(caveRoot, ground);
                if (!allowRaise && mouthErr <= MouthSurfaceToleranceMeters)
                    break;

                if (Mathf.Abs(mouthErr) <= MouthSurfaceToleranceMeters)
                {
                    var rootDepthErr = Mathf.Abs(MeasureRootDepthError(caveRoot, ground));
                    if (!IsRootPlacementAcceptable(caveRoot, ground) &&
                        allowRaise &&
                        TrySyncRootToMouthAnchor(caveRoot, ground, out var syncRoot))
                    {
                        message = AppendHillshadeNote(syncRoot, hillshadeNote);
                        return true;
                    }

                    // fl-bay-hillshade / unity6-terrain-heightmap: walkable lip can match while root still sits in void.
                    if (allowRaise &&
                        rootDepthErr > MaxVerticalErrorMeters &&
                        ForceSyncRootDepthFromWalkableMouth(caveRoot, ground, out var forceSync))
                    {
                        message = AppendHillshadeNote(forceSync, hillshadeNote);
                        return true;
                    }

                    if (passes == 0 && rootDepthErr <= MaxVerticalErrorMeters)
                    {
                        if (allowRaise && TrySyncRootToMouthAnchor(caveRoot, ground, out var syncOnly))
                        {
                            message = AppendHillshadeNote(syncOnly, hillshadeNote);
                            return true;
                        }

                        if (IsGroundPlacementAcceptable(caveRoot, ground))
                        {
                            message =
                                $"Entrance mouth already at surface (Δ{mouthErr:F2}m, root Y={caveRoot.position.y:F2}). {hillshadeNote}";
                            return false;
                        }
                    }

                    if (rootDepthErr <= MaxVerticalErrorMeters)
                        break;
                }

                if (!allowRaise && mouthErr <= 0f)
                    break;

                var perPassCap = Mathf.Abs(mouthErr) > MaxMouthSnapDeltaPerPassMeters * 3f
                    ? Mathf.Min(24f, Mathf.Abs(mouthErr))
                    : MaxMouthSnapDeltaPerPassMeters;
                var delta = Mathf.Clamp(mouthErr, -perPassCap, perPassCap);
                if (!allowRaise && delta < 0f)
                    break;

                pos.y -= delta;
                caveRoot.position = pos;
                totalDelta += delta;
            }

            var residual = MeasureEntranceMouthSurfaceError(caveRoot, ground);
            var rootSynced = false;
            // USGS DS 926 / PP 1807 — mouth on walkable surface defines root depth; do not clamp back to stale edge Y.
            if (allowRaise &&
                (Mathf.Abs(residual) <= MouthSurfaceToleranceMeters ||
                 Mathf.Abs(MeasureRootDepthError(caveRoot, ground)) > MaxVerticalErrorMeters))
            {
                if (TrySyncRootToMouthAnchor(caveRoot, ground, out var syncMsg) && !string.IsNullOrEmpty(syncMsg))
                {
                    rootSynced = true;
                    message = string.IsNullOrEmpty(message) ? syncMsg : message + " " + syncMsg;
                }
            }
            else if (allowRaise && Mathf.Abs(residual) > MaxEntranceMouthSurfaceErrorMeters)
            {
                if (TrySyncRootToMouthAnchor(caveRoot, ground, out var recoverSync) && !string.IsNullOrEmpty(recoverSync))
                {
                    rootSynced = true;
                    message = string.IsNullOrEmpty(message) ? recoverSync : message + " " + recoverSync;
                }
                else
                    ClampRootToExpectedDepth(caveRoot, ground);
            }
            else if (allowRaise &&
                     TrySyncRootToMouthAnchor(caveRoot, ground, out var nearSync) &&
                     !string.IsNullOrEmpty(nearSync))
            {
                rootSynced = true;
                message = string.IsNullOrEmpty(message) ? nearSync : message + " " + nearSync;
            }

            residual = MeasureEntranceMouthSurfaceError(caveRoot, ground);
            if (allowRaise &&
                Mathf.Abs(MeasureRootDepthError(caveRoot, ground)) > MaxVerticalErrorMeters &&
                ForceSyncRootDepthFromWalkableMouth(caveRoot, ground, out var finalSync))
            {
                rootSynced = true;
                message = string.IsNullOrEmpty(message) ? finalSync : message + " " + finalSync;
            }

            residual = MeasureEntranceMouthSurfaceError(caveRoot, ground);
            message = AppendHillshadeNote(
                $"Depth-only mouth snap ({passes} pass(es), Δ{totalDelta:F2}m, residual {residual:F2}m) — XZ locked at ({pos.x:F1}, {pos.z:F1}).",
                hillshadeNote);
            return Mathf.Abs(totalDelta) > 0.01f || rootSynced;
        }

        /// <summary>
        /// Re-seat root Y from bare-earth walkable lip − canonical mouth offset (fl-usgs-3dep-elevation-structure).
        /// </summary>
        static bool ForceSyncRootDepthFromWalkableMouth(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return false;

            RestoreLockedRootXZ(caveRoot);
            EnsureEntranceMarkerForPlacement(caveRoot);

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                NormalizeEntranceShaftDepth(entrance);

            var mouthOffset = ResolveMouthOffsetForExpectedPlacement(caveRoot);
            var mouth = GetEntranceMouthWorld(caveRoot);
            var surfaceY = SampleWalkableSurfaceWorldY(ground, mouth);
            var target = new Vector3(mouth.x, surfaceY - mouthOffset, mouth.z);
            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
            {
                target.x = caveRoot.position.x;
                target.z = caveRoot.position.z;
            }

            if (Mathf.Abs(caveRoot.position.y - target.y) < AlignVerticalToleranceMeters &&
                IsGroundPlacementAcceptable(caveRoot, ground))
                return false;

            CaveEditorUndo.RecordObject(caveRoot, "Force Cave Root Depth From Mouth");
            caveRoot.position = target;
            message =
                $"Forced root depth to ({target.x:F1}, {target.y:F2}, {target.z:F1}) from walkable lip − {mouthOffset:F1}m offset.";
            return true;
        }

        static void EnsureEntranceMarkerForPlacement(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var entrance = caveRoot.Find("Entrance");
            if (entrance == null)
            {
                EnsureEntranceLocalDepth(caveRoot);
                entrance = caveRoot.Find("Entrance");
            }

            if (entrance != null)
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);
        }

        static string ResolveHillshadeCitation(Transform caveRoot)
        {
            var seed = 0;
            var meta = caveRoot != null ? caveRoot.GetComponent<CaveBuildMetadata>() : null;
            if (meta != null)
                seed = meta.seed;

            if (SurfaceDemGeoreferenceAuthor.TryLoadGeorefForSeed(seed, out var georef, out var manifestRel))
            {
                return
                    $"Hillshade: Assets/EnvironmentKit/ResearchCache/images/fl-{georef.CountyId}-hillshade/hillshade.png ({manifestRel}).";
            }

            return
                "Hillshade: Assets/EnvironmentKit/ResearchCache/images/fl-bay-hillshade/hillshade.png (fl-panhandle-lidar-dem-2018 / fl-usgs-3dep-elevation-structure).";
        }

        static string AppendHillshadeNote(string message, string hillshadeNote) =>
            string.IsNullOrEmpty(message) ? hillshadeNote : message + " " + hillshadeNote;

        /// <summary>
        /// Locked-placement repair for meat loop / Cursor ladder (EvoTest 2026 episode repair): marker restore,
        /// iterative depth snap, then root re-seat — never full XZ re-align.
        /// </summary>
        public static bool TryRepairLockedGroundPlacement(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
            {
                message = "Missing cave root or scene ground anchor.";
                return false;
            }

            var changed = false;
            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
            {
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);
                changed = true;
            }

            if (TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, out var snapMsg))
            {
                changed = true;
                message = string.IsNullOrEmpty(message) ? snapMsg : message + " " + snapMsg;
            }
            else if (RecoverRootFromExcessiveDepth(caveRoot, ground, out var recoverMsg))
            {
                changed = true;
                message = recoverMsg;
            }

            if (!IsRootPlacementAcceptable(caveRoot, ground) &&
                TrySyncRootToMouthAnchor(caveRoot, ground, out var syncMsg))
            {
                changed = true;
                message = string.IsNullOrEmpty(message) ? syncMsg : message + " " + syncMsg;
            }

            if (!changed)
                message = "Locked ground placement already acceptable.";

            if (IsGroundPlacementAcceptable(caveRoot, ground))
                EnsureRootWorldXZLock(caveRoot);

            if (changed && ground.Terrain != null)
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                CaveTerrainUtility.ApplyCaveEntranceMouth(
                    ground.Terrain, meta != null ? meta.seed : 0, caveRoot);
            }

            return changed;
        }

        /// <summary>Recover cave root when mouth/marker corruption pushed it far below the anchor surface.</summary>
        public static bool RecoverRootFromExcessiveDepth(Transform caveRoot, SceneGroundInfo ground, out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return false;

            var depthErr = MeasureRootDepthError(caveRoot, ground);
            if (Mathf.Abs(depthErr) <= MaxVerticalErrorMeters * 2f)
                return false;

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
                return TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, out message);

            return TryAlignUndergroundRoot(caveRoot, ground, out message);
        }

        static void ClampRootToExpectedDepth(Transform caveRoot, SceneGroundInfo ground)
        {
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return;

            // FGS OFMS 104 LiDAR mouth anchor — never undo a successful depth-only surface snap.
            if (Mathf.Abs(MeasureEntranceMouthSurfaceError(caveRoot, ground)) <= MouthSurfaceToleranceMeters)
                return;

            if (TrySyncRootToMouthAnchor(caveRoot, ground, out _))
                return;

            var depthErr = MeasureRootDepthError(caveRoot, ground);
            if (Mathf.Abs(depthErr) <= MaxVerticalErrorMeters)
                return;

            RestoreLockedRootXZ(caveRoot);
            var pos = caveRoot.position;
            pos.y = ExpectedRootWorldY(ground, caveRoot);
            caveRoot.position = pos;
        }

        public static bool TryAlignUndergroundRoot(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
            {
                message = "Missing cave root or scene ground anchor.";
                return false;
            }

            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
            {
                message = "Skipped XZ align — cave root locked to surface opening.";
                return false;
            }

            var target = ExpectedRootWorldPosition(ground, caveRoot);
            var err = caveRoot.position - target;
            var horiz = new Vector2(err.x, err.z).magnitude;
            if (Mathf.Abs(err.y) < AlignVerticalToleranceMeters &&
                horiz < AlignHorizontalToleranceMeters)
            {
                message = $"Cave root already at underground offset (Y={caveRoot.position.y:F2}).";
                return false;
            }

            CaveEditorUndo.RecordObject(caveRoot, "Align Cave Underground");
            caveRoot.position = target;
            var forward = ground.HorizontalForward;
            if (forward.sqrMagnitude > 0.01f)
                caveRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var mouthOffset = ResolveMouthOffsetAboveRoot(caveRoot);
            message =
                $"Aligned {caveRoot.name} to ({target.x:F1}, {target.y:F2}, {target.z:F1}) " +
                $"(surface − {mouthOffset:F1}m mouth offset).";
            return true;
        }

        /// <summary>Aligns cave root below surface and snaps entrance mouth to terrain/grid surface Y.</summary>
        public static bool EnsureGroundPlacement(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
            {
                message = "Missing cave root or scene ground anchor.";
                return false;
            }

            return FinalizeGroundPlacement(caveRoot, ground, out message);
        }

        /// <summary>
        /// Idempotent mouth→surface snap then root re-seat (FDG HDPCG controlled grounding; IEEE CoG spatial anchor).
        /// Call after spawn alignment, terrain carve, and compact-shell passes.
        /// </summary>
        public static bool FinalizeGroundPlacement(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message,
            int seed = 0)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
            {
                message = "Missing cave root or scene ground anchor.";
                return false;
            }

            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot) ||
                CaveBuildWorkflowCoordinator.IsGroundPlacementLocked)
            {
                return TryRepairLockedGroundPlacement(caveRoot, ground, out message);
            }

            var changed = false;
            var effectiveSeed = seed;
            if (effectiveSeed == 0)
            {
                var meta = caveRoot.GetComponent<CaveBuildMetadata>();
                if (meta != null)
                    effectiveSeed = meta.seed;
            }

            if (CaveTerrainIntegrationUtility.EnsureForGroundPlacement(
                    ground, caveRoot, effectiveSeed, out var terrainMsg))
            {
                changed = true;
                message = terrainMsg;
            }
            else if (!string.IsNullOrEmpty(terrainMsg))
                message = terrainMsg;

            // fl-usgs-3dep-elevation-structure / Bay hillshade: mouth on LiDAR surface but root drifted after depth-only snap.
            var mouthSurfaceErr = MeasureEntranceMouthSurfaceError(caveRoot, ground);
            if (Mathf.Abs(mouthSurfaceErr) <= MouthSurfaceToleranceMeters &&
                !IsRootPlacementAcceptable(caveRoot, ground))
            {
                var entranceEarly = caveRoot.Find("Entrance");
                if (entranceEarly != null)
                    SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entranceEarly);
            }

            // NVIDIA 3D-GENERALIST (2026): layout anchor must match terrain-sampled mouth, not SceneGroundInfo alone.
            if (Mathf.Abs(mouthSurfaceErr) <= MaxEntranceMouthSurfaceErrorMeters &&
                !IsRootPlacementAcceptable(caveRoot, ground) &&
                TrySyncRootToMouthAnchor(caveRoot, ground, out var earlySyncMsg))
            {
                changed = true;
                message = string.IsNullOrEmpty(message) ? earlySyncMsg : message + " " + earlySyncMsg;
            }

            var entrance = caveRoot.Find("Entrance");
            if (entrance == null)
            {
                EnsureEntranceLocalDepth(caveRoot);
                entrance = caveRoot.Find("Entrance");
            }

            if (entrance != null)
            {
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);
                if (NormalizeEntranceShaftDepth(entrance))
                    changed = true;

                if (SnapEntranceMouthToSurface(caveRoot, ground, entrance, out var snapMsg))
                {
                    changed = true;
                    message = string.IsNullOrEmpty(message) ? snapMsg : message + " " + snapMsg;
                }
            }

            if (TryAlignUndergroundRoot(caveRoot, ground, out var alignMsg))
            {
                changed = true;
                message = string.IsNullOrEmpty(message) ? alignMsg : message + " " + alignMsg;
            }

            if (entrance != null && SnapEntranceMouthToSurface(caveRoot, ground, entrance, out var resnapMsg))
            {
                changed = true;
                message = string.IsNullOrEmpty(message) ? resnapMsg : message + " " + resnapMsg;
            }

            if (SyncRootToMouthAnchor(caveRoot, ground, out var rootMsg))
            {
                changed = true;
                message = string.IsNullOrEmpty(message) ? rootMsg : message + " " + rootMsg;
            }

            for (var i = 0; i < 2 &&
                 !IsGroundPlacementAcceptable(caveRoot, ground); i++)
            {
                entrance ??= caveRoot.Find("Entrance");
                if (SnapEntranceMouthToSurface(caveRoot, ground, entrance, out var snapLoop))
                {
                    changed = true;
                    message = string.IsNullOrEmpty(message) ? snapLoop : message + " " + snapLoop;
                }

                if (SyncRootToMouthAnchor(caveRoot, ground, out var syncLoop))
                {
                    changed = true;
                    message = string.IsNullOrEmpty(message) ? syncLoop : message + " " + syncLoop;
                }
            }

            if (!changed)
                message =
                    $"Ground placement OK (root Y={caveRoot.position.y:F2}, mouth Δ{MeasureEntranceMouthSurfaceError(caveRoot, ground):F2}m).";

            if (IsGroundPlacementAcceptable(caveRoot, ground))
                EnsureRootWorldXZLock(caveRoot);

            return changed;
        }

        /// <summary>
        /// FDG 2026 HDPCG controlled grounding: translate cave root so the mouth meets terrain surface while
        /// keeping Entrance local Y at <see cref="CaveGeometryPaths.UndergroundDepthMeters"/> (IEEE CoG spatial anchor).
        /// </summary>
        static bool SnapEntranceMouthToSurface(
            Transform caveRoot,
            SceneGroundInfo ground,
            Transform entrance,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null)
                return false;

            if (entrance != null)
                NormalizeEntranceShaftDepth(entrance);

            var mouthErr = MeasureEntranceMouthSurfaceError(caveRoot, ground);
            if (Mathf.Abs(mouthErr) <= MouthSurfaceToleranceMeters)
                return false;

            CaveEditorUndo.RecordObject(caveRoot, "Snap Cave Entrance Mouth");
            var pos = caveRoot.position;
            pos.y -= mouthErr;
            caveRoot.position = pos;
            message =
                $"Snapped cave root for surface mouth (Δ{mouthErr:F2}m; shaft stays {CaveGeometryPaths.UndergroundDepthMeters}m).";
            return true;
        }

        static void EnsureEntranceLocalDepth(Transform caveRoot)
        {
            if (caveRoot == null)
                return;

            var entrance = caveRoot.Find("Entrance");
            if (entrance == null)
                return;

            NormalizeEntranceShaftDepth(entrance);
        }

        static bool NormalizeEntranceShaftDepth(Transform entrance)
        {
            if (entrance == null)
                return false;

            var local = entrance.localPosition;
            if (Mathf.Abs(local.y - CaveGeometryPaths.UndergroundDepthMeters) < 0.05f)
                return false;

            CaveEditorUndo.RecordObject(entrance, "Set Cave Entrance Depth");
            local.y = CaveGeometryPaths.UndergroundDepthMeters;
            entrance.localPosition = local;
            return true;
        }

        /// <summary>After mouth snap, re-seat root so mouth offset matches surface − root (IEEE CoG spatial PCG anchor).</summary>
        public static bool TrySyncRootToMouthAnchor(Transform caveRoot, SceneGroundInfo ground, out string message) =>
            SyncRootToMouthAnchor(caveRoot, ground, out message);

        static bool SyncRootToMouthAnchor(Transform caveRoot, SceneGroundInfo ground, out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return false;

            RestoreLockedRootXZ(caveRoot);
            EnsureEntranceMarkerForPlacement(caveRoot);

            var mouthOffset = ResolveMouthOffsetForExpectedPlacement(caveRoot);
            var mouth = GetEntranceMouthWorld(caveRoot);
            var surfaceY = SampleWalkableSurfaceWorldY(ground, mouth);
            var target = new Vector3(mouth.x, surfaceY - mouthOffset, mouth.z);
            if (CaveBuildMetadata.ShouldPreserveRootXZ(caveRoot))
            {
                target.x = caveRoot.position.x;
                target.z = caveRoot.position.z;
            }

            var err = caveRoot.position - target;
            var horiz = new Vector2(err.x, err.z).magnitude;
            if (Mathf.Abs(err.y) < AlignVerticalToleranceMeters &&
                horiz < AlignHorizontalToleranceMeters)
                return false;

            CaveEditorUndo.RecordObject(caveRoot, "Sync Cave Root To Mouth");
            caveRoot.position = target;
            message =
                $"Re-seated cave root to ({target.x:F1}, {target.y:F2}, {target.z:F1}) after mouth anchor (offset {mouthOffset:F1}m).";
            return true;
        }

        /// <summary>World Y offset from cave root to the walk-in mouth (marker when present).</summary>
        public static float ResolveMouthOffsetAboveRoot(Transform caveRoot)
        {
            if (caveRoot == null)
                return DefaultMouthOffsetAboveRootMeters;

            var mouth = GetEntranceMouthWorld(caveRoot);
            return ClampMouthOffsetAboveRoot(mouth.y - caveRoot.position.y);
        }

        public static Transform FindEntranceMouthTransform(Transform caveRoot)
        {
            if (caveRoot == null)
                return null;

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
            {
                var marker = entrance.Find(CaveEntranceTeleport.EntranceMarkerObjectName);
                if (marker != null)
                    return marker;
            }

            var rootMarker = caveRoot.Find(CaveEntranceTeleport.EntranceMarkerObjectName);
            if (rootMarker != null)
                return rootMarker;

            return entrance;
        }

        /// <summary>World position of the walk-in mouth (CaveEntrance_Marker, not shaft pivot).</summary>
        public static Vector3 GetEntranceMouthWorld(Transform caveRoot)
        {
            if (caveRoot == null)
                return Vector3.zero;

            var mouth = FindEntranceMouthTransform(caveRoot);
            if (mouth != null)
                return mouth.position;

            return caveRoot.position + Vector3.up * CaveGeometryPaths.UndergroundDepthMeters;
        }

        /// <summary>
        /// Iteratively lowers the cave root until no enabled mesh protrudes above the heightmap (all surface tiles).
        /// Mouth snap is depth-only (never raises root after burial).
        /// </summary>
        public static bool EnsureFullyBuriedUnderSurface(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return false;

            var terrain = ground.Terrain ?? Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                message = "No terrain for burial envelope.";
                return false;
            }

            var openings = CollectBurialEntryExemptions(caveRoot, ground);
            const float buryMarginMeters = 0.75f;
            const int maxPasses = 14;
            var totalLowered = 0f;
            var passes = 0;

            for (passes = 0; passes < maxPasses; passes++)
            {
                var protrusion = MeasureMaxCaveProtrusionAboveHeightmap(
                    caveRoot, ground, openings, out _);
                if (protrusion <= buryMarginMeters)
                    break;

                RestoreLockedRootXZ(caveRoot);
                var delta = protrusion - buryMarginMeters + 0.12f;
                CaveEditorUndo.RecordObject(caveRoot, "Bury cave under heightmap");
                var pos = caveRoot.position;
                pos.y -= delta;
                caveRoot.position = pos;
                totalLowered += delta;
                TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, allowRaise: false, out _);
            }

            var residual = MeasureMaxCaveProtrusionAboveHeightmap(caveRoot, ground, openings, out _);
            SurfaceCaveRoofAuditor.AuditAndStrip(caveRoot, ground, out var stripMsg);

            if (totalLowered < 0.05f && residual <= buryMarginMeters + 0.15f)
            {
                message = residual <= buryMarginMeters
                    ? "Cave fully buried under heightmap."
                    : $"Cave near burial target (residual protrusion {residual:F2}m). {stripMsg}";
                return false;
            }

            message =
                $"Buried cave {totalLowered:F1}m over {passes} pass(es) (residual protrusion {residual:F2}m). {stripMsg}";
            Debug.Log("[CaveBuild] " + message);
            return totalLowered > 0.05f;
        }

        /// <summary>
        /// FullWorld: after LiDAR terrain finishes, bury the entrance ceiling under the lowest terrain lip
        /// at the mouth footprint, then re-snap the walk-in mouth to the surface.
        /// </summary>
        public static bool ReseatCaveUnderTerrainAfterSurface(
            Transform caveRoot,
            SceneGroundInfo ground,
            out string message)
        {
            message = string.Empty;
            if (caveRoot == null || ground == null || !ground.HasAnchor)
                return false;

            var terrain = ground.Terrain ?? Object.FindAnyObjectByType<Terrain>();
            if (terrain == null)
            {
                message = "No terrain to sample for underground re-seat.";
                return false;
            }

            var mouth = GetEntranceMouthWorld(caveRoot);
            if (mouth.sqrMagnitude < 0.01f)
            {
                message = "Missing entrance mouth marker for underground re-seat.";
                return false;
            }

            var minTerrainY = SampleMinTerrainYAroundPoint(terrain, ground, mouth, radiusMeters: 24f);
            var maxCeilingY = EstimateEntranceCeilingWorldY(caveRoot, mouth);
            const float buryMarginMeters = 0.65f;
            var overflow = maxCeilingY - (minTerrainY - buryMarginMeters);
            var changed = false;

            if (overflow > 0.08f)
            {
                RestoreLockedRootXZ(caveRoot);
                var entrance = caveRoot.Find("Entrance");
                if (entrance != null)
                    SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

                CaveEditorUndo.RecordObject(caveRoot, "Re-seat cave under terrain");
                var pos = caveRoot.position;
                pos.y -= overflow;
                caveRoot.position = pos;
                changed = true;
            }

            if (EnsureFullyBuriedUnderSurface(caveRoot, ground, out var fullBurialMsg))
                changed = true;

            TrySnapMouthToSurfaceDepthOnly(caveRoot, ground, allowRaise: false, out var snapMsg);

            var meta = caveRoot.GetComponent<CaveBuildMetadata>();
            var seed = meta != null ? meta.seed : 0;
            CaveTerrainUtility.ApplyCaveEntranceMouth(terrain, seed, caveRoot);

            var authoring = caveRoot.GetComponent<CaveSplinePathAuthoring>();
            if (authoring?.Knots != null && authoring.Knots.Count >= 2)
            {
                var spline = new CaveSplinePath();
                spline.SetKnots(authoring.Knots);
                CaveTerrainCarveUtility.CarveForCaveSystem(caveRoot, spline, null);
            }

            EnsureRootWorldXZLock(caveRoot);
            if (!changed)
            {
                message =
                    $"Cave already enclosed (ceiling peak {maxCeilingY:F1}m ≤ terrain lip {minTerrainY:F1}m). {fullBurialMsg}";
                return false;
            }

            message =
                $"Buried cave {overflow:F1}m deeper (ceiling {maxCeilingY:F1}m → under terrain lip {minTerrainY:F1}m). {fullBurialMsg} {snapMsg}";
            Debug.Log("[CaveBuild] " + message);
            return true;
        }

        static List<Vector3> CollectBurialEntryExemptions(Transform caveRoot, SceneGroundInfo ground)
        {
            var openings = new List<Vector3>();
            foreach (var marker in SurfaceWorldGenerator.FindCaveOpenings())
            {
                if (marker != null)
                    openings.Add(marker.transform.position);
            }

            var mouth = GetEntranceMouthWorld(caveRoot, ground);
            if (mouth.sqrMagnitude > 0.01f)
                openings.Add(mouth);

            return openings;
        }

        /// <summary>Max world Y of enabled cave mesh above heightmap at its footprint (positive = protruding).</summary>
        public static float MeasureMaxCaveProtrusionAboveHeightmap(
            Transform caveRoot,
            SceneGroundInfo ground,
            IReadOnlyList<Vector3> entryExemptions,
            out Vector3 worstPoint)
        {
            worstPoint = Vector3.zero;
            var maxProtrusion = 0f;
            if (caveRoot == null || ground == null)
                return 0f;

            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled)
                    continue;
                if (IsBurialExemptRenderer(mr))
                    continue;

                var bounds = mr.bounds;
                var center = bounds.center;
                if (IsNearBurialEntry(center, entryExemptions))
                    continue;

                var surfaceY = SampleHeightmapAtBoundsFootprint(ground, bounds);
                var protrusion = bounds.max.y - surfaceY;
                if (protrusion > maxProtrusion)
                {
                    maxProtrusion = protrusion;
                    worstPoint = center;
                }
            }

            return maxProtrusion;
        }

        static float SampleHeightmapAtBoundsFootprint(SceneGroundInfo ground, Bounds bounds)
        {
            var maxSurfaceY = float.NegativeInfinity;
            var samples = new[]
            {
                new Vector3(bounds.min.x, 0f, bounds.min.z),
                new Vector3(bounds.max.x, 0f, bounds.min.z),
                new Vector3(bounds.min.x, 0f, bounds.max.z),
                new Vector3(bounds.max.x, 0f, bounds.max.z),
                new Vector3(bounds.center.x, 0f, bounds.center.z),
            };

            foreach (var xz in samples)
            {
                var y = SampleHeightmapWorldY(ground, xz);
                if (!float.IsNaN(y))
                    maxSurfaceY = Mathf.Max(maxSurfaceY, y);
            }

            return maxSurfaceY > float.NegativeInfinity ? maxSurfaceY : bounds.min.y;
        }

        static bool IsBurialExemptRenderer(Renderer r)
        {
            if (r == null)
                return true;
            var n = r.gameObject.name;
            if (string.IsNullOrEmpty(n))
                return false;
            return n.Contains("SurfaceWalkIn") ||
                   n.Contains("MouthPad") ||
                   n.Contains("EntranceMarker") ||
                   n.Contains("CaveEntrance_Marker") ||
                   n.Contains("Shrine") ||
                   n.Contains("Opening");
        }

        static bool IsNearBurialEntry(Vector3 world, IReadOnlyList<Vector3> openings)
        {
            if (openings == null || openings.Count == 0)
                return false;

            const float radius = SurfaceCaveRoofAuditor.EntryExemptRadiusMeters;
            foreach (var o in openings)
            {
                if (Vector3.Distance(new Vector3(world.x, 0f, world.z), new Vector3(o.x, 0f, o.z)) <= radius)
                    return true;
            }

            return false;
        }

        static float SampleMinTerrainYAroundPoint(
            Terrain terrain,
            SceneGroundInfo ground,
            Vector3 center,
            float radiusMeters)
        {
            if (terrain == null)
                return center.y;

            var minY = float.PositiveInfinity;
            const int samples = 20;
            for (var i = 0; i < samples; i++)
            {
                var angle = i * Mathf.PI * 2f / samples;
                var ring = 0.25f + 0.75f * (i / (float)samples);
                var sample = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radiusMeters * ring);
                var y = ground != null
                    ? SampleHeightmapWorldY(ground, sample)
                    : terrain.SampleHeight(sample) + terrain.transform.position.y;
                if (!float.IsNaN(y))
                    minY = Mathf.Min(minY, y);
            }

            if (minY < float.PositiveInfinity)
                return minY;

            return terrain.SampleHeight(center) + terrain.transform.position.y;
        }

        static float EstimateEntranceCeilingWorldY(Transform caveRoot, Vector3 mouthWorld)
        {
            var maxY = mouthWorld.y + 4f;

            var geometry = caveRoot.Find(CaveGeometryPaths.GeometryRoot);
            var ceilingRoot = geometry != null ? geometry.Find(CaveGeometryPaths.RouteTerrainCeiling) : null;
            if (ceilingRoot != null)
            {
                foreach (var mr in ceilingRoot.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != null && mr.enabled)
                        maxY = Mathf.Max(maxY, mr.bounds.max.y);
                }
            }

            var mainTube = caveRoot.Find("SplineMesh/MainCaveTube");
            if (mainTube != null)
            {
                var mr = mainTube.GetComponent<MeshRenderer>();
                if (mr != null && mr.enabled)
                    maxY = Mathf.Max(maxY, mr.bounds.max.y);
            }

            var outerShell = caveRoot.Find("SplineMesh/MainCaveOuterShell");
            if (outerShell != null)
            {
                var mr = outerShell.GetComponent<MeshRenderer>();
                if (mr != null && mr.enabled)
                    maxY = Mathf.Max(maxY, mr.bounds.max.y);
            }

            // Global safety: include any enabled mesh renderer under cave root (block tunnel/shell variants).
            // This prevents high stray sections from remaining above surface after terrain-first placement.
            foreach (var mr in caveRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null || !mr.enabled)
                    continue;
                maxY = Mathf.Max(maxY, mr.bounds.max.y);
            }

            var clearanceEstimate = caveRoot.position.y + ResolveMouthOffsetForPlacement(caveRoot) + 22f;
            maxY = Mathf.Max(maxY, clearanceEstimate);

            return maxY;
        }

        /// <summary>Mouth world position with scene-ground fallback when markers are missing (compile_gate / surface entrance).</summary>
        public static Vector3 GetEntranceMouthWorld(Transform caveRoot, SceneGroundInfo ground)
        {
            var mouth = GetEntranceMouthWorld(caveRoot);
            if (mouth.sqrMagnitude > 0.01f)
                return mouth;
            if (ground != null && ground.HasAnchor)
                return ground.AnchorWorld;
            return mouth;
        }
   
        [MenuItem("Window/Environment Kit/Cave Build/Repair Only/Reset Cave To Ground Anchor")]
        public static void MenuResetCaveGroundPlacement()
        {
            var caveRoot = CaveGeometryPaths.FindCaveSystemRoot();
            if (caveRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "Cave Ground",
                    "UndergroundCaveSystem not found. Use Build Complete Cave Level (Active Scene) — not required after a full build.",
                    "OK");
                return;
            }

            var ground = SceneGroundResolver.Resolve();
            if (!ground.HasAnchor)
            {
                EditorUtility.DisplayDialog("Cave Ground", "No scene ground anchor (Grid / terrain).", "OK");
                return;
            }

            var entrance = caveRoot.Find("Entrance");
            if (entrance != null)
                SplineCaveSpawnAligner.RestoreEntranceMarkerAtShaftMouth(entrance);

            RecoverRootFromExcessiveDepth(caveRoot, ground, out _);
            FinalizeGroundPlacement(caveRoot, ground, out var msg);
            EditorUtility.SetDirty(caveRoot.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Cave Ground", msg, "OK");
        }
    }
}
