# Hub — XR cave adventure (Unity)

Unity 6 project for a **VITURE XR** adventure game with procedural **Florida karst surface** and **underground lava-tube** levels. Gameplay direction: walkable trails into a cave mouth, enclosed tunnels, minable blocks, Ragnarok Online / Zelda-style exploration.

The primary authoring system is **`com.cursor.environment-authoring-kit`** (local UPM package under `Packages/`). Current package version: **0.2.0**.

## Quick start

| Step | Action |
|------|--------|
| 1 | Open this repo in **Unity Hub** (Unity **6000.x**, URP). |
| 2 | Open scene **`MainScene`** (or your active cave scene). |
| 3 | Place / confirm cave entrance: GameObject **`PortalFive`** (shop portal is separate). |
| 4 | Confirm **Ground** anchor / terrain (9-tile grid when expanded). |
| 5 | **Window → Environment Kit → Build Complete Cave Level (Active Scene)** |
| 6 | Watch **Cave Build → Diagnostics → Pipeline Console** until **63/63** |
| 7 | Optional: **Cave Build Grader** for letter grade and failing stages |

Stuck on ramp-only / no tunnels? **Cave Build → Advanced → Build Complete Cave — Full AAA Rebuild (invalidate ladder)**.

For Cursor automation: [Package README](Packages/com.cursor.environment-authoring-kit/README.md) · [Cave grading & Cursor](Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md).

## Repository layout

```
Hub/
├── README.md                 ← this file
├── REQUIREMENTS.md           ← product & technical requirements
├── docs/
│   ├── README.md             ← documentation index
│   └── CHANGELOG.md          ← dated changes
├── Assets/                   ← game content & scenes
│   └── EnvironmentKit/       ← Generated/, ResearchCache/, Recipes/
├── Packages/
│   └── com.cursor.environment-authoring-kit/
│       ├── README.md
│       ├── CHANGELOG.md
│       ├── docs/
│       ├── Editor/
│       ├── Runtime/
│       └── Tools/cave-grader/
```

## Main editor menus

All under **Window → Environment Kit**:

| Menu | Purpose |
|------|---------|
| **Build Complete Cave Level** | FullWorld: surface + 9-tile terrain + vegetation, then 63-step cave pipeline |
| **Build Surface World Only** | Surface / terrain / props only |
| **Build Cave Only — Align to Surface** | Underground only (existing surface) |
| **Rebuild Complete Cave (MainScene)** | Full rebuild in MainScene |
| **Build Complete Cave — Full AAA Rebuild** | Invalidate ladder cache; force full geo |
| **Cave Build Grader** | Scores, prompts, Cursor agent |
| **Terrain Build Grader** | Surface terrain ladder |

Full table: [Package README](Packages/com.cursor.environment-authoring-kit/README.md).

## Documentation policy

1. **`REQUIREMENTS.md`** — scope and acceptance criteria  
2. **`docs/CHANGELOG.md`** — dated implementation notes  
3. **Package `docs/`** — pipeline, grading, attribution (source of truth for kit behavior)  
4. **Package `CHANGELOG.md`** — package version history for publishing  

When you land a change, update the changelog and any affected requirement section.

## Research cache (Florida panhandle)

References under `Assets/EnvironmentKit/ResearchCache/`. Refresh from the kit:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
# Set HUB_ROOT in .env to this repo’s absolute path
npm run sync-research-pull
```

**Data credits:** [RESEARCH_DATA_ATTRIBUTION.md](Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md).

## License

**Original kit code and project docs** are **[CC0 1.0](LICENSE)**. Unity, game `Assets/`, npm dependencies, Cursor SDK, and **government geospatial data** are not CC0 — see **[docs/THIRD_PARTY_AND_LICENSE_SCOPE.md](docs/THIRD_PARTY_AND_LICENSE_SCOPE.md)**.

## Requirements

See **[REQUIREMENTS.md](REQUIREMENTS.md)** and the package **[docs/REQUIREMENTS.md](Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md)**.
