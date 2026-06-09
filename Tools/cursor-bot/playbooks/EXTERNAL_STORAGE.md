# EXTERNAL_STORAGE

1. `Assets/EnvironmentKit/Generated/` may symlink to `/Volumes/Lexar/EnvironmentKit-Hub`.
2. JSON on disk is truth — read locally even if git ignores Generated/.
3. Never commit Generated/, ResearchCache/, or DemoCapture/ blobs.
4. Hub project root in settings should match actual workspace path.

Tag: `// [bot:rung:external_storage:DATE]`
