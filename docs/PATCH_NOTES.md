# Patch Notes — Deep Train Academy (Hub)

**Author:** [JacobForges](https://github.com/JacobForges)  
**Product:** Hub reference project — procedural karst world + AI squadmate demo

This document summarizes the **current feature set** for portfolio reviewers and classmates. It is not a chronological changelog — see [CHANGELOG.md](CHANGELOG.md) for kit-level history.

---

## World & exploration

- Procedural Florida karst **surface** + **lava-tube cave** (Environment Authoring Kit)
- Playable demo loop: spawn → portal → cave → goal → combat
- Season/world loading from generated manifests
- Content updater: remote manifest, map bundle cache, lobby version gate

---

## AI agent (competition layer)

- Onboarding wizard → first agent spawn
- Chat commands: explore, follow, hold, battle, course, trainer, lineage, export/import
- ONNX brains + policy fallback; movement authority guard
- Agent eye PiP, train bar, progression XP, personality chat
- Security guard announcements in online sessions

---

## Multiplayer (Unity Gaming Services)

- **Netcode for GameObjects** + **Unity Transport** + **Relay** + **Lobby**
- Host Session (listen) + Play Online (join listen lobby)
- Up to **24 players** per session (free-tier design target)
- **Vivox positional voice** — 25 m range, push-to-talk (**V**), optional always-on
- Graceful quit: Vivox leave, lobby delete, NGO safe teardown
- Content `contentVersion` + `worldSeed` matching on join

---

## Voice & speech

- **Player voice:** Vivox positional channel on host/join; PTT default **V**
- **Agent voice commands:** Chat panel **Voice ON/OFF** toggle (PlayerPrefs)
- Cloud STT via `HUB_STT_API_URL` (OpenAI-compatible Whisper)
- VAD listening; pauses during PTT
- Voice transcripts visible to nearby players (proximity channel)

---

## UI & controls

- Portfolio title menu (SlimUI-themed)
- MMO HUD: inventory, character, skills, settings
- Competition chat panel: Chat + System tabs
- Gameplay controls hint overlay
- Challenge PvP panel, pause menu, Deep Train Academy intro

---

## Operator tooling (Editor)

- **Game → Setup Portfolio Menu (MainScene)**
- **Game → Setup Vivox** / **Install Vivox Package**
- **Game → Publish Content Manifest**
- **Game → Build/Windows Standalone Client** → `Builds/WindowsClient/DeepTrainAcademy.exe`
- **Game → Build/macOS Standalone Client** → `Builds/macOSClient/DeepTrainAcademy.app`

---

## Known limits

- **Clients:** macOS `.app` (Intel 64-bit for Vivox) and Windows `.exe` (x64, Vivox supported)
- **Multiplayer:** Host Session + Play Online (listen host) — no 24/7 server required
- macOS **player** builds: Intel 64-bit required for Vivox (ARM64-only Mac player TBD by Unity)
- Voice commands require operator STT endpoint configuration
- World generation grades and `buildAcceptable` are local/authoring concerns — not multiplayer gates
- Public repo omits scenes, generated builds, and licensed store art

---

## Documentation index

| Doc | Audience |
|-----|----------|
| [PLAYER_MANUAL.md](PLAYER_MANUAL.md) | Players |
| [MULTIPLAYER_GUIDE.md](MULTIPLAYER_GUIDE.md) | Classmates joining online |
| [OPERATIONS.md](OPERATIONS.md) | JacobForges — publish & host |
| [CREDITS.md](CREDITS.md) | Attribution |
| [LEGAL/README.md](LEGAL/README.md) | Portfolio EULA placeholder |
