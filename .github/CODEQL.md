# Code scanning (CodeQL)

**User guide:** [docs/CODEQL_SETUP_AND_USE.md](../docs/CODEQL_SETUP_AND_USE.md)  
**Install / legal / deep troubleshooting:** [Packages/.../docs/CODEQL_SELFHOSTED_INSTALL.md](../Packages/com.cursor.environment-authoring-kit/docs/CODEQL_SELFHOSTED_INSTALL.md)

## One workflow only

Use **`.github/workflows/codeql.yml`** — do **not** add GitHub’s auto-generated **CodeQL Advanced** template from the web UI.

**Settings → Code security** → turn **off** “Code scanning default setup”.  
Default + advanced causes: *“advanced configurations cannot be processed when default setup is enabled”*.

## Jobs (verified 2026-05-29)

| Job | Runner | Languages |
|-----|--------|-----------|
| **Analyze (csharp — Unity)** | `self-hosted` (your Mac) | C# — Unity prep + `dotnet build` |
| **Analyze (javascript-typescript)** | `ubuntu-latest` | `Tools/cave-grader` |
| **Analyze (actions)** | `ubuntu-latest` | Workflow YAML |

## Maintainer checklist

1. `cd ~/actions-runner && ./run.sh` (runner **≥ v2.327.1** for `actions/checkout@v5`)
2. Repo variable **`UNITY_PATH`** → Unity binary (trimmed path)
3. Default CodeQL setup **disabled**
4. Local smoke: `Packages/.../Tools/run-codeql-local-verify.sh`

## Autofix

Enable in **Settings → Code security** (Copilot Autofix). Works with this advanced workflow after alerts upload — **not** blocked by using manual Unity build. See [docs/CODEQL_SETUP_AND_USE.md](../docs/CODEQL_SETUP_AND_USE.md#copilot-autofix-optional).
