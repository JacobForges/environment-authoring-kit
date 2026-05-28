/**
 * NW Florida panhandle county bounding boxes (WGS84) for LiDAR subset / hillshade sampling.
 */
export type FloridaCountyId = "bay" | "washington" | "jackson" | "calhoun";

export type CountyBbox = {
  id: FloridaCountyId;
  name: string;
  west: number;
  south: number;
  east: number;
  north: number;
  /** Representative center for point queries */
  centerLon: number;
  centerLat: number;
};

export const FLORIDA_TARGET_COUNTIES: CountyBbox[] = [
  {
    id: "bay",
    name: "Bay",
    west: -85.95,
    south: 29.92,
    east: -85.35,
    north: 30.45,
    centerLon: -85.66,
    centerLat: 30.17,
  },
  {
    id: "washington",
    name: "Washington",
    west: -85.85,
    south: 30.35,
    east: -85.25,
    north: 30.95,
    centerLon: -85.55,
    centerLat: 30.61,
  },
  {
    id: "jackson",
    name: "Jackson",
    west: -85.55,
    south: 30.55,
    east: -84.85,
    north: 31.15,
    centerLon: -85.22,
    centerLat: 30.84,
  },
  {
    id: "calhoun",
    name: "Calhoun",
    west: -85.45,
    south: 30.1,
    east: -85.0,
    north: 30.75,
    centerLon: -85.2,
    centerLat: 30.44,
  },
];

export function countyById(id: FloridaCountyId): CountyBbox | undefined {
  return FLORIDA_TARGET_COUNTIES.find((c) => c.id === id);
}
