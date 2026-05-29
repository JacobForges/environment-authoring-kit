# Code scanning (CodeQL)

## One workflow only

Use **`.github/workflows/codeql.yml`** — do **not** add GitHub’s auto-generated `CodeQL Advanced` workflow separately.

**Settings → Code security** → turn **off** “Code scanning default setup”.  
Default + this file causes: *“advanced configurations cannot be processed when default setup is enabled”*.

## Jobs

| Job | Runner | Languages |
|-----|--------|-----------|
| **Analyze (csharp — Unity)** | Your Mac (`self-hosted`) | C# with Unity + `dotnet build` |
| **Analyze (javascript-typescript)** | GitHub cloud | `Tools/cave-grader` |
| **Analyze (actions)** | GitHub cloud | Workflow YAML |

## Setup checklist

1. `cd ~/actions-runner && ./run.sh` (keep running)
2. Repo variable **`UNITY_PATH`** → Unity binary (trimmed path, no newlines)
3. Default CodeQL setup **disabled**
4. Local smoke test: `Packages/.../Tools/run-codeql-local-verify.sh`

Full guide: `Packages/com.cursor.environment-authoring-kit/docs/CODEQL_SELFHOSTED_INSTALL.md`
