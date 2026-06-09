/**
 * FullWorld grid ~70% — CC0 import finalize (catalog, Resources, asset save) editor stalls.
 * Category: `fullworld_cc0_finalize`
 */
import type { ResearchEntry } from "./research-catalog.js";

const C = "fullworld_cc0_finalize";

function e(
  lab: string,
  title: string,
  year: number,
  venue: string,
  url: string,
  topics: string,
  notes?: string
): ResearchEntry {
  return { lab, title, year, venue, url, topics, provenInProduction: true, notes };
}

export const FULLWORLD_CC0_FINALIZE_PAPERS: ResearchEntry[] = [
  e(
    "Unity",
    "AssetDatabase.StartAssetEditing — batch imports",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.StartAssetEditing.html",
    `${C}, CC0 Resources copy, CopyAsset deps, one import per queue step`,
    "Wrap titan Resources CopyAsset + prefab write; StopAssetEditing once per paced step — not per dependency."
  ),
  e(
    "Unity",
    "AssetDatabase.SaveAssetIfDirty — scoped persist",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.SaveAssetIfDirty.html",
    `${C}, item registry, WorldItemCatalog, never project-wide SaveAssets`,
    "SaveAssets() during 289-tile build flushes all dirty terrains/meshes — beachball at CC0 finalize."
  ),
  e(
    "Unity",
    "AssetDatabase API — avoid sync import storms",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.html",
    `${C}, scoped refresh, Library status writes, batch asset ops`,
    "Pair with EnvironmentKitScopedAssetRefresh — never Refresh() whole project mid-grid."
  ),
  e(
    "Unity",
    "PrefabUtility.SaveAsPrefabAsset",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PrefabUtility.SaveAsPrefabAsset.html",
    `${C}, CC0 Resources prefab copy already on disk`,
    "No follow-up SaveAssets per prefab; paced finalize saves ScriptableObjects only."
  ),
  e(
    "Unity",
    "Resources.UnloadUnusedAssets",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Resources.UnloadUnusedAssets.html",
    `${C}, defer full unload until queue idle or memory pressure`,
    "Periodic trim during CC0 save steps caused 30–60s stalls on 16GB Mac despite 23% budget."
  ),
  e(
    "Unity",
    "EditorUtility.UnloadUnusedAssetsImmediate",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorUtility.UnloadUnusedAssetsImmediate.html",
    `${C}, memory guard full release only above pressure threshold`,
    "Use terrain heightmap ReleaseWorkingSet + GC on routine steps; full unload at pause only."
  ),
  e(
    "Unity",
    "EditorApplication.delayCall",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication-delayCall.html",
    `${C}, one registry/catalog save per queue frame`,
    "Same pattern as seam stitch 8/8 fix — defer heavy persist across editor frames."
  ),
  e(
    "Unity",
    "EditorApplication.update — live Hub heartbeat",
    2026,
    "Unity Scripting",
    "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication-update.html",
    `${C}, sub-action must update during asset save micro-steps`,
    "Stale sub-action during main-thread stall = false freeze diagnosis."
  ),
  e(
    "Unity Discussions",
    "Editor freezes during terrain / asset operations",
    2024,
    "Unity Discussions",
    "https://discussions.unity.com/t/editor-freezes-when-modifying-terrain/",
    `${C}, split main-thread work, profile 30–60s stalls`,
    "FullWorld CC0 finalize at grid 70% is asset I/O not terrain — still same main-thread rule."
  ),
  e(
    "Guerrilla",
    "Streaming the World — incremental commit",
    2026,
    "GDC archive",
    "https://www.guerrilla-games.com/read/Streaming-the-World-of-Horizon-Zero-Dawn",
    `${C}, commit in slices, defer global flush`,
    "Catalog 12 items/step + registry/catalog SO save mirrors streaming cell commits."
  ),
  e(
    "Environment Kit",
    "Cc0ContentImportPipeline paced finalize",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/Cc0ContentImportPipeline.cs",
    `${C}, prep → items 1/frame → registry → catalog slices → titan Resources → SO save`,
    "Post-weld bottleneck after extended flat grid + terraform ~70%."
  ),
  e(
    "Environment Kit",
    "CaveBuildMemoryGuard — tiered working-set release",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildMemoryGuard.cs",
    `${C}, light GC vs full UnloadUnusedAssets`,
    "Routine step: terrain cache + GC. Pressure ≥72% or queue idle: full unload."
  ),
  e(
    "Environment Kit",
    "EnvironmentKitScopedAssetRefresh",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/EnvironmentKitScopedAssetRefresh.cs",
    `${C}, no bulk Generated JSON import during build`,
    "CC0 deferred refresh uses task-scoped import only."
  ),
  e(
    "Environment Kit",
    "WorldRuntimeResourcesAuthor — Hollow Titan Resources only",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/World/WorldRuntimeResourcesAuthor.cs",
    `${C}, legendary slots + loot, not EnsureAllEnemyPrefabsInResources at grid weld`,
    "World enemy Resources copy deferred to surface finalize — cuts finalize RAM spike."
  ),
  e(
    "Environment Kit",
    "RESEARCH_PIPELINE_RESPONSIVENESS",
    2026,
    "Hub Docs",
    "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_PIPELINE_RESPONSIVENESS.md",
    `${C}, parent category — queue pacing + live status`,
    "Seam stitch fix pattern applies to CC0 finalize save chain."
  ),
  e(
    "Environment Kit",
    "RESEARCH_FULLWORLD_DO_NOT",
    2026,
    "Hub Docs",
    "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_FULLWORLD_DO_NOT.md",
    `${C}, research guides agents; C# execution fixes stalls`,
    "DO NOT FullWorld before surface lock; CC0 runs after grid weld at ~70%."
  ),
  e(
    "Environment Kit",
    "SURFACE_BUILD_RESPONSIVENESS",
    2026,
    "Hub Docs",
    "Packages/com.cursor.environment-authoring-kit/docs/SURFACE_BUILD_RESPONSIVENESS.md",
    `${C}, one heavy step per frame, skip SceneView repaint during long build`,
    "FullWorld 289 tiles × micro steps — never sync SaveAssets/Refresh on hot path."
  ),
  e(
    "Environment Kit",
    "MacBook hardware budget",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/EnvironmentKitHardwareBudget.cs",
    `${C}, UnloadUnusedAssetsBetweenQueueSteps throttled when queue busy`,
    "16GB unified memory — CC0 finalize must not trigger project-wide asset flush."
  ),
  e(
    "Unity",
    "Profiling the Editor",
    2026,
    "Unity Manual",
    "https://docs.unity3d.com/6000.0/Documentation/Manual/Profiler.html",
    `${C}, verify CC0 save sub-steps under 2s each`,
    "If a single SaveAssetIfDirty exceeds 5s, split catalog entries further."
  ),
  e(
    "Environment Kit",
    "LavaTubeMaterialUpgrader — deferred SaveAssets flush",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/LavaTubeMaterialUpgrader.cs",
    `${C}, defer disk flush until build teardown, _deferredAssetFlush flag`,
    "Catalog persist mirrors FlushDeferredAssetChanges — not mid-grid SaveAssetIfDirty on Resources."
  ),
  e(
    "Environment Kit",
    "LavaTubeCaaBuilder — StartAssetEditing session nest",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/LavaTubeCaveBuilder.cs",
    `${C}, BeginBuildAssetEditing nest, StopAssetEditing once at teardown`,
    "Cc0ContentImportAssetSession uses same nest pattern during paced CC0 import."
  ),
  e(
    "Environment Kit",
    "WorldItemCatalogBuilder.QueuePacedSaveIfDirty",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/World/WorldItemCatalogBuilder.cs",
    `${C}, defer Resources catalog YAML write until MarkFullWorldGridPipelineFinished`,
    "SaveAssetIfDirty on WorldItemCatalog in Resources/ caused grid ~70% beachball."
  ),
  e(
    "Environment Kit",
    "PLAN_FULLWORLD_CC0_FINALIZE",
    2026,
    "Hub Docs",
    "Packages/com.cursor.environment-authoring-kit/docs/PLAN_FULLWORLD_CC0_FINALIZE.md",
    `${C}, verification checklist for grid 70% → CC0 complete`,
    "Success = CC0 finalize → grid continues; catalog disk persist at grid finish."
  ),
  e(
    "Environment Kit",
    "Build Complete Cave vs Full AAA Rebuild — grid pipeline parity",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceTerrainTileExpansion.cs",
    `${C}, fullworld_simple_build_parity, QueueFullWorldLayAllGroundTilesOnly, MarkFullWorldGridPipelineStarted`,
    "Simple build used lay-only ground path without grid prepare/snap safeguards; Full AAA purge+prepare worked. Fix: same MarkFullWorldGridPipelineStarted + RunFullWorldGridPrepare as directional pipeline."
  ),
  e(
    "Environment Kit",
    "CaveBuildAaaSessionPolicy — provisional extended grid before request bind",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildAaaSessionPolicy.cs",
    `${C}, fullworld_simple_build_parity, PreferOneTilePerQueueStep, batch size 1, step counter 7500`,
    "UsesExtendedOpenWorldGrid was false until BindActiveRequest — simple build snapped 289 tiles with 8–12/batch and wrong Hub phase label."
  ),
  e(
    "Unity",
    "AssetDatabase API restrictions during import (Unity 6.4)",
    2026,
    "Unity Discussions",
    "https://discussions.unity.com/t/changes-to-assetdatabase-apis-when-called-during-import/1689358",
    `${C}, fullworld_simple_build_parity, SetDirty ScriptableObject mid-build, SaveAssetIfDirty exception`,
    "SetDirty(CaveBuildCursorSettings) during preflight caused CaveBuildCursorSettings.asset reimport while grid snap ran — avoid asset writes during long builds."
  ),
  e(
    "Unity",
    "Domain reload — avoid AssetDatabase.Refresh in editor hooks",
    2026,
    "Unity Discussions",
    "https://discussions.unity.com/t/current-stage-of-the-reload-script-assemblies-problem-in-2023/927393/1",
    `${C}, fullworld_simple_build_parity, import storm, incremental additive surface`,
    "Incremental Build Complete Cave keeps existing terrains; orphan purge in RunFullWorldGridPrepare is required before 289-tile snap — Full AAA invalidate skips stale slots."
  ),
  e(
    "Environment Kit",
    "QueueAaaPreTerraformLandmarkAndSeams — CC0 paced import at grid weld",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceTerrainTileExpansion.cs",
    `${C}, fullworld_simple_build_parity, Cc0ContentImportPipeline.QueueEnsureAll, UsesExtendedOpenWorldGrid`,
    "Extended grid weld must queue paced CC0 before Hollow Titan — not direct PlaceAaaBeforeTerraform. Both build modes share this when UsesExtendedOpenWorldGrid is true."
  ),
  e(
    "Environment Kit",
    "Hollow Titan forceSyncFullBuild — grid ~70% post-CC0 freeze",
    2026,
    "Hub Package",
    "Packages/com.cursor.environment-authoring-kit/Editor/World/HollowTitanStumpSculptPhases.cs",
    `${C}, fullworld_simple_build_parity, RunAllSync, PlaceAaaBeforeTerraform, grid 70 percent`,
    "After CC0 import complete, delayCall invoked PlaceAaaBeforeTerraform(forceSync:true) → RunAllSync(32 stump sculpt passes) on main thread. Hub stuck on CC0 import complete. Fix: pace meat+stump whenever IsLongBuildActive."
  ),
];
