# Online test checklist (JacobForges)

Run this **before** handing builds to class.

**Model:** one classmate **Host Session**, others **Play Online** (listen-server teamwork — no VPS).

Full setup: [MULTIPLAYER_FREE_SETUP.md](MULTIPLAYER_FREE_SETUP.md) · [OPERATIONS.md](OPERATIONS.md)

---

## Dashboard & Editor (one-time per machine)

- [ ] Unity Dashboard: **Auth**, **Relay**, **Lobby** enabled (Vivox for voice)
- [ ] Editor Project ID matches Dashboard
- [ ] **Game → Setup Portfolio Menu (MainScene)** done
- [ ] **Game → Setup Vivox** (if testing voice)

---

## Host Session teamwork test

### Host machine (classmate A)

- [ ] Sign in (guest OK) → footer **Up to date** (or matching content/seed)
- [ ] Click **Host Session**
- [ ] Console: `[Multiplayer] Hosting DeepTrainAcademy-Listen · join via Quick Join`
- [ ] HUD: **Listen host** · spawn OK

### Join machine (classmate B — built `.app` / `.exe` or second Editor)

- [ ] Sign in (guest OK) → footer content matches host
- [ ] Click **Play Online**
- [ ] HUD: **Listen host** · connected count ≥ 2
- [ ] Spawn in world; movement + interaction OK
- [ ] (Optional) Voice ON → Vivox joins; positional chat works

---

## Pass criteria

| Check | Expected |
|-------|----------|
| Listen host log | `[Multiplayer] Hosting DeepTrainAcademy-Listen` |
| Listen client HUD | `Listen host · 2+ connected` |
| Play Online | Joins latest `DeepTrainAcademy-Listen` lobby only |
| No session | *No live session — ask a classmate to Host Session* |
| Content failover | Stop primary manifest host — footer **Using backup catalog (GitHub).** when `githubManifestUrl` is set |
| Offline boot | Disable Wi‑Fi — footer **No internet — solo & LAN host still work.**; Solo works; Play Online blocked |

---

## If something fails

1. Confirm host still has **Host Session** active (laptop on, not quit)
2. Unity Dashboard project ID + Relay/Lobby quotas
3. Content `worldSeed` / `contentVersion` match ([OPERATIONS.md](OPERATIONS.md) manifest section)
4. Both clients signed in to UGS (guest OK)
