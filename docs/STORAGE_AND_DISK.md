# Storage, external drive, and disk pressure

**Read this if you see `IOException: Disk full`** on any path under `Assets/EnvironmentKit/Generated/` or `Hub/Library/`.

---

## Naming (repo vs product vs folder)

| Name | What it is |
|------|------------|
| **Environment Authoring Kit** (EnvKit) | The product — UPM package `com.cursor.environment-authoring-kit` |
| **environment-authoring-kit** | GitHub repository name (keep this — accurate and searchable) |
| **Hub** | This Unity 6 **reference workspace** — hosts the kit, playable demo (`Assets/Scripts/`), and bot tooling. Not the product name. |
| **Environment Kit Hub** | The editor window (**Window → Environment Kit → Hub**) — build monitor and settings |

Your local clone can live at `~/Hub`; the git remote should stay `environment-authoring-kit`.

---

## Why the Mac disk fills up

Unity stores **import caches** under `Hub/Library/` (Artifacts, PackageCache, Bee, ShaderCache, BurstCache, ArtifactDB). A FullWorld build also writes JSON checkpoints, quality reports, and optional demo captures.

On a 228 GB Mac SSD, **12–16 GB of Library cache on the internal drive** plus macOS leaves little headroom. Builds then fail on tiny JSON writes (`CaveBuildLadderCompletion.json`, `CaveBuildPacedStepCheckpoint.json`) even though those files are kilobytes.

---

## Recommended setup (external volume)

When a writable volume is mounted (e.g. **Lexar** at `/Volumes/Lexar`):

1. **Quit Unity completely** (not just Hub).
2. From the repo root:

```bash
cd ~/Hub   # or your clone path
HUB_ROOT="$PWD" bash Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/migrate-envkit-to-external.sh
```

3. Reopen the project from the **same** `Hub` folder.

The script:

- Symlinks `Assets/EnvironmentKit/Generated` → `/Volumes/<Drive>/EnvironmentKit-Hub/Generated`
- Symlinks `Assets/EnvironmentKit/ResearchCache` → external ResearchCache
- Moves `Library/Artifacts`, `Bee`, `PackageCache`, `ShaderCache`, `BurstCache`, `ArtifactDB` → `/Volumes/<Drive>/EnvironmentKit-Hub/UnityLibraryCache/`
- Sets `ENVIRONMENT_KIT_DATA_ROOT` for grader Python paths

**In Unity:** **Environment Kit → Storage → Move heavy data to external drive** runs the same script (with confirmation dialog).

**Keep the external drive mounted** during builds. If you unplug it, symlinks break and Unity may recreate local caches on the Mac disk.

---

## When Lexar (or external) is unplugged

Run the repair script so Unity can work offline (local fallbacks — Mac disk will grow again):

```bash
./Tools/repair-lexar-symlinks.sh
```

When the drive is back: quit Unity, re-run `migrate-envkit-to-external.sh`.

---

## Disk-full behavior in code (2026-06-09+)

Advisory JSON writes use `EnvironmentKitDataRoot.TryWriteAllText()`:

| File | On disk pressure |
|------|------------------|
| `CaveBuildPacedStepCheckpoint.json` | Checkpoints **pause** (one warning) |
| `CaveBuildLadderCompletion.json` | Write **skipped** (build continues; ladder cache may be stale until space returns) |

**Paced checkpoints** throttle via `EnvironmentKit_PacedStepJsonSaveInterval` (default: every 100 steps).

---

## Free space checklist

| Action | Typical savings |
|--------|-----------------|
| Run external migration (above) | **10–20 GB** off internal SSD |
| Empty Trash | varies |
| Clear `~/Library/Caches/unityhub-updater`, `com.unity3d.unityhub.ShipIt` | ~1 GB |
| Delete `Assets/EnvironmentKit/Generated/HealCheckpointDb` (if present, mid-heal only) | hundreds of MB |
| Hub → Stuck? → avoid duplicate Full AAA Rebuilds | prevents duplicate terrain/capture bloat |

Target **≥ 5 GB free** on the volume that holds `Generated/` (internal or external).

---

## Bot playbooks

- [DISK_FULL_PACED.md](../Tools/cursor-bot/playbooks/DISK_FULL_PACED.md)
- [EXTERNAL_STORAGE.md](../Tools/cursor-bot/playbooks/EXTERNAL_STORAGE.md)

---

## Policy

- **Never commit** `Generated/`, `ResearchCache/`, `Library/`, `.env`, or demo capture blobs.
- JSON on disk is truth for agents — read locally even when gitignored.
- After changing storage paths, update [PUBLIC_REPO_SCOPE.md](PUBLIC_REPO_SCOPE.md) only if committed vs local rules change.
