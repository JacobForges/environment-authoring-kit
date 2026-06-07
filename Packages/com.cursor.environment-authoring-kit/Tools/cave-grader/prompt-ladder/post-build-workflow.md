## Post-build Cursor workflow (mandatory after every cave generation)

Unity runs this sequence automatically when **auto-invoke after build** is enabled:

| Order | Phase | Cursor rung | What happens |
|-------|--------|-------------|----------------|
| 1 | Research | `research` | Web + papers → **plan table only** (no C#) |
| 2 | Compile gate | `compile_gate` | Fix **all** C# errors using plan + diagnostics |
| 3 | Ladder | `visual_shell` … | Up to 3 failing rungs, informed by research + memory |
| 4 | Verify | (user) | Remove Layered Shells → Build Complete Cave → Re-grade |

**Failure memory:** `CaveBuildAgentMemory.json` lists mistakes — do not repeat them.

**Full research catalog** is on disk; the prompt only lists a small subset per rung to avoid overload.
