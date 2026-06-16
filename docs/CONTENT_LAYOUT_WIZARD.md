# Content Layout Wizard — NPCs, Enemies & Props

Interactive Q&A planner (tab inside **AI Build Wizard**, port **8766**) for **MainScene** content — not terrain tiles.

## Open the wizard

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
npm run build-wizard   # if UI changed
python3 ensure-build-wizard.py
```

Or **Window → Environment Kit → Hub → Build Complete Cave**, then switch to the **Content layout** tab.

URL must include hub root: `http://127.0.0.1:8766/?hub=/path/to/Hub`

Requires `CURSOR_API_KEY` in `Tools/cave-grader/.env` (same as world planner).

## MainScene vs world planner

| Tool | What it changes |
|------|-----------------|
| **World build** tab | Procedural terrain, play disk, caves, planner markers |
| **Content layout** tab | NPCs, dialog, enemies, props in **MainScene** at world coordinates |
| **Video / Music** tabs | AI Director Studio — recap video and music production (after pipeline tabs) |

Content layout **does not** re-run the world generator. It adds a `ContentLayout_Authored` root to whatever scene is open in the Editor (use **Open MainScene && Apply** if unsure).

## AI Responder

On the **Content layout** tab, use preset buttons:

- **Town hub** — Polyvania quest/ambient NPCs
- **Arena trainers** — coaches + training-grade shop blockers  
- **Full MainScene** — complete checklist pass

Responder fills Q&A automatically, then stops at **Review brief** for your approval.

API: `POST /api/content/auto-respond/step` · `POST /api/content/auto-respond`

## Workflow

1. **Content layout** tab → describe your request (NPCs, enemies, props, dialog, shops, blockers).
2. Answer **one checklist question per turn** (14 topics) or type **move on** when ready.
3. **Review** JSON brief → **Approve & write brief**.
4. Unity: **Game → Export Content Layout World Snapshot** (scene-measured zones), then **Game → Open MainScene && Apply Surface Content Layout**.
5. **Play Mode** — walk to NPCs, open **Talk / Shop / Leave** dialog; Console shows `[ContentLayoutSmoke] PASS` when required NPCs were used.

## Output files

| File | Purpose |
|------|---------|
| `Assets/EnvironmentKit/Generated/CaveBuildContentLayoutSession.json` | Chat + checklist state |
| `Assets/EnvironmentKit/Generated/CaveBuildContentLayoutBrief.json` | Unity contract (NPCs, dialog, patrols, props) |
| `Assets/EnvironmentKit/Generated/CaveBuildContentLayoutWorldSnapshot.json` | Scene-measured zones (export from Unity before approve/apply) |
| `Assets/EnvironmentKit/ContentLayout/ContentLayoutWorldAnchors.json` | Slot templates (offsets per entity id) |

## Brief schema (summary)

- **npcs** — `id`, `role` (quest_giver, trainer, ambient, blocker_gate), `worldPosition`, hybrid `dialog` (nodes + shop)
- **enemies** — spawner position + `patrolWaypoints[]`
- **props** — position + `textureHint`
- **blockers** — `agent_training_level` (min grade), `quest_completed` (questId)
- **playmodeSmokeTest** — `requiredNpcIds[]` for verification

## Runtime (gameplay)

Scripts under `Assets/Scripts/WorldContent/`:

| Script | Role |
|--------|------|
| `ContentLayoutRuntime` | Loads brief at play |
| `NpcInteractable` | Trigger → open dialog |
| `NpcDialogUI` | Talk branches + Shop tab + Leave |
| `ContentLayoutProgress` | Quest flags + agent training grade gates |
| `ContentLayoutSmokeTest` | Play Mode pass log |

### Blockers

- Set `ContentLayoutProgress.AgentTrainingGrade` from competition/training code when wired.
- Complete quests: `ContentLayoutProgress.CompleteQuest("quest_id")`.

## API routes

| Method | Path |
|--------|------|
| GET | `/api/content/session` |
| POST | `/api/content/start` |
| POST | `/api/content/chat` |
| POST | `/api/content/resume` |
| POST | `/api/content/approve` |
| POST | `/api/content/reset` |

## Your choices (session config)

- **Scene:** MainScene as-is at Play
- **NPC roles:** trainer, quest giver, ambient, blocker gate, enemy patrol
- **Dialog:** hybrid Talk + Shop
- **Blockers:** agent training level + quest completed
- **Verify:** Play Mode smoke test
