#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Editor.Generation;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Compares terrain/prop fingerprints and marks only dirty ladder rungs (incremental skip).
    /// </summary>
    public static class CaveBuildTerrainContractCheck
    {
        public const string PhaseId = "terrain_contract_check";

        public static void QueueAtTerrainPipelineStart(
            SceneGroundInfo ground,
            WorldGenerationRequest request,
            Action onComplete)
        {
            if (ground?.Terrain == null || request == null)
            {
                onComplete?.Invoke();
                return;
            }

            CaveBuildActionPacing.ScheduleLight(
                () =>
                {
                    var seed = request.Seed;
                    var main = ground.Terrain;
                    var currentPlay = CaveBuildTerrainFingerprint.ComputePlayDiskFingerprint(main);
                    var store = CaveBuildTerrainFingerprint.LoadStore(seed);
                    var dirty = new List<string>();

                    if (store.seed == seed &&
                        !string.IsNullOrEmpty(store.playDiskFingerprint) &&
                        store.playDiskFingerprint != currentPlay)
                    {
                        dirty.Add(CaveBuildPhaseContractRegistry.RungMacroTerrain);
                        dirty.Add(CaveBuildPhaseContractRegistry.RungTrailsNav);
                        dirty.Add(CaveBuildPhaseContractRegistry.RungSurfaceProps);
                    }

                    foreach (var tileId in CaveBuildTerrainFingerprint.GetDirtyTileIds(seed))
                    {
                        if (tileId.StartsWith("SurfaceMountainFoothill", StringComparison.Ordinal))
                            dirty.Add(CaveBuildPhaseContractRegistry.RungLayoutAuditFoothillRing);
                        if (tileId.StartsWith("SurfaceMountainPeak", StringComparison.Ordinal))
                            dirty.Add(CaveBuildPhaseContractRegistry.RungLayoutAuditWilderness);
                    }

                    foreach (var rung in dirty)
                        CaveBuildPhaseContractRegistry.InvalidateRung(rung);

                    if (dirty.Count > 0)
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] {PhaseId} — marked {dirty.Count} rung(s) dirty from fingerprints / layout audit.",
                            forceUnityConsole: true);
                    }
                    else
                    {
                        CaveBuildEditorLog.LogSurface(
                            $"[Surface] {PhaseId} — terrain fingerprints match stored artifacts (seed {seed}).",
                            forceUnityConsole: false);
                    }

                    CaveBuildPhaseContractRegistry.MarkRungComplete(
                        CaveBuildPhaseContractRegistry.RungTerrainContractCheck,
                        seed);
                    onComplete?.Invoke();
                },
                CaveBuildPipelineDomains.QueueLabel(PhaseId));
        }
    }
}
#endif
