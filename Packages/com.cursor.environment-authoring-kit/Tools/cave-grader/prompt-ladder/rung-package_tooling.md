# Pre-build rung: package_tooling

**Goal:** `Tools/cave-grader` is installable and research seed exists.

## Fix (kit repo only)

- [ ] `research-catalog.seed.json` present — run `npm run sync-research-catalog` in `Tools/cave-grader` if missing.
- [ ] `grade-and-fix.ts` and `node_modules` — `npm install` in `Tools/cave-grader`.
- [ ] No broken package paths under `Packages/com.cursor.environment-authoring-kit/`.

If checks already pass (score 92+), make **no** edits.

## Phase complete (required)

Last line of your response must be exactly:

`[CaveCursor:phase-complete] workflow=pre_build rung=package_tooling reason=done`

Do not ask the user to re-grade in Unity — Unity advances automatically.
