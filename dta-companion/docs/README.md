# DTA Training Companion — Documentation

**Deep Train Academy™** external training laboratory · © 2026 **JacobForges**

| Doc | Audience | Contents |
|-----|----------|----------|
| [GETTING_STARTED.md](GETTING_STARTED.md) | Everyone | Install, first launch, onboarding |
| [ONBOARDING.md](ONBOARDING.md) | Players & teachers | Step-by-step setup wizard walkthrough |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Developers | How companion ↔ Unity sync works |
| [API_REFERENCE.md](API_REFERENCE.md) | Integrators | HTTP API routes |
| [MODEL_MARKET.md](MODEL_MARKET.md) | Players | Brain marketplace & 3 model slots |
| [AGENT_STUDIO.md](AGENT_STUDIO.md) | Players | Cosmetics editor & store |
| [COACH.md](COACH.md) | Players | AI coach engines & API keys |
| [FIREBASE_README.md](FIREBASE_README.md) | Admins | Optional cloud auth |
| [PLAYER_FLOW.md](PLAYER_FLOW.md) | Players | Training loop expectations |
| [LEGAL.md](LEGAL.md) | Everyone | License, copyright, trademarks |

## Quick answers

**Does it work?** Yes — `npm run build` and `npm run lint` pass. Run `npm run dev` (dev) or `Start-DTA-Companion` (shipped build). The UI needs the local Express server; opening `dist/index.html` alone will not work.

**What is it for?** Train and manage squadmate AI agents *outside* the Unity game: coaching, ONNX brain selection, checkpoint deploy, cosmetics, and classroom tools — while the game handles play, sample collection, and auto-import.

**First launch?** A 6-step onboarding wizard runs automatically until completed. See [ONBOARDING.md](ONBOARDING.md).

## Legal (summary)

Proprietary license — free for educational/personal non-commercial use. Commercial use requires a separate license from JacobForges. See [../LICENSE](../LICENSE), [../COPYRIGHT.md](../COPYRIGHT.md), [../TRADEMARK.md](../TRADEMARK.md).
