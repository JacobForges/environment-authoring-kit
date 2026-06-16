# API Reference

Base URL: `http://localhost:3000` (dev) or `http://127.0.0.1:37123` (shipped).

All POST bodies are JSON. Errors return `{ "error": "message" }` with 4xx/5xx.

## Health & settings

### `GET /api/health`

```json
{
  "status": "ok",
  "hasCursorKey": true,
  "hasGeminiKey": false,
  "firebase": false,
  "coachEngine": "auto",
  "modelSlots": []
}
```

### `GET /api/settings` / `POST /api/settings`

Load or patch companion settings. Masked keys return `••••` + last 4 chars.

### `GET /api/onboarding`

```json
{
  "needsOnboarding": true,
  "onboardingVersion": 1,
  "savedVersion": 0,
  "displayName": "",
  "syncFolder": ".../checkpoints/agent_local",
  "companionRoot": ".../DeepTrainAcademy"
}
```

### `POST /api/onboarding/complete`

```json
{
  "displayName": "Scout",
  "coachEngine": "auto",
  "cursorApiKey": "optional",
  "geminiApiKey": "optional"
}
```

## Firebase

### `GET /api/firebase/config`

Returns public Firebase web config if `firebase-applet-config.json` exists, else `{ "enabled": false }`.

## Sync

### `GET /api/syncSnapshot?agentId=`

Player name, active agent, row counts, sync folder path. Proxies Unity `:8765` when available.

## Coach

### `POST /api/coach`

```json
{
  "userPrompt": "How do I train battle focus?",
  "focus": "battle",
  "engine": "auto",
  "agentContext": {},
  "chatHistory": [{ "role": "user", "text": "..." }]
}
```

Response: `{ "text", "confidence", "provenance" }`

## Models

### `GET /api/models/catalog`

Merged bundled + GitHub + Hugging Face entries.

### `GET /api/models/slots`

Installed slot metadata.

### `POST /api/models/install`

```json
{ "agentId": "agent_train_0c1", "catalogId": "...", "slot": 1, "url": "optional" }
```

## Deploy & train

### `POST /api/deploy` / `POST /api/train`

Checkpoint deploy and training studio actions. Proxies Unity when host is up.

## Cosmetics

### `GET /api/cosmetics/catalog`

Seed items from `catalog/cosmetics.json`.

### `GET /api/cosmetics/inventory?agentId=`

Owned items, equipped slots, currency.

### `POST /api/cosmetics/equip`

```json
{ "agentId": "...", "slot": "helmet", "itemId": "cos_helmet_01" }
```

`itemId: null` unequips.

### `POST /api/cosmetics/purchase`

```json
{ "agentId": "...", "itemId": "cos_trail_02" }
```

### `POST /api/cosmetics/appearance`

Bulk save equipped map; writes `agent_appearance.json` to sync folder.
