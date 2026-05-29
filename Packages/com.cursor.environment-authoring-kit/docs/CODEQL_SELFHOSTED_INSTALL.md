# CodeQL self-hosted install (Unity + full C# build)

Complete guide for running **GitHub Code scanning (CodeQL)** on the Environment Authoring Kit Hub repo when you want **compiler-accurate C# analysis** instead of cloud `build-mode: none`.

---

## What this kit does (and does not do)

| Does | Does not |
|------|----------|
| Security/static analysis (CodeQL alerts in **Security → Code scanning**) | Replace **cave-grader** / terrain quality ladders |
| Regenerate `Hub.sln` via Unity batchmode | Run Unity on GitHub-hosted `ubuntu-latest` |
| Build with `dotnet`/`msbuild` so CodeQL traces real compilation | Grant a commercial license (see **License** below) |
| Scan package C# + `Tools/cave-grader` TypeScript (cloud job) | Analyze Unity Package Cache or `Library/` (gitignored) |

---

## License (your repo / this kit)

Files in this zip are part of **Environment Authoring Kit** and the **Hub** repository. They are **not** a separate product license.

- **Your code in this repo:** see `LICENSE` at Hub root and `Packages/com.cursor.environment-authoring-kit/LICENSE.md`.
- **Educational / personal non-commercial use:** allowed under that license.
- **Commercial use:** requires written permission or a commercial license from the copyright holder (Jacob Adkins), as stated in those files.
- **This zip does not change those terms** — it only adds CI/scripts under the same project.

You may commit and use these workflows in **your** Hub repo without extra permission beyond your existing repo license.

---

## Third-party services and software (legal / ToS checklist)

You are responsible for complying with each vendor’s terms when you enable CI.

| Component | Who provides it | What you need |
|-----------|-----------------|---------------|
| **GitHub Actions** | GitHub | Repo on GitHub; Actions enabled; [GitHub Terms](https://docs.github.com/en/site-policy/github-terms/github-terms-of-service) |
| **CodeQL / code scanning** | GitHub | Available on eligible repos (public, or private with Code Security); [CodeQL license](https://github.com/github/codeql-cli-binaries/blob/main/LICENSE) (OSS for security research); [About CodeQL](https://docs.github.com/en/code-security/code-scanning/introduction-to-code-scanning/about-code-scanning-with-codeql) |
| **Self-hosted runner** | You install on **your** machine | Runner process runs with access to your disk; [Self-hosted runner security](https://docs.github.com/en/actions/hosting-your-own-runners/managing-self-hosted-runners/about-self-hosted-runners#self-hosted-runner-security) — use only on machines you trust |
| **Unity Editor** | Unity Technologies | Valid Unity license for your use; [Unity Terms](https://unity.com/legal); batchmode on your Mac is under your existing Unity agreement |
| **.NET SDK** | Microsoft | [MIT license](https://github.com/dotnet/core/blob/main/license.txt) typical for SDK |
| **Cursor / cave-grader SDK** (if used locally) | Cursor | Separate from CodeQL; not required for CodeQL workflows |

**No additional copyright notice is required in the zip** beyond your repo’s existing `LICENSE.md`, but you **must** comply with GitHub and Unity terms when running CI.

**Privacy:** CodeQL on GitHub uploads analysis metadata to GitHub’s code scanning service. Self-hosted runners still send results to GitHub for the Security tab. Do not put secrets in workflow logs.

---

## Prerequisites checklist

Before installing, confirm:

- [ ] Hub repo cloned; package path `Packages/com.cursor.environment-authoring-kit/` exists
- [ ] Unity Hub + one Editor version installed (same version you use for the project)
- [ ] GitHub repo admin (to add runners, variables, disable default CodeQL)
- [ ] Mac or PC that can stay online for scheduled scans (or use `workflow_dispatch` only)
- [ ] .NET SDK 6+ (`dotnet --version`) or MSBuild
- [ ] ~10–30 GB free disk (Unity Library folder grows on first batchmode run)

---

## Part 1 — Install files from zip

1. Download / locate `codeql-unity-selfhosted-kit.zip`.
2. **Unzip at Hub repo root** (folder that contains `Assets/`, `Packages/`, `ProjectSettings/`).
3. Merge when prompted; structure should look like:

```
Hub/
  .github/workflows/codeql-unity-selfhosted.yml
  .github/workflows/codeql.yml          # optional cloud fallback
  .github/codeql/codeql-config.yml
  Packages/com.cursor.environment-authoring-kit/
    Editor/CodeQlUnityBootstrap.cs
    Tools/run-codeql-unity-prep.sh
    Tools/codeql-build-csharp.sh
    docs/CODEQL_SELFHOSTED_INSTALL.md   # this file
```

4. Make scripts executable (macOS/Linux):

```bash
chmod +x Packages/com.cursor.environment-authoring-kit/Tools/run-codeql-unity-prep.sh
chmod +x Packages/com.cursor.environment-authoring-kit/Tools/codeql-build-csharp.sh
```

5. Open the Hub project once in Unity Editor (optional but helps first-time Library generation).

6. Commit and push:

```bash
git add .github Packages/com.cursor.environment-authoring-kit
git commit -m "Add CodeQL self-hosted Unity workflow"
git push origin main
```

---

## Part 2 — GitHub repository settings

### A. Turn off conflicting default CodeQL

1. Repo → **Settings** → **Code security and analysis** (or **Security**).
2. Under **Code scanning**, if **Default setup** is enabled and failing on Unity → switch to **Advanced setup** or disable default.
3. You want workflows in `.github/workflows/` to run, not hidden autobuild on cloud runners only.

### B. Repository variable `UNITY_PATH`

1. **Settings** → **Secrets and variables** → **Actions** → **Variables** tab.
2. **New repository variable**
   - Name: `UNITY_PATH`
   - Value: full path to Unity binary, e.g.  
     `/Applications/Unity/Hub/Editor/6000.0.46f1/Unity.app/Contents/MacOS/Unity`
3. Find path: Unity Hub → Installed Editors → gear → **Show in Finder** → right-click Unity.app → **Show Package Contents** → `Contents/MacOS/Unity`.

### C. Enable GitHub Actions (if needed)

**Settings** → **Actions** → **General** → allow actions for this repository.

---

## Part 3 — Self-hosted runner (macOS)

CodeQL Unity job uses `runs-on: self-hosted`. GitHub cloud machines cannot run Unity.

1. Repo → **Settings** → **Actions** → **Runners** → **New self-hosted runner**.
2. Choose **macOS** (or your OS).
3. Copy the registration commands GitHub shows; run them in Terminal on the Mac that has Unity installed.
4. When asked for labels, defaults are fine (`self-hosted`, `macOS`, etc.).
5. Start the runner (often as a service):

```bash
cd ~/actions-runner
./run.sh
```

Keep it running while workflows execute, or install as a service per GitHub’s instructions.

**Security:** Anyone who can push workflow files to your repo can run code on this machine. Use a **private repo**, branch protection, and trusted collaborators only.

---

## Part 4 — Local smoke test (recommended)

Run on the same machine as the runner **before** relying on GitHub:

```bash
export UNITY_PATH="/Applications/Unity/Hub/Editor/6000.0.46f1/Unity.app/Contents/MacOS/Unity"
cd /path/to/Hub

Packages/com.cursor.environment-authoring-kit/Tools/run-codeql-unity-prep.sh
# Expect: "OK — kit project present" or "OK — solution present".
# Log: Logs/codeql-unity-prep.log

Packages/com.cursor.environment-authoring-kit/Tools/codeql-build-csharp.sh
# Expect: dotnet/msbuild completes (warnings may appear; errors must be fixed)
```

If prep fails, open `Logs/codeql-unity-prep.log` for Unity compile errors.

---

## Part 5 — Run on GitHub

1. **Actions** tab → **CodeQL (Unity self-hosted)** → **Run workflow** (or push to `main`).
2. Job **C# (Unity compile)** should run on your self-hosted runner (not `ubuntu-latest`).
3. Job **TypeScript (cave-grader)** runs on `ubuntu-latest` (no Unity).
4. Results: **Security** → **Code scanning alerts**.

Upload artifact **codeql-unity-prep-log** if the C# job fails.

---

## Workflow reference

| Workflow file | Runner | C# mode | When to use |
|---------------|--------|---------|-------------|
| `codeql-unity-selfhosted.yml` | `self-hosted` + Unity | Manual build (best) | Primary — your Mac with Unity |
| `codeql.yml` | `ubuntu-latest` | `build-mode: none` | Backup when runner offline |

You may disable `codeql.yml` if you only want self-hosted scans.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|--------|----------------|-----|
| Workflow queued forever | Runner offline | Start `./run.sh` on runner machine |
| `UNITY_PATH` error | Variable missing/wrong | Set repo variable; test path with `"$UNITY_PATH" -version` or run binary |
| No `.csproj` / `.sln` | Unity prep failed or timed out on initial import | Read `Logs/codeql-unity-prep.log`; prep no longer forces a second full recompile |
| `dotnet build` fails | No SDK or Unity refs broken | Install .NET SDK; run prep first; open project in Editor once |
| `.env.codeql` missing locally | Not created yet | `cp Packages/.../Tools/env.codeql.example .env.codeql` at Hub root |
| Two CodeQL runs conflict | Default setup + workflows | Disable default setup |
| `cursoragent` on commits | Cursor attribution | Disable Attribution in Cursor; not related to CodeQL |
| Alerts seem wrong for Unity | Partial extraction in `none` mode | Use self-hosted workflow, not cloud-only |

---

## FAQ

**Do I need to ship the zip to users of my game?**  
No. This is for **your GitHub repo CI** only.

**Does CodeQL pass my class assignment / quality bar?**  
No. Use `Tools/cave-grader` and Unity playtests for environment quality.

**Can I use this on a private repo?**  
Yes, if your GitHub plan includes code scanning for private repos (Code Security).

**Is the zip public domain?**  
No. Same license as Environment Authoring Kit / Hub (see above).

---

## Support

- GitHub CodeQL docs: https://docs.github.com/en/code-security/code-scanning
- Unity batchmode: https://docs.unity3d.com/Manual/CommandLineArguments.html
- Kit product boundary: `docs/PRODUCT_BOUNDARY.md` in the package
