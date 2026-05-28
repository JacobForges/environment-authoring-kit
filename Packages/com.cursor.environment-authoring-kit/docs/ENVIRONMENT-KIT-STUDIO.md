# Environment Kit Studio — standalone preview & approve workflow

Product idea: a **standalone authoring experience** where users tune all kit settings, **watch the world build live in 3D**, then **approve or reject** before exporting a **portable world pack** (prefab + manifest) into their real game project.

This is **not** a second build pipeline. It is the **same** `com.cursor.environment-authoring-kit` pipeline (120-step cave queue, surface ladder, graders) running inside a **dedicated Unity project** with a **minimal editor shell** and an explicit **approval gate** before export.

---

## Why this is differentiated

| Typical terrain / cave tools | Environment Kit Studio |
|------------------------------|-------------------------|
| Editor-only, full Unity chrome | Purpose-built “studio” project + optional Hub launcher |
| Export heightmaps or meshes only | Live Unity scene (terrain, cave, props, NavMesh) while building |
| No human gate before import | **Approve / Reject / Regenerate** before anything ships to the game repo |
| Opaque batch jobs | Same graded pipeline you already have (JSON reports, letter grade) |

You are not competing with “another procedural generator.” You are competing with **workflow**: *generate → see it for real → sign off → drop into production.*

---

## What “mini Unity editor” actually means (honest)

You **cannot** embed or reimplement the Unity Editor inside a random desktop app without Unity’s runtime/editor license and binaries. Building a custom OpenGL viewport would **not** show your real URP terrain, cave meshes, or NavMesh.

**Practical definition (what users will accept):**

1. A **thin Hub shell** (settings, recipes, API keys, “Start build”) — optional; can be Tauri, Electron, or a small CLI.
2. A **Studio Unity project** opened in Unity 6 with **only** the panels you need:
   - Scene / Game view (live watch)
   - Slim settings inspector (kit + recipe + seed)
   - Build progress (120/120)
   - **Approve · Reject · Regenerate**
3. On **Approve**, export a **World Pack** the main project imports.

Users experience this as “the Hub app,” but the 3D view **is** Unity — stripped down, not reinvented.

```
┌─────────────────────────────────────────────────────────────┐
│  Hub Launcher (optional)     Settings · Recipes · API key   │
│  [ Open Studio ]  [ Headless batch ]                        │
└──────────────────────────┬──────────────────────────────────┘
                           │ spawns / focuses
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Unity 6 — "Environment Kit Studio" project                 │
│  ┌─────────────────────┐  ┌──────────────────────────────┐  │
│  │ Scene / Game (live) │  │ Studio panel                 │  │
│  │                     │  │ Seed, scope, presets         │  │
│  │  terrain + cave     │  │ Build 47/120 …               │  │
│  │  updating in place  │  │ Grade B+ (preview)           │  │
│  │                     │  │ [Approve] [Reject] [Rebuild] │  │
│  └─────────────────────┘  └──────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │ Approve
                           ▼
              Assets/EnvironmentKit/Exports/WorldPack_<seed>/
                · WorldRoot.prefab
                · manifest.json (seed, settings, kit version)
                · optional .unitypackage
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Your game Unity project — drag prefab or import package    │
│  Tune gameplay, lighting overrides, test Play Mode          │
└─────────────────────────────────────────────────────────────┘
```

---

## Approval gate (core product feature)

**When to pause**

- **Option A (MVP):** Pause once at pipeline end (step 120/120) + quality report ready.
- **Option B (stronger):** Pause at phase boundaries (surface done, cave geo done, post-meat) — matches `CaveBuildQueuedPipelineSchedule` blocks.

**What the user sees**

- Live scene (already building — no separate “preview pipeline”).
- Letter grade + link to `CaveBuildQualityReport.json` / failing stages.
- Optional orbit camera preset at cave mouth.

**Actions**

| Action | Behavior |
|--------|----------|
| **Approve** | Run `WorldPackExporter` → write prefab + manifest under `Exports/` |
| **Reject** | Discard or archive generated roots; do not export |
| **Regenerate** | New seed or same seed + invalidate ladder; rebuild |

Pipeline integration: add `CaveBuildApprovalGate` that sets `EditorApplication.isPaused` or stops scheduling the next queued step until `StudioSessionState.approved == true`. No duplicate geometry code.

---

## World Pack export (what “prefab” means)

Export **roots**, not the whole project:

- `GeneratedSurfaceWorld` (or `EnvironmentRoot` subtree)
- `UndergroundCaveSystem` / cave geometry root
- Optional: baked NavMesh data reference, TerrainData assets as sub-assets or linked copies

**Deliverables**

```
Assets/EnvironmentKit/Exports/WorldPack_4242/
  WorldRoot.prefab              # parent with surface + cave children
  WorldPackManifest.json        # seed, scope, recipe id, kit version, bounds
  README.txt                    # import steps for game project
```

**Game project import**

1. Copy folder or import `.unitypackage`.
2. Place `WorldRoot` in scene; re-link gameplay spawns if paths differ.
3. Override materials/lighting in the main editor as today.

Prefab variant / addressables is a phase-2 enhancement.

---

## Hub Launcher (optional standalone shell)

Responsibilities **only**:

- Edit settings mirrored to `CaveBuildCursorSettings` + recipes JSON (read/write Hub path).
- `Open Studio` → `unity -projectPath <StudioProject>` (or `open` on macOS).
- Show last build status from `CaveBuildLiveRunStatus.md` (already exported).
- Optional: headless `RunShowcaseHeadless` for CI without UI.

**Not** responsible for: mesh generation, terrain sculpting, or a custom 3D engine.

---

## Suggested phases

### Phase 0 — Studio project template (1–2 weeks)

- New repo or `Hub/EnvironmentKitStudio/` Unity project with URP + kit package only.
- Single scene `Studio.unity`: Ground anchor, PortalFive, 9-tile setup.
- Menu: **Environment Kit → Studio → Open Studio Window**.

### Phase 1 — Approval gate (1 week)

- `CaveBuildApprovalGate` hooks end-of-queue (and optional phase boundaries).
- Studio window: progress, grade, Approve / Reject / Regenerate.
- No export yet — approve only marks session “cleared to export.”

### Phase 2 — World Pack exporter (1–2 weeks)

- `WorldPackExporter` builds prefab + manifest.
- Validation: playable cave contract + nine-tile vegetation flag.

### Phase 3 — Hub Launcher (optional)

- Small desktop or CLI that opens Studio + edits `.env` / settings JSON.

### Phase 4 — Polish

- Side-by-side before/after seed comparison, thumbnail capture for marketing, commercial license flow in launcher.

---

## What stays in the main game project

- Gameplay, quests, combat tuning, addressables, production lighting.
- Kit is **authoring**; game project is **shipping**.

---

## Risks

| Risk | Mitigation |
|------|------------|
| Users expect a non-Unity app with Unity quality | Brand it “Studio (Unity-powered preview)” |
| Two Unity projects to maintain | Studio template versioned with kit `package.json` |
| Prefab breaks on import (paths, layers) | Manifest documents required layers/tags; export validation |
| Long builds still feel slow | Keep phased pauses + live scene updates (already paced queue) |

---

## Next implementation step

Add in the **package** (not a fork of the pipeline):

1. `Editor/Studio/EnvironmentKitStudioWindow.cs` — UI shell.
2. `Editor/Studio/CaveBuildApprovalGate.cs` — pause + resume queued steps.
3. `Editor/Studio/WorldPackExporter.cs` — prefab + manifest on approve.

Keep **one** pipeline; Studio is a **mode** + **export** layer.
