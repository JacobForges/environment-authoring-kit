# Competition Agent & Comp World — Master Plan

**Updated:** 2026-06-11  
**Unity editor:** wait until Hub world-gen run finishes before any `Assets/` work.

Legend: `[x]` done (Tools/offline) · `[~]` partial · `[ ]` not started · `[—]` blocked on frozen map / editor

---

## A. Planning & architecture

| | Item |
|---|------|
| [x] | Gameplay RL + chat spec (`GAMEPLAY_RL_AND_CHAT_SPEC.md`) |
| [x] | Social chat + commands + proximity + 15s cooldown (`CHAT_SOCIAL_AND_COMMANDS_SPEC.md`) |
| [x] | Master feature layouts (`FEATURE_LAYOUT.md`) |
| [x] | Lifelong KB / delete agent / reward design (conversation + safeguards) |
| [x] | End-to-end TODO phases documented (this file) |
| [ ] | JSON Schema files (`schemas/*.schema.json`) — optional formal validation |

---

## B. ONNX models (offline training)

| | Item | Location / notes |
|---|------|------------------|
| [x] | Setup model scaffold export | `export_competition_onnx.py` |
| [x] | Setup model **trained** (synthetic Q&A) | `trained/competition_setup.onnx` ~160 KB, ~79% archetype acc |
| [x] | Policy v1 **trained** (synthetic route) | `trained/competition_policy.onnx` — legacy fallback |
| [x] | Gameplay v2 **trained** (synthetic BC, 96→12) | `trained/competition_gameplay.onnx` ~490 KB |
| [x] | Chat intent **trained** (18 intents, ~76% acc) | `trained/competition_chat_intent.onnx` ~107 KB |
| [x] | Gameplay manifest (obs blocks, action layout) | `trained/gameplay_manifest.json` |
| [x] | Chat intent manifest | `trained/chat_intent_manifest.json` |
| [x] | Validate all models (onnxruntime) | `validate_models.py` → `validate_report.json` |
| [ ] | Setup retrain on **real** onboarding logs | after players use UI |
| [—] | Gameplay v2 retrain on **Season 1** route + combat | needs frozen map + episode capture |
| [—] | PPO RL fine-tune gameplay | after BC on real trajectories |
| [ ] | Per-agent adapter ONNX (lifelong) | v2 feature |

**Trainers:** `train/setup_trainer.py`, `policy_trainer.py`, `gameplay_trainer.py`, `chat_intent_trainer.py`

---

## C. Game data (no Unity yet)

| | Item | Location |
|---|------|----------|
| [x] | Onboarding Q&A script (12 steps) | `onboarding/questions.json` |
| [x] | Dialogue templates (global, proximity, command, replies) | `dialogue/templates.json` |
| [x] | Season manifest template | `season/season_manifest.template.json` |
| [ ] | Episode JSONL schema + example row | `schemas/episode.example.jsonl` |
| [ ] | Profile / knowledge entry schema docs | `schemas/` |
| [ ] | Expand dialogue for all 18 intents + agent↔agent proximity lines |

---

## D. Unity packaging (after Hub run — **NOT STARTED**)

| | Item |
|---|------|
| [ ] | Copy `trained/*.onnx` → `Assets/StreamingAssets/Competition/Models/` |
| [ ] | Copy `questions.json`, `templates.json`, season template → `StreamingAssets` or `Resources` |
| [ ] | Add Competition scenes to **Build Settings** |
| [ ] | File → Build → `.app` includes StreamingAssets (~+1 MB models) |

---

## E. Runtime C# — core (`Assets/Scripts/Competition/`)

| | Item |
|---|------|
| [ ] | `CompetitionPaths.cs` — persistentDataPath layout |
| [ ] | `CompetitionProfileStore.cs` — profile, atomic save |
| [ ] | `CompetitionAgentRegistry.cs` — list agents, create, **delete wipes folder** |
| [ ] | `CompetitionTempSession.cs` — overwrite on reload / new run |
| [ ] | `CompetitionMigration.cs` — schema version, corrupt recovery |
| [ ] | `FirstRunGate.cs` — skip onboarding if complete |
| [ ] | `CompetitionFeatureEncoder.cs` — Q&A → `features[100]` |
| [ ] | `CompetitionTextEncoder.cs` — chat → `text_features[64]` |
| [ ] | `CompetitionObsBuilder.cs` — world → `state[96]` |

---

## F. Sentis / brains

| | Item |
|---|------|
| [ ] | `CompetitionSetupBrain.cs` — load setup ONNX, dispose after onboarding |
| [ ] | `CompetitionGameplayBrain.cs` — gameplay v2 @ 15 Hz, CPU |
| [ ] | `CompetitionChatIntentBrain.cs` — intent logits → enum |
| [ ] | `CompetitionActionApplier.cs` — actions → locomotion + combat |
| [ ] | `AgentCommandState.cs` — player orders override policy |
| [ ] | Legacy loader for `competition_policy.onnx` v1 (optional fallback) |

---

## G. Onboarding UI (Unity UI, new scene)

| | Item |
|---|------|
| [ ] | `CompetitionUiTheme` / extend `PortfolioUiKit` |
| [ ] | `Onboarding.unity` — **new scene**, not MainScene |
| [ ] | Phase A character Q&A (6 steps) |
| [ ] | Phase B agent Q&A (6 steps) |
| [ ] | Avatar preview rig + morph from setup output |
| [ ] | Pick 1 of 3 avatar variants |
| [ ] | Save profile + `z`, wipe temp, mark complete |
| [ ] | Hook `FirstRunGate` → title menu (last step) |

---

## H. Gameplay & RL loop

| | Item |
|---|------|
| [ ] | `CompetitionRunController.cs` — timer, spawn bot, end episode |
| [ ] | Checkpoint / goal hooks → rewards |
| [ ] | `CompetitionEpisodeWriter.cs` — JSONL transitions |
| [ ] | Practice vs ranked mode flag |
| [—] | Record expert trajectories (Season 1) for retrain |
| [ ] | Offline retrain pipeline doc / script invoke from exported episodes |

---

## I. Knowledge DB (lifelong)

| | Item |
|---|------|
| [ ] | `CompetitionKnowledgeStore.cs` — entries, index, categories |
| [ ] | `CompetitionKnowledgePromoter.cs` — evidence thresholds |
| [ ] | `CompetitionSummarizer.cs` — rule-based v1 |
| [ ] | Confidence decay on season change |
| [ ] | Memory hints injected into obs [76:84] |
| [ ] | Delete agent removes entire `Agents/{uuid}/` tree |

---

## J. Reward & punishment (in-game felt)

| | Item |
|---|------|
| [ ] | `CompetitionLedger.cs` |
| [ ] | `CompetitionRewardController.cs` |
| [ ] | `CompetitionPunishmentController.cs` — debuffs, strikes UI |
| [ ] | Recovery rules (checkpoint clears minor debuff) |
| [ ] | End-of-run summary screen |

---

## K. Chat & social

| | Item |
|---|------|
| [ ] | `ChatMessage` + `ChatChannel` (global, proximity, command, system) |
| [ ] | `ChatQueueHost` / `ChatQueueClient` — ordered `sequenceId` |
| [ ] | `AgentCooldownTracker.cs` — **15s per agent** |
| [ ] | Proximity filter (~25 m) |
| [ ] | `DialogueResolver.cs` — templates + slots |
| [ ] | Command channel → `AgentCommandState` |
| [ ] | Agent bark from gameplay head → queue (respect cooldown) |
| [ ] | Agent↔agent proximity social lines |
| [ ] | Chat panel UI (merged timeline) |

---

## L. Multiplayer (portfolio)

| | Item |
|---|------|
| [~] | Title menu / Relay / Vivox shell exists (`PortfolioMainMenuController`) |
| [ ] | NGO RPC `SubmitChat` / `ReceiveChat` |
| [ ] | Sync agent/player positions for proximity |
| [ ] | Host validates cooldown + sequence |
| [ ] | “My agents” entry on title menu |

---

## M. Map seasons

| | Item |
|---|------|
| [—] | Freeze **Season 1** from completed Hub build |
| [ ] | `CompetitionSeasonManager.cs` |
| [ ] | Addressables / bundle per season (v2) |
| [ ] | Remote manifest poll + download UI |
| [ ] | Rotate when completion threshold met |
| [ ] | Leaderboard keyed by `seasonId` |

---

## N. Editor / pipeline (unchanged — **not** comp world)

| | Item | Status |
|---|------|--------|
| [~] | Hub world gen / grader / wizard | separate; your 4h+ run |
| [x] | Comp work isolated under `Tools/competition-models/` | does not block editor |
| [ ] | `CaveBuildWorldSessionManifest` → season seed stamp | editor hook when build done |

---

## O. Acceptance (demo ready)

| | Item |
|---|------|
| [—] | Hub run completes, proof saved |
| [ ] | Copy models to StreamingAssets, one compile batch |
| [ ] | First run: onboarding → agent created |
| [ ] | Practice run: bot moves, combat actions fire, episode logged |
| [ ] | Chat: global + command + proximity smoke |
| [ ] | Delete agent: folder gone, knowledge lost |
| [ ] | Build `.app`, launch without Unity installed |
| [ ] | Neo 6–8 GB: lite scene preset if needed |

---

## Progress summary

| Area | Done | Total (approx) |
|------|------|----------------|
| Planning & specs | 5 | 6 |
| ONNX offline | 9 | 13 |
| Game data files | 3 | 6 |
| Unity / C# | 0 | ~45 |
| Multiplayer chat | 0 | 5 |
| Seasons | 0 | 5 |
| Acceptance | 0 | 8 |

**~35% of comp-world work** is complete — all offline/model/planning. **~65%** is Unity integration + real map training + multiplayer wiring — **after** Hub run.

---

## When Unity run finishes — do in this order

1. Save proof (grade report, timelapse, manifest).  
2. Copy ONNX + JSON → `StreamingAssets/Competition/`.  
3. Phase E paths + profile store (no scene hook yet).  
4. Sentis brains smoke test in empty test scene.  
5. `Onboarding.unity` + Phase G.  
6. `FirstRunGate` → portfolio menu.  
7. `CompetitionRunController` + ledger (Phase H–J).  
8. Chat queue (Phase K).  
9. Record Season 1 episodes → retrain gameplay ONNX.  
10. Build `.app` (Phase D + O).

---

## Do not until build done

- Edit `MainScene.unity` or `Assets/EnvironmentKit/Generated/`  
- Kick new Hub Full rebuild  
- Bulk `Assets/Scripts` drop without compile window  
