# AI Coach

The **Copilot rail** (right sidebar) and coach API power training advice for behavior cloning.

## Engines

| ID | Label | When to use |
|----|-------|-------------|
| `auto` | Auto (recommended) | Cursor key if set → else Gemini → else rules |
| `cursor` | Cursor API | You have a Cursor API key |
| `gemini-3.5-flash` | Google Gemini | You have a Gemini / AI Studio key |
| `local-gguf` | Local LLM | llama.cpp / MLX OpenAI-compatible server |
| `rules-only` | Offline | No network, template responses |

Configure under **Settings** or during **onboarding**.

## Priority (Auto mode)

```
1. Cursor API (@cursor/sdk, model composer-2.5)
2. Google Gemini (gemini-2.0-flash)
3. Rules-only fallback templates
```

## Keys

| Variable | UI field | Notes |
|----------|----------|-------|
| `CURSOR_API_KEY` | Cursor API key | Your key only — not shared |
| `GEMINI_API_KEY` | Gemini API key | From Google AI Studio |

Stored in `companion_settings.json`. Never commit to git.

## Disclosure mode

Copilot can split responses into **objective facts** vs **coaching strategy** (toggle in rail). Helps students separate telemetry from advice.

## Local LLM

Default endpoint:

```
http://127.0.0.1:8080/v1/chat/completions
```

Compatible with OpenAI chat schema. Change in Settings.

## API

`POST /api/coach` — see [API_REFERENCE.md](API_REFERENCE.md).

Response includes `provenance`: `cursor-api`, `gemini-user-key`, `rules-only`, etc.
