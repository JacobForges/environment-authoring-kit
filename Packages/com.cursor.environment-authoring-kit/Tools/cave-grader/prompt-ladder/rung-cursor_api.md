# Pre-build rung: cursor_api

**Goal:** `CURSOR_API_KEY` available for automated pre/post-build workflows.

## Fix

- [ ] Create `Tools/cave-grader/.env` from `.env.example` with `CURSOR_API_KEY=`.
- [ ] Unity: **Window → Environment Kit → Cave Build → Sync API Key from .env**.
- [ ] Optional: set key on `CaveBuildCursorSettings` asset.

Non-blocking for manual builds; required for auto-invoke workflows.
