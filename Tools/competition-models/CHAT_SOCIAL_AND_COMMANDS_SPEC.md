# Chat, commands & social — global queue + proximity + agent cooldown

**Game-side only.** Extends `GAMEPLAY_RL_AND_CHAT_SPEC.md`.  
**Does not** change setup/onboarding ONNX.

---

## What you want (confirmed)

1. **Player commands their agent** — direct orders (“hold”, “rush”, “attack”, “defend”, “follow me”).
2. **Global chat room** — all **human players** and all **agents** may post; everyone sees it in **strict queued order** (no simultaneous spam).
3. **Agent send cooldown** — each agent waits **≥ 15 seconds** between messages **it** sends (auto-bark or reply).
4. **Proximity chat** — agents (and optionally players) also talk in **local text** when **close enough** in the world; only nearby participants receive those lines.
5. **Agents talk to each other** — when two agents are within proximity radius, their messages can target each other (local channel), not only humans.

---

## Channel model (four lanes, one display queue)

| Channel | Who speaks | Who hears | Purpose |
|---------|------------|-----------|---------|
| **Global** | Players, agents (cooldown) | Everyone in session | Session-wide room |
| **Proximity** | Players, agents (cooldown) | Entities within `R` meters | “Overheard” local talk |
| **Command** | Owner player only | Owner’s agent + UI ack | Orders (not public roleplay) |
| **System** | Game | Everyone | Checkpoints, punishments, season |

**Display:** One **merged timeline** sorted by `sequenceId` (host-assigned). Each line tagged: `[Global]`, `[Local]`, `[Command]`, `[System]`.

**Chaos control:**

- Host (or dedicated chat service) assigns monotonic `sequenceId`.
- Messages buffered max **2 lines/sec** session-wide for **agent-generated** posts (humans: optional 1/sec soft limit).
- **Per-agent cooldown:** `now - lastAgentSendUtc >= 15s` or message rejected / deferred to queue tail.

---

## Proximity rules

```
proximityRadiusM = 25   // tune per map
```

- Each networked entity reports `position` (player pawn or agent pawn).
- **Proximity message** delivered to clients whose listener position is within `R` of **speaker** position.
- **Agent ↔ agent:** both must be within `R` of each other (or within R of speaker — use speaker-centric: hear if `dist(listener, speaker) <= R`).
- **Global** messages ignore distance (everyone gets them).
- Same utterance may **not** duplicate in global and local unless agent chooses “shout” (v2); v1: **either** global **or** local per message.

**Local-world note:** world geometry is scene-static and shared in portfolio MP; agent/player **positions must be network-synced** for proximity to mean anything. Command channel is local to owner client always.

---

## Player → agent commands

### UI

- Chat input routing (Hub runtime):
  - **Plain text** → direct to owner’s agent (`[A]` command channel) — e.g. `hold`, `follow`, `how are you?`
  - **`@` prefix** → global session broadcast (`[G]`)
  - UI **Command** strip on agent eye — same orders without typing

### Processing

```
Player command text
  → competition_chat_intent.onnx (command subset)
  → CommandType enum
  → AgentCommandState on owner's agent (duration or until complete)
  → Gameplay ONNX obs includes command_one_hot + time_remaining
  → RL policy respects overrides (e.g. HOLD zeros move actions)
```

### Command types (v1)

| Intent | Effect on agent |
|--------|-----------------|
| `hold` | Zero locomotion, defend ready |
| `follow` | Steer toward owner player |
| `rush` | Boost move bias toward goal |
| `attack` | Force attack_logit high |
| `defend` | Force defend 3s |
| `skill1..3` | Trigger hotbar slot |
| `cancel` | Clear override |

Commands **do not** appear in global chat (optional echo: “You ordered: hold” in command tab only).

---

## Agent messages (auto + reply)

### Sources

1. **Bark head** — gameplay ONNX proposes `bark_category` on events.
2. **Intent reply** — player `how are you?` (direct agent line) → template response on command channel.
3. **Agent-initiated social** — proximity detect other agent → optional `social_bark` if cooldown clear.

### Cooldown (hard rule)

```csharp
bool CanAgentSpeak(agentId) =>
    (UtcNow - lastSendByAgent[agentId]).TotalSeconds >= 15;
```

If bark fires during cooldown → **queue deferred** with `earliestSendUtc` or drop (v1: defer one line max).

### Proximity agent ↔ agent

```
Each agent tick (1 Hz social, not 10 Hz policy):
  nearestOtherAgent = within R
  if nearestOtherAgent && CanAgentSpeak(self) && roll < socialChance(z):
    propose local message (template: greet, taunt, cooperate, warn)
    host enqueues proximity message
```

Personality `z` biases chattiness; cooldown prevents spam.

---

## Network architecture (portfolio multiplayer)

**Authority:** host assigns `sequenceId`, validates cooldowns, fans out.

```
Client                          Host
  |-- ChatSubmit(channel, text) -->|
  |                                 | validate, cooldown, sequence++
  |<-- ChatBroadcast(payload)-------|  (NGO RPC or custom messaging)
```

**Payload:**

```json
{
  "sequenceId": 1042,
  "channel": "global|proximity|command|system",
  "speakerKind": "player|agent",
  "speakerId": "uuid",
  "displayName": "…",
  "text": "…",
  "position": [x,y,z],
  "utc": "…"
}
```

Clients filter proximity lines locally by distance to `position`.

**Vivox:** voice stays separate; **text** via NGO/Relay custom messages or Lobby chat API — do not mix Personal Voice pipeline.

---

## ONNX / gameplay model (unchanged scope split)

| Model | Role |
|-------|------|
| `competition_setup.onnx` | Onboarding only |
| `competition_gameplay.onnx` | RL + combat + `bark_category` proposal |
| `competition_chat_intent.onnx` | Player text → intent (incl. command vs social vs status) |

Chat **delivery** is systems code (queue, cooldown, RPC), not ONNX.

---

## UI (Unity, after build)

- **Chat panel:** scroll timeline (merged queue), channel color tags.
- **Input:** Global / Local / Command selector.
- **Agent roster:** mute agent auto-chat per agent (optional).
- **Cooldown indicator:** subtle “agent listening…” for 15s after agent speaks.

Reuse `PortfolioUiKit` styling.

---

## Safeguards

| Risk | Mitigation |
|------|------------|
| Chat flood | Host queue + agent 15s cooldown + 2 agent msgs/sec session cap |
| Offensive text | Local block list; no LLM generation |
| Command griefing | Commands only affect **own** agent |
| Desync proximity | Host position authority on networked pawns |
| Cooldown bypass | Host rejects; server timestamp |
| Agent spam in global | Default barks → **proximity**; global only for major events (goal, death) |

---

## Implementation TODO (add to master list)

### Phase Chat-A — Core queue (no ONNX)
- [ ] `ChatMessage.cs` payload + `ChatChannel` enum
- [ ] `ChatQueueHost.cs` — sequence, broadcast, rate limits
- [ ] `ChatQueueClient.cs` — display timeline, filter proximity
- [ ] `AgentCooldownTracker.cs` — 15s per `agentId`

### Phase Chat-B — Commands
- [ ] `AgentCommandState.cs` + policy obs injection
- [ ] Command UI mode + parsing

### Phase Chat-C — Intent ONNX
- [ ] Train `competition_chat_intent.onnx` (command + social + status intents)
- [ ] `ChatIntentResolver.cs`

### Phase Chat-D — Agent social
- [ ] Proximity scan 1 Hz
- [ ] Agent-agent template pairs
- [ ] Wire bark head → enqueue if cooldown ok

### Phase Chat-E — Multiplayer
- [ ] NGO RPC `SubmitChat` / `ReceiveChat`
- [ ] Sync agent pawn positions when “my agent” visible to others

---

## Training while world gen runs (safe)

- [ ] Synthetic `chat_intent_trainer.py` — includes **command** intents (`hold`, `attack`, …) and **social** (`greet`, `taunt`, …)
- [ ] Template JSON files for global vs proximity tone
- [ ] **No** Unity scripts until Hub build completes

---

## Yes — this matches your intent

- **Command** your agent: yes, private command channel + policy override.  
- **Global freedom** to chat: yes, ordered queue for all players and agents.  
- **No chaos:** sequence queue + agent 15s cooldown + session rate cap.  
- **Agents near each other:** yes, proximity channel for agent-agent (and local player) text.
