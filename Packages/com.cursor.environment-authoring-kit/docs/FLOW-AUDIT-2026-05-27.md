# Flow audit — 2026-05-27 (updated 2026-05-28)

> **Archive:** Historical audit snapshot. For current pipeline numbers use [PIPELINE_TRUTH.md](../../../../docs/PIPELINE_TRUTH.md) and [FULLWORLD_TERRAIN_AND_HUB.md](FULLWORLD_TERRAIN_AND_HUB.md).

Reviews Hub/pipeline consistency after the queued-pipeline migration and Hub/provider work. **Current queue:** 122 steps (`CaveBuildQueuedPipelineSchedule.Total`). **FullWorld terrain:** ~289 tiles default (81-tile core).

## Scope checked

- Build entry points (Hub + menu)
- Run status and generated artifact paths
- Provider key/model settings vs **`grade-and-fix.ts` runtime**
- Messaging consistency (122-step wording, provider hints, license docs)

## Resolved

1. **Stale step-count docs** — pipeline total = **122**; geo **1–15**; meat loop entry = step **65** (not 63).

2. **Provider UI warnings** — centralized `CaveBuildCursorSettings.CursorWorkflowCredentialHint()`.

3. **Hub provider settings** — selection, keys, base URL, model IDs; flow audit panel on Data tab.

4. **Process env export** — `CAVE_AI_PROVIDER`, `CAVE_ACTIVE_MODEL`, `CAVE_ACTIVE_BASE_URL`, `CAVE_ACTIVE_API_KEY`, provider-specific keys.

5. **Non-Cursor runtime (was mis-documented)** — **`grade-and-fix.ts` routes non-Cursor providers** to HTTP APIs (`invokeExternalProvider`: Anthropic, Gemini, OpenAI-compatible / OpenRouter / custom base URL). Only **Cursor** uses `@cursor/sdk` and **`CURSOR_API_KEY`**. Hub exports `CAVE_AI_PROVIDER` from your selection.

6. **License docs (was mis-documented)** — kit is **not CC0**. Single terms: [LICENSE.md](../LICENSE.md) — educational/personal non-commercial free; **commercial use requires separate license or purchase from copyright holder**. [THIRD_PARTY_AND_LICENSE_SCOPE.md](../../../../docs/THIRD_PARTY_AND_LICENSE_SCOPE.md) updated to match.

## Current limitations (accurate)

| Topic | Limitation |
|-------|------------|
| **Cursor provider** | Needs `CURSOR_API_KEY`; local agent needs Cursor app; cloud needs `CAVE_CURSOR_REPO_URL` |
| **Non-Cursor providers** | Need `CAVE_ACTIVE_API_KEY` (or provider env var); file edits via JSON execution layer only (not Cursor agent workspace) |
| **External edits** | Opt-in (`CAVE_EXTERNAL_APPLY_EDITS`); dry-run default |
| **In-editor build** | Does not require any LLM API |

## Optional hardening (not required for accuracy)

- Stricter validation when non-Cursor provider is selected but API key is empty
- Split grader runners into separate modules for easier testing
