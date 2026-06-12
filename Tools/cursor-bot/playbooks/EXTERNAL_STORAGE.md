# EXTERNAL_STORAGE

1. **`Generated/` and `ResearchCache/`** may symlink to `/Volumes/<Drive>/EnvironmentKit-Hub/` after migration.
2. **Unity `Library/` caches** (Artifacts, PackageCache, Bee, …) should live on external volume during FullWorld builds — see [docs/STORAGE_AND_DISK.md](../../docs/STORAGE_AND_DISK.md).
3. **JSON on disk is truth** — read locally even if git ignores `Generated/`.
4. **Never commit** `Generated/`, `ResearchCache/`, `Library/`, or `DemoCapture/` blobs.
5. **Lexar unplugged?** Run `Tools/repair-lexar-symlinks.sh` for local fallbacks; re-run migration when drive returns.
6. **Hub project root** in settings must match actual workspace path (`~/Hub` or clone folder).

Tag: `// [bot:rung:external_storage:DATE]`
