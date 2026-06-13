# ML-Agents + per-agent adapter (free, local)

## Per-agent adapter (lifelong learning)

**Global brain:** `competition_gameplay.onnx` in Resources (everyone shares).

**Personal layer:** `~/Library/Application Support/.../Competition/Agents/{agentId}/models/gameplay_adapter.onnx`

| Mode | Adapter |
|------|---------|
| Practice / solo explore | **On** — blended 35% with global ONNX |
| Ranked challenge arena | **Off** — `practiceMode = false` |

### Train adapter

1. Play with your agent (episodes mirror to `Agents/{id}/dataset/*.jsonl`).
2. Unity menu: **Hub → Competition → Train Per-Agent Adapter (Play Mode)** — or HUD **Train Agent**.
3. Respawn agent — log should show `Loaded per-agent adapter`.

**Embodied readiness:** open **Train Agent** — top section lists ✓/○ for senses, courses, samples.

**Global gameplay refresh:** **Hub → Competition → Retrain Gameplay ONNX from Episodes** (then **Sync Trained Models to Resources**).

CLI:

```bash
cd Tools/competition-models
.venv-competition-onnx/bin/python3 train/adapter_trainer.py YOUR_AGENT_ID
```

Delete agent wipes `dataset/` + `models/` with the profile folder.

---

## Unity ML-Agents 4 (PPO training)

Package: `com.unity.ml-agents` 4.0.0 (Unity 6 + Inference Engine).

### Play Mode setup

1. Spawn agent in Play Mode.
2. **Hub → Competition → Enable ML-Agents Training on Agent Pawn**.
3. Disable when done: **Disable ML-Agents Training on Agent Pawn**.

While enabled, `CompetitionMlAgentsAgent` drives the pawn; `CompetitionRunController` is paused.

### Python training (free)

Install [ML-Agents Python](https://github.com/Unity-Technologies/ml-agents) matching release 23 / package 4.x:

```bash
pip install mlagents==1.1.0
cd /path/to/Hub
mlagents-learn Tools/competition-models/mlagents/competition_agent.yaml --run-id=competition_agent --force
```

Press Play in Unity when the trainer connects. Observations: **96** floats (same as `CompetitionObsBuilder`). Actions: **12** continuous.

Export trained `.onnx` from ML-Agents or continue BC pipeline into `gameplay_adapter.onnx` per agent.

---

## Cost

All inference is **CPU / local**. No API keys. Training runs on your machine.
