# Onboarding — First-Time Setup

The companion shows a **6-step setup wizard** when `onboardingComplete` is false or the saved `onboardingVersion` is older than the app version.

## Step 1 — Welcome

- Enter your **display name** (shown in the lab UI)
- Explains that this is an external lab, not in-game UI

## Step 2 — Purpose

Three cards summarize the split:

| Layer | Role |
|-------|------|
| **Unity game** | Play, collect samples, in-world training |
| **Companion** | Coach, brains, deploy, cosmetics |
| **Disk sync** | Shared checkpoint folder |

## Step 3 — Lab check

- Verifies `GET /api/health` — server must be running
- Shows your **checkpoint sync folder** path, e.g.  
  `~/Library/Application Support/DeepTrainAcademy/checkpoints/agent_local/`

If health fails:

```bash
# Development
cd dta-companion && npm run dev

# Shipped build
open DTACompanion/Start-DTA-Companion.command
```

Ensure Node.js is on your PATH.

## Step 4 — Coach

Choose engine:

| Engine | Behavior |
|--------|----------|
| **auto** (default) | Cursor API → Gemini → offline rules |
| **cursor** | Cursor only |
| **gemini-3.5-flash** | Gemini only |
| **rules-only** | Offline templates, no network |

Optionally paste **Cursor** and **Gemini** API keys. You can skip and configure later under **Settings**.

## Step 5 — Link game

1. Play Deep Train Academy until you have an agent
2. Use **Entrance → Generate link code** (optional) or rely on disk sync
3. Deploy from **Model Market** or **Deploy** — game imports on next launch

## Step 6 — Ready

Wizard saves settings and closes. Recommended first pages:

- **Agent Roster** — pick active agent
- **Agent Studio** — equip cosmetics
- **Copilot rail** — ask the coach a question

## Persistence

Saved to:

```
~/Library/Application Support/DeepTrainAcademy/companion_settings.json
```

Fields written on complete:

```json
{
  "onboardingComplete": true,
  "onboardingVersion": 1,
  "displayName": "Your Name",
  "coachEngine": "auto",
  "cursorApiKey": "...",
  "geminiApiKey": "..."
}
```

## Replay onboarding

**Option A** — Settings → **Run setup wizard again** (if exposed in UI)

**Option B** — Edit settings file:

```json
"onboardingComplete": false
```

Refresh the browser.

## API

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/onboarding` | `needsOnboarding`, paths, saved name |
| POST | `/api/onboarding/complete` | Finish wizard, save keys & name |

See [API_REFERENCE.md](API_REFERENCE.md).
