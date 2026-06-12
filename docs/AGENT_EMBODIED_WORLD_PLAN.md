# Agent embodied world — plan

**North star:** The agent believes this world is all of reality. It sees, hears, and feels its body; learns from play; runs courses (signs → platforms → labyrinths → puzzles → fights) without GPS cheats or meta talk.

**Main task complete (runtime):** embodied senses, courses (sign + player gates with START/CP/FINISH + overtime), train/retrain loops, ML-Agents obs parity, Hub editor menus, Train panel readiness checklist, **Strategic + Handyman specialist brains** (`plan` / `fix` in direct agent chat).

---

## Phase A — Body + course runtime (mostly done)

| Item | Purpose |
|------|---------|
| Proprioception obs (slots 12–17) | Grounded, speed, gait, balance — “feeling” for jumps/climb |
| `Course` behavior mode | Obstacle flows distinct from plain race |
| Course signs | Vision-colored waypoints; chain start → finish |
| Puzzle gates | Riddle on sign; `answer …` unlocks next |
| Enemy gates | Defeat tagged foe before next sign |
| Obs + episodes | Full 96-dim incl. course state for fresh-user training |

**Play without retrain:** vision color guide + probes + nav assist steer the agent; old gameplay ONNX still runs but guides fill the gap.

**Try it:** `Window → Competition → Spawn Test Course Chain`, then `course` in chat to your agent. Puzzle: `answer moonlight`.

**Phase C (partial):** platform jump markers, labyrinth junctions (vision color pick), full ladder menu spawn.

**Phase D (operator):** play → episodes (96-dim embodied) → **Hub → Competition → Retrain Gameplay ONNX from Episodes** → **Sync Trained Models to Resources** → respawn agent. Per-agent: **Train Per-Agent Adapter** + filter chips.

**Optional later:** vision/audio encoder retrain from Neo captures; dedicated puzzle ONNX (rules + speech work today).

**Vision + voice (new):** signs render readable text; eye raycast + contrast confirms read; proximity chat is heard and understood (`CompetitionTextEncoder`); agent replies without `@` for greetings, questions, natural commands, puzzle answers spoken aloud.

---

## Phase B — Fresh-user training loop

| Item | Purpose |
|------|---------|
| Onboarding copy in Train panel | “Play 50 samples → Train” |
| Default agent profile on first launch | No saved data required |
| Episode quality tags | course / follow / battle logged — **Train panel filter chips** |
| Laptop: bootstrap `competition_gameplay` | Train on obs with vision/audio/proprio filled |

---

## Phase C — Course content ladder

| Level | Mechanics |
|-------|-----------|
| 1 Signs | Follow colored markers to finish |
| 2 Platforms | Jump/climb; vision + proprio + jump assist |
| 3 Labyrinth | Junction signs (left/right); no global map |
| 4 Puzzles | Riddle gates; in-world chat only |
| 5 Combat | Required enemy clears before next sign |

---

## Phase D — Brains + world voice

| Item | Purpose |
|------|---------|
| Retrain vision/audio encoders | From Neo captures |
| Retrain gameplay + adapter | Fused embodied obs |
| Puzzle brain (small ONNX) | Optional; rules work first |
| In-world dialogue filter | No Unity/model/meta in agent lines |

---

## Obs map (96-dim, frozen)

| Slots | Channel |
|-------|---------|
| 0–9 | Position, command, bearing, time |
| 10–11 | Follow/search awareness |
| 12–17 | Proprioception |
| 18–25 | Environment probes |
| 28–33 | Course state |
| 34–37 | Vision text read (contrast, confidence, hash, recency) |
| 41–44 | Vision embedding (+ text contrast in slot 4) |
| 45–47 | Audio embedding |
| 48–51 | Heard speech (strength, intent, text hash, recency) |
| 52–75 | Agent Z |
| 76–95 | Memory, ledger, season |

---

## Success criteria

| Criterion | How |
|-----------|-----|
| Embodied course (signs) | `Window → Competition → Spawn Full Course Ladder`, `course` in chat |
| Player gate course | HUD **Course** button → place gate pairs → **Offer** |
| Fresh-user train | Play 24+ samples → **Train Agent** (optional activity filter) |
| Global brain refresh | **Hub → Competition → Retrain Gameplay ONNX from Episodes** |
| Arena fair play | Follow-search penalties off when `ChallengeArenaController` active |
| In-world voice | No Unity/ONNX/meta in agent proximity lines |

## Hub menus (Editor)

- **Train Per-Agent Adapter** — Play Mode only
- **Retrain Gameplay ONNX from Episodes** — uses `Tools/competition-models/retrain_from_play.sh`
- **Sync Trained Models to Resources**
- **Enable / Disable ML-Agents Training on Agent** — Play Mode; full 96-dim embodied obs
