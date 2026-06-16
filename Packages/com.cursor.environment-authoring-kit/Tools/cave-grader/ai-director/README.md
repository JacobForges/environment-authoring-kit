# AI Director

Studio UI for Environment Kit recap films — separate from the build planner and legacy recap dashboard.

## User-provided media (no upload API)

Drop files directly into the capture run folder before compose:

- **Play Mode B-roll:** `{capture}/uploads/playthroughs/playtest-001.mp4` (`.mov` also works)
- **Full-screen build recording:** `{capture}/uploads/build-screen.mov` (symlink or copy)

Compose scripts pick these up automatically (`hybrid_recap_common.resolve_playthrough_recordings`).

## Run

```bash
bash start-ai-director.sh /path/to/DemoCapture/<timestamp>
```

Unity opens this when **Open AI Director before compose** is enabled in Hub after a manual recording session — not during normal builds.

- API: `http://127.0.0.1:8767`
- Dev UI: `cd ai-director && npm run dev` → `http://127.0.0.1:5174`

## Layout

- **Left (~25%)**: Director chat + AI Responder + brief checklist
- **Right (~75%)**: Preview, Segments, Pacing, Voice persona, Script, Cards, Export (YouTube)

Pipeline unchanged: silent compose → script review → Personal Voice → final `DemoRecapPresentation.mp4`.

## License / Copyright

- Tooling is covered by `../LICENSE_TOOL.md` (JacobForges non-commercial tooling terms).
- Copyright (c) JacobForges.
- Game/project code and content remain proprietary and protected.
