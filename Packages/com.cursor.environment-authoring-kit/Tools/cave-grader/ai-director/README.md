# AI Director

Studio UI for Environment Kit recap films — separate from the build planner and legacy recap dashboard.

## Run

```bash
bash start-ai-director.sh /path/to/DemoCapture/<timestamp>
```

Unity opens this automatically when **Open AI Director before compose** is enabled in Hub.

- API: `http://127.0.0.1:8767`
- Dev UI: `cd ai-director && npm run dev` → `http://127.0.0.1:5174`

## Layout

- **Left (~25%)**: Director chat + AI Responder + brief checklist
- **Right (~75%)**: Preview, Segments, Pacing, Voice persona, Script, Cards, Export (YouTube)

Pipeline unchanged: silent compose → script review → Personal Voice → final `DemoRecapPresentation.mp4`.
