# Glossary — Environment Authoring Kit

Use these terms consistently in docs, GitHub, and chat.

**Author:** [JacobForges](https://github.com/JacobForges) — sole author. No co-authors in documentation, commits, or GitHub About.

**Voice recap tooling:** macOS Personal Voice scripts may reference a registered voice name (e.g. in `say -v "…"`). That is a **narration device label**, not a second copyright holder.

| Term | Meaning |
|------|---------|
| **Environment Authoring Kit** | Product name. UPM package `com.cursor.environment-authoring-kit`. Procedural Florida karst surface + lava-tube cave pipeline for Unity 6. |
| **EnvKit** | Short alias for Environment Authoring Kit. |
| **environment-authoring-kit** | Official **GitHub repository** name. Prefer this over renaming to "Hub". |
| **Hub** (capital H) | This **Unity reference project** workspace (`~/Hub`). Contains the kit, demo gameplay scripts, and cursor-bot. |
| **Environment Kit Hub** | Editor UI window — build monitor, presets, settings (**Window → Environment Kit → Hub**). |
| **cave-grader** | Node/TypeScript tooling in `Packages/.../Tools/cave-grader/` — quality JSON, Cursor SDK, planner wizard. |
| **cursor-bot** | Shell orchestration in `Tools/cursor-bot/` — phased agent sessions (world → playable demo). |
| **FullWorld** | Default build scope — large surface terrain grid (~289 tiles) then queued cave pipeline. |
| **Planner session** | Layout-first build — brief JSON drives terrain/layout. **AI wizard optional**; non-AI path = edit JSON + finalize. See [PLANNER_SESSION.md](PLANNER_SESSION.md). |
| **Build Wizard** | Optional Node UI (`npm run build-wizard`) — Q&A checklist, concept PNG. Not required for builds. |
| **Phase 1 / 2 / 3** | Bot phases: world ladder (grade ≥ 85) → playable demo → polish. |
| **Ladder / rung** | Grading ladder step (research, terrain, nav, cave geo, etc.). |
| **122 steps** | Internal queued cave schedule index count; Hub shows a live **Step** counter for all paced queue work. |
| **Generated/** | Per-machine build output (gitignored). May symlink to external drive. |
| **Lexar / external bundle** | `/Volumes/<Drive>/EnvironmentKit-Hub/` — heavy data + Unity Library caches. |

## Repo & project naming (official)

| Use this name | For |
|---------------|-----|
| **environment-authoring-kit** | **GitHub repository** — do not rename to `Hub` |
| **Environment Authoring Kit** | Product, package `displayName`, docs title |
| **Hub** | Local Unity **reference project** folder only (`~/Hub`) |
| **Environment Kit Hub** | Editor window (build monitor) |

**JacobForges** is the sole author ([@JacobForges](https://github.com/JacobForges)). The repo URL stays `github.com/JacobForges/environment-authoring-kit`.

Renaming the GitHub repo to `Hub` alone is discouraged — too generic, poor search, hides terrain/cave scope.
