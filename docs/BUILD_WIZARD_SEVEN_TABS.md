# AI Build Wizard — seven-tab pipeline (spec)

Canonical plan for the browser wizard (`http://127.0.0.1:8766`) and lightweight Unity Hub integration.

## Goals

1. **Seven independent planners** — each tab owns one domain only.
2. **Optional process** — tabs are ordered left→right as a suggested pipeline; **no tab is required** and **order is free**.
3. **Isolated persistence** — per-tab session + memory; **no tab may overwrite or clear another tab’s work** unless the user explicitly resets that tab or runs a full build restart.
4. **Same UX per tab** — help intro → Q&A + AI Responder → checklist sidebar → approve brief → optional desktop export.
5. **Lightweight Unity Hub** — monitoring only (logs, usage graph, step counter, run/pause); planning lives in the wizard.
6. **Optional full auto-pipeline** — one button runs tabs **1→6** hands-free with AI Responder; tab 7 remains manual.

---

## Full auto-pipeline (tabs 1 → 6)

A single wizard control — e.g. **Run full pipeline (auto)** — on the shell (visible from any tab, or on tab 1):

### What it does

1. Starts at **tab 1 (Terrain)** with the tab’s default **AI Responder** preset (or a user-chosen pipeline preset before launch).
2. Runs that tab’s full cycle: Q&A auto-fill → checklist complete → **auto-approve brief** (or pause at approval if policy requires human gate — default: **fully automated** per user request).
3. Waits until tab 1 is **successfully complete** (`phase: finalized` + `gradePassed: true` or equivalent smoke/grader PASS).
4. **Automatically opens tab 2** and repeats with AI Responder — without clearing tab 1’s session.
5. Continues **2 → 3 → 4 → 5 → 6** the same way.
6. **Stops after tab 6** — tab 7 (Video) is **not** included in this run (manual / story-driven).

### Rules

| Rule | Detail |
|------|--------|
| **Sequential only** | Next tab does not start until the current tab reports **success** |
| **No cross-tab wipe** | Completed tabs keep their sessions and briefs on disk |
| **AI Responder per tab** | Each step uses that tab’s auto-respond loop, not one shared chat |
| **Failure stops pipeline** | If a tab fails (API error, grader fail, timeout), pipeline **pauses** on that tab with error + **Resume pipeline** / **Cancel pipeline** |
| **Cancel** | User can cancel mid-run; finished tabs stay done; current tab may be partial |
| **Optional human gates** | Settings flag `pipelineAutoApprove: true` (default on for this button) vs pause at each brief review |

### UI while running

- Global **pipeline status bar**: `Running tab 3 of 6 — Caves & dungeons…`
- Tab bar highlights active tab; completed tabs show ✓ chip
- **Cancel pipeline** button (does not reset completed tabs)

### Backend

```
POST /api/wizard/pipeline/start     # { presetProfile?, autoApprove?: true }
POST /api/wizard/pipeline/step      # internal tick — advance one auto-respond turn or handoff
POST /api/wizard/pipeline/cancel
GET  /api/wizard/pipeline/status    # { active, currentTab, completedTabs[], error? }
```

State file: `CaveBuildWizardPipelineRun.json` (run id, current tab index, status) — separate from per-tab sessions.

### Hub (optional mirror)

- **Run full planning pipeline** opens wizard and starts the same job (or deep-links with `?pipeline=auto`).
- Hub still does **not** run tab 7 automatically.

---

## Tab order (left → right)

| # | Hash | Tab | Job only |
|---|------|-----|----------|
| 1 | `#terrain` | **Terrain** | Surface tile scope, biome, terrain passes, surface trails, nav integration |
| 2 | `#surface-content` | **Surface content** | Surface/MainScene NPCs, monsters, items, quests, dialog, shops, blockers |
| 3 | `#caves` | **Caves & dungeons** | Cave/dungeon **structure** — routes, rooms, entrances, depth (not interior loot/NPCs) |
| 4 | `#mazes` | **Labyrinths & mazes** | Maze/labyrinth **topology** on terrain and links to entrances |
| 5 | `#interior-content` | **Interior content** | Props, enemies, NPCs, quests **inside** caves/dungeons/mazes; quest items; player/agent/special gear |
| 6 | `#atmosphere` | **Atmosphere & cinematics** | Water, VFX, sun/moon/sky, lighting, **cutscene trigger** definitions (not final video) |
| 7 | `#video` | **Video & cutscenes** | Cinematic/recap generator; story beats; ties to tab 6 triggers and tab 2/5 quests — **rightmost tab** |

**Redirects in every system prompt:** if the user asks for another domain, the planner says which tab to use — never absorbs that work.

---

## Per-tab UX (identical shell)

```
┌─────────────────────────────────────────────────────────────────┐
│ Tab bar (7) + scope banner                                       │
├──────────────────┬──────────────────────────────────────────────┤
│ CHECKLIST        │  Phase bar                                    │
│ (left sidebar)   │  Help intro (first visit / idle)              │
│                  │  Live chat (human or AI Responder)            │
│ Your decisions   │  Composer + AI Responder presets            │
│ progress bar     │  Approve / revise brief                     │
│ Up next          │  [After pass] Export plan to desktop (opt.)   │
└──────────────────┴──────────────────────────────────────────────┘
```

- **Checklist on the left** for all tabs (same component, tab-specific items).
- **Human chat** or **AI Responder** (preset kickoffs per tab).
- **Help intro** shown when the user opens a tab **before** starting Q&A — static README for that tab (scope, outputs, Unity apply step, what this tab does *not* do). Disappears or collapses once the user clicks **Start planning**.

---

## Session persistence & isolation

### One session file per tab (never shared)

| Tab | Session JSON | Brief JSON |
|-----|--------------|------------|
| 1 Terrain | `CaveBuildTerrainSession.json` | `CaveBuildTerrainBrief.json` |
| 2 Surface content | `CaveBuildSurfaceContentSession.json` | `CaveBuildSurfaceContentBrief.json` |
| 3 Caves | `CaveBuildCaveSession.json` | `CaveBuildCaveBrief.json` |
| 4 Mazes | `CaveBuildMazeSession.json` | `CaveBuildMazeBrief.json` |
| 5 Interior content | `CaveBuildInteriorContentSession.json` | `CaveBuildInteriorContentBrief.json` |
| 6 Atmosphere | `CaveBuildAtmosphereSession.json` | `CaveBuildAtmosphereBrief.json` |
| 7 Video | `CaveBuildVideoSession.json` | `CaveBuildVideoBrief.json` |

All under `Assets/EnvironmentKit/Generated/` (gitignored locally).

**Migration:** existing `CaveBuildPlannerSession.json` → tab 1; `CaveBuildContentLayoutSession.json` → tab 2 (rename paths when implementing).

### Registry manifest (optional index)

`CaveBuildWizardManifest.json` — lists each tab’s `phase`, `briefApproved`, `lastExportUtc`, `gradePassed` (no message bodies). Used by Hub for a one-line status row without loading all sessions.

### Reset rules

| Action | Effect |
|--------|--------|
| **Reset this tab** | Clears only that tab’s session + brief; other tabs untouched |
| **Restart build** (Hub) | Clears **all** tab sessions + briefs + paced build state — explicit confirm dialog |
| Tab switch | Load that tab’s session only; unmount other tab React trees (`key` per tab) |

### Prompting

- Each tab: dedicated `SYSTEM_QNA` in its own Python module (`terrain_planner.py`, `surface_content_planner.py`, …).
- **No shared chat history** across tabs.
- **Soft references only** in briefs (e.g. tab 5 may reference `questId` from tab 2 brief JSON on disk — read-only, never merge sessions).

### APIs (pattern)

```
GET  /api/{tab}/session
POST /api/{tab}/start
POST /api/{tab}/chat
POST /api/{tab}/resume
POST /api/{tab}/approve
POST /api/{tab}/reset
POST /api/{tab}/auto-respond/step
POST /api/{tab}/export          # after completion only
POST /api/wizard/restart-build  # all tabs — Hub only with confirm
POST /api/wizard/pipeline/start
POST /api/wizard/pipeline/step
POST /api/wizard/pipeline/cancel
GET  /api/wizard/pipeline/status
```

---

## Completion, grading, and desktop export

1. User finishes checklist → **Approve brief** → `phase: finalized`.
2. Unity **Apply** for that tab runs; grader/smoke test where defined.
3. When tab marks **`gradePassed: true`** (or smoke PASS in JSON):
   - Show optional **Export plan to desktop** — writes a bundle to user Desktop (or browser download):
     - `{TabName}-brief.json`
     - `{TabName}-session-transcript.md` (messages + checklist decisions)
     - `{TabName}-system-prompt-snapshot.txt` (for reuse in other tools)
   - Export is **never required**; idempotent; does not mutate session.

---

## Tab 7 — Video (AI Director)

- Embed today’s **AI Director** (`Tools/cave-grader/ai-director`, port 8767) as the **seventh** wizard tab (iframe or shared Vite bundle).
- Consumes triggers from tab 6 brief and quest/story IDs from tabs 2/5.
- Stays **last on the right**.

---

## Unity Hub — lightweight target

**Keep (monitoring):**

- Pipeline log (auto-scroll)
- Hardware usage mini-graph + RAM budget
- Step counter / ETC / phase line
- Run / pause / resume build controls
- Pin during build
- Settings + API keys (Settings tab)
- Generated JSON preview (Data tab)
- Grade panel when idle

**Simplify (Build tab):**

- Single primary action: **Open AI Build Wizard** (opens browser; optional `?tab=` hash).
- Compact **7-tab status chips** (idle / in progress / brief approved / passed) from `CaveBuildWizardManifest.json`.
- One **Apply** submenu or auto-detect which briefs are approved (not seven duplicate help boxes).
- Fold **Build Surface Only / Cave Only** into a small “Quick build” dropdown if still needed.

**Remove or relocate to wizard:**

- Long planner onboarding copy in Hub
- Duplicate “approved plan” prose that repeats wizard help
- Standalone “Open AI Director” if tab 7 covers it
- Obsolete monolithic “one planner for everything” wording

**Do not remove:** logs, usage graph, grader shortcuts, emergency unfreeze.

---

## Implementation status (2026-06-15)

| Phase | Status |
|-------|--------|
| A — 7-tab shell | **Done** |
| B — Tabs 3–6 backends | **Done** (generic planner) |
| C — Unity apply per brief | **Partial** (surface content only) |
| D — Video tab embed | **Done** (iframe → :8767) |
| E — Hub slim-down | **Partial** (wizard launcher row) |
| F — Pipeline auto 1→6 | **Done** (needs `CURSOR_API_KEY`) |


| Phase | Deliverable |
|-------|-------------|
| **A** | `WizardShell` — 7 tabs, hash routes, shared `PlannerTabApp` shell, help intros, left checklist layout |
| **B** | Split backends: migrate tab 1–2; stub 3–6 with checklists + prompts |
| **C** | Tab 5 cross-ref fields; tab 6 triggers; Unity apply authors per brief |
| **D** | Tab 7 Director embed; export-to-desktop API |
| **E** | Hub slim-down + manifest status row + full build restart |
| **F** | **Run full pipeline (auto)** — tabs 1→6 orchestration + status UI |

---

## Acceptance

- [ ] Open any tab alone — others’ sessions unchanged on disk
- [ ] Reset tab 3 — tabs 1, 2, 4–7 still have their messages and briefs
- [ ] Each tab’s AI refuses work outside its scope and names the correct tab
- [ ] First visit shows help intro; Start planning begins Q&A
- [ ] After graded pass, export bundle appears on Desktop (optional)
- [ ] Hub Build tab fits on one screen without planner essay text; log + graph still visible
- [ ] **Run full pipeline (auto)** completes tabs 1→6 sequentially via AI Responder; tab 7 untouched
- [ ] Pipeline failure pauses on failed tab; completed tabs retain sessions
- [ ] Cancel pipeline mid-run leaves finished tabs intact
