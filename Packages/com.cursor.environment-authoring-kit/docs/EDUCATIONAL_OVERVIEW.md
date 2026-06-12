# Educational Overview — Environment Authoring Kit

**Repository:** public GitHub project `environment-authoring-kit` (clone URL in root `README.md`)  
**Package:** `com.cursor.environment-authoring-kit` · Unity 6 · URP 17+  
**Author:** [JacobForges](https://github.com/JacobForges) — sole author. For classmates who have not opened the project.

This is not a sales page. It is what I wish someone had told me before treating a **Prototype-tier** build like a shipped game.

---

## 0. Two realities: GitHub clone vs my laptop

Everything below is grounded in an **audit of my actual Unity project** on this machine (May 2026). Your mileage will differ; the **repo alone** is the lower bound.

### What you get from a fresh clone (basic path)

| Included on GitHub | Not on GitHub |
|--------------------|---------------|
| Package source under `Packages/com.cursor.environment-authoring-kit/` | My `Assets/*.unity` scenes (`MainScene`, `GeneratedCaveWorld`, etc.) |
| `Assets/EnvironmentKit/Presets/`, `Recipes/`, `Documentation/` | Asset Store / marketplace folders (lava-tube packs, characters, city kits) |
| `ProjectSettings/`, UPM manifest (Unity 6, URP, OpenXR stack) | `Assets/EnvironmentKit/Generated/` — empty until you build |
| Root docs, `.github/workflows/` | `Assets/EnvironmentKit/ResearchCache/` — run sync locally |
| `Tools/cave-grader/.env.example` only | Real `.env` API keys, `node_modules/` |

**Out of the box (no AI keys):** you can run **Build Complete Cave (122)** — deterministic C# pipeline: multi-tile surface, vegetation contracts, 122 paced cave steps, JSON grading. You can also use a **non-AI planner** (brief JSON only) — see Hub [PLANNER_SESSION.md](../../../../docs/PLANNER_SESSION.md). You do **not** get iterative agent loops unless you add API keys.

### What I run on my personal machine (reference ceiling)

This is what **I** do when developing the kit — not what GitHub guarantees:

| On my laptop | Approx. scale (audit) |
|--------------|------------------------|
| Unity scenes | `MainScene`, `GeneratedCaveWorld`, `GeneratedWorld`, plus store demo scenes |
| `Assets/EnvironmentKit/Generated/` | **66+ JSON** artifacts per iteration (quality, probes, prompts, ladder context) |
| `Assets/EnvironmentKit/ResearchCache/` | **188** catalog entries after `npm run sync-research-pull` |
| Licensed prefabs | Lava-tube modules, rocks, props under `Assets/` (not redistributable) |
| AI workflow | **Cursor** as primary; `.env` + Hub → `grade-and-fix.ts` for grade/fix loops |
| Node grader | `npm install` in `Tools/cave-grader`; research enrich, URL digest, execution briefs |

**I generate and consume JSON on my machine with AI assistance.** The procedural core writes the measurements; the agents read them and optionally patch code or kit files. Classmates cloning the repo start at **layer 1** below; my daily work is **layer 2–3**.

### Honest grades on my machine right now

These numbers come from my local `CaveBuildQualityReport.json` / `SurfaceTerrainQualityReport.json` — proof the rubric works and the world is **still in R&D**:

| Build | Score | Tier | What it means |
|-------|-------|------|----------------|
| Surface terrain ladder | **87** | B+ | Terrain side is often acceptable for playtest |
| Full cave (same project) | **58 weighted**, **Blocked** (dud) | Not shippable | Critical failures: `path` (10), `block_tunnel` (35), `geometry_integrity` (5) |

So: **I can build a world; I have not automated my way to Ship (95+).** This doc describes the **system I am building toward** (production-style automation), not a finished product.

---

## 1. What this project is (and what I am still building)

**Today:** an **editor-time procedural framework** — Florida karst–inspired surface + lava-tube-style cave, commercial-style grading, optional AI repair loops.

**Tomorrow (slowly):** tighter automation — batch seeds, stricter gates, less hand-fixing between meat-loop passes.

```text
[L4] Production automation     ← goal: CI regression, policy-locked agents, Ship enforced
[L3] AI-assisted repair      ← what I use daily (Cursor + JSON artifacts)
[L2] Deterministic procedural  ← what the repo always gives you (no API key)
[L1] Your Unity project + art  ← you supply scenes and prefabs
```

This overview teaches **L2 + L3**. **L4 is aspirational.**

---

## 2. Scientific framing — treat the build as an experiment

| Piece | In this kit |
|-------|-------------|
| **Hypothesis** | Recipe + seed + catalog → walkable surface → mouth → cave route |
| **Procedure** | `FullWorld`: surface phases → optional pre-build gate → cave queue (122 steps) |
| **Measurement** | `CaveBuildQualityReport.json`, stage scores, `dudReasons[]`, `shipBlockers[]` |
| **Confounders** | Wrong prefab catalog, missing NavMesh, agent editing unrelated files |

**Critical distinction:** build progress **122/122** is **queue completion**, not quality. A **58/100** gate score is the **rubric**, not “58% done.”

### DAG mental model

```text
seed, Ground anchor, recipe JSON, prefab catalog
        │
        ▼
surface height (6 phases) → trails / cave markers → NavMesh band
        │
        ▼
vegetation contracts (grid + interstitial passes per tile)
        │
        ▼
pre-build readiness ladder (optional; can block cave geo)
        │
        ▼
cave layout → route floor → shell → props / atmosphere
        │
        ▼
probes + commercial rubric (20+ stages)
```

**Invalidation:** change trails → downstream mouth/cave rungs must rerun. See [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md).

---

## 3. Basic path — no AI (what every clone can do)

### Requirements

- Unity **6000.x** + URP  
- **Node 18+** on the machine (first FullWorld runs `npm install` in `Tools/cave-grader`)  
- A **Ground** anchor and **your** modular cave / vegetation prefabs (kit scans `Assets/`)  
- Create or copy a `.unity` scene locally (none are committed on GitHub)

### One-click procedural build

1. **Window → Environment Kit → Hub**  
2. **Build Complete Cave (122)**  
3. Watch **Environment Kit Hub → Build** (activity feed + sub-action); optional **Pipeline Console** pop-out; Unity Console stays **errors-only** during long builds by default  
4. Open `Assets/EnvironmentKit/Generated/CaveBuildQualityReport.json`

### What deterministic code does (no LLM)

| Phase | Systems | Output |
|-------|---------|--------|
| Surface | `SurfaceTerrainAiPhases`, `SurfaceWorldGenerator`, `SurfaceIntelligentPropPlacer` | 9 tiles, trails, vegetation instances |
| Pre-build | `CaveBuildPreBuildLadder` | `CaveBuildPreBuildLadderReport.json` (can block build if enforced) |
| Cave | `CaveBuildActionPacing` — **122** queued indices | `UndergroundCaveSystem`, walk-in, atmosphere zone |
| QC | Route probes, shell audit, commercial grader | JSON + Hub / Generated reports |

**No API key required** for any of the above. Grading, prompt **export**, and probe JSON are also procedural — exporting `CaveBuildAgentPrompt.md` does not call a model until you invoke the grader.

---

## 4. AI path — what I do on my machine (and how you wire it)

I developed and test this kit with **Cursor** (IDE + `@cursor/sdk`). Other providers are implemented in code, but **I do not promise parity** — different models, different tool access, different failure modes.

### 4.1 Architecture: two execution channels

```text
                    ┌─────────────────────────────────────┐
                    │  Unity Editor (C#)                   │
                    │  Build → grade → export JSON/MD      │
                    └──────────────┬──────────────────────┘
                                   │
                    ┌──────────────▼──────────────────────┐
                    │  Tools/cave-grader/grade-and-fix.ts  │
                    └──────────────┬──────────────────────┘
           ┌───────────────────────┼───────────────────────┐
           ▼                       ▼                       ▼
    Channel A: Cursor          Channel B: HTTP APIs    Channel C: Local HTTP
    @cursor/sdk                Gemini, Anthropic,      Ollama, LM Studio
    local/cloud agent          OpenAI-compatible,      (OpenAI-shaped)
                               OpenRouter, Custom
```

| Channel | Write mechanism | Scope control |
|---------|-----------------|---------------|
| **A — Cursor** | Agent tools in repo (`cwd` = `HUB_ROOT`) | Prompt ladder: **one rung** per invoke (`visual_shell`, `navmesh`, …) |
| **B — Cloud HTTP** | Model returns JSON `{ edits: [...] }` | Path allow-list + `CAVE_EXTERNAL_APPLY_EDITS` |
| **C — Local HTTP** | Same as B | Same; often weaker at multi-file refactors |

### 4.2 All eight providers (Hub → environment variables)

Configured in **Hub → Settings** (`CaveBuildCursorSettings`). Unity spawns `grade-and-fix.ts` with env from `CaveBuildCursorProcessResolver.cs`.

| # | Provider | Hub / EditorPrefs key | Primary env vars | Default model / endpoint | API key required? |
|---|----------|----------------------|------------------|--------------------------|-------------------|
| 0 | **Cursor** | `CaveBuild_CursorApiKey` | `CURSOR_API_KEY`, `CAVE_CURSOR_MODEL`, optional `CAVE_CURSOR_REPO_URL` | `auto` | **Yes** (cloud/local agent) |
| 1 | **Google Gemini** | `CaveBuild_GoogleApiKey` | `GOOGLE_API_KEY`, `CAVE_ACTIVE_API_KEY` | `gemini-2.5-flash` | Yes |
| 2 | **Anthropic Claude** | `CaveBuild_AnthropicApiKey` | `ANTHROPIC_API_KEY`, `CAVE_ACTIVE_API_KEY` | `claude-3-7-sonnet-latest` | Yes |
| 3 | **OpenAI-compatible** | `CaveBuild_OpenAiApiKey` | `OPENAI_API_KEY`, `CAVE_ACTIVE_BASE_URL` | `gpt-4.1-mini`, base `http://localhost:11434/v1` | Yes |
| 4 | **OpenRouter** | `CaveBuild_OpenRouterApiKey` | `OPENROUTER_API_KEY`, `CAVE_ACTIVE_API_KEY` | `openai/gpt-4.1-mini` | Yes |
| 5 | **Local Ollama** | *(none)* | `CAVE_ACTIVE_BASE_URL` → `http://localhost:11434/v1` | e.g. `qwen2.5-coder:14b` | Hub says no; script still needs non-empty `CAVE_ACTIVE_API_KEY` unless you set one |
| 6 | **Local LM Studio** | *(none)* | base `http://localhost:1234/v1` | `local-model` | Same caveat as Ollama |
| 7 | **Custom endpoint** | `CaveBuild_CustomApiKey` | `CUSTOM_API_KEY`, `CAVE_ACTIVE_BASE_URL` | Your label + URL | Yes |

**Active provider** is stored as `CaveBuild_AiProvider` and exported as `CAVE_AI_PROVIDER`. Model and base URL export as `CAVE_ACTIVE_MODEL`, `CAVE_ACTIVE_BASE_URL`.

**Setup I use:**

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
cp .env.example .env
# Edit: HUB_ROOT=/absolute/path/to/your/Unity/project
#       CURSOR_API_KEY=...   (from cursor.com dashboard → Cloud Agents)
npm install
npm run doctor
```

Unity: **Window → Environment Kit → Cave Build → Sync API Key from .env**

`.env` is **gitignored**. GitHub only has `.env.example` with placeholders — the README row “`.env` API keys” means **not included on GitHub**, not “keys are in the repo.”

### 4.3 What the AI layer actually does (when attached)

| Step | Who | Artifact |
|------|-----|----------|
| 1. Grade scene | Unity C# | `CaveBuildQualityReport.json`, `CaveBuildFailingStages.json` |
| 2. Pick one failing **rung** | `grade-and-fix.ts` | `CaveBuildLadderContext.json` |
| 3. Attach research (if cache exists) | Node sync + export | `CaveBuildResearch.json`, execution briefs |
| 4. Write scoped prompt | Unity + TS | `CaveBuildAgentPrompt.md`, `CaveBuildActiveRungPrompt.md` |
| 5. Invoke model | Cursor SDK or HTTP | `CaveBuildCursorLastRun.json` |
| 6. Optional rebuild | Unity menu | Re-run **Build Complete Cave** after code fixes compile |

**Research on my machine:** I run `npm run sync-research-pull` so `Assets/EnvironmentKit/ResearchCache/` fills with categorized URLs, summaries, optional Florida hillshade paths. Builds can **read cache offline**; network sync is optional (`CAVE_FORCE_RESEARCH_SYNC=1` to refresh).

**Prompt discipline:** one rung per agent pass — e.g. fix `visual_shell` onion layers, not “rewrite the entire generator.”

### 4.4 If you are not using Cursor

| Topic | Cursor (my reference) | Other providers |
|-------|----------------------|-----------------|
| Local repo agent | `@cursor/sdk` with project `cwd` | Chat completion only |
| `npm run doctor` | Key + smoke test | Partial |
| Research tooling | Tied to my Node scripts + cache layout | May not match |
| File edits | Broad agent tools | JSON edit blocks only (Channel B/C) |
| Quality outcome | What I test against | **Unknown — treat as experiment** |

**Bottom line:** the deterministic build is the same; the **repair distribution** is not.

---

## 5. Model file access — permissions and risks

I included non-Cursor file writes because this is a **research platform**, not because unrestricted writes are safe.

### 5.1 Non-Cursor JSON edits (Channel B/C)

| Env / Hub toggle | Default | Effect |
|------------------|---------|--------|
| `CAVE_EXTERNAL_APPLY_EDITS` / *Allow external provider edits* | **0 / off** | Parse edits, apply nothing |
| `CAVE_EXTERNAL_APPLY_DRY_RUN` / *External edits dry-run only* | **1 / on** | Log intended writes, no `writeFileSync` |

**Allowed paths** (`grade-and-fix.ts` → `isPathAllowed`):

- `Packages/com.cursor.environment-authoring-kit/`
- `Assets/EnvironmentKit/`
- `README.md`
- `docs/` (repo root)

Edits escaping the repo root or hitting `Assets/Scripts/` gameplay code are **rejected**. Ops: `replace` | `append` | `write` inside a fenced JSON block in the model response.

### 5.2 Cursor (Channel A)

The SDK agent can modify files under the project `cwd` per agent policy — **wider than the JSON allow-list**. I mitigate by:

- Branch or commit before **Invoke Cursor Agent**  
- Reading `CaveBuildDoNotPrompt.md` / active rung prompt  
- Re-grading JSON after every pass  
- Keeping `autoRebuildAfterAgentSuccess` off until I trust the diff  

### 5.3 Threat model (why classmates should care)

1. **Schema drift** — compiles but breaks phase contracts  
2. **Scope creep** — “fix materials” rewrites `CaveAdventureCaveGenerator`  
3. **Non-reproducibility** — seed 42 no longer matches after opaque edits  
4. **Key leakage** — never commit `.env`; rotate keys if accidentally pushed  

---

## 6. JSON and artifacts — who creates what

Everything under `Assets/EnvironmentKit/Generated/` is **gitignored** on the public repo. It is the **telemetry layer** I use with AI on my laptop.

### 6.1 Procedural only (Unity / Node, no LLM call)

| File | Role |
|------|------|
| `CaveBuildQualityReport.json` | Commercial tiers: Ship 95+, Beta 85+, Alpha 70+, Prototype 50–69, Blocked &lt;50 |
| `CaveBuildGradingManifest.json` | Stage weights for grader |
| `CaveBuildPreBuildLadderReport.json` | Readiness before cave geometry (target ~88+) |
| `CaveBuildRouteProbe.json`, `CaveBuildSurfaceRouteProbe.json` | NavMesh / reachability |
| `CaveBuildVisualShellAudit.json` | Onion / shell metrics |
| `SurfacePropPlacementPlan.json`, `SurfacePropPlacementGrade.json` | Per-tile vegetation contracts |
| `SurfaceTerrainQualityReport.json` | Terrain ladder (my last run: **87 B+**) |
| `CaveBuildCompileDiagnostics.json` | Script compile state for agents |
| `CaveBuildCommercialProductionManifest.json` | 100-point checklist export |

### 6.2 AI-oriented (exported for agents; may be updated by grader)

| File | Role |
|------|------|
| `CaveBuildAgentPrompt.md` | Human/agent summary for active rung |
| `CaveBuildMeatLoopAgentPrompt.md` | Dud / meat-loop focus |
| `CaveBuildTailoredAgentPrompt.md`, `CaveBuildActiveRungPrompt.md` | Scoped instructions |
| `CaveBuildDoNotPrompt.md`, `CaveBuildNextStepsPrompt.md` | Autonomous loop guardrails |
| `CaveBuildCursorLastRun.json` | Last TS run (my audit: `post_build`, `visual_shell`, exit 0) |
| `TerrainBuildCursorLastRun.json` | Terrain workflow diagnostics |
| `CaveBuildAgentMemory.json`, `CaveBuildAgentSession.json` | Cross-pass context |
| `CaveBuildResearch.json`, `*ResearchExecutionBrief.json` | Research catalog slice for prompts |

### 6.3 Research cache (local machine, not GitHub)

| Path | Role |
|------|------|
| `Assets/EnvironmentKit/ResearchCache/index.json` | Master index |
| `Assets/EnvironmentKit/ResearchCache/entries/` | Per-paper / per-URL records |
| `Assets/EnvironmentKit/Generated/CaveBuildResearchCache.json` | Pointer into cache |

**Commands I use** (`Tools/cave-grader/package.json`):

- `npm run sync-research-pull` — cache + audit + Florida hillshades + catalog + execution brief  
- `npm run sync-research-catalog`, `audit-research-cache`, `research-maintenance-bot`  

Florida LiDAR / aquifer data inform **cave structure only** — not water simulation. See [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md).

---

## 7. Workflows — pre-build, build, post-build, terrain

### 7.1 Pre-build gate (before cave geometry)

**Rungs:** `compile_gate`, `package_tooling`, `scene_ground`, `prefab_catalog`, `ai_provider`, `research_manifest`, `scene_portal`, `prior_cave_state` — `CaveBuildPreBuildLadder.cs`

| Setting | My typical use |
|---------|----------------|
| `enforcePreBuildGate` | On — blocks **Build Complete Cave** until readiness ≥ ~88 |
| `preBuildReloopUntilPass` | Local fixes up to 12 attempts before calling AI |
| `autoInvokePreBuildWorkflow` | Off unless I want Cursor: research → plan → compile → ≤3 readiness rungs |

Env: `CAVE_WORKFLOW=pre_build` on `grade-and-fix.ts`.

### 7.2 Full build (deterministic core)

**122 steps** — `CaveBuildQueuedPipelineSchedule.Total`. Geo block = steps **1–15**. **Meat-loop entry = step 65** (not 63). See [PIPELINE_TRUTH.md](../../../../docs/PIPELINE_TRUTH.md).

Surface walk-in stack: `CaveUndergroundEntranceEnforcer`, `CaveEntranceVolumeBuilder`, `SurfaceTrailCaveMouthConnector`.

### 7.3 Post-build (grade + meat loop + optional AI)

| Stage | Behavior |
|-------|----------|
| In-editor meat loop | `CaveBuildQualityStageFixer` — procedural fixes, re-grade |
| Post-build Cursor | `CAVE_WORKFLOW=post_build` — research → compile_gate retries → up to **3** ladder rungs |
| `autoInvokeOnDud` | Cursor on dud builds |
| `autoInvokeEachMeatLoopPass` | I often enable — agent after each meat pass writes JSON |
| `enableAutonomousUntilShip` | Phase prompts until Ship or max iterations (default 8) |

**Meat loop ≠ one step.** It is a quality-driven repair cycle when `recommendedAction` is `RunMeatLoop`.

### 7.4 Terrain-only workflow

**Window → Environment Kit → Terrain Build Grader** / **Invoke Cursor Agent** — `CAVE_WORKFLOW=terrain`. Separate from cave queue; my surface report hit **B+** while cave was still **Blocked**.

---

## 8. Menus and Hub — quick map

| Entry | Purpose |
|-------|---------|
| **Hub** | Builds, provider keys, automation toggles, artifact preview (Data tab) |
| **Cave Build Grader** | Re-grade, export prompt, invoke agent |
| **Terrain Build Grader** | Surface ladder + terrain agent |
| **Cave Build → Sync API Key from .env** | Copy keys to EditorPrefs |
| **Cave Build → Invoke Cursor Agent (Grade & Fix)** | Headless `grade-and-fix.ts` |
| **Cave Build → Request Live Fix (Cursor)** | Play Mode issues → agent |
| **Cave Build → Run Pre-Build Gate Only** | Readiness without full cave geo |
| **Environment Kit Hub** | Live build monitor (primary); Pipeline Console optional pop-out |

There is **no** magic “offline AI” button in Hub — offline means **procedural preset** when no credentials are detected (`CaveBuildOutOfBoxPreset`).

---

## 9. Grading rubric (the instrument)

From [COMMERCIAL-PRODUCTION-GRADING.md](COMMERCIAL-PRODUCTION-GRADING.md):

| Tier | Score | Meaning |
|------|-------|---------|
| **Ship** | 95–100 | Release gate; critical stages ≥ 90 |
| **Beta** | 85–94 | Structured playtest |
| **Alpha** | 70–84 | Internal slice |
| **Prototype** | 50–69 | Demo / class inspection — **common on first full builds** |
| **Blocked** | &lt;50 or **dud** | Critical stage &lt; 65, hard failure |

`buildAcceptable` ≈ Beta+ (≥85). **`meetsShipTarget`** is stricter.

**My live example (cave dud):** `path:10`, `block_tunnel:35`, `geometry_integrity:5` — the report tells me **which stage to fix**, not “try again randomly.”

---

## 10. Comparison to industry (2026)

| Approach | This kit |
|----------|----------|
| Manual Unity Terrain | Automates **multi-tile** + contracts |
| Gaia / Gena | Adds **cave queue + JSON grading** |
| Houdini → Unity | Stays **in-editor**; smaller scope |
| UE5 PCG | Same **DAG** idea, different engine |
| “Just use ChatGPT” | No 122-step queue, no NavMesh probes, no rubric |

References: [Far Cry 5 procedural worlds](https://tools.engineer/gdc2018-procedural-world-generation-of-far-cry-5) · [Horizon GPU placement](https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn) · [Fly, Fail, Fix (arXiv)](https://arxiv.org/abs/2507.12666) — similar spirit to meat-loop repair.

---

## 11. Future direction (what I am working toward)

| Direction | Why today’s kit matters |
|-----------|-------------------------|
| `(seed, metrics)` datasets | Every build emits comparable JSON |
| CI batch runners | `CaveBuildBatchRunner` / nightly workflow skeleton |
| Agent benchmarks | Score **which rung** was fixed, not chat quality |
| Production automation (L4) | Enforce Ship on merge; narrow agent write policies |

---

## 12. Hands-on for classmates

### Minimal (match GitHub — basic)

1. Clone the repo · open in Unity 6 + URP  
2. Add a scene + Ground · assign prefabs in catalog  
3. Hub → **Build Complete Cave (122)**  
4. Read `CaveBuildQualityReport.json` — expect **Prototype** until you fix listed stages  

### Full (match my laptop — AI-assisted)

1. Everything above  
2. `npm install` + `.env` + `npm run doctor`  
3. `npm run sync-research-pull` (optional but close to my workflow)  
4. Hub → set provider → Sync API Key  
5. Re-grade → Export Agent Prompt → Invoke agent **on a branch**  
6. Compare `CaveBuildCursorLastRun.json` before/after  

---

## 13. Glossary

| Term | Meaning |
|------|---------|
| **HUB_ROOT** | Absolute path to Unity project root in `.env` |
| **Rung** | One ladder step (`visual_shell`, `navmesh`, …) |
| **122 steps** | Cave queue length |
| **Meat loop** | Quality-driven fix cycle |
| **Dud** | Hard failure — critical stage below floor |
| **Channel A/B/C** | Cursor SDK vs cloud HTTP vs local HTTP |
| **Generated/** | Local build telemetry (not on GitHub) |

---

## 14. What to tell someone in 30 seconds

> I open-sourced a Unity **R&D pipeline** that builds a surface + underground cave with **deterministic code** and **JSON grading**. A fresh clone gets the **basic** procedural build; **my** setup adds research cache, store art, scenes, and **Cursor-first** AI loops on a personal machine. Scores are often **Prototype or Blocked** until specific stages pass — that is honest. I am slowly moving toward **production automation**; this doc is the lesson in staged generation and measurement, not a finished auto-ship product.

---

## 15. Further reading

| Document | Topic |
|----------|--------|
| [README.md](../README.md) | Install, first build |
| [PUBLIC_REPO_SCOPE.md](../../../../docs/PUBLIC_REPO_SCOPE.md) | GitHub vs local-only |
| [CaveGradingAndCursor.md](CaveGradingAndCursor.md) | Cursor + providers in depth |
| [COMMERCIAL-PRODUCTION-GRADING.md](COMMERCIAL-PRODUCTION-GRADING.md) | Tier definitions |
| [PHASE_CONTRACTS.md](PHASE_CONTRACTS.md) | Invalidation rules |
| [PRODUCT_BOUNDARY.md](PRODUCT_BOUNDARY.md) | In / out of scope |

**License:** Educational/personal non-commercial — [LICENSE.md](../LICENSE.md). Commercial use requires separate permission from the copyright holder.

---

*Audit snapshot: package ~0.3.x, 66 Generated JSONs, 188 ResearchCache entries, surface B+ / cave Blocked on local MainScene. Constants and step counts change in code — verify `package.json`, `CaveBuildQueuedPipelineSchedule`, `SurfaceTerrainPropPlacementRegion.cs`.*
