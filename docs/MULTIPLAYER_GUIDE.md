# Deep Train Academy — Multiplayer Guide

**Author:** [JacobForges](https://github.com/JacobForges)

This guide covers playing Hub online with classmates — hosting, joining, voice, and content updates.

Setup reference for operators: [MULTIPLAYER_FREE_SETUP.md](MULTIPLAYER_FREE_SETUP.md) · [OPERATIONS.md](OPERATIONS.md)

---

## What you need

| Requirement | Detail |
|-------------|--------|
| Client build | macOS `.app` **or** Windows `.exe` from JacobForges |
| Content match | Same **content version** and **world seed** as the host |
| Internet | Relay + Lobby (no port forwarding) |
| Unity account | Anonymous sign-in works; Player Accounts optional |
| Microphone | For Vivox voice (online sessions) — allow OS mic permission |
| Cloud STT env | For agent **voice commands** (operator configures) |

No credit card is required on Unity’s free tier for class-scale sessions.

---

## How to play online (teamwork model)

1. **One person hosts** — click **Host Session** and keep their laptop on during class.
2. **Everyone else joins** — click **Play Online** to find the host's `DeepTrainAcademy-Listen` lobby automatically.
3. **No dedicated server required** — classmates take turns hosting; no VPS or cloud VM setup.

**Caps:** 24 players per session (under Relay free-tier design target).

---

## Title menu options

| Button | What it does |
|--------|----------------|
| **Play Solo** | Local world + agent; no Relay |
| **Host Session** | Create `DeepTrainAcademy-Listen` lobby; you are host + Relay |
| **Play Online** | Join the latest classmate **Host Session** |
| **Sign in** | Unity Player Accounts or guest |

Footer status shows content update checks: *Checking…* · *Up to date* · *Update available*.

---

## Host Session (listen server)

1. Sign in from the title menu.
2. Wait until the footer shows matching content (download updates if prompted).
3. Click **Host Session**.
4. Relay allocates; Vivox positional voice joins on the lobby channel.
5. Tell classmates to click **Play Online** — they join your listen lobby.

Your machine must stay on and connected for the duration of the session.

---

## Play Online (join)

1. Launch the game — let the menu check for map updates.
2. Sign in.
3. Click **Play Online**.
4. The client finds the latest **Host Session** lobby (`DeepTrainAcademy-Listen`).
5. On success, HUD shows **Listen host** and player count.

If join fails with a **content version** or **world seed mismatch**, restart from the menu after downloading the latest manifest.

If no session is live: *No live session — ask a classmate to Host Session.*

---

## Voice in session

- **Positional Vivox** — 25 m audible range; hold **V** for push-to-talk (default).
- **Always-on voice** — disable PTT in **O** → Settings.
- Voice attaches on host/join; leaves on quit or disconnect.
- **Windows x64** — Vivox supported; allow microphone in Windows privacy settings.
- **macOS player builds** — **Intel 64-bit** required for Vivox (Apple Silicon-only `.app` not supported yet; Editor on M-series Macs is fine for dev).

---

## Chat in session

- **T** — open chat panel.
- Plain text → your agent (command channel).
- `@ text` → global broadcast to all players.
- **Voice Cmd** toggle — cloud STT agent commands (see [PLAYER_MANUAL.md](PLAYER_MANUAL.md)).
- Nearby players can see voice-command transcripts on the local proximity channel.

---

## Content updates (classmates)

JacobForges publishes map + config updates via **Game → Publish Content Manifest**. GitHub (raw URLs or Releases) is used for static content sync only — not game hosting.

Classmates need:

1. The same app build (or newer).
2. A reachable `hub-content-manifest.json` URL (env `HUB_CONTENT_MANIFEST_URL` or baked `remoteManifestUrl` / `githubManifestUrl`).
3. Menu boot download of `map-bundle.zip` when the footer shows an update.

After the footer reads **Up to date** with matching seed, **Play Online** is allowed.

---

## What classmates should install

1. **Deep Train Academy** client from JacobForges:
   - **macOS:** `DeepTrainAcademy.app` (Intel 64-bit for Vivox voice)
   - **Windows:** `DeepTrainAcademy.exe` (x64; Vivox voice supported)
2. No separate Vivox install — voice is embedded in both clients.
3. Allow **microphone** permission when the OS prompts (required for voice).
4. Optional: set `HUB_CONTENT_MANIFEST_URL` if not baked into the shipped manifest.
5. Optional: headphones for voice.

They do **not** need Unity Editor, Relay dashboard access, or VPS credentials.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| No live session | Ask a classmate to **Host Session** |
| No voice | Operator ran **Game → Setup Vivox**; check mic permissions (macOS Privacy or Windows Settings → Privacy → Microphone) |
| Play Online blocked | Wait for content download; check version/seed footer |
| Relay join failed | Lobby full or expired — retry or host a new session |
| Voice commands silent | Operator set `HUB_STT_API_URL` + `HUB_STT_API_KEY` |
| Can't hear host | Confirm PTT (**V**) or disable PTT in settings |

---

## Related docs

- [PLAYER_MANUAL.md](PLAYER_MANUAL.md) — controls & commands  
- [MULTIPLAYER_FREE_SETUP.md](MULTIPLAYER_FREE_SETUP.md) — Unity Dashboard checklist  
- [OPERATIONS.md](OPERATIONS.md) — publish manifest, Vivox setup, GitHub content sync  
