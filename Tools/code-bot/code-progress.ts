import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

export const HUB_CODE_PROGRESS_REL = "Assets/Scripts/HubCodeProgress.json";

export type CodeTaskStatus = "pending" | "in_progress" | "done" | "blocked";

export type CodeTask = {
  stream?: string;
  status?: CodeTaskStatus;
  title?: string;
  acceptance?: string;
  files?: string[];
  notes?: string;
};

export type HubCodeProgressDoc = {
  version?: number;
  codeReady?: boolean;
  notes?: string;
  streams?: Record<string, string>;
  tasks?: Record<string, CodeTask>;
};

export const CODE_TASK_ORDER = [
  "demo_g8_smoke",
  "integrate_compile_smoke",
  "demo_mainscene_idempotent",
  "comp_bootstrap_chain",
  "comp_title_to_spawn",
  "comp_chat_commands",
  "comp_combat_death",
  "comp_lineage_ecosystem",
  "challenge_network_relay",
  "challenge_arena_duel",
  "mp_portfolio_menu",
  "mp_agent_net_sync",
  "train_adapter_ui",
] as const;

export type CodeTaskId = (typeof CODE_TASK_ORDER)[number];

export function loadCodeProgress(hubRoot: string): HubCodeProgressDoc | null {
  const path = join(hubRoot, HUB_CODE_PROGRESS_REL);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8")) as HubCodeProgressDoc;
  } catch {
    return null;
  }
}

export function saveCodeProgress(hubRoot: string, doc: HubCodeProgressDoc): void {
  const path = join(hubRoot, HUB_CODE_PROGRESS_REL);
  writeFileSync(path, `${JSON.stringify(doc, null, 2)}\n`, "utf8");
}

export function pickActiveCodeTask(
  progress: HubCodeProgressDoc | null,
  override?: string | null
): string {
  if (override && progress?.tasks?.[override]) return override;

  for (const id of CODE_TASK_ORDER) {
    const row = progress?.tasks?.[id];
    const status = row?.status ?? "pending";
    if (status !== "done") return id;
  }

  for (const id of Object.keys(progress?.tasks ?? {})) {
    if ((progress?.tasks?.[id]?.status ?? "pending") !== "done") return id;
  }

  return CODE_TASK_ORDER[CODE_TASK_ORDER.length - 1];
}

export function codeReady(progress: HubCodeProgressDoc | null): boolean {
  if (progress?.codeReady === true) return true;
  const tasks = progress?.tasks ?? {};
  const ids = Object.keys(tasks);
  if (!ids.length) return false;
  return ids.every((id) => tasks[id]?.status === "done");
}

export function parseCodeTaskArg(argv: string[]): string | null {
  for (const arg of argv) {
    if (arg.startsWith("--task=")) return arg.slice("--task=".length).trim();
  }
  return process.env.CODE_BOT_TASK?.trim() || null;
}

export function pendingCodeTasks(progress: HubCodeProgressDoc | null): string[] {
  const tasks = progress?.tasks ?? {};
  return Object.keys(tasks).filter((id) => (tasks[id]?.status ?? "pending") !== "done");
}
