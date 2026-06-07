/**
 * Multi-tile Unity terrain — SetNeighbors, heightmap edge stitch, open-world grids.
 * Feeds ResearchCache category terrain_tiling for merge/seed agents.
 */
import type { ResearchEntry } from "./research-catalog.js";

const T = "terrain_tiling";

export const TERRAIN_TILING_PAPERS: ResearchEntry[] = [
  {
    lab: "Unity Technologies",
    title: "Terrain.SetNeighbors — LOD stitching between adjacent tiles",
    year: 2026,
    venue: "Unity Scripting API",
    url: "https://docs.unity3d.com/ScriptReference/Terrain.SetNeighbors.html",
    topics: `${T}, SetNeighbors, LOD match, Flush after neighbor wiring`,
    provenInProduction: true,
  },
  {
    lab: "Unity Technologies",
    title: "Create Neighbor Terrains — Fill Heightmap Using Neighbors",
    year: 2026,
    venue: "Unity Manual",
    url: "https://docs.unity3d.com/Manual/terrain-CreateNeighborTerrains.html",
    topics: `${T}, cross-blend heightmap edges, auto connect grouping id, seam-free grid`,
    provenInProduction: true,
  },
  {
    lab: "Bugnet",
    title: "Fix Unity Terrain Holes at Chunk Boundaries",
    year: 2025,
    venue: "Bugnet Blog",
    url: "https://bugnet.io/blog/fix-unity-terrain-holes-appearing-at-chunk-boundaries",
    topics: `${T}, heightmap edge mismatch, stitch pass, SetNeighbors both directions`,
    provenInProduction: true,
  },
  {
    lab: "Unity Community",
    title: "Seamless terrain — manual height edge blend",
    year: 2024,
    venue: "Unity Discussions",
    url: "https://discussions.unity.com/t/terrain-setneighbors-example/383003",
    topics: `${T}, edge height must match manually, linear blend seam band, not automatic heights`,
    provenInProduction: true,
  },
  {
    lab: "Darrell Bircsak",
    title: "Terrain Stitch Function — match shared heightmap edges",
    year: 2018,
    venue: "Blog",
    url: "https://darrellbircsak.com/page/2/",
    topics: `${T}, copy edge heights neighbor to neighbor, split terrain tiles, resolution mismatch`,
    provenInProduction: false,
  },
  {
    lab: "USGS 3DEP",
    title: "Seamless DEM Mosaics for Large-Area Terrain",
    year: 2024,
    venue: "USGS",
    url: "https://www.usgs.gov/3d-elevation-program",
    topics: `${T}, DEM tile seams, georeferenced stamp per cell, open world LiDAR grid`,
    provenInProduction: true,
  },
];
