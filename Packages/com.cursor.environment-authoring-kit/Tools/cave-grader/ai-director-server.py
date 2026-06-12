#!/usr/bin/env python3
"""AI Director API — studio UI for recap compose, segments, voice persona, chat.

Reuses recap-dashboard-server compose pipeline; serves ai-director static UI.

  python3 ai-director-server.py [--capture /path/to/DemoCapture/ts] [--port 8767]
  bash start-ai-director.sh [capture_path]
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import mimetypes
import os
import sys
from http.server import ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, unquote, urlparse

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

import build_planner as planner  # noqa: E402
import director_session as director  # noqa: E402

planner.load_dotenv(None)

DIRECTOR_DIR = _TOOLS / "ai-director"
DEFAULT_PORT = 8767

_spec = importlib.util.spec_from_file_location("recap_srv", _TOOLS / "recap-dashboard-server.py")
recap_srv = importlib.util.module_from_spec(_spec)
assert _spec.loader
_spec.loader.exec_module(recap_srv)

recap_srv.DASHBOARD_DIR = DIRECTOR_DIR


class DirectorHandler(recap_srv.Handler):
    """Extends recap API with /api/director/* routes."""

    def _resolve_cap_director(self, qs: dict, body: dict | None = None) -> Path | None:
        body = body or {}
        raw = body.get("capture") or qs.get("path", [None])[0] or (str(self.capture) if self.capture else None)
        return recap_srv.resolve_capture(raw)

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        path = unquote(parsed.path)
        qs = parse_qs(parsed.query)

        if path.startswith("/api/director/"):
            cap = self._resolve_cap_director(qs)
            if path == "/api/director/health":
                return self._json(
                    200,
                    {
                        "ok": True,
                        "server": "ai-director",
                        "dataRoot": str(recap_srv.resolve_envkit_root()),
                        "defaultCapture": str(self.capture) if self.capture else None,
                        "cursorApiConfigured": planner.cursor_api_configured(),
                        "cursorModel": os.environ.get("CAVE_CURSOR_MODEL", "composer-2.5"),
                    },
                )
            if not cap and path not in ("/api/director/health",):
                return self._json(404, {"error": "capture not found"})
            if path == "/api/director/session":
                try:
                    director.start_session(cap)
                except FileNotFoundError as exc:
                    return self._json(404, {"error": str(exc)})
                return self._json(200, director.public_session(cap))
            if path == "/api/director/segments":
                return self._json(200, {"segments": director.list_segments(cap), "capture": str(cap)})
            if path == "/api/director/voice-persona":
                return self._json(200, {"persona": director.read_voice_persona(cap), "capture": str(cap)})
            if path == "/api/director/voice-profile":
                return self._json(200, director.read_voice_profile(cap))
            if path == "/api/director/voice-test/audio":
                wav = director.voice_test_wav_path(cap)
                if not wav.is_file():
                    return self._json(404, {"error": "no voice test yet — click Test voice"})
                data = wav.read_bytes()
                return self._binary(200, data, "audio/wav")
            if path == "/api/director/export/youtube":
                return self._json(200, director.youtube_export_payload(cap))
            if path == "/api/director/presentation/video":
                kind = qs.get("kind", ["final"])[0]
                mp4 = cap / "DemoRecapPresentation.mp4"
                if kind == "silent":
                    for candidate in (
                        cap / "_presentation_compose/_final_video.mp4",
                        cap / "_presentation_compose_preview120/_final_video.mp4",
                    ):
                        if candidate.is_file():
                            mp4 = candidate
                            break
                if not mp4.is_file():
                    return self._json(404, {"error": "video not found"})
                data = mp4.read_bytes()
                return self._binary(200, data, "video/mp4")
            if path == "/api/director/segment/video":
                name = qs.get("name", [None])[0]
                work = qs.get("work", ["_presentation_compose"])[0]
                if not name or ".." in name or "/" in name:
                    return self._json(400, {"error": "name required"})
                mp4 = cap / work / "segments" / name
                if not mp4.is_file():
                    return self._json(404, {"error": "segment not found"})
                data = mp4.read_bytes()
                return self._binary(200, data, "video/mp4")
            return self._json(404, {"error": "unknown director route", "path": path})

        if path in ("/api/health", "/api/status"):
            payload: dict[str, Any] = {
                "ok": True,
                "server": "ai-director",
                "dataRoot": str(recap_srv.resolve_envkit_root()),
                "defaultCapture": str(self.capture) if self.capture else None,
                "agent": recap_srv.agent_status(),
            }
            if self.capture:
                payload["capture"] = recap_srv.capture_status(self.capture)
            return self._json(200, payload)

        rel = path.lstrip("/") or "index.html"
        for base in (DIRECTOR_DIR / "dist", DIRECTOR_DIR):
            candidate = (base / rel).resolve()
            if not str(candidate).startswith(str(base.resolve())):
                continue
            if candidate.is_file():
                data = candidate.read_bytes()
                ctype = mimetypes.guess_type(str(candidate))[0] or "application/octet-stream"
                self.send_response(200)
                self.send_header("Content-Type", ctype)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
                return

        return super().do_GET()

    def do_POST(self) -> None:
        parsed = urlparse(self.path)
        path = parsed.path
        if not path.startswith("/api/director/"):
            return recap_srv.Handler.do_POST(self)

        qs = parse_qs(parsed.query)
        try:
            body = self._read_json()
        except ValueError as exc:
            return self._json(400, {"error": str(exc)})

        cap = self._resolve_cap_director(qs, body)
        if not cap:
            return self._json(404, {"error": "capture not found"})
        if path == "/api/director/session/start":
            try:
                return self._json(200, director.start_session(cap))
            except FileNotFoundError as exc:
                return self._json(404, {"error": str(exc)})
        if path == "/api/director/chat":
            msg = body.get("message")
            bootstrap = bool(body.get("bootstrap"))
            try:
                return self._json(200, director.chat_turn(cap, msg, bootstrap=bootstrap))
            except Exception as exc:
                return self._json(500, {"error": str(exc)})
        if path == "/api/director/responder/step":
            preset = str(body.get("preset") or "student-portfolio")
            if body.get("runAll") or body.get("runUntilDone"):
                return self._json(200, director.responder_run_until_done(cap, preset))
            max_turns = int(body.get("maxTurns") or 1)
            return self._json(200, director.responder_step(cap, preset, max_turns=max_turns))
        if path == "/api/director/responder/run-until-done":
            preset = str(body.get("preset") or "student-portfolio")
            return self._json(200, director.responder_run_until_done(cap, preset))
        if path == "/api/director/voice-persona":
            patch = body.get("persona") if isinstance(body.get("persona"), dict) else body
            persona = director.write_voice_persona(cap, patch)
            return self._json(200, {"ok": True, "persona": persona, "profile": director.read_voice_profile(cap)})
        if path == "/api/director/voice-profile/sync":
            return self._json(200, director.sync_persona_from_approved(cap))
        if path == "/api/director/voice-test":
            text = body.get("text") or body.get("testPhrase")
            result = director.run_voice_test(
                cap,
                str(text) if text else None,
                open_player=bool(body.get("openPlayer", True)),
            )
            code = 200 if result.get("ok") else 500
            return self._json(code, result)
        if path == "/api/director/script/save":
            md = body.get("markdown") or body.get("script")
            if not isinstance(md, str) or not md.strip():
                return self._json(400, {"error": "markdown required"})
            return self._json(200, director.save_narration_script(cap, md))
        if path == "/api/director/voice-persona/assistant-adjust":
            result = director.assistant_adjust_voice(cap, str(body.get("instruction") or ""))
            code = 200 if result.get("ok", True) else 500
            return self._json(code, result)
        if path == "/api/director/script/chat":
            msg = body.get("message")
            bootstrap = bool(body.get("bootstrap"))
            try:
                return self._json(200, director.script_chat_turn(cap, msg, bootstrap=bootstrap))
            except Exception as exc:
                return self._json(500, {"error": str(exc)})
        if path == "/api/director/script/assistant-adjust":
            instruction = str(body.get("instruction") or "")
            if not instruction:
                return self._json(400, {"error": "instruction required"})
            result = director.assistant_adjust_script(cap, instruction)
            code = 200 if result.get("ok") else 400
            return self._json(code, result)
        if path == "/api/director/enter-editor":
            try:
                return self._json(200, director.enter_editor(cap))
            except RuntimeError as exc:
                return self._json(409, {"error": str(exc)})
        if path == "/api/director/return-briefing":
            return self._json(200, director.return_to_briefing(cap))
        if path == "/api/director/phase":
            phase = str(body.get("phase") or "")
            if not phase:
                return self._json(400, {"error": "phase required"})
            return self._json(200, director.set_phase(cap, phase))
        if path == "/api/director/export/youtube":
            return self._json(200, director.run_youtube_export(cap))
        if path == "/api/director/apply-brief":
            try:
                spec = recap_srv.read_timeline(cap)
                merged = director.apply_brief_to_timeline(cap, spec)
                recap_srv.write_timeline(cap, merged)
                return self._json(200, {"ok": True, "timeline": merged})
            except (FileNotFoundError, json.JSONDecodeError, ValueError) as exc:
                return self._json(400, {"error": str(exc)})
        return self._json(404, {"error": "unknown director route", "path": path})


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--capture", type=Path, help="Default capture folder for API calls")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT)
    ap.add_argument("--bind", default="127.0.0.1")
    args = ap.parse_args()

    cap = recap_srv.resolve_capture(str(args.capture) if args.capture else None)
    DirectorHandler.capture = cap

    httpd = ThreadingHTTPServer((args.bind, args.port), DirectorHandler)
    base = f"http://{args.bind}:{args.port}"
    print(f"AI Director API on {base}/")
    if cap:
        print(f"Default capture: {cap}")
    print(f"Data root: {recap_srv.resolve_envkit_root()}")
    print("One command (server + UI): bash start-ai-director.sh [capture_path]")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
