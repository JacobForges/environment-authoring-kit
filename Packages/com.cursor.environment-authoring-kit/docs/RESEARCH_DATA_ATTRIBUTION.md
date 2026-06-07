# Research data attribution

The Environment Authoring Kit references **public government and open geospatial datasets** for real-world terrain and **Floridan aquifer / karst structure** when aligning cave mouths and underground scale. This document gives credit to data providers and clarifies how the kit uses their work.

**Kit code and TypeScript tooling** (excluding `node_modules`) are under **[LICENSE.md](../LICENSE.md)** (educational use free; commercial use requires a separate license) — see [THIRD_PARTY_AND_LICENSE_SCOPE.md](../../../docs/THIRD_PARTY_AND_LICENSE_SCOPE.md) where applicable. **Third-party data** below is **not** licensed by this project; use it under each provider’s terms.

---

## How the kit uses geospatial data

| Use in cave pipeline | Allowed sources | Excluded (water / hydrology visuals) |
|----------------------|-----------------|--------------------------------------|
| Surface mouth height, entrance carve | Bare-earth LiDAR DEM (Class 2 ground), USGS 3DEP | Bathymetry, inundation DEMs, hydro-flattened water surfaces for void layout |
| Void scale, ceiling depth hints | USGS Floridan aquifer **structural surfaces** and unit **thickness** (DS 926) | 10,000 mg/L TDS boundary, potentiometric / head surfaces |
| Collapse pockets, choke points | FGS / FDEP subsidence & karst incident GIS | Spring discharge, stream hydrography |

Agents and builders should read **local** files under `Assets/EnvironmentKit/ResearchCache/` before downloading bulk tiles or re-searching the web.

---

## Elevation & LiDAR

| Provider | Dataset / service | Role in kit | Credit / terms |
|----------|-------------------|-------------|----------------|
| **U.S. Geological Survey (USGS)** | [3D Elevation Program (3DEP)](https://www.usgs.gov/3d-elevation-program), National Map Elevation ImageServer, [EPQS](https://epqs.nationalmap.gov/v1/docs) | County hillshade PNGs (`sync-florida-hillshades`), terrain fallback | U.S. Government work; USGS data policies — [https://www.usgs.gov/information-policies](https://www.usgs.gov/information-policies) |
| **NOAA Office for Coastal Management** | [2018 Florida Panhandle LiDAR](https://www.fisheries.noaa.gov/inport/item/58332), DEM/LAZ on AWS Open Data | Primary 1 m panhandle DEM/LAZ references (Bay, Washington, Jackson, Calhoun) | NOAA / U.S. Department of Commerce; [Digital Coast](https://coast.noaa.gov/) terms |
| **Florida Division of Emergency Management** | State LiDAR program index | Portal links for county LAS | State of Florida / partner terms per download page |
| **NWFWMD** | District GIS / hydrogeology portal | Inland panhandle context | Northwest Florida Water Management District — use per district site terms |

---

## Aquifer & karst structure (Florida)

| Provider | Dataset / publication | Role in kit | Credit / terms |
|----------|----------------------|-------------|----------------|
| **USGS** | [Data Series 926](https://pubs.usgs.gov/ds/0926/) — digital surfaces & thicknesses, Floridan aquifer system | Structural top/base, unit thickness for cave void scale | U.S. Geological Survey; cite Williams & Dixon, 2015, DS 926 |
| **USGS** | [Professional Paper 1807](https://pubs.usgs.gov/publication/pp1807/) — revised hydrogeologic framework | Framework boundaries, faults | Williams & Kuniansky, 2015 |
| **USGS** | [Data Series 584](https://pubs.usgs.gov/ds/584/) — legacy Floridan surfaces | Fallback structural contours | USGS RASA digital surfaces |
| **USGS** | [Caribbean-Florida Water Science Center](https://fl.water.usgs.gov/) | Regional Floridan studies index | USGS publications as cited |
| **Florida Geological Survey (FGS)** / **FDEP** | Subsidence incident GIS, karst open data, [external data sites](https://floridadep.gov/fgs/data-maps/content/fgs-external-data-sites) | Sinkhole / subsidence seeds for collapse geometry | FDEP / FGS — open data terms on [geodata.dep.state.fl.us](https://geodata.dep.state.fl.us/) |
| **FGS** | OFMS 104 / OFR 98 — LiDAR geomorphology methodology (springs, sinkholes, caves on DEM) | Workflow reference for karst mapping | Florida Geological Survey; UFDC / DEP publication terms |

---

## Game & engine references (non-government)

Studio and engine visual references (Ubisoft, EA, Epic, Unity, shipped-game Steam CDN previews, etc.) are documented in `Tools/cave-grader/research-visual-references-*.ts`. Those materials remain subject to **their respective publishers’** terms; the kit stores **metadata and optional preview URLs** in `ResearchCache` for agent guidance only.

---

## Suggested citation (academic / reports)

If you publish work that relied on these datasets:

> Terrain reference: USGS 3DEP and NOAA 2018 Florida Panhandle LiDAR (Class 2 ground). Subsurface structure reference: USGS Data Series 926, Floridan aquifer system digital surfaces (Williams & Dixon, 2015). Karst features: Florida Geological Survey subsidence incident database, Florida DEP GeoData.

---

## Maintainer commands

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
HUB_ROOT=/path/to/Hub npm run sync-research-cache
HUB_ROOT=/path/to/Hub npm run sync-florida-hillshades
```

Optional preview images: `npm run sync-research-cache:images`  
Unity build (optional): set `CAVE_SYNC_FL_HILLSHADES=1` to run hillshades during the research phase.
