/**
 * Mountain LiDAR / DEM references — separate from Florida play-disk LiDAR (cave structure).
 * Use for outer-ring massifs, cliff silhouettes, and open-world tile continuity.
 */
import type { VisualReference } from "./research-visual-references.js";

export const MOUNTAIN_LIDAR_VISUAL_REFERENCES: VisualReference[] = [
  {
    id: "mountain-usgs-3dep-elevation",
    title: "USGS 3DEP — National Elevation Dataset (mountain context)",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    docUrl: "https://www.usgs.gov/3d-elevation-program",
    imageUrls: [],
    dataUrls: ["https://www.usgs.gov/3d-elevation-program/get-involved/data-map"],
    notes:
      "Bare-earth DEM for macro ridge lines. Outer ring only — do not flatten play-disk center. Pair with ResearchCache hillshades.",
  },
  {
    id: "mountain-opentopography-srtm",
    title: "OpenTopography — SRTM / global DEM tiles",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    docUrl: "https://opentopography.org/",
    imageUrls: [],
    dataUrls: ["https://portal.opentopography.org/datasetMetadata?otCollectionID=OT.042013.4326.1"],
    notes: "Global mountain relief sampling for open-world tile seeds. World-space noise must align across tiles.",
  },
  {
    id: "mountain-copernicus-dem",
    title: "Copernicus DEM — GLO-30 mountainous terrain",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    docUrl: "https://spacedata.copernicus.eu/collections/copernicus-digital-elevation-model",
    imageUrls: [],
    dataUrls: ["https://dataspace.copernicus.eu/"],
    notes: "30m DEM for distant peak silhouettes and cliff band planning.",
  },
  {
    id: "mountain-fl-panhandle-dem-outer-ring",
    title: "Florida panhandle DEM — outer tile hillshade (creative)",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    region: "Florida panhandle",
    counties: ["Bay", "Washington", "Jackson", "Calhoun"],
    docUrl: "https://www.usgs.gov/3d-elevation-program",
    imageUrls: [],
    dataUrls: [],
    notes:
      "Reuse ResearchCache/images/fl-*-hillshade for neighbor tiles. Main play disk stays Ground/LiDAR authoritative; mountains are stylized outer ring.",
  },
  {
    id: "mountain-usgs-appalachian-3dep",
    title: "USGS 3DEP — Appalachian Mountains bare-earth DEM",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    docUrl: "https://www.usgs.gov/3d-elevation-program",
    imageUrls: [],
    dataUrls: ["https://apps.nationalmap.gov/downloader/"],
    notes:
      "Rolling ridge-and-valley relief (Blue Ridge / Appalachian Plateau). Wilderness ring only — broad crests, gentle foothills, no knife-edge tile rims.",
  },
  {
    id: "mountain-usgs-great-smoky-3dep",
    title: "USGS 3DEP — Great Smoky Mountains DEM & hillshade",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    docUrl: "https://www.nps.gov/grsm/learn/nature/geology.htm",
    imageUrls: [],
    dataUrls: ["https://www.usgs.gov/3d-elevation-program"],
    notes:
      "Smoky-style rounded summits, long ridgelines, deep saddles. Satellite/hillshade for silhouette only — not height stamps on play disk.",
  },
  {
    id: "mountain-unity-terrain-heightmap-lidar-workflow",
    title: "Unity 6 — Importing heightmaps from DEM GeoTIFF",
    year: 2026,
    category: "mountain_lidar",
    provenInProduction: true,
    studio: "Unity Technologies",
    docUrl: "https://docs.unity3d.com/6000.5/Documentation/Manual/terrain-Heightmaps.html",
    imageUrls: [
      "https://docs.unity3d.com/6000.5/Documentation/uploads/Main/terrain-Heightmap-Tools.png",
    ],
    notes: "Stamp outer band only; paced SetHeightsDelayLOD + single SyncHeightmap per tile.",
  },
];
