# Hub documentation index

Documentation for the **environment-authoring-kit** GitHub repository.

**Author:** [JacobForges](https://github.com/JacobForges) — sole author. No co-authors.

**Start here:** [PUBLIC_REPO_SCOPE.md](PUBLIC_REPO_SCOPE.md) — what is committed vs local-only (accuracy contract).

---

## Repository docs

| Document | Content |
|----------|---------|
| [../README.md](../README.md) | Project overview, quick start, XR honesty |
| [PUBLIC_REPO_SCOPE.md](PUBLIC_REPO_SCOPE.md) | GitHub vs local machine — scenes, art, cache, secrets, **author policy** |
| [GLOSSARY.md](GLOSSARY.md) | Product vs Hub vs repo naming |
| [STORAGE_AND_DISK.md](STORAGE_AND_DISK.md) | Lexar migration, disk-full recovery |
| [PIPELINE_TRUTH.md](PIPELINE_TRUTH.md) | Canonical pipeline numbers (122 steps, geo 1–15, meat 65, tile scopes) |
| [PLANNER_SESSION.md](PLANNER_SESSION.md) | Planner — AI optional, non-AI JSON path, session flags |
| [../REQUIREMENTS.md](../REQUIREMENTS.md) | Hub-level product requirements |
| [CHANGELOG.md](CHANGELOG.md) | Dated Hub / pipeline notes |
| [HEAL_AND_SYSTEM_MATRIX.md](HEAL_AND_SYSTEM_MATRIX.md) | Auto-heal checkpoints + integrate/manual/delete matrix for all subsystems |
| [THIRD_PARTY_AND_LICENSE_SCOPE.md](THIRD_PARTY_AND_LICENSE_SCOPE.md) | Educational free vs commercial license; third-party terms |
| [../Packages/com.cursor.environment-authoring-kit/LICENSE.md](../Packages/com.cursor.environment-authoring-kit/LICENSE.md) | Kit license (educational free; commercial by permission) |
| [../LICENSE](../LICENSE) | Hub pointer to package LICENSE.md |

---

## Gameplay & AI agent (competition layer)

| Document | Content |
|----------|---------|
| [COMPETITION_AGENT_GAME.md](COMPETITION_AGENT_GAME.md) | **Class-sharing guide** — onboarding, agent AI, movement authority, discipline, chat, persistence, file map |
| [GAMEPLAY_DEMO_ACCEPTANCE.md](GAMEPLAY_DEMO_ACCEPTANCE.md) | Play Mode demo checklist (G1–G8, `demoReady`) |
| [../Assets/Scripts/Competition/README.md](../Assets/Scripts/Competition/README.md) | Short pointer in the Competition code folder |

---

## Environment Authoring Kit (package)

| Document | Content |
|----------|---------|
| [../Packages/com.cursor.environment-authoring-kit/README.md](../Packages/com.cursor.environment-authoring-kit/README.md) | Install, menus, **122-step** pipeline, Hub |
| [../Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_TERRAIN_AND_HUB.md](../Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_TERRAIN_AND_HUB.md) | FullWorld grid (~289 tiles), Hub monitoring, sequential terrain |
| [../Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_GENERATION_PRESETS.md](../Packages/com.cursor.environment-authoring-kit/docs/FULLWORLD_GENERATION_PRESETS.md) | Hub FullWorld presets 1–20 — when to use each, flags, troubleshooting |
| [../Assets/EnvironmentKit/Presets/README.md](../Assets/EnvironmentKit/Presets/README.md) | Unity atmosphere/terrain ScriptableObjects (vs Hub presets) |
| [../Packages/com.cursor.environment-authoring-kit/docs/README.md](../Packages/com.cursor.environment-authoring-kit/docs/README.md) | Package documentation index |
| [../Packages/com.cursor.environment-authoring-kit/CHANGELOG.md](../Packages/com.cursor.environment-authoring-kit/CHANGELOG.md) | Package version history (**0.3.4**) |
| [../Packages/com.cursor.environment-authoring-kit/LICENSE.md](../Packages/com.cursor.environment-authoring-kit/LICENSE.md) | UPM package license (educational / commercial) |

---

## Policy

When you change behavior, update **PUBLIC_REPO_SCOPE** (if visibility changes), **REQUIREMENTS**, **CHANGELOG**, and the package docs that describe menus or contracts.

**Author policy:** credit **JacobForges** only. Never add co-authors (including AI/Cursor) to docs, LICENSE, or commit trailers.
