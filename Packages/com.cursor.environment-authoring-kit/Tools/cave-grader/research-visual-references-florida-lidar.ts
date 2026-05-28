/**
 * Northwest Florida panhandle LiDAR / DEM — public federal data (proven, no API).
 * Counties: Bay, Washington, Jackson, Calhoun (+ regional 2018 USGS/NOAA panhandle mosaic).
 */

import type { VisualReference } from "./research-visual-references.js";

/** LiDAR-backed terrain references for surface mouth / real-world height sampling. */
export const FLORIDA_LIDAR_VISUAL_REFERENCES: VisualReference[] = [
  {
    id: "fl-panhandle-lidar-dem-2018",
    title: "2018 USGS LiDAR DEM — Florida Panhandle (1 m bare-earth)",
    year: 2018,
    category: "lidar_terrain",
    provenInProduction: true,
    region: "florida-panhandle",
    counties: [
      "Bay",
      "Washington",
      "Jackson",
      "Calhoun",
      "Gulf",
      "Holmes",
      "Walton",
      "Franklin",
    ],
    docUrl: "https://www.fisheries.noaa.gov/inport/item/58332",
    dataUrls: [
      "https://noaa-nos-coastal-lidar-pds.s3.amazonaws.com/dem/FL_Panhandle_DEM_2018_8942/index.html",
      "https://coast.noaa.gov/dataviewer/",
      "https://www.floridadisaster.org/dem/ITM/geographic-information-systems/lidar/",
    ],
    imageUrls: [],
    notes:
      "Primary 1 m hydro-flattened DEM for NW panhandle. Use DAV to subset Bay/Washington/Jackson/Calhoun tiles; import heightmap into Unity Terrain for surface walk-in mouth alignment.",
  },
  {
    id: "fl-panhandle-inundation-dem-east",
    title: "NOAA Coastal Inundation DEM — Florida Panhandle East",
    year: 2022,
    category: "lidar_terrain",
    provenInProduction: true,
    region: "florida-panhandle-east",
    counties: ["Washington", "Bay", "Calhoun", "Gulf", "Franklin", "Jefferson", "Leon", "Wakulla"],
    docUrl: "https://www.fisheries.noaa.gov/inport/item/66602",
    dataUrls: [
      "https://chs.coast.noaa.gov/htdata/raster2/elevation/FL_Panhandle_DEM_2018_8942",
      "https://coast.noaa.gov/dataviewer/",
    ],
    imageUrls: [],
    notes:
      "Sea-level viewer DEM mosaic; includes Washington, Bay, and Calhoun. Good for coastal elevation context near Panama City / Chipley corridor.",
  },
  {
    id: "fl-bay-county-panama-city-dem",
    title: "Panama City FL Coastal DEM (Bay County — NCEI)",
    year: 2007,
    category: "lidar_terrain",
    provenInProduction: true,
    region: "bay-county-fl",
    counties: ["Bay"],
    docUrl: "https://www.ncei.noaa.gov/access/metadata/landing-page/bin/iso?id=gov.noaa.ngdc.mgg.dem:243",
    dataUrls: [
      "https://www.ncei.noaa.gov/maps/bathymetry/",
      "https://www.ngdc.noaa.gov/mgg/coastal/",
    ],
    imageUrls: [],
    notes:
      "Bay County / Panama City coastal bathy-topo DEM (~10 m grid). Use for entrance terrain scale near Gulf coast; combine with 2018 1 m lidar where overlapping.",
  },
  {
    id: "fl-pensacola-escambia-dem",
    title: "Pensacola FL Coastal DEM (adjacent to Bay County)",
    year: 2016,
    category: "lidar_terrain",
    provenInProduction: true,
    region: "escambia-fl",
    counties: ["Bay"],
    docUrl: "https://www.ncei.noaa.gov/access/metadata/landing-page/bin/iso?id=gov.noaa.ngdc.mgg.dem:11507",
    dataUrls: ["https://coast.noaa.gov/dataviewer/"],
    imageUrls: [],
    notes:
      "Western panhandle coastal DEM context (Pensacola). Useful when aligning cave mouth toward Gulf / low relief coastal plain.",
  },
  {
    id: "fl-jackson-calhoun-nwf-lidar",
    title: "NW Florida Water Management District — panhandle LiDAR (Jackson / Calhoun)",
    year: 2018,
    category: "lidar_terrain",
    provenInProduction: true,
    region: "inland-panhandle",
    counties: ["Jackson", "Calhoun", "Washington"],
    docUrl: "https://www.floridadisaster.org/dem/ITM/geographic-information-systems/lidar/",
    dataUrls: [
      "https://www.floridadisaster.org/dem/ITM/geographic-information-systems/lidar/",
      "https://noaa-nos-coastal-lidar-pds.s3.amazonaws.com/dem/FL_Panhandle_DEM_2018_8942/index.html",
    ],
    imageUrls: [],
    notes:
      "Inland panhandle counties (Marianna / Blountstown / Chipley area) covered by 2018 panhandle lidar delivery. Contact NWFWMD for LAS; use NOAA bulk GeoTIFF for DEM hillshade.",
  },
  {
    id: "fl-usgs-3dep-elevation",
    title: "USGS 3DEP / National Map — elevation (panhandle fallback)",
    year: 2025,
    category: "lidar_terrain",
    provenInProduction: true,
    region: "florida-panhandle",
    counties: ["Bay", "Washington", "Jackson", "Calhoun"],
    docUrl: "https://data.usgs.gov/datacatalog/data/USGS:35f9c4d4-b113-4c8d-8691-47c428c29a5b",
    dataUrls: [
      "https://apps.nationalmap.gov/downloader/",
      "https://www.usgs.gov/3d-elevation-program",
    ],
    imageUrls: [
      "https://www.usgs.gov/sites/default/files/styles/full_width/public/thumbnail/images/3DEP_0.png",
    ],
    notes:
      "Fallback seamless DEM if county tile subset fails. Export 1 m or 1/3 arc-sec for Unity Terrain carve tests.",
  },
];
