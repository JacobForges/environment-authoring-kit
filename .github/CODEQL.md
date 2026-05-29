# Code scanning (CodeQL) — Hub repo

## Quick start (honest full C# scan)

1. Copy `Packages/com.cursor.environment-authoring-kit/Tools/env.codeql.example` → **Hub root** `.env.codeql` and set `UNITY_PATH`.
2. Run locally:

```bash
Packages/com.cursor.environment-authoring-kit/Tools/run-codeql-local-verify.sh
```

3. On GitHub: **disable Code scanning default setup** (it autobuilds without Unity and fails).
4. Register a **self-hosted runner** on the Mac that has Unity (Settings → Actions → Runners).
5. Set repo variable **`UNITY_PATH`** (same path as `.env.codeql`).
6. Push; run **Actions → CodeQL (Unity self-hosted)**.

Full guide: `Packages/com.cursor.environment-authoring-kit/docs/CODEQL_SELFHOSTED_INSTALL.md`

## Workflows

| File | Where it runs | Purpose |
|------|----------------|---------|
| `codeql-unity-selfhosted.yml` | **Your Mac** (`self-hosted`) | Unity + compile + full C# CodeQL |
| `codeql.yml` | GitHub cloud | Fallback `build-mode: none` (less complete) |

**Security alerts** ≠ cave/terrain **quality grade** (`Tools/cave-grader`).
