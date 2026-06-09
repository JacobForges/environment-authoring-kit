#!/usr/bin/env python3
"""Local API for Environment Kit AI build planner (port 8766)."""
from __future__ import annotations

import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import build_planner as planner

PORT = int(os.environ.get("BUILD_WIZARD_PORT", "8766"))
ACTIVE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json")
STATE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardState.json")
DIST = Path(__file__).resolve().parent / "build-wizard" / "dist"


def _hub_root(query: dict) -> Path | None:
    raw = (query.get("hub") or query.get("hubRoot") or [""])[0]
    if not raw:
        return None
    p = Path(raw).expanduser().resolve()
    return p if p.is_dir() else None


def _defaults() -> dict:
    return planner._defaults_config()


def _normalize(cfg: dict) -> dict:
    return planner._normalize_config(cfg)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def _json(self, code: int, payload: dict):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        if length <= 0:
            return {}
        return json.loads(self.rfile.read(length).decode("utf-8"))

    def _err(self, code: int, msg: str):
        return self._json(code, {"error": msg})

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        parsed = urlparse(self.path)
        q = parse_qs(parsed.query)
        hub = _hub_root(q)

        if parsed.path == "/api/health":
            return self._json(200, {"ok": True, "port": PORT, "mode": "ai-planner"})

        if parsed.path == "/api/planner/session":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"session": planner.public_session(hub)})

        if parsed.path == "/api/build/config":
            cfg = _defaults()
            if hub:
                active = hub / ACTIVE_REL
                if active.is_file():
                    try:
                        cfg = _normalize(json.loads(active.read_text(encoding="utf-8")))
                    except Exception:
                        pass
            return self._json(200, {"config": cfg, "hubRoot": str(hub) if hub else None})

        if parsed.path == "/api/planner/asset":
            if not hub:
                return self._err(400, "hub required")
            rel = (q.get("rel") or [""])[0]
            if not rel or ".." in rel:
                return self._err(400, "invalid rel")
            target = (hub / rel).resolve()
            if not str(target).startswith(str(hub.resolve())) or not target.is_file():
                return self._err(404, "not found")
            data = target.read_bytes()
            ctype = "image/png" if target.suffix.lower() == ".png" else "application/octet-stream"
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return

        if parsed.path == "/" or parsed.path.startswith("/assets"):
            return self._serve_static(parsed.path)

        return self._err(404, "not found")

    def do_POST(self):
        parsed = urlparse(self.path)
        q = parse_qs(parsed.query)
        hub = _hub_root(q)
        body = self._read_json()

        try:
            if parsed.path == "/api/planner/start":
                if not hub:
                    return self._err(400, "hub query required")
                msg = (body.get("message") or "").strip()
                if not msg:
                    return self._err(400, "message required")
                session = planner.start_session(hub, msg, bool(body.get("internetResearch")))
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/resume":
                if not hub:
                    return self._err(400, "hub query required")
                session = planner.resume_qna(hub)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/chat":
                if not hub:
                    return self._err(400, "hub query required")
                msg = (body.get("message") or "").strip()
                if not msg:
                    return self._err(400, "message required")
                session = planner.chat_turn(hub, msg)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/approve":
                if not hub:
                    return self._err(400, "hub query required")
                session = planner.approve_concept(
                    hub,
                    bool(body.get("approved", True)),
                    str(body.get("feedback") or ""),
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/research/approve":
                if not hub:
                    return self._err(400, "hub query required")
                session = planner.approve_research(
                    hub,
                    bool(body.get("approved", True)),
                    body.get("selectedIds"),
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/plan/approve":
                if not hub:
                    return self._err(400, "hub query required")
                session = planner.approve_plan(hub, bool(body.get("approved", True)))
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/reset":
                if not hub:
                    return self._err(400, "hub query required")
                session = planner.reset_session(hub, str(body.get("mode") or "plan"))
                return self._json(200, {"ok": True, "session": session or None})

            if parsed.path == "/api/build/finalize":
                if not hub:
                    return self._err(400, "hub query required")
                cfg = _normalize(body.get("config") or body)
                active = hub / ACTIVE_REL
                active.parent.mkdir(parents=True, exist_ok=True)
                active.write_text(json.dumps(cfg, indent=2) + "\n", encoding="utf-8")
                state = {"phase": "finalized", "hubRoot": str(hub), "cancelled": False}
                (hub / STATE_REL).parent.mkdir(parents=True, exist_ok=True)
                (hub / STATE_REL).write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
                return self._json(200, {"ok": True, "config": cfg})

            if parsed.path == "/api/build/cancel":
                if hub:
                    planner.cancel(hub)
                return self._json(200, {"ok": True})

        except Exception as ex:
            return self._err(500, str(ex))

        return self._err(404, "not found")

    def _serve_static(self, path: str):
        rel = "index.html" if path in ("/", "") else path.lstrip("/")
        target = (DIST / rel).resolve()
        if not str(target).startswith(str(DIST.resolve())) or not target.is_file():
            target = DIST / "index.html"
            if not target.is_file():
                return self._err(503, "build-wizard dist missing — run npm run build")
        data = target.read_bytes()
        ctype = "text/html"
        if target.suffix == ".js":
            ctype = "application/javascript"
        elif target.suffix == ".css":
            ctype = "text/css"
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def main():
    host = os.environ.get("BUILD_WIZARD_HOST", "127.0.0.1")
    httpd = ThreadingHTTPServer((host, PORT), Handler)
    print(f"AI Build Planner API http://{host}:{PORT}/", flush=True)
    httpd.serve_forever()


if __name__ == "__main__":
    main()
