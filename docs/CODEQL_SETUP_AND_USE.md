# CodeQL — setup and use (verified on self-hosted Mac)

GitHub **code scanning** for this repo: security/static analysis in **Security → Code scanning alerts**. This is **not** the in-editor **cave-grader** (environment quality).

**Status (2026-05-29):** Workflow **CodeQL** on `main` completed successfully on a self-hosted Mac runner — Unity prep ~2½ min, full C# job ~16 min (first run includes one-time CodeQL bundle download).

---

## What runs where

| Job | Runner | What it scans |
|-----|--------|----------------|
| **Analyze (csharp — Unity)** | Your Mac (`self-hosted`) | Package C# with Unity + `dotnet build` (compiler-accurate) |
| **Analyze (javascript-typescript)** | GitHub cloud | `Tools/cave-grader` |
| **Analyze (actions)** | GitHub cloud | Workflow YAML |

Workflow file: [`.github/workflows/codeql.yml`](../.github/workflows/codeql.yml)  
Config (paths): [`.github/codeql/codeql-config.yml`](../.github/codeql/codeql-config.yml)

---

## One-time setup

### 1. GitHub — disable default CodeQL wizard

**Settings → Code security and analysis** → turn **off** “Code scanning default setup”.

Use **only** the workflow in `.github/workflows/codeql.yml`. Do **not** commit GitHub’s web “CodeQL Advanced” template (different cron, no Unity, conflicts with advanced setup).

### 2. Repository variable

**Settings → Secrets and variables → Actions → Variables**

| Name | Value |
|------|--------|
| `UNITY_PATH` | Full path to Unity binary, e.g. `/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity` |

No leading/trailing spaces or newlines. Match `ProjectSettings/ProjectVersion.txt`.

### 3. Self-hosted runner (Mac with Unity)

1. **Settings → Actions → Runners → New self-hosted runner** → macOS.
2. Run GitHub’s registration commands on that Mac.
3. Keep the runner online:

```bash
cd ~/actions-runner
./run.sh
```

**Security:** Workflows can execute code on this machine. Use branch protection and trusted collaborators on private repos.

### 4. .NET SDK

Install .NET 6+ (`dotnet --version`) on the same Mac as Unity and the runner.

### 5. Optional local smoke test

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.4.6f1/Unity.app/Contents/MacOS/Unity"
cd /path/to/Hub

Packages/com.cursor.environment-authoring-kit/Tools/run-codeql-unity-prep.sh
Packages/com.cursor.environment-authoring-kit/Tools/codeql-build-csharp.sh
```

Expect: `OK — kit project present` and a successful `dotnet build`.

---

## How to run scans

| Trigger | How |
|---------|-----|
| **Manual (recommended while developing)** | **Actions → CodeQL → Run workflow** → branch `main` |
| **Weekly** | **Sunday ~2:00 AM US Central** (cron `0 7 * * 0` UTC) — only if your Mac runner is online |
| **Push / PR** | **Off** — avoids tying up your self-hosted Mac on every commit |

While you are actively building in Unity, **ignore CodeQL** unless you want a security pass. Your **~1 hour cave build** is separate local testing; CodeQL does not replace that.

### Typical dev loop (Hub)

1. Work in **`~/Hub`** in Unity — build, grade, iterate (no GitHub required).
2. **Commit / push** when you want backup or to sync another machine — **CodeQL will not start** from a normal push.
3. Optional second clone (e.g. **Repo Test**): `git pull` there to verify the package — still no CodeQL unless you run the workflow manually.
4. Before a milestone or after large C# changes: **Actions → CodeQL → Run workflow** (runner must be online).

**Night runner (optional):** Saturday night leave `cd ~/actions-runner && ./run.sh` running (or start it before 2 AM Sunday Central) so the weekly job can run while you sleep; stop it Monday if you do not need the Mac for CI.

### Do not

- Re-run old jobs named **“CodeQL (Unity self-hosted)”** — use workflow **CodeQL** on latest `main`.
- Cancel the runner (`Ctrl+C` in `./run.sh`) mid-job.
- Commit the GitHub UI “setup CodeQL” template over `.github/workflows/codeql.yml`.

---

## What a successful C# job looks like

1. **Validate UNITY_PATH** — passes quickly  
2. **Unity — regenerate solution and compile scripts** — ~2–5 min after warm `Library`; longer on cold CI checkout  
3. **Initialize CodeQL** — first run may download the CodeQL bundle (~10–15 min); later runs use cache  
4. **Build C# for CodeQL tracing** — `dotnet build` on `EnvironmentAuthoringKit.*.csproj`  
5. **Perform CodeQL Analysis** — uploads SARIF to GitHub  

View alerts: **Security → Code scanning**.

Failed C# job: download artifact **codeql-unity-prep-log** or read `Logs/codeql-unity-prep.log` on the runner machine under `_work/environment-authoring-kit/...`.

---

## CodeQL vs cave-grader

| | CodeQL | cave-grader |
|--|--------|-------------|
| **Purpose** | Security vulnerabilities (C#, TS, Actions) | World quality, terrain/cave grades |
| **Runs on** | GitHub Actions | Local Node + optional Cursor SDK |
| **Needs Unity on CI** | Yes (C# job only) | No |
| **Results** | Security tab | `CaveBuildQualityReport.json`, Hub grader |

---

## Troubleshooting (short)

| Symptom | Fix |
|---------|-----|
| Job queued forever | Start `./run.sh` on the Mac runner |
| `UNITY_PATH` error | Fix repo variable; test binary is executable |
| Unity prep > 45 min then fail | Update to latest `main` (prep no longer forces a second full recompile) |
| SARIF / “default setup enabled” | Disable default CodeQL setup in repo settings |
| Cursor opened `environment-authoring-kit` under `_work/` | Harmless CI checkout; close window. Caused by Unity “Open C# Project” fallback when API sync fails |
| Stale work tree | `rm -rf ~/actions-runner/_work` then re-run workflow |

Deep install, legal checklist, zip layout: [CODEQL_SELFHOSTED_INSTALL.md](../Packages/com.cursor.environment-authoring-kit/docs/CODEQL_SELFHOSTED_INSTALL.md).

---

## Maintainer files

| Path | Role |
|------|------|
| `Editor/CodeQlUnityBootstrap.cs` | Batchmode: wait for initial import, sync `.csproj`, exit |
| `Tools/run-codeql-unity-prep.sh` | Invokes Unity prep |
| `Tools/codeql-build-csharp.sh` | `dotnet build` for CodeQL tracing |
| `Tools/run-codeql-local-verify.sh` | Local end-to-end check |
| `.github/CODEQL.md` | One-page maintainer note |
