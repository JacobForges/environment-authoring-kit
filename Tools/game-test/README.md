# Hub game test pipeline

Automated wiring verification for everything already in `Assets/Scripts/` — **no new features**, only test + safe auto-fix + your **fix decisions**.

## Run from terminal (for agents / CI)

```bash
./Tools/game-test/run-game-tests.sh
```

Close the Unity Editor first (batchmode needs exclusive access).

**Pipeline:** setup solo → test → auto-wire → retest → write reports.

On failures → `Assets/Scripts/HubGameTestRemainderReport.json` with `fixDecision: pending` on each item.

## Fix decisions (your part — not “does it work?”)

For each failed test you choose **what the agent should do**:

| `fixDecision` | Meaning |
|---------------|---------|
| `use_suggestion` | Apply the bot’s suggested minimal wiring fix |
| `fix_wiring` | Agent fixes with smallest diff (you don’t specify how) |
| `custom` | You write `fixInstruction` — exact direction for the agent |
| `skip` | Don’t fix this session |
| `defer` | Later milestone |

**In Play Mode:** form opens after HUD **Run Game Tests**, or **Hub → Game Tests → Open Fix Decisions From Last Report**.

**Headless:** edit `HubGameTestRemainderReport.json` directly.

Example:

```json
{
  "fixDecision": "custom",
  "fixInstruction": "Wire hold/follow on CompetitionAgentPawn prefab only — no voice changes"
}
```

## Also runs

```bash
./Tools/competition-models/run-hub-smoke.sh   # ONNX + compile only (no Play Mode)
```

## License / Copyright

- Tooling in this folder is licensed under `Tools/LICENSE_JACOBFORGES_TOOL_NONCOMMERCIAL.md`.
- Copyright (c) JacobForges.
- The game/project code and content remain proprietary and protected.
