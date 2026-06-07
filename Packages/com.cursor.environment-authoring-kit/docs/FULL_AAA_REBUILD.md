# Full AAA Rebuild (Hub)

Use **Full AAA Rebuild** or **Full AAA Rebuild + Recording** when a normal build stopped mid-way, seams are bad, or prompts were stale during a long run.

## What “real AAA” means here

| Expectation | Behavior |
|-------------|----------|
| Per-phase prompts | Each pipeline phase runs `generate-phase-prompts` + research bundle (not disk-cache-only while building). Active file: `Assets/EnvironmentKit/Generated/CaveBuildActivePhasePrompt.md`. |
| Non-additive surface | Replaces `GeneratedSurfaceWorld` / terrain instead of extending stale land. |
| Layout gate | `WorldLayoutAudit.json` must pass (or `CAVE_LAYOUT_PLAN_FORCE=1` for headless only). |
| Labyrinth | Carved on **south 3×2 annex** only — seed-random DFS maze spines, **no** hub-and-spoke star overlay. |
| Props | Full surface pass during terrain AI phases (before cave); cave-only props in meat loop after cave; surface **locked** when cave geometry starts. |
| Hollow Titan | One random above-ground landmark per seed; never on mouths, labyrinth, or trails. |

## Editor safeguards (0.3.4+)

- Queue coalescing: at most **12** pending actions per identical queue label during long builds.
- Stall warning when depth ≥ **384** and no progress for ~**7 minutes**.

## Recording

**Full AAA Rebuild + Recording** enables the demo recorder preset (paced terrain, smaller heightmaps on 16 GB Macs). Keep Unity awake; prefer Game view for capture after a clean build.

## Normal builds

**Build Complete Cave** may still use incremental/additive FullWorld when prior geometry is valid. Use Full AAA Rebuild when you need a clean slate.
