# Ladder rung: compile_gate (mandatory — after research + plan)

**Goal:** Zero C# compile errors in `Packages/com.cursor.environment-authoring-kit/` before any cave scene / mesh / layout edits.

## Pre-build (Unity workflow)

- If `CaveBuildCompileDiagnostics.json` has **errorCount: 0** → last line must be `[CaveCursor:phase-complete] workflow=pre_build rung=compile_gate reason=no_errors` and **stop** (no file edits).
- `currentPhase` in workflow JSON must match this rung (`compile_gate`). Do not execute `readiness_ladder` tasks here.

## Rules (only when errorCount &gt; 0)

1. Read `CaveBuildCompileDiagnostics.json` — fix every listed error.
2. Use your **research plan** from the previous phase — each fix must reference a plan step in a one-line comment.
3. **Forbidden in this pass:** changing cave generation logic, meshes, materials, or scene objects unless required to fix a compile error.
4. After edits, assume Unity recompiles — if errors would remain, keep fixing.

## Checklist

- [ ] All CS errors in diagnostics resolved (or errorCount already 0 → `PREBUILD_COMPILE_CLEAN`).
- [ ] No new `HashSet`/LINQ issues — prefer `CopyTo`, explicit loops (Unity editor may lack LINQ extensions).

When done, Unity advances to **readiness_ladder** (package_tooling, scene_ground, …) in a **new** agent pass.
