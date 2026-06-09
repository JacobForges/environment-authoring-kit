# STALE_CHECKPOINT

1. Check `CaveBuildPacedStepCheckpoint.json` — reject if current step > 2× planned budget.
2. On planner approve: `CaveBuildPersistedSessionReset.ClearForNewBuild("planner fresh build")`.
3. Delete stale checkpoint file; reset Hub step counter to 0 / planned total from session.
4. Never resume 8000+ step runs from broken terrain meat loops.

Tag: `// [bot:rung:stale_checkpoint:DATE]`
