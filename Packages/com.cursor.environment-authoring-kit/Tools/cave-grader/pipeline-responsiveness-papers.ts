/**
 * Editor responsiveness, terrain seam I/O, Unity queue pacing — Environment Kit pipeline R&D.
 * Category: `pipeline_editor_responsiveness`
 */
import type { ResearchEntry } from "./research-catalog.js";

const P = "pipeline_editor_responsiveness";

function e(
  lab: string,
  title: string,
  year: number,
  venue: string,
  url: string,
  topics: string
): ResearchEntry {
  return { lab, title, year, venue, url, topics, provenInProduction: true };
}

export const PIPELINE_RESPONSIVENESS_PAPERS: ResearchEntry[] = [
  e("Unity", "Unity Manual — Execution Order", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html", `${P}, editor frame, script lifecycle, defer heavy work`),
  e("Unity", "Unity Manual — EditorApplication.delayCall", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication-delayCall.html", `${P}, next-frame deferral, avoid blocking OnGUI`),
  e("Unity", "Unity Manual — EditorApplication.update", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorApplication-update.html", `${P}, heartbeat polling, live status during long imports`),
  e("Unity", "Terrain.SetNeighbors", 2026, "Unity Scripting", "https://docs.unity3d.com/ScriptReference/Terrain.SetNeighbors.html", `${P}, 9-tile seam LOD, pace SetNeighbors one terrain per frame`),
  e("Unity", "TerrainData.SetHeightsDelayLOD", 2026, "Unity Scripting", "https://docs.unity3d.com/ScriptReference/TerrainData.SetHeightsDelayLOD.html", `${P}, row-band height commits, micro terrain writes`),
  e("Unity", "Terrain.Flush", 2026, "Unity Scripting", "https://docs.unity3d.com/ScriptReference/Terrain.Flush.html", `${P}, batch flush after all tiles not per-tile during stitch`),
  e("Unity", "Terrain Tools — PaintContext", 2026, "Unity Manual", "https://docs.unity3d.com/6000.2/Documentation/ScriptReference/TerrainTools.PaintContext.html", `${P}, cross-tile height edits, seam-aware authoring`),
  e("Guerrilla", "HZD Streaming the World (production reference)", 2026, "GDC archive", "https://www.guerrilla-games.com/read/Streaming-the-World-of-Horizon-Zero-Dawn", `${P}, streaming + incremental commit patterns for open worlds`),
  e("Epic", "World Partition streaming sources", 2026, "Epic Docs", "https://dev.epicgames.com/documentation/en-us/unreal-engine/world-partition-in-unreal-engine", `${P}, cell load/unload pacing analogy for terrain tiles`),
  e("Unity Discussions", "Editor freezes during terrain operations", 2024, "Unity Discussions", "https://discussions.unity.com/t/editor-freezes-when-modifying-terrain/", `${P}, GetHeights/SetHeights main-thread cost, split work`),
  e("Unity", "Profiling the Editor", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/Profiler.html", `${P}, find 30–60s stalls in FullWorld builds`),
  e("Unity", "PlayerLoop vs Editor updates", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/um-editor.html", `${P}, why Scene view stalls when main thread blocked`),
  e("INRIA", "3DGS training — not for editor main thread", 2023, "GitHub", "https://github.com/graphdeco-inria/gaussian-splatting", `${P}, keep GPU splats off hot path during terrain build`),
  e("Environment Kit", "CaveBuildActionPacing queue", 2026, "Hub Package", "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildActionPacing.cs", `${P}, Light/Normal/Heavy queue, ScheduleNextEditorFrame`),
  e("Environment Kit", "CaveBuildMicroTerrainHeightmap", 2026, "Hub Package", "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildMicroTerrainHeightmap.cs", `${P}, row-band SetHeightsDelayLOD during peak normalize`),
  e("Environment Kit", "SurfaceTerrainTileExpansion seams", 2026, "Hub Package", "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/SurfaceTerrainTileExpansion.cs", `${P}, neighbor stitch 8/8 pacing, connectivity defer`),
  e("Environment Kit", "CaveBuildRunStatusPublisher", 2026, "Hub Package", "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildRunStatusPublisher.cs", `${P}, Library live status, in-memory Hub feed, heartbeat publish`),
  e("Google", "Immersive Navigation — not editor pipeline", 2026, "Google Blog", "https://blog.google/products-and-platforms/products/maps/ask-maps-immersive-navigation/", `${P}, contrast: consumer runtime vs Unity editor pacing`),
  e("Khronos", "3D Tiles streaming LOD", 2026, "Cesium", "https://cesium.com/blog/2026/04/27/3d-gaussian-splats-lod/", `${P}, stream tiles vs sync load all neighbors`),
  e("Unity", "AsyncGPUReadback", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html", `${P}, avoid sync GPU read during terrain polish`),
  e("Unity", "AssetDatabase.ImportAsset batching", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.html", `${P}, write status to Library not Assets every frame`),
  e("Environment Kit", "RESEARCH_OPEN_WORLD_STREAMING", 2026, "Hub Docs", "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_OPEN_WORLD_STREAMING.md", `${P}, chunk streaming plan aligns with paced 9-tile`),
  e("Environment Kit", "MacBook hardware budget", 2026, "Hub Package", "Packages/com.cursor.environment-authoring-kit/Editor/Blockout/EnvironmentKitHardwareBudget.cs", `${P}, conserve GPU, paced CPU, defer NavMesh`),
  e("Unity", "LogType — reduce Console mirror during build", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LogType.html", `${P}, Hub/Pipeline feed vs mirrorPacedBuildLogsToConsole`),
  e("Environment Kit", "PLAN_PIPELINE_RESPONSIVENESS", 2026, "Hub Docs", "Packages/com.cursor.environment-authoring-kit/docs/PLAN_PIPELINE_RESPONSIVENESS.md", `${P}, full pipeline audit start-to-finish`),
];
