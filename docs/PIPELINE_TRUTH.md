# Pipeline truth (code source of record)

**Author:** [JacobForges](https://github.com/JacobForges) · **Package:** v0.3.4 · **Repo:** [environment-authoring-kit](https://github.com/JacobForges/environment-authoring-kit)

When docs disagree, trust this file and `CaveBuildQueuedPipelineSchedule.cs`.

---

## Product & naming

| Term | Meaning |
|------|---------|
| **Environment Authoring Kit** | Product / UPM `com.cursor.environment-authoring-kit` |
| **environment-authoring-kit** | GitHub repository name |
| **Hub** | Local Unity reference project folder (`~/Hub`) |
| **Environment Kit Hub** | Editor window — **Window → Environment Kit → Hub** |

---

## Build entry paths

| Path | Steps |
|------|--------|
| **A — Planner-first** | Wizard (`npm run build-wizard`) or hand-edit `CaveBuildPlannerBrief.json` + `CaveBuildActiveSessionConfig.json` → Hub → Build. See [PLANNER_SESSION.md](PLANNER_SESSION.md). |
| **B — Classic FullWorld** | Hub preset → **Build Complete Cave (122)**. No planner session required. |

**No API keys** required for either path. `agentInvokes: false` (planner default) skips blocking tsx agents during build.

---

## Surface terrain scope

| Mode | Tiles | When |
|------|-------|------|
| **Extended FullWorld (classic default)** | **~289** (17×17, Chebyshev 8) | `UseExtendedOpenWorldGrid=true` (default) |
| **81-tile mountain core** | 9 play + 16 foothill + 24 peak + 32 horizon | Inside extended grid |
| **Planner fast demo** | **81** or **13** (`floatingTiles`) | `CaveBuildActiveSessionConfig.json` |
| **Nine-tile play disk** | 3×3 center | Always anchors layout |

Surface runs **before** queued cave step 0 on FullWorld startup.

---

## Queued cave pipeline — 122 steps

`CaveBuildQueuedPipelineSchedule.Total = 122`

| Steps | Phase | Count |
|-------|--------|-------|
| **0** | Validate & prep | 1 |
| **1–15** | Geo (maze, shell, blocks, mouth, spawn) | 15 |
| **16–33** | Playability | 18 |
| **34–39** | Validation | 6 |
| **40–49** | Ground polish / burial | 10 |
| **50–64** | World stages (nav, spawn, XR LOD, …) | 15 |
| **65** | **Meat loop** entry | 1 |
| **66–89** | Post-meat | 24 |
| **90–101** | Research | 12 |
| **102–119** | Finalize polish | 18 |
| **120** | AAA manifest | 1 |
| **121** | Finish report | 1 |

**Meat loop is step index 65**, not 63. **122/122** = queue complete, not quality grade.

---

## Planner session (layout-first)

| File | Role |
|------|------|
| `CaveBuildPlannerBrief.json` | Layout truth — markers, specs, trails |
| `CaveBuildActiveSessionConfig.json` | Scope truth — tiles, caves, `agentInvokes` |
| `concept.png` | Optional — 75% weight when present; marker-only fallback |

Unity authors: `CaveBuildPlannerTerrainGuide`, `LayoutAuthor`, `MeshLandscapeAuthor`, `TrailAuthor`, `ContentAuthor`, `FidelityGate`.

---

## Bot phases (cursor-bot)

| Phase | Exit |
|-------|------|
| **1 — World** | `CaveBuildQualityReport.json` → `buildAcceptable: true`, grade ≥ **85** |
| **2 — Gameplay** | `HubGameProgress.json` → `demoReady: true` |
| **3 — Polish** | Human-directed |

---

## Gitignored generated truth

`Assets/EnvironmentKit/Generated/` — reports, briefs, session config, checkpoints (may symlink to external drive — [STORAGE_AND_DISK.md](STORAGE_AND_DISK.md)).
