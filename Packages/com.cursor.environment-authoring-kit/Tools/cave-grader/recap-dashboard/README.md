# Recap Dashboard (optional)

Local React UI for observing recap artifacts and tuning `DemoRecapTimeline.json` **between** compose passes. Does not change the restored Unity → `--no-narrator` → Terminal Personal Voice flow unless you click a compose button.

## Run

One command (recommended):

```bash
cd ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
bash start-recap-dashboard.sh ~/Library/EnvironmentKit/DemoCapture/<timestamp>
```

Or split terminals:

```bash
python3 recap-dashboard-server.py --capture ~/Library/EnvironmentKit/DemoCapture/<timestamp>
cd recap-dashboard && npm install && npm run dev
```

Open http://127.0.0.1:5173/ (Vite proxies `/api` to port 8765).

Unity can auto-open this after recording stops (Hub: **Open recap dashboard before compose**). Click **Proceed to compose** to release the gate.

Agent API: `../RECAP_AGENT_API.md`.

Production static build:

```bash
npm run build
python3 recap-dashboard-server.py
# open http://127.0.0.1:8765/
```

## Safe defaults

- Saving timeline rejects `videoPlaybackFactor != 1.0`.
- Narration compose uses `run-recap-terminal.sh` so Personal Voice stays in Terminal.app.
- Unity can gate compose until you proceed from this UI (or Hub **Skip recap review**).
