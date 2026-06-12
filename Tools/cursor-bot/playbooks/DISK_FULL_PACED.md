# DISK_FULL_PACED

1. **`IOException: Disk full`** on `CaveBuildPacedStepCheckpoint.json`, `CaveBuildLadderCompletion.json`, or any `Generated/*.json` — Mac SSD is full; stop the build.
2. **Free space first** — target ≥ 5 GB on the volume holding `Generated/` (internal or Lexar).
3. **Migrate heavy data** — quit Unity, run `migrate-envkit-to-external.sh` or **Environment Kit → Storage → Move heavy data to external drive**. See [docs/STORAGE_AND_DISK.md](../../docs/STORAGE_AND_DISK.md).
4. **Advisory writes** — `EnvironmentKitDataRoot.TryWriteAllText` skips ladder/checkpoint JSON with one warning (build may continue; ladder cache stale until space returns).
5. **Throttle checkpoints** — `EnvironmentKit_PacedStepJsonSaveInterval` (default 100 steps); disable paced scene save if needed.
6. **Clear stale checkpoint** after recovery — delete `CaveBuildPacedStepCheckpoint.json` if from a crashed run.
7. **Planner approve** — run `ClearForNewBuild` only after disk headroom is restored.

Tag: `// [bot:rung:disk_full:DATE]`
