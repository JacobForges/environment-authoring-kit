# Hub documentation index

| Document | Purpose |
|----------|---------|
| [../README.md](../README.md) | Project overview, quick start, doc policy |
| [../REQUIREMENTS.md](../REQUIREMENTS.md) | **Requirements** — functional, quality, XR, acceptance criteria |
| [CHANGELOG.md](CHANGELOG.md) | **Changelog** — dated implementation and fix notes |
| [THIRD_PARTY_AND_LICENSE_SCOPE.md](THIRD_PARTY_AND_LICENSE_SCOPE.md) | **CC0 scope** vs Unity, Assets, npm, Cursor SDK, geospatial data |
| [../Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md](../Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md) | **Data credits** — USGS, NOAA, FGS/FDEP (Florida LiDAR & aquifer) |
| [../LICENSE](../LICENSE) | CC0 1.0 — public domain (original repo work) |
| [../Packages/com.cursor.environment-authoring-kit/README.md](../Packages/com.cursor.environment-authoring-kit/README.md) | Environment Authoring Kit — install, menus, 63-step pipeline |
| [../Packages/com.cursor.environment-authoring-kit/CHANGELOG.md](../Packages/com.cursor.environment-authoring-kit/CHANGELOG.md) | **Package changelog** (UPM version history) |
| [../Packages/com.cursor.environment-authoring-kit/docs/README.md](../Packages/com.cursor.environment-authoring-kit/docs/README.md) | Package documentation index |
| [../Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md](../Packages/com.cursor.environment-authoring-kit/docs/REQUIREMENTS.md) | Kit requirements (9-tile props, cave contract) |
| [../Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md](../Packages/com.cursor.environment-authoring-kit/docs/CaveGradingAndCursor.md) | Grading outputs, Cursor API, pre/post workflows, dud rules |
| [../Packages/com.cursor.environment-authoring-kit/docs/SURFACE-WORLD-BUILD.md](../Packages/com.cursor.environment-authoring-kit/docs/SURFACE-WORLD-BUILD.md) | Surface scopes, vegetation contract |
| [../Packages/com.cursor.environment-authoring-kit/docs/PHASE_CONTRACTS.md](../Packages/com.cursor.environment-authoring-kit/docs/PHASE_CONTRACTS.md) | Ladder rung I/O + queued step map |

## When to update what

| You changed… | Update |
|--------------|--------|
| User-visible behavior or menu flow | `CHANGELOG.md` + relevant README section |
| Scope, priority, or acceptance bar | `REQUIREMENTS.md` |
| Grade stages, JSON paths, Cursor env | `CaveGradingAndCursor.md` |
| Research cache, Florida terrain, attribution | `RESEARCH_DATA_ATTRIBUTION.md` + `Assets/EnvironmentKit/ResearchCache/README.md` |
| New editor menu or pipeline stage | Package `README.md` + both `CHANGELOG.md` files |
| Package version / publish | `Packages/.../CHANGELOG.md` + `package.json` version |
