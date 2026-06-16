import fs from "fs";
import os from "os";
import path from "path";

export function companionRoot() {
  if (process.platform === "win32") {
    return path.join(
      process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"),
      "DeepTrainAcademy",
    );
  }
  if (process.platform === "darwin") {
    return path.join(os.homedir(), "Library", "Application Support", "DeepTrainAcademy");
  }
  return path.join(os.homedir(), ".local", "share", "DeepTrainAcademy");
}

export function settingsPath() {
  return path.join(companionRoot(), "companion_settings.json");
}

export function syncFolder(agentId: string) {
  return path.join(companionRoot(), "checkpoints", agentId);
}

export function modelSlotPath(slot: number) {
  return path.join(companionRoot(), "model_slots", `slot_${slot}`, "gameplay_adapter.onnx");
}

/** Unity Competition adapter path (Deep Train Academy standalone). */
export function unityAgentAdapterPath(agentId: string) {
  const company = "JacobForges";
  const product = "Deep Train Academy";
  if (process.platform === "win32") {
    const base = process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local");
    return path.join(base, company, product, "Competition", "Agents", agentId, "models", "gameplay_adapter.onnx");
  }
  if (process.platform === "darwin") {
    return path.join(
      os.homedir(),
      "Library",
      "Application Support",
      company,
      product,
      "Competition",
      "Agents",
      agentId,
      "models",
      "gameplay_adapter.onnx",
    );
  }
  return path.join(os.homedir(), ".local", "share", company, product, "Competition", "Agents", agentId, "models", "gameplay_adapter.onnx");
}

export function hubRepoRoot() {
  const cwd = process.cwd();
  const candidates = [cwd, path.join(cwd, ".."), path.join(cwd, "../..")];
  for (const c of candidates) {
    if (fs.existsSync(path.join(c, "Assets", "MainScene.unity"))) return c;
  }
  return path.join(cwd, "..");
}
