# Gameplay model — RL combat + agent chat (not setup)

**Scope:** `competition_gameplay.onnx` only. Setup/onboarding stays `competition_setup.onnx`.

**Platforms:** Neo 6–8 GB — CPU Sentis, 10–20 Hz policy, no generative LLM in build.

---

## 1. What “chatting with the agent” means here

| Approach | In build? | Notes |
|----------|-----------|--------|
| Generative LLM (free-form chat) | **No** | RAM / latency / keys |
| **Template dialogue + slots** | **Yes** | “HP {hp}% — I remember pit at cp3.” |
| **Tiny ONNX intent classifier** | **Yes** | Player text → intent id |
| **Bark head on gameplay model** | **Yes** | Game state → `bark_category` every ~2s |
| Knowledge DB lookup | **Yes** | Answers “what did you learn?” from indexed facts |

### Chat flow (runtime)

```
Player types in UI
  → char-ngram features [64] (same family as setup encoder)
  → chat_intent.onnx OR gameplay chat head → intent_id (8–16 classes)
  → DialogueResolver:
        intent + agent stats + top-3 KB facts + ledger mood
        → pick template line + fill slots
  → show in chat log + optional TTS later
```

**Intent classes (v1):** `greet`, `status`, `strategy`, `encourage`, `taunt`, `learned`, `hurt`, `victory`, `stuck`, `unknown`

Player never needs the model to write prose — only **pick behavior + line category**.

During gameplay without player typing, **bark head** fires on events (low HP, kill, checkpoint, punishment).

---

## 2. Single gameplay ONNX (RL policy + optional bark)

Replace narrow `competition_policy.onnx` with **`competition_gameplay.onnx`** (version 2).

### Observations — `state` float32 `[1, 96]`

| Block | Dim | Features |
|-------|-----|----------|
| Locomotion | 18 | pos xz, vel xz, yaw sin/cos, next cp vec, dist cp, dist goal, time |
| Rays | 8 | forward/down/side hit distances |
| Combat self | 12 | hp%, maxHp, stamina%, defending flag, attack cd, skill cds×3, combo, threat |
| Combat target | 14 | nearest enemy dist, bearing, enemy hp%, attacking me?, last damage dir, aggro |
| Agent latent | 24 | `z` from onboarding (personality) |
| Memory hint | 8 | retrieved KB confidence features (checkpoint hazard, etc.) |
| Ledger mood | 6 | recent reward sum, strike count, debuff flags |
| Season | 6 | season one-hot or hash |
| **Total** | **96** | |

### Actions — `actions` float32 `[1, 12]`

| Idx | Name | Use |
|-----|------|-----|
| 0–1 | move_x, move_z | Tanh → locomotion |
| 2 | turn | Tanh |
| 3 | jump_logit | Sigmoid |
| 4 | attack_logit | Sigmoid → `CombatStats` strike |
| 5 | defend_logit | Sigmoid → block stance |
| 6–8 | skill_1..3_logit | Sigmoid → hotbar skills |
| 9 | target_cycle_logit | Sigmoid → next enemy |
| 10 | bark_trigger_logit | Sigmoid → emit bark this tick |
| 11 | bark_category | Tanh → bucket 0–7 for template picker |

Unity applies activations; `CavePlaytestBotController` / `CombatStats` already exist for attack/defend.

### Chat-only micro-model (optional v1b)

`competition_chat_intent.onnx` — **~50K params**

- Input: `text_features [1, 64]`
- Output: `intent_logits [1, 12]`

Keeps gameplay ONNX smaller; load only during chat panel open.

---

## 3. RL training (gameplay only)

### Stage A — Imitation (BC)

Expert: scripted bot with NavMesh + combat probe (`CavePlaytestBotController` behavior).

Record JSONL per transition:

```json
{"state": [96], "actions": [12], "reward": 0.0, "done": false,
 "episodeId": "...", "seasonId": "2026-06-a"}
```

Train with behavior cloning + MSE on continuous, BCE on logits.

### Stage B — RL fine-tune (PPO)

**Environment:** Unity headless or ML-Agents clone of Season map.

**Reward shaping:**

| Signal | Reward |
|--------|--------|
| Checkpoint progress | +2.0 |
| Goal finish | +20 − time_penalty |
| Damage dealt | +0.1 × dmg |
| Kill enemy | +5 |
| Successful defend (reduced dmg) | +1 |
| Skill hit | +2 |
| Take damage | −0.15 × dmg |
| Death | −15 |
| Stuck 8s | −3 |
| Pit | −8 |

**Punishment ledger** mirrors these → in-game debuffs (already planned).

Algorithm: PPO (stable for continuous+discrete hybrid). Export actor-only ONNX.

### Stage C — Lifelong (per agent, optional)

- Append episodes to `Agents/{id}/dataset/`
- Periodic **offline** PPO/BC fine-tune → `Agents/{id}/models/gameplay_adapter.onnx`
- Small adapter (last layer only) ~100KB — delete agent wipes this

**Do not** online-train inside Unity on Neo during play (RAM + stability).

---

## 4. Chat training (gameplay bundle, not setup)

### Template bank

`Resources/Competition/Dialogue/*.json` — categorized lines with `{hp}`, `{strikes}`, `{fact}` slots.

### Train intent model

Synthetic: paraphrase lists per intent → `competition_chat_intent.onnx`.

### Train bark head

Supervised from logged (state, event_type) → bark_category on combat/route events.

Can be **extra output heads** on same gameplay trunk (multi-task RL+BC).

---

## 5. Files (no Unity until build done)

```
Tools/competition-models/
  GAMEPLAY_RL_AND_CHAT_SPEC.md     (this file)
  trained/
    competition_gameplay_v2.onnx   (after training)
    competition_chat_intent.onnx   (optional)
  train/
    gameplay_rl_trainer.py         (BC + export)
    record_episode_schema.json
```

`model_manifest.json` → bump `gameplayVersion: 2` when swapping.

---

## 6. Safeguards

- Gameplay model never writes to knowledge DB — **promoter** does after episode
- Chat templates sanitized — no PII in bark generation
- Ranked mode: fixed global gameplay ONNX; practice allows per-agent adapter
- 96-dim obs documented — version field in episode JSON

---

## 7. Migration from v1 policy (42→96, 4→12)

- Keep `competition_policy.onnx` as legacy fallback
- Loader checks manifest version
- Retrain required — no weight transfer from 42-dim model
