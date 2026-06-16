# DTA Training Companion — Player Training Loop & Expectations
This document outlines before/after training expectations for gameplay behavior cloning (BC) within Deep Train Academy (Unity 6).

---

## 1. Before Training

### Core Status of Agent Brain
- **Baseline Policy**: Relying solely on the global baseline policy `competition_gameplay.onnx` combined with fuzzy state machine overrides.
- **Behavior Patterns**: Highly general, frequently failing to align rail stability in specialized biomes (e.g. forgetting brakes during Basalt Bridge collapses or signals navigation gaps in Slate Canyons).
- **Ledger Inconsistencies**: Multiple marks/strikes recorded due to manual overrides and watch replays.

### Training Material Preparation (The .hubai Pack)
- Capture gameplay files inside the game:
  - Game records action/observation history into local buffers as `.jsonl` lines.
  - Each observation captures 96 feature dimensions; each action maps to a 12-dimensional output vector.
  - User packages these files by exporting the **Agent Vault** (`.hubai`) from the game's menu.

---

## 2. In-Lab Active Behavior Cloning

Within the DTA Training Laboratories, players:
1. **Apply Quality Gates**: Filter out "poison" or poor performance rows (e.g., episodes matching panic manual overrides or training under rapid punishment spikes).
2. **Train custom adapter weights (`bc_adapter`)**: Behavior clone gameplay vectors to construct `gameplay_adapter.onnx`, a specialized low-latency layer.
3. **Calibrate Confidence**: Analyze the decision trail before exporting to evaluate whether the agent understood optimal rail thresholds.

---

## 3. After Deployment

### Automatic Import Integration
- Once the user clicks "Deploy to Game", the companion writes the updated ONNX model and records `pending_import.json` into the OS sync folder.
- When `DeepTrainAcademy.exe` launches, the auto-importer copies the files, immediately overriding the specific Agent slot weights.

### Observable Improvements
- **Tactical Adjustments**: The agent demonstrates customized behaviors matching the trained activity (e.g. aggressive acceleration and precise curves in battle mode, or quiet scouting pathfinding in exploration).
- **Before-after Comparison**: High baseline rewards, zero ledger friction strikes, and complete biome mastery.
- **Syndicate Progression**: Earned points boost classroom cohort standing on the training maturity leaderboard.
