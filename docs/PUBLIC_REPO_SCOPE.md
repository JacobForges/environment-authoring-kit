# Public repository scope (read this first)

**Repository:** [github.com/JacobForges/environment-authoring-kit](https://github.com/JacobForges/environment-authoring-kit)

This file is the **accuracy contract** for what is on GitHub vs what you must supply locally. Other docs should match this.

---

## What this repo is

- **Environment Authoring Kit** — Unity 6 editor package (`Packages/com.cursor.environment-authoring-kit`)
- **Project skeleton** — `ProjectSettings/`, UPM `manifest.json`, committed **`Assets/EnvironmentKit/`** presets & recipes
- **Documentation** — requirements, pipeline design, grader prompts
- **Positioning** — AI-assisted **procedural base framework** for terrain + caves (you refine the final world)

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
| `Assets/EnvironmentKit/Generated/` | **No** (gitignored) | Build reports, prompts, live status — regenerated per machine |
| `Assets/EnvironmentKit/ResearchCache/` | **No** (gitignored) | Run `npm run sync-research-pull` locally; see attribution doc |
| `Assets/*.unity` scenes | **No** | Create scenes in your clone; menu **Rebuild Complete Cave (MainScene)** only works if you add `MainScene.unity` |
| Other `Assets/*` (store packs, props, meshes) | **No** | Your licenses; assign paths in kit catalog / scatter settings |
| `.env`, API keys | **No** | `Tools/cave-grader/.env.example` only |
| VITURE / GlassesGateway natives | **No** | Install per vendor docs in your project if needed |

---

## Pipeline facts (current code)

| Topic | Accurate statement |
|-------|-------------------|
| Queued cave build | **120** paced steps — `CaveBuildQueuedPipelineSchedule.Total` — UI shows **Build X/120** |
| Step index **63** | **Meat loop** entry index only — not “63-step pipeline” |
| FullWorld order | Surface (9-tile terrain, vegetation, terrain ladder) **then** cave queue |
| Default recipe | `Assets/EnvironmentKit/Recipes/aaa-full-cave-production.json` |
| Package version | **0.3.0** (`package.json`) |

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
| [`LICENSE.md`](../Packages/com.cursor.environment-authoring-kit/LICENSE.md) | **Educational / personal non-commercial — free.** **Commercial use (monetary gain, public sale, paid products built on the kit) requires a separate license or purchase from the copyright holder (Jacob).** |
| Root [`LICENSE`](../LICENSE) | Points to the same `LICENSE.md` |

**Not public domain. Not CC0.** Third-party Unity, npm, Cursor, store assets, and government geodata stay under their own terms — [THIRD_PARTY_AND_LICENSE_SCOPE.md](THIRD_PARTY_AND_LICENSE_SCOPE.md).

---

## After clone (minimum)

See **[README — Required before your first build](../README.md#required-before-your-first-build-read-this-after-clone)** for the full checklist.

1. Unity Hub → **Unity 6000.x** → open repo root  
2. Wait for UPM to resolve packages  
3. Scene with **`Ground`** + **`PortalFive`**  
4. **Licensed cave prefabs** under `Assets/` (default path `Assets/BillemotdonggulLavaTubePack/Prefabs/` — not in git)  
5. `cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader && npm install`  
6. Preflight **PASS** (read `CaveBuildPreflightReport.md` if blocked)  
7. **Hub** → **Build Complete Cave Level**

Optional: `.env` from `.env.example` for Cursor/LLM automation only.
