# DISK_FULL_PACED

1. `IOException: Disk full` on `CaveBuildPacedStepCheckpoint.json` — stop paced writes immediately.
2. Migrate heavy data to Lexar via Hub Storage / `EnvironmentKitDataRoot`.
3. Raise `JsonSaveIntervalSteps` in paced checkpoint writer; clear stale checkpoint.
4. `ClearForNewBuild` on planner approve after freeing disk space.

Tag: `// [bot:rung:disk_full:DATE]`
