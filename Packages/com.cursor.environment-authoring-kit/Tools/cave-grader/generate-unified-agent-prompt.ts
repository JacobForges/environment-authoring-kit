#!/usr/bin/env npx tsx
/**
 * Builds CaveBuildGeneratedJsonManifest.json + CaveBuildUnifiedAgentPrompt.md from ALL on-disk JSON.
 * Phase digest is a single overwrite file (CaveBuildPhaseDataDigest.md), not per-phase files.
 */
import { existsSync, mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import {
  JSON_MANIFEST_REL,
  PHASE_DATA_DIGEST_REL,
  UNIFIED_AGENT_PROMPT_REL,
  RESEARCH_AGENT_PROMPT_REL,
  digestFrontMatter,
} from "./agent-artifact-paths.js";
import {
  buildManifest,
  formatManifestMarkdown,
  pathsForPhase,
  readJsonExcerpt,
  discoverAllJson,
} from "./generated-json-catalog.js";
import { PIPELINE_PHASES } from "./pipeline-phases.js";
import { resolveHubRoot } from "./hub-root.js";
import { buildHardcodedUnifiedPromptHeader } from "./hardcoded-agent-prompts.js";
import { useHardcodedPrompts } from "./prompt-ladder.js";

const hubRoot = resolveHubRoot();
const gen = join(hubRoot, "Assets/EnvironmentKit/Generated");

const EXCERPT_CHARS = 1500;
const MAX_EXCERPT_FILES = 5;

function parsePhaseArg(argv: string[]): string | undefined {
  for (const a of argv) {
    if (a.startsWith("--phase=")) return a.slice("--phase=".length).trim();
  }
  return process.env.CAVE_ACTIVE_PHASE?.trim() || undefined;
}

function shouldSkipManifestRebuild(): boolean {
  if (process.env.CAVE_SKIP_MANIFEST_REBUILD === "1") return true;
  const manifestPath = join(hubRoot, JSON_MANIFEST_REL);
  if (!existsSync(manifestPath)) return false;
  try {
    return Date.now() - statSync(manifestPath).mtimeMs < 30 * 60 * 1000;
  } catch {
    return false;
  }
}

function writePhaseDataDigest(phaseId: string) {
  const all = discoverAllJson(hubRoot);
  const phaseDef = PIPELINE_PHASES.find((p) => p.id === phaseId);
  const pathSet = new Set<string>([
    ...pathsForPhase(phaseId, all),
    ...(phaseDef?.jsonPaths ?? []),
  ]);

  const lines: string[] = [
    digestFrontMatter(phaseId),
    `# Live JSON data digest — ${phaseId}`,
    "",
    "Auto-generated excerpts for the active phase only (not every Generated JSON).",
    "Full research context: `CaveBuildResearchAgentPrompt.md`.",
    "",
  ];

  for (const rel of [...pathSet].sort().slice(0, MAX_EXCERPT_FILES)) {
    const excerpt = readJsonExcerpt(hubRoot, rel, EXCERPT_CHARS);
    lines.push(`## ${rel}`, "", "```json", excerpt, "```", "");
  }

  writeFileSync(join(hubRoot, PHASE_DATA_DIGEST_REL), lines.join("\n"), "utf8");
  return PHASE_DATA_DIGEST_REL;
}

function main() {
  const activePhase = parsePhaseArg(process.argv);
  mkdirSync(gen, { recursive: true });

  let manifest: ReturnType<typeof buildManifest>;
  if (shouldSkipManifestRebuild() && existsSync(join(hubRoot, JSON_MANIFEST_REL))) {
    manifest = JSON.parse(
      readFileSync(join(hubRoot, JSON_MANIFEST_REL), "utf8")
    ) as ReturnType<typeof buildManifest>;
    console.log("[CaveCursor:info] Reused existing JSON manifest (fresh <30m)");
  } else {
    manifest = buildManifest(hubRoot, activePhase);
    writeFileSync(join(hubRoot, JSON_MANIFEST_REL), JSON.stringify(manifest, null, 2), "utf8");
  }

  let unified = useHardcodedPrompts()
    ? `${buildHardcodedUnifiedPromptHeader(hubRoot, activePhase)}\n\n---\n\n`
    : "";
  unified += formatManifestMarkdown(manifest);
  unified += "\n\n## Consolidated research (read first for research/terrain/cave)\n";
  unified += `- \`${hubRoot}/${RESEARCH_AGENT_PROMPT_REL}\` — terrain + cave summaries, max 5 local images (no full JSON paste)\n`;

  const excerptTargets = activePhase
    ? pathsForPhase(activePhase, manifest.files).slice(0, MAX_EXCERPT_FILES)
    : manifest.files.map((f) => f.relativePath).slice(0, MAX_EXCERPT_FILES);

  if (excerptTargets.length) {
    unified += `\n## Active phase JSON excerpts (max ${MAX_EXCERPT_FILES}, ${EXCERPT_CHARS} chars each)\n`;
    for (const rel of excerptTargets) {
      unified += `\n### ${rel}\n\`\`\`json\n${readJsonExcerpt(hubRoot, rel, EXCERPT_CHARS)}\n\`\`\`\n`;
    }
  }

  writeFileSync(join(hubRoot, UNIFIED_AGENT_PROMPT_REL), unified, "utf8");

  const written: string[] = [JSON_MANIFEST_REL, UNIFIED_AGENT_PROMPT_REL];
  if (activePhase) {
    written.push(writePhaseDataDigest(activePhase));
  }

  console.log(
    `[CaveCursor:info] Unified agent prompt: ${manifest.fileCount} JSON files → ${written.join(", ")}`
  );
}

main();
