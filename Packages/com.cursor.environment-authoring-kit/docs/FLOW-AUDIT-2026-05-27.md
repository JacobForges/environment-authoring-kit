# Flow audit — 2026-05-27

This audit reviews Hub/pipeline consistency after the 120-step migration and Hub/provider work.

## Scope checked

- Build entry points (Hub + menu)
- Run status and generated artifact paths
- Provider key/model settings vs runtime execution path
- Messaging consistency (120-step wording, provider hints)

## Mismatches found and fixed

1. **Outdated 63-step docs**
   - `docs/AAA-PROCEDURAL-CAVE-PIPELINE.md`
   - `docs/SURFACE-WORLD-BUILD.md`
   - Updated to 120-step flow labels.

2. **Provider confusion in UI warnings**
   - Several windows previously hardcoded `CURSOR_API_KEY not set`.
   - Updated key warnings to use a centralized hint:
     `CaveBuildCursorSettings.CursorWorkflowCredentialHint()`.

3. **Hub/provider configuration visibility**
   - Hub now shows provider selection, per-provider keys, base URL, and model IDs in one place.
   - Added flow audit panel in Hub Data tab for immediate mismatch warnings.

4. **Process environment mismatch risk**
   - Process bootstrap now exports provider routing vars:
     - `CAVE_AI_PROVIDER`
     - `CAVE_ACTIVE_MODEL`
     - `CAVE_ACTIVE_BASE_URL`
     - `CAVE_ACTIVE_API_KEY`
     - provider-specific key vars (`GOOGLE_API_KEY`, `ANTHROPIC_API_KEY`, etc.)

5. **.env discoverability**
   - `.env.example` now includes optional provider key placeholders and provider/base-url notes.

## Current intentional limitation (important)

The built-in `Tools/cave-grader/grade-and-fix.ts` runtime still executes through **Cursor SDK** and requires `CURSOR_API_KEY` for automation.

Provider toggles/keys in Hub are fully configurable and exported for external routing, but non-Cursor providers are currently treated as:

- **configuration-ready**, not fully runtime-switched inside `grade-and-fix.ts`.

Hub Flow Audit warns when this mismatch is present (e.g., non-Cursor provider selected while Cursor automation toggles are enabled).

## Recommended next hardening step

If true runtime provider switching is required, introduce a provider abstraction in `Tools/cave-grader`:

- `cursor-runner.ts` (existing)
- `openai-compatible-runner.ts` (for OpenAI-compatible, OpenRouter, local endpoints)
- `anthropic-runner.ts`
- `gemini-runner.ts`

and route from `grade-and-fix.ts` using `CAVE_AI_PROVIDER`.
