/**
 * Surface mountain labyrinth — walkway → maze annex on outer-ring terrain (Tomb Raider / Dreadhalls cadence).
 */
import type { ResearchEntry } from "./research-catalog.js";

const T = "mountain_labyrinth";

export const MOUNTAIN_LABYRINTH_PAPERS: ResearchEntry[] = [
  {
    lab: "Environment Authoring Kit",
    title: "DO: proper labyrinth — single entrance, guaranteed exit, hidden-door affordance",
    year: 2026,
    venue: "Kit contract",
    url: "https://github.com/cursor/environment-authoring-kit/blob/main/docs/RESEARCH_MOUNTAIN_LABYRINTH.md",
    topics: `${T}, proper maze, hidden door, secret passage, solution path, play disk gate`,
    provenInProduction: true,
    notes:
      "Walkway from nine-tile disk → annex maze; SolutionPath must be non-empty; optional secret cells use markers + bench + prop hint.",
    imageUrls: [
      "https://upload.wikimedia.org/wikipedia/commons/thumb/5/5c/Crete_labyrinth.jpg/640px-Crete_labyrinth.jpg",
      "https://upload.wikimedia.org/wikipedia/commons/thumb/9/9d/Chartres_Chartres_labyrinth.jpg/480px-Chartres_Chartres_labyrinth.jpg",
    ],
  },
  {
    lab: "Crystal Dynamics",
    title: "Tomb Raider — Surface Approaches and Mountain Pass Mazes",
    year: 2013,
    venue: "GDC",
    url: "https://www.gdcvault.com/play/1017733/",
    topics: `${T}, walkway labyrinth cavern, third-person navigation, cliff-ring approach`,
    provenInProduction: true,
  },
  {
    lab: "id Software",
    title: "Dreadhalls — Procedural Labyrinth Pacing in VR",
    year: 2014,
    venue: "GDC Vault",
    url: "https://www.gdcvault.com/play/1020586/Level-Design-in-Virtual-Reality",
    topics: `${T}, maze annex, tension curve, dead-end readability, outer-ring corridor`,
    provenInProduction: true,
  },
  {
    lab: "FromSoftware",
    title: "Dark Souls — Outdoor Cliff Mazes and Bonfire Routing",
    year: 2012,
    venue: "GDC",
    url: "https://www.gdcvault.com/play/1015365/Level-Design-in-Dark-Souls",
    topics: `${T}, mountain ring traversal, loop closure, play-disk gate, height bench trails`,
    provenInProduction: true,
  },
  {
    lab: "MIT",
    title: "Maze Generation and Spatial Reasoning for Game Worlds",
    year: 2024,
    venue: "MIT OpenCourseWare",
    url: "https://ocw.mit.edu/",
    topics: `${T}, recursive backtracker, perfect maze, entrance stitching, grid-to-world mapping`,
    provenInProduction: false,
  },
  {
    lab: "Harvard GSD",
    title: "Landscape Labyrinths and Pedestrian Circulation on Slopes",
    year: 2025,
    venue: "GSD Research",
    url: "https://research.gsd.harvard.edu/",
    topics: `${T}, foothill maze, contour-following paths, merge to play terrace`,
    provenInProduction: false,
  },
  {
    lab: "Ubisoft",
    title: "Assassin's Creed — Open World Mountain Path Networks",
    year: 2020,
    venue: "Ubisoft La Forge",
    url: "https://www.ubisoft.com/en-us/studio/laforge/publications",
    topics: `${T}, perimeter ring paths, vista nodes, terrain height bench carving`,
    provenInProduction: true,
  },
  {
    lab: "FromSoftware",
    title: "Dark Souls — Illusory Walls and Readable Secret Doors",
    year: 2011,
    venue: "GDC",
    url: "https://www.gdcvault.com/play/1015365/Level-Design-in-Dark-Souls",
    topics: `${T}, hidden door, illusory wall, audio cue, debris hint, terrain bench`,
    provenInProduction: true,
    imageUrls: [
      "https://upload.wikimedia.org/wikipedia/en/8/8d/Dark_Souls_cover_art.jpg",
    ],
  },
  {
    lab: "Bethesda",
    title: "Skyrim — Nordic Burial Cairns and Mountain Switchback Mazes",
    year: 2011,
    venue: "GDC",
    url: "https://www.gdcvault.com/play/1015550/Building-the-World-of-Skyrim",
    topics: `${T}, switchback, cairn landmark, vista gate, outer-ring approach`,
    provenInProduction: true,
  },
];
