# Deep Train Academy — Player Manual

**Game:** Hub (Deep Train Academy)  
**Author:** [JacobForges](https://github.com/JacobForges)  
**Version:** Portfolio demo — see [PATCH_NOTES.md](PATCH_NOTES.md)

Welcome to Deep Train Academy — explore a procedural Florida karst surface, descend into lava-tube caves, and command your AI squadmate through chat or voice.

---

## Platforms

| Platform | Install |
|----------|---------|
| **macOS** | `DeepTrainAcademy.app` from JacobForges (Intel 64-bit build for Vivox voice) |
| **Windows** | `DeepTrainAcademy.exe` — Windows x64; Vivox voice supported; allow mic access when prompted |

---

## Getting started

1. Launch the game and sign in (guest is fine for class sessions).
2. Watch the intro briefing (first run only) or skip from the title menu.
3. Choose **Play Solo** to explore alone, or **Host Session** / **Play Online** for multiplayer with classmates.
4. Complete onboarding if prompted — name your agent, pick a look, confirm your character sheet.

**Title menu footer:** *Checking for updates…* runs automatically. If the primary catalog is down, you may see *Using backup catalog (GitHub).* With no internet, *No internet — solo & LAN host still work.* — Solo still works; Play Online needs connectivity and a classmate hosting.

See [MULTIPLAYER_GUIDE.md](MULTIPLAYER_GUIDE.md) for online play with classmates.

---

## Movement & combat

| Action | Default key |
|--------|-------------|
| Move | **W A S D** |
| Look | Hold **Right Mouse Button** |
| Attack | **Left Mouse Button** |
| Bash | **Q** |
| Heal (hotbar slot 1) | **1** |
| Hotbar slots 2–6 | **2** – **6** |
| Craft | **G** |
| Challenge panel | **B** |
| Pause / UI | **Esc** |

The control strip at the bottom of the screen summarizes these during gameplay.

---

## Chat & agent commands

Open chat with **T**. Type naturally to talk to **your** agent — no prefix required.

| Prefix / input | Channel | Who hears it |
|----------------|---------|--------------|
| Plain text (`hold`, `follow`, `explore`) | Command | Your agent + you |
| `@ message` | Global | Everyone in the session |
| Voice command (toggle ON) | Command | Your agent; nearby players see a local transcript |

Press **Esc** to close chat and return to gameplay.

### Common agent commands

Type `help` in chat for the full list. Highlights:

- **explore** — scout the route  
- **follow** / **hold** / **rush** — movement orders  
- **train** — practice jumps and gaps  
- **battle** — hunt enemies  
- **course** — obstacle course builder  
- **trainer** — Trainer Bot Q&A  
- **export ai** / **import ai** — move agent vault between maps  

Full command reference: [COMPETITION_AGENT_GAME.md](COMPETITION_AGENT_GAME.md).

---

## Voice chat (multiplayer)

When you **Host Session** or **Play Online**, Vivox positional voice attaches automatically (25 m range).

| Action | Default key |
|--------|-------------|
| Push-to-talk | Hold **V** |
| Always-on voice | Disable PTT in **O** Settings |

Voice fades with distance — players far away are inaudible, matching local chat proximity.

---

## Agent voice commands

Toggle **Voice ON / Voice OFF** at the top-right of the chat panel. When enabled:

1. The game listens via your microphone (simple voice-activity detection).
2. Speech is sent to a **cloud STT** endpoint (configured by the operator).
3. Transcripts are parsed with the same command set as typed chat (`AgentCommandParser`).
4. Listening **pauses while you hold PTT (V)** so voice chat and commands do not fight.

Configure cloud STT before expecting voice commands to work — see [OPERATIONS.md](OPERATIONS.md).

---

## Inventory & character

| Panel | Key |
|-------|-----|
| Inventory | **I** |
| Character sheet | **C** |
| Skill tree | **K** |
| Settings | **O** |

Settings include look sensitivity, SFX volume, graphics quality, push-to-talk, and voice-command toggles.

---

## Agent eye & training

Click the **agent eye** PiP (bottom-right) to watch your agent’s view and issue quick commands from the action rail.

Train your agent by talking, completing courses, and using **Train Agent** when the progress bar fills.

---

## Solo vs online

| Mode | Description |
|------|-------------|
| **Play Solo** | Full world + agent; no Relay required |
| **Host Session** | You host up to 24 classmates via Unity Relay + Lobby — your laptop stays on (**teamwork now**) |
| **Play Online** | Join a classmate's **Host Session** (`DeepTrainAcademy-Listen`) |

Content version and world seed must match the host for Play Online — the title menu footer shows update status.

**Class teamwork:** one person clicks **Host Session**; everyone else clicks **Play Online**.

---

## Quit & save

- **Logout / Quit** in Settings runs a graceful exit (Vivox leave, network teardown, profile save).
- Agent progress saves locally under `Application.persistentDataPath/Competition/`.
- Registered Unity Player Accounts can sync profile data via Cloud Save.

---

## More help

- [MULTIPLAYER_GUIDE.md](MULTIPLAYER_GUIDE.md) — host, join, classmates  
- [PATCH_NOTES.md](PATCH_NOTES.md) — current feature set  
- [CREDITS.md](CREDITS.md) — author & attribution  
- [LEGAL/README.md](LEGAL/README.md) — portfolio license placeholder  
