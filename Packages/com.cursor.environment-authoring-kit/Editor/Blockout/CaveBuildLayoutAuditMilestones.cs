#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Milestone layout audits + targeted fixes (re-stitch edges, play perimeter, prop snap).
    /// </summary>
    public static class CaveBuildLayoutAuditMilestones
    {
        public const string MilestonePlayDisk = "layout_audit_play_disk";
        public const string MilestoneFoothillRing = "layout_audit_foothill_ring";
        public const string MilestoneWilderness = "layout_audit_wilderness";
        public const string MilestonePropsPrefix = "layout_audit_props_";

        public const string FixRestitchEdge = "restitch_edge";
        public const string FixRestitchPlayPerimeter = "restitch_play_perimeter";
        public const string FixRestitchWildernessTiles = "restitch_wilderness_tiles";
        public const string FixLockFoothillInner = "lock_foothill_inner";
        public const string FixEnforceWildernessGrid = "enforce_wilderness_grid";
        public const string FixPropSnapCategory = "prop_snap_category";
        public const string FixNone = "none";

        public static CaveBuildWorldLayoutAudit.AuditReport LastReport =>
            CaveBuildWorldLayoutAudit.LastReport;

        public static bool LayoutPlanForce =>
            Environment.GetEnvironmentVariable("CAVE_LAYOUT_PLAN_FORCE") == "1";

        public static void QueueAfterPlayDisk(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (mainTerrain == null || ground == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    CaveBuildTerrainFingerprint.RecordPlayDiskFingerprint(request?.Seed ?? 0, mainTerrain);
                    RunMilestoneWithOptionalFix(
                        ground,
                        request,
                        CaveBuildWorldLayoutAudit.MilestonePlayDisk,
                        mainTerrain,
                        () =>
                        {
                            if (CaveBuildWorldLayoutAudit.LastReport?.blocksSurfaceContinue == true &&
                                !LayoutPlanForce)
                            {
                                CaveBuildEditorLog.LogSurfaceWarning(
                                    "[Surface] Play-disk layout audit still failing after targeted fix — " +
                                    "wilderness ring may show seams. Set CAVE_LAYOUT_PLAN_FORCE=1 to override.");
                            }

                            onComplete?.Invoke();
                        });
                },
                CaveBuildPipelineDomains.QueueLabel(MilestonePlayDisk));
        }

        public static void QueueAfterFoothillRing(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (mainTerrain == null || ground == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () => RunMilestoneWithOptionalFix(
                    ground,
                    request,
                    CaveBuildWorldLayoutAudit.MilestoneFoothillRing,
                    mainTerrain,
                    onComplete),
                CaveBuildPipelineDomains.QueueLabel(MilestoneFoothillRing));
        }

        public static void QueueAfterWilderness(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (mainTerrain == null || ground == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () => RunMilestoneWithOptionalFix(
                    ground,
                    request,
                    CaveBuildWorldLayoutAudit.MilestoneWilderness,
                    mainTerrain,
                    onComplete),
                CaveBuildPipelineDomains.QueueLabel(MilestoneWilderness));
        }

        public static void QueuePropsCategoryAudit(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            string categoryName,
            Action onComplete)
        {
            if (ground == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    var milestone = MilestonePropsPrefix + categoryName;
                    var report = CaveBuildWorldLayoutAudit.Run(
                        ground,
                        request,
                        milestone,
                        categoryName);
                    if (!report.layoutAcceptable &&
                        report.suggestedFix == FixPropSnapCategory &&
                        !string.IsNullOrEmpty(categoryName) &&
                        Enum.TryParse(categoryName, out SurfacePropCategory category))
                    {
                        var surfaceRoot = GameObject.Find("GeneratedSurfaceWorld")?.transform;
                        var veg = surfaceRoot != null ? surfaceRoot.Find(SurfaceWorldPaths.VegetationName) : null;
                        if (veg != null && ground.Terrain != null)
                            CaveBuildPropSnapRepair.SnapVegetationCategory(ground.Terrain, veg, category, out _);
                    }

                    CaveBuildPhaseContractRegistry.MarkRungComplete(MapMilestoneToRung(milestone), request?.Seed ?? 0);
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel(MilestonePropsPrefix + categoryName));
        }

        static void RunMilestoneWithOptionalFix(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            string milestone,
            Terrain mainTerrain,
            Action onComplete)
        {
            if (mainTerrain != null)
                SurfaceTerrainTileExpansion.SnapFullWorldTerrainGridNow(mainTerrain);

            var report = CaveBuildWorldLayoutAudit.Run(ground, request, milestone, null);
            InvalidateRungsForMilestone(milestone, report);

            if (report.layoutAcceptable || report.suggestedFix == FixNone || LayoutPlanForce)
            {
                CaveBuildPhaseContractRegistry.MarkRungComplete(MapMilestoneToRung(milestone), request?.Seed ?? 0);
                onComplete?.Invoke();
                return;
            }

            CaveBuildEditorLog.LogSurface(
                $"[Surface] {milestone} — applying targeted fix: {report.suggestedFix} " +
                $"({report.failingTileIds?.Length ?? 0} tile(s)).",
                forceUnityConsole: true);

            QueueApplyTargetedFix(mainTerrain, ground, request, report, () =>
            {
                var retry = CaveBuildWorldLayoutAudit.Run(ground, request, milestone, null);
                InvalidateRungsForMilestone(milestone, retry);
                CaveBuildPhaseContractRegistry.MarkRungComplete(MapMilestoneToRung(milestone), request?.Seed ?? 0);
                onComplete?.Invoke();
            });
        }

        static void InvalidateRungsForMilestone(string milestone, CaveBuildWorldLayoutAudit.AuditReport report)
        {
            if (report.layoutAcceptable)
                return;

            switch (milestone)
            {
                case CaveBuildWorldLayoutAudit.MilestonePlayDisk:
                    CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungLayoutAuditPlayDisk);
                    CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungMacroTerrain);
                    break;
                case CaveBuildWorldLayoutAudit.MilestoneFoothillRing:
                    CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungLayoutAuditFoothillRing);
                    CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungLayoutAuditWilderness);
                    break;
                case CaveBuildWorldLayoutAudit.MilestoneWilderness:
                    CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungLayoutAuditWilderness);
                    CaveBuildPhaseContractRegistry.InvalidateRung(CaveBuildPhaseContractRegistry.RungPreBuildGate);
                    break;
            }

            if (report.failingTileIds != null && report.failingTileIds.Length > 0 && report.seed > 0)
                CaveBuildTerrainFingerprint.MarkDirtyTiles(report.seed, report.failingTileIds);
        }

        static void QueueApplyTargetedFix(
            Terrain mainTerrain,
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            CaveBuildWorldLayoutAudit.AuditReport report,
            Action onComplete)
        {
            switch (report.suggestedFix)
            {
                case FixRestitchEdge:
                    QueueRestitchFailingEdges(mainTerrain, report, onComplete);
                    break;
                case FixRestitchPlayPerimeter:
                    SurfaceTerrainTileExpansion.QueueStitchPlayPerimeterToFoothills(mainTerrain, onComplete);
                    break;
                case FixEnforceWildernessGrid:
                    QueueEnforceWildernessGridAndPlaySeams(mainTerrain, onComplete);
                    break;
                case FixRestitchWildernessTiles:
                    QueueRestitchWildernessTiles(mainTerrain, report, onComplete);
                    break;
                case FixLockFoothillInner:
                    SurfaceTerrainTileExpansion.QueueLockFoothillInnerEdgesToPlayDisk(mainTerrain, onComplete);
                    break;
                default:
                    onComplete?.Invoke();
                    break;
            }
        }

        static void QueueRestitchFailingEdges(
            Terrain mainTerrain,
            CaveBuildWorldLayoutAudit.AuditReport report,
            Action onComplete)
        {
            var edges = report.failingSeamEdges;
            if (edges == null || edges.Length == 0 || mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            QueueRestitchEdgeAtIndex(mainTerrain, edges, 0, onComplete);
        }

        static void QueueRestitchEdgeAtIndex(
            Terrain mainTerrain,
            CaveBuildWorldLayoutAudit.SeamEdgeRecord[] edges,
            int index,
            Action onComplete)
        {
            if (index >= edges.Length)
            {
                onComplete?.Invoke();
                return;
            }

            var edge = edges[index];
            if (!TryFindTerrainByName(mainTerrain, edge.tileA, out var tileA) ||
                !TryFindTerrainByName(mainTerrain, edge.tileB, out var tileB))
            {
                CaveBuildActionPacing.ScheduleLight(
                    () => QueueRestitchEdgeAtIndex(mainTerrain, edges, index + 1, onComplete),
                    CaveBuildPipelineDomains.QueueLabel("layout fix — restitch edge"));
                return;
            }

            SurfaceTerrainTileExpansion.QueueStitchLayoutTile(
                mainTerrain,
                tileA,
                () =>
                {
                    SurfaceTerrainTileExpansion.QueueStitchLayoutTile(
                        mainTerrain,
                        tileB,
                        () =>
                        {
                            CaveBuildActionPacing.ScheduleLight(
                                () => QueueRestitchEdgeAtIndex(mainTerrain, edges, index + 1, onComplete),
                                CaveBuildPipelineDomains.QueueLabel("layout fix — restitch edge"));
                        });
                });
        }

        static void QueueRestitchWildernessTiles(
            Terrain mainTerrain,
            CaveBuildWorldLayoutAudit.AuditReport report,
            Action onComplete)
        {
            var ids = report.failingTileIds;
            if (ids == null || ids.Length == 0 || mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            var tiles = new List<Terrain>(ids.Length);
            foreach (var id in ids)
            {
                if (TryFindTerrainByName(mainTerrain, id, out var t) && t != null)
                    tiles.Add(t);
            }

            if (tiles.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            SurfaceTerrainTileExpansion.SnapFullWorldTerrainGridNow(mainTerrain);

            var groupList = new List<Terrain> { mainTerrain };
            groupList.AddRange(SurfaceTerrainTileExpansion.CollectGameplayTiles(mainTerrain));
            groupList.AddRange(SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain));
            QueueRestitchTileAtIndex(mainTerrain, tiles.ToArray(), groupList.ToArray(), 0, onComplete);
        }

        static void QueueRestitchTileAtIndex(
            Terrain mainTerrain,
            Terrain[] tiles,
            Terrain[] group,
            int index,
            Action onComplete)
        {
            if (index >= tiles.Length)
            {
                onComplete?.Invoke();
                return;
            }

            var tile = tiles[index];
            SurfaceTerrainTileExpansion.QueueStitchLayoutTile(
                mainTerrain,
                tile,
                () =>
                {
                    CaveBuildActionPacing.ScheduleLight(
                        () => QueueRestitchTileAtIndex(mainTerrain, tiles, group, index + 1, onComplete),
                        CaveBuildPipelineDomains.QueueLabel("layout fix — restitch tile"));
                });
        }

        static string MapMilestoneToRung(string milestone) => milestone switch
        {
            CaveBuildWorldLayoutAudit.MilestonePlayDisk => CaveBuildPhaseContractRegistry.RungLayoutAuditPlayDisk,
            CaveBuildWorldLayoutAudit.MilestoneFoothillRing => CaveBuildPhaseContractRegistry.RungLayoutAuditFoothillRing,
            CaveBuildWorldLayoutAudit.MilestoneWilderness => CaveBuildPhaseContractRegistry.RungLayoutAuditWilderness,
            _ => milestone,
        };

        static void QueueEnforceWildernessGridAndPlaySeams(Terrain mainTerrain, Action onComplete)
        {
            if (mainTerrain == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    SurfaceTerrainTileExpansion.SnapFullWorldTerrainGridNow(mainTerrain);
                    SurfaceTerrainTileExpansion.QueueStitchPlayPerimeterToFoothills(
                        mainTerrain,
                        () => SurfaceTerrainTileExpansion.QueueLockFoothillInnerEdgesToPlayDisk(
                            mainTerrain,
                            onComplete));
                },
                CaveBuildPipelineDomains.QueueLabel(FixEnforceWildernessGrid));
        }

        static bool TryFindTerrainByName(Terrain mainTerrain, string tileName, out Terrain terrain)
        {
            terrain = null;
            if (string.IsNullOrEmpty(tileName) || mainTerrain == null)
                return false;

            if (mainTerrain.name == tileName)
            {
                terrain = mainTerrain;
                return true;
            }

            foreach (var t in SurfaceTerrainPlayRegion.CollectSurfaceTerrains(mainTerrain))
            {
                if (t != null && t.name == tileName)
                {
                    terrain = t;
                    return true;
                }
            }

            foreach (var t in SurfaceTerrainTileExpansion.CollectMountainWildernessTiles(mainTerrain))
            {
                if (t != null && t.name == tileName)
                {
                    terrain = t;
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
