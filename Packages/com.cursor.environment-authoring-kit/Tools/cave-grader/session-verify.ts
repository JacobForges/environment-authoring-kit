/**
 * Post-agent verification — session is NOT done until compile is clean (or explicitly skipped).
 * Unity bridge (CaveBuildCursorAgentBridge) already retries compile_gate when the editor is open;
 * this covers terminal `./run-session.sh` runs without Unity watching stdout.
 */
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

const GENERATED = "Assets/EnvironmentKit/Generated";
const COMPILE_JSON = `${GENERATED}/CaveBuildCompileDiagnostics.json`;
const EDITOR_PROJ = "EnvironmentAuthoringKit.Editor.csproj";

export type CompileDiagnosticsDoc = {
  capturedUtc?: string;
  hasCompileErrors?: boolean;
  isCompiling?: boolean;
  errorCount?: number;
  verifiedErrorCount?: number;
  staleErrorCount?: number;
  scriptCompilationFailed?: boolean;
  errors?: {
    code?: string;
    file?: string;
    line?: number;
    message?: string;
    verifiedOnDisk?: boolean;
  }[];
};

export type SessionVerifyResult = {
  ok: boolean;
  reason: "clean" | "compile_errors" | "stale_diagnostics" | "unity_verify_failed" | "dotnet_failed" | "skipped";
  compileErrors: { code: string; file: string; line: number; message: string }[];
  notes: string[];
};

function loadCompileDiagnostics(hubRoot: string): CompileDiagnosticsDoc | null {
  const path = join(hubRoot, COMPILE_JSON);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as CompileDiagnosticsDoc;
  } catch {
    return null;
  }
}

function blockingErrorsFromDoc(doc: CompileDiagnosticsDoc | null): SessionVerifyResult["compileErrors"] {
  if (!doc?.errors?.length) return [];
  const verified = doc.errors.filter((e) => e.verifiedOnDisk !== false);
  const count = doc.verifiedErrorCount ?? verified.length;
  if (count <= 0 && !doc.hasCompileErrors && !doc.scriptCompilationFailed) return [];
  const list = verified.length ? verified : doc.errors;
  return list.slice(0, 12).map((e) => ({
    code: e.code ?? "error",
    file: e.file ?? "?",
    line: e.line ?? 0,
    message: e.message ?? "",
  }));
}

function newestKitCsMtime(hubRoot: string): number {
  const kitEditor = join(hubRoot, "Packages/com.cursor.environment-authoring-kit/Editor");
  if (!existsSync(kitEditor)) return 0;
  // Cheap heuristic: if compile JSON is older than the fixer file, diagnostics are stale.
  const probe = join(kitEditor, "Blockout/CaveBuildQualityStageFixer.cs");
  try {
    return statSync(probe).mtimeMs;
  } catch {
    return 0;
  }
}

function diagnosticsCapturedMs(doc: CompileDiagnosticsDoc | null): number {
  if (!doc?.capturedUtc) return 0;
  const t = Date.parse(doc.capturedUtc);
  return Number.isFinite(t) ? t : 0;
}

function runUnityCompileExport(hubRoot: string): { ok: boolean; note: string } {
  const unity = process.env.UNITY_PATH?.trim();
  if (!unity || !existsSync(unity)) {
    return { ok: true, note: "UNITY_PATH not set — skipped Unity compile export." };
  }

  const logDir = join(hubRoot, "Logs");
  const logFile = join(logDir, "session-verify-compile.log");
  try {
    execFileSync(
      unity,
      [
        "-batchmode",
        "-nographics",
        "-projectPath",
        hubRoot,
        "-executeMethod",
        "EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.ExportCompileDiagnosticsForAgent",
        "-quit",
        "-logFile",
        logFile,
      ],
      { stdio: "pipe", timeout: 600_000 }
    );
    return { ok: true, note: `Unity exported compile diagnostics (log: ${logFile}).` };
  } catch (err) {
    const code =
      err && typeof err === "object" && "status" in err ? (err as { status?: number }).status : 1;
    return {
      ok: code === 0 || code === 2,
      note:
        code === 2
          ? "Unity export finished with compile errors (exit 2)."
          : `Unity compile export failed (exit ${code ?? "?"}). See Logs/session-verify-compile.log`,
    };
  }
}

function runDotnetCompileCheck(hubRoot: string, timeoutMs: number): { ok: boolean; output: string } {
  const proj = join(hubRoot, EDITOR_PROJ);
  if (!existsSync(proj)) {
    return { ok: true, output: "No EnvironmentAuthoringKit.Editor.csproj — skipped dotnet." };
  }

  const result = spawnSync("dotnet", ["build", proj, "-c", "Release", "-v", "q", "--nologo"], {
    cwd: hubRoot,
    encoding: "utf8",
    timeout: timeoutMs,
  });
  const output = `${result.stdout ?? ""}\n${result.stderr ?? ""}`.trim();
  return { ok: result.status === 0, output };
}

export function verifySessionAfterAgent(hubRoot: string): SessionVerifyResult {
  if (process.env.CAVE_SKIP_SESSION_VERIFY === "1") {
    return { ok: true, reason: "skipped", compileErrors: [], notes: ["CAVE_SKIP_SESSION_VERIFY=1"] };
  }

  const notes: string[] = [];

  if (process.env.CAVE_SESSION_VERIFY_UNITY !== "0") {
    const unity = runUnityCompileExport(hubRoot);
    notes.push(unity.note);
  }

  const doc = loadCompileDiagnostics(hubRoot);
  const errors = blockingErrorsFromDoc(doc);

  const capturedMs = diagnosticsCapturedMs(doc);
  const csMtime = newestKitCsMtime(hubRoot);
  const stale =
    capturedMs > 0 &&
    csMtime > capturedMs + 60_000 &&
    process.env.CAVE_SESSION_VERIFY_UNITY === "0" &&
    !process.env.UNITY_PATH;

  if (errors.length === 0 && stale) {
    notes.push(
      "Compile diagnostics look stale (C# edited after last Unity export). Set UNITY_PATH or open Unity to re-export."
    );
  }

  if (errors.length === 0 && process.env.CAVE_SESSION_VERIFY_DOTNET === "1") {
    const dotnet = runDotnetCompileCheck(hubRoot, 180_000);
    notes.push(dotnet.ok ? "dotnet build: OK" : "dotnet build: FAILED");
    if (!dotnet.ok) {
      const parsed = dotnet.output
        .split("\n")
        .filter((l) => /error CS\d+/.test(l))
        .slice(0, 8);
      for (const line of parsed) notes.push(line);
      return {
        ok: false,
        reason: "dotnet_failed",
        compileErrors: parsed.map((l) => ({
          code: l.match(/error CS\d+/)?.[0] ?? "error",
          file: "?",
          line: 0,
          message: l,
        })),
        notes,
      };
    }
  }

  if (errors.length > 0) {
    return { ok: false, reason: "compile_errors", compileErrors: errors, notes };
  }

  if (stale && process.env.CAVE_SESSION_VERIFY_STALE_FAIL === "1") {
    return { ok: false, reason: "stale_diagnostics", compileErrors: [], notes };
  }

  return { ok: true, reason: "clean", compileErrors: [], notes };
}

export function formatVerifyFailureMessage(result: SessionVerifyResult): string {
  const lines = [
    "",
    "[CaveCursor:verify-failed] Session NOT complete — compile verification failed.",
    "The agent edited code but the project is not compile-clean.",
    "",
  ];
  for (const n of result.notes) lines.push(`  • ${n}`);
  if (result.compileErrors.length) {
    lines.push("", "Blocking errors:");
    for (const e of result.compileErrors) {
      lines.push(`  • ${e.code} ${e.file}:${e.line} — ${e.message}`);
    }
  }
  lines.push(
    "",
    "Fix: run another session with --workflow=pre_build (compile_gate rung), or open Unity so compile_gate retries.",
    "Optional: UNITY_PATH=/path/to/Unity ./Tools/cursor-bot/run-session.sh (refreshes diagnostics before done).",
    "Opt out (not recommended): CAVE_SKIP_SESSION_VERIFY=1",
    ""
  );
  return lines.join("\n");
}
