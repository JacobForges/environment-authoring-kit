# Public repository scope (read this first)

**Repository:** [github.com/JacobForges/environment-authoring-kit](https://github.com/JacobForges/environment-authoring-kit)  
**Product:** **Environment Authoring Kit** (UPM `com.cursor.environment-authoring-kit`)  
**Author:** **[JacobForges](https://github.com/JacobForges)** — sole author and copyright holder. No co-authors.  
**This workspace:** **Hub** — Unity 6 reference project that hosts the kit + playable demo. See [GLOSSARY.md](GLOSSARY.md).

This file is the **accuracy contract** for what is on GitHub vs what you must supply locally. Other docs should match this.

---

## Author & attribution

| Rule | Detail |
|------|--------|
| **Copyright holder** | [JacobForges](https://github.com/JacobForges) |
| **Co-authors** | **None** — do not list Cursor, AI agents, or bots as authors in commits, docs, or GitHub metadata |
| **AI tooling** | Cursor SDK / LLM graders are **optional automation** — not credited as co-authors of the kit |
| **Commits** | Authored as **JacobForges** only; no `Co-authored-by:` trailers (see package `.cursor/rules/git-commit-as-user.mdc`) |

---

## What this repo is

- **Environment Authoring Kit** — Unity 6 editor package (`Packages/com.cursor.environment-authoring-kit`)
- **Planner pipeline** — layout brief → paced terrain/caves/props; **AI wizard optional**, **non-AI JSON** supported ([PLANNER_SESSION.md](PLANNER_SESSION.md))
- **Hub reference project** — playable demo (`Assets/Scripts/`), phased cursor-bot (`Tools/cursor-bot/`), demo scenes (local)
- **FullWorld pipeline** — ~289-tile surface grid + **122**-step queued cave build (classic preset path)
- **Project skeleton** — `ProjectSettings/`, UPM `manifest.json`, committed **`Assets/EnvironmentKit/`** presets & recipes
- **Documentation** — requirements, pipeline design, grader prompts, storage guide
- **Positioning** — procedural **base framework** for terrain + caves; you refine art and gameplay

## What this repo is not

- A finished, shippable XR game
- “Plug in VITURE glasses and play” without your scenes, art, SDK, and device setup
- A redistribution of Asset Store / marketplace art, audio, or characters
- A guarantee of commercial **Ship** tier on first build (grading targets are goals, not promises)

---

## Committed vs local-only

| Path | On GitHub | Notes |
|------|-----------|--------|
| `Packages/com.cursor.environment-authoring-kit/` | Yes | Kit source, `Tools/cave-grader` sources (not `node_modules`) |
| `Assets/EnvironmentKit/Presets/` | Yes | Includes `VitureXRPro.asset` (XR **budget** profile, not device SDK) |
| `Assets/EnvironmentKit/Recipes/` | Yes | JSON build recipes |
| `Assets/EnvironmentKit/Documentation/` | Yes | Kit-oriented docs |
| `Assets/EnvironmentKit/Generated/` | **No** (gitignored) | Build reports, prompts, live status — regenerated per machine; **may symlink** to external drive — [STORAGE_AND_DISK.md](STORAGE_AND_DISK.md) |
| `Assets/EnvironmentKit/ResearchCache/` | **No** (gitignored) | Run `npm run sync-research-pull` locally; see attribution doc; may symlink externally |
| `Hub/Library/` (Artifacts, PackageCache, …) | **No** | Unity import cache — **move to external volume** on Mac builds to avoid disk-full crashes |
| `Assets/*.unity` scenes | **No** | Create scenes in your clone; menu **Rebuild Complete Cave (MainScene)** only works if you add `MainScene.unity` |
| Other `Assets/*` (store packs, props, meshes) | **No** | Your licenses; assign paths in kit catalog / scatter settings |
| `.env`, API keys | **No** | `Tools/cave-grader/.env.example` only |
| VITURE / GlassesGateway natives | **No** | Install per vendor docs in your project if needed |

---

## Pipeline facts (current code)

| Topic | Accurate statement |
|-------|-------------------|
| Queued cave schedule | **122** paced indices — `CaveBuildQueuedPipelineSchedule.Total` — see [PIPELINE_TRUTH.md](PIPELINE_TRUTH.md) |
| Hub progress UI | Live **Step** counter (all paced queue work); **122/122** = queue done, not quality |
| Step index **65** | **Meat loop** entry only — not total step count (old docs wrongly said 63) |
| Geo block | Queued steps **1–15** (not 1–13) |
| FullWorld order | Surface grid (**~289 tiles** default) **then** queued cave pipeline |
| Play disk core | 3×3 nine-tile region anchors layout; 81-tile mountain core inside extended grid |
| Default recipe | `Assets/EnvironmentKit/Recipes/aaa-full-cave-production.json` |
| Package version | **0.3.4** (`Packages/com.cursor.environment-authoring-kit/package.json`) |
| Disk pressure | Advisory JSON writes skip safely (`EnvironmentKitDataRoot.TryWriteAllText`) — see [STORAGE_AND_DISK.md](STORAGE_AND_DISK.md) |
| Planner session | **Layout-first** — `CaveBuildPlannerBrief.json` + `CaveBuildActiveSessionConfig.json`. **AI wizard optional**; non-AI = hand-edit JSON + finalize. Concept PNG optional (75/25 weight when present). See [PLANNER_SESSION.md](PLANNER_SESSION.md) |

---

## XR / VITURE (honest)

| Included | Not included |
|----------|----------------|
| Unity XR packages in `Packages/manifest.json` (OpenXR, Android XR OpenXR, XRI, etc.) | VITURE neckband / glasses **native SDK** |
| Editor **`XROptimizationProfile`** pass (LOD, colliders, URP hints) during builds | Device QA, comfort, or store submission |
| Optional `VitureIntegration` **detection** if **you** install a VITURE assembly | Demo scene with XR rig pre-wired |

**Summary:** XR is **authoring-time optimization + Unity XR stack**, not a glasses-ready product in this repository.

---

## AI / automation (honest — updated to match code)

| Provider | How `grade-and-fix.ts` runs |
|----------|-----------------------------|
| **Cursor** (default) | `@cursor/sdk` — needs **`CURSOR_API_KEY`**; local or cloud agent |
| **Google / Anthropic / OpenAI-compatible / OpenRouter / Custom** | Direct HTTP APIs via `CAVE_AI_PROVIDER` + **`CAVE_ACTIVE_API_KEY`** (and model/base URL from Hub export) |
| **All providers** | In-editor grading, Hub, exported prompts work without any API |

**Non-Cursor file edits:** models return JSON edit blocks; applied only if `CAVE_EXTERNAL_APPLY_EDITS=1` (Hub: *Allow external provider edits*; dry-run default).

**Earlier docs were wrong** when they said non-Cursor was “config only.” Runtime switching **is implemented** in `Tools/cave-grader/grade-and-fix.ts`. Cursor-specific features (local agent, cloud repo URL, `npm run doctor` SQLITE checks) apply only when provider = Cursor.

---

## License (one terms file — not CC0)

| File | Meaning |
|------|---------|
| [`LICENSE.md`](../Packages/com.cursor.environment-authoring-kit/LICENSE.md) | **Educational / personal non-commercial — free.** **Commercial use (monetary gain, public sale, paid products built on the kit) requires a separate license or purchase from the copyright holder (JacobForges).** |
| Root [`LICENSE`](../LICENSE) | Points to the same `LICENSE.md` |

**Not public domain. Not CC0.** Third-party Unity, npm, Cursor, store assets, and government geodata stay under their own terms — [THIRD_PARTY_AND_LICENSE_SCOPE.md](THIRD_PARTY_AND_LICENSE_SCOPE.md).

---

## After clone (minimum)

1. Unity Hub → **Unity 6000.x** → open repo root (folder may be named `Hub` or `environment-authoring-kit`).
2. **Node 18+** on the machine.
3. **Mac SSD tight on space?** Run external storage migration before long builds — [STORAGE_AND_DISK.md](STORAGE_AND_DISK.md).
4. **Window → Environment Kit → Hub → Build Complete Cave** — first click runs **clone setup** (starter scene, placeholder modules, `npm install`). Watch **Hub → Build** during the run (Pipeline Console is optional).
5. **Licensed prefabs** under `Assets/` — Hub → Settings → **Prefab folders** (default modules path not in git).
6. Preflight **PASS** (read `CaveBuildPreflightReport.md` if blocked).

Optional: `.env` from `.env.example` for Cursor/LLM automation only.

See **[README — First build](../README.md#first-build-after-clone)**.
