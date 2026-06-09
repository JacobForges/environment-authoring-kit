# LAYOUT_AUDIT_SEAMS

1. Read `CaveBuildLayoutAudit.json` — `blocksSurfaceContinue` or seam gap > 0.5m.
2. Planner fresh build: call `CaveBuildPersistedSessionReset.ClearForNewBuild` then purge broken terrain.
3. Restitch play disk tiles before cave queue when `use3DCaveSystem: false` skip is not enough.
4. Do not block fast demos on cave queue when planner disabled caves.

Tag: `// [bot:rung:layout_audit:DATE]`
