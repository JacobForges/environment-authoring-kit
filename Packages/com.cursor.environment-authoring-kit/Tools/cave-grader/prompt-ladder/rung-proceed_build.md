# Pre-build phase: proceed_build

All weighted readiness rungs passed (≥92 each) and compile diagnostics report zero CS errors.

## Agent actions

- Confirm `CaveBuildCompileDiagnostics.json` has `errorCount: 0`.
- Confirm `CaveBuildPreBuildLadderReport.json` shows `buildAcceptable: true`.
- **Do not** generate cave meshes or edit scene objects in this workflow.
- **Do not** edit kit C# unless new compile errors appear.

## User actions (Unity)

1. **Window → Environment Kit → Remove Cave Layered Shells** (if re-building)
2. **Window → Environment Kit → Build Complete Cave Level**
3. Re-run post-build grading if needed.
