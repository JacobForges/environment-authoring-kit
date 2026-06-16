#!/usr/bin/env python3
"""Local API for Environment Kit AI build planner (port 8766)."""
from __future__ import annotations

import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import base64

import build_planner as planner
import content_planner as content
import planner_recording as recording
import wizard_manifest as wizard_manifest_mod
import wizard_pipeline as wizard_pipeline_mod
import wizard_pipeline_apply as wizard_pipeline_apply_mod
import wizard_tab_apply as wizard_tab_apply_mod
import wizard_director as wizard_director_mod
import wizard_music as wizard_music_mod
import wizard_registry as wizard_registry_mod
import onnx_brain_config as onnx_cfg_mod
from envkit_paths import ENV_VAR, ensure_planner_process_env, planner_ipc_temp_dir, planner_tmpdir_ok, resolve_envkit_root

planner.load_dotenv(None)
os.environ.setdefault(ENV_VAR, str(resolve_envkit_root()))
ensure_planner_process_env()

PORT = int(os.environ.get("BUILD_WIZARD_PORT", "8766"))
ACTIVE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildActiveSessionConfig.json")
STATE_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildWizardState.json")
DIST = Path(__file__).resolve().parent / "build-wizard~" / "dist~"


def _hub_root(query: dict) -> Path | None:
    raw = (query.get("hub") or query.get("hubRoot") or [""])[0]
    if not raw:
        return None
    p = Path(raw).expanduser().resolve()
    return p if p.is_dir() else None


def _resolve_planner_asset(hub: Path, rel: str) -> Path | None:
    """Resolve a hub-relative asset path (follows ResearchCache symlinks to external volumes)."""
    if not rel or ".." in rel.replace("\\", "/"):
        return None
    logical = hub / rel
    if not logical.is_file():
        return None
    # ResearchCache often symlinks off-repo (e.g. external Lexar) — realpath may leave hub tree.
    norm = rel.replace("\\", "/")
    if norm.startswith("Assets/EnvironmentKit/"):
        return logical
    try:
        logical.resolve().relative_to(hub.resolve())
        return logical
    except ValueError:
        return None


def _defaults() -> dict:
    return planner._defaults_config()


def _normalize(cfg: dict) -> dict:
    return planner._normalize_config(cfg)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def _parse_tab_path(self, path: str) -> tuple[str | None, str | None]:
        parts = path.strip("/").split("/")
        if len(parts) < 3 or parts[0] != "api":
            return None, None
        tab_id = wizard_registry_mod.resolve_tab_id(parts[1])
        if not tab_id or tab_id in ("video", "music"):
            return None, None
        if len(parts) == 3:
            return tab_id, parts[2]
        if len(parts) == 4 and parts[2] == "auto-respond" and parts[3] == "step":
            return tab_id, "auto-respond/step"
        return None, None

    def _match_tab_route(self, path: str, action: str) -> str | None:
        tab_id, act = self._parse_tab_path(path)
        if tab_id and act == action:
            return tab_id
        return None

    def _handle_tab_post(self, hub: Path, tab_id: str, action: str, body: dict):
        if action == "start":
            msg = (body.get("message") or "").strip()
            if not msg:
                return self._err(400, "message required")
            session = wizard_registry_mod.start_session(
                hub, tab_id, msg, internetResearch=bool(body.get("internetResearch"))
            )
            return self._json(200, {"ok": True, "session": session})
        if action == "chat":
            msg = (body.get("message") or "").strip()
            if not msg:
                return self._err(400, "message required")
            session = wizard_registry_mod.chat_turn(hub, tab_id, msg)
            return self._json(200, {"ok": True, "session": session})
        if action == "resume":
            session = wizard_registry_mod.resume_qna(hub, tab_id)
            return self._json(200, {"ok": True, "session": session})
        if action == "approve":
            session = wizard_registry_mod.approve(
                hub, tab_id, bool(body.get("approved", True)), str(body.get("feedback") or "")
            )
            return self._json(200, {"ok": True, "session": session})
        if action == "reset":
            session = wizard_registry_mod.reset_session(hub, tab_id)
            return self._json(200, {"ok": True, "session": session})
        if action == "export":
            result = wizard_registry_mod.export_tab(hub, tab_id)
            return self._json(200, {"ok": True, **result})
        if action == "auto-respond/step":
            preset = (body.get("preset") or "").strip().lower()
            if not preset:
                return self._err(400, "preset required")
            result = wizard_registry_mod.auto_respond_step(
                hub, tab_id, preset, internetResearch=bool(body.get("internetResearch"))
            )
            return self._json(
                200,
                {
                    "ok": True,
                    "done": bool(result.get("done")),
                    "waiting": bool(result.get("waiting")),
                    "session": result.get("session"),
                },
            )
        if action == "auto-respond":
            preset = (body.get("preset") or "").strip().lower()
            if not preset:
                return self._err(400, "preset required")
            session = wizard_registry_mod.auto_respond_until_done(
                hub, tab_id, preset, internetResearch=bool(body.get("internetResearch"))
            )
            return self._json(200, {"ok": True, "session": session})
        return None

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
            return self._json(
                200,
                {
                    "ok": True,
                    "port": PORT,
                    "mode": "ai-planner",
                    "contentApi": True,
                    "contentApiVersion": 1,
                    "cursorApiConfigured": planner.cursor_api_configured(hub),
                    "cursorModel": os.environ.get("CAVE_CURSOR_MODEL", "composer-2.5"),
                    "plannerTmpDir": str(planner_ipc_temp_dir()),
                    "plannerTmpDirOk": planner_tmpdir_ok(),
                    "wizardTabs": len(wizard_registry_mod.TAB_DEFS),
                    "musicApi": True,
                    "pipelineApi": True,
                    "directorApi": True,
                    "directorReady": wizard_director_mod.public_status(hub).get("ready") if hub else False,
                },
            )

        if parsed.path == "/api/wizard/manifest":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"manifest": wizard_manifest_mod.read_manifest(hub)})

        if parsed.path == "/api/wizard/pipeline/status":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"pipeline": wizard_pipeline_mod.public_status(hub)})

        if parsed.path == "/api/wizard/pipeline/apply/status":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"apply": wizard_tab_apply_mod.public_status(hub)})

        if parsed.path == "/api/wizard/director/status":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"director": wizard_director_mod.public_status(hub)})

        if parsed.path == "/api/wizard/music/status":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"music": wizard_music_mod.public_status(hub)})

        if parsed.path == "/api/onnx/config":
            if not hub:
                return self._err(400, "hub query required")
            cfg = onnx_cfg_mod.read_config(hub)
            onnx_cfg_mod.apply_config_to_env(cfg, hub)
            return self._json(
                200,
                {
                    "ok": True,
                    "config": cfg,
                    "models": onnx_cfg_mod.available_models(hub),
                    "diagnostics": onnx_cfg_mod.diagnostics(hub, cfg),
                },
            )

        tab_id, _action = self._parse_tab_path(parsed.path)
        if tab_id and hub:
            if parsed.path.endswith("/session"):
                return self._json(200, {"session": wizard_registry_mod.public_session(hub, tab_id)})
            if parsed.path.endswith("/pulse"):
                return self._json(200, {"pulse": wizard_registry_mod.session_pulse(hub, tab_id)})

        if parsed.path == "/api/planner/session":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"session": planner.public_session(hub, side_effects=False)})

        if parsed.path == "/api/content/session":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"session": content.public_session(hub)})

        if parsed.path == "/api/planner/pulse":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"pulse": planner.session_pulse(hub)})

        if parsed.path == "/api/recording/session":
            if not hub:
                return self._err(400, "hub query required")
            return self._json(200, {"recording": recording.public_recording_session(hub)})

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
            target = _resolve_planner_asset(hub, rel)
            if target is None:
                return self._err(404, "not found")
            data = target.read_bytes()
            ctype = "image/png" if target.suffix.lower() == ".png" else "application/octet-stream"
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
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
            if parsed.path == "/api/recording/chapter":
                if not hub:
                    return self._err(400, "hub query required")
                cap = recording.read_capture_session(hub)
                run_folder = cap.get("runFolder") or ""
                if not run_folder:
                    return self._json(200, {"ok": False, "message": "No capture session"})
                recording.append_chapter(
                    Path(run_folder),
                    str(body.get("chapterId") or "chapter"),
                    str(body.get("title") or "Chapter"),
                    phase=str(body.get("phase") or ""),
                    source="wizard",
                )
                return self._json(200, {"ok": True})

            if parsed.path == "/api/recording/wizard-frame":
                if not hub:
                    return self._err(400, "hub query required")
                if not recording.wizard_ui_capture_active(hub):
                    return self._json(200, {"ok": False, "message": "Wizard UI capture finalized"})
                cap = recording.read_capture_session(hub)
                run_folder = cap.get("runFolder") or ""
                if not run_folder:
                    return self._json(200, {"ok": False, "message": "No capture session"})
                b64 = str(body.get("pngBase64") or "")
                if not b64:
                    return self._err(400, "pngBase64 required")
                try:
                    png = base64.b64decode(b64)
                except Exception:
                    return self._err(400, "invalid pngBase64")
                idx = recording.save_wizard_frame(Path(run_folder), png)
                return self._json(200, {"ok": True, "frameIndex": idx})

            if parsed.path == "/api/recording/wizard-finalize":
                if not hub:
                    return self._err(400, "hub query required")
                result = recording.finalize_wizard_capture(hub)
                return self._json(200, result)

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

            if parsed.path == "/api/planner/auto-respond":
                if not hub:
                    return self._err(400, "hub query required")
                preset = (body.get("preset") or "").strip().lower()
                if preset not in ("small", "medium", "large"):
                    return self._err(400, "preset must be small, medium, or large")
                session = planner.auto_respond_until_concept(
                    hub,
                    preset,
                    bool(body.get("internetResearch")),
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/auto-respond/step":
                if not hub:
                    return self._err(400, "hub query required")
                preset = (body.get("preset") or "").strip().lower()
                if preset not in ("small", "medium", "large"):
                    return self._err(400, "preset must be small, medium, or large")
                result = planner.auto_respond_step(
                    hub,
                    preset,
                    bool(body.get("internetResearch")),
                )
                return self._json(
                    200,
                    {
                        "ok": True,
                        "done": bool(result.get("done")),
                        "session": result.get("session"),
                    },
                )

            if parsed.path == "/api/planner/concept/approve":
                if not hub:
                    return self._err(400, "hub query required")
                session = planner.approve_concept(
                    hub,
                    bool(body.get("approved", True)),
                    str(body.get("feedback") or ""),
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/revise":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                revision_note = str(body.get("revisionNote") or "").strip()
                session = planner.concept_card_revise(
                    hub, card_id, revision_note=revision_note
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/accept":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                session = planner.concept_card_accept(hub, card_id)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/unaccept":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                session = planner.concept_card_unaccept(hub, card_id)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/count":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                try:
                    count = int(body.get("count", 0))
                except (TypeError, ValueError):
                    return self._err(400, "count must be an integer")
                session = planner.concept_card_set_count(hub, card_id, count)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/sculpt-character":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                sculpt_hint = str(body.get("sculptHint") or body.get("hint") or "").strip()
                if not sculpt_hint:
                    return self._err(400, "sculptHint required (1–2 words)")
                session = planner.concept_card_sculpt_character(hub, card_id, sculpt_hint)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/generate-mesh":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                session = planner.concept_card_generate_mesh(hub, card_id)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/planner/concept/card/load-preview":
                if not hub:
                    return self._err(400, "hub query required")
                card_id = str(body.get("cardId") or "").strip()
                if not card_id:
                    return self._err(400, "cardId required")
                session = planner.concept_card_load_preview(hub, card_id)
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

            if parsed.path == "/api/onnx/config":
                if not hub:
                    return self._err(400, "hub query required")
                incoming = body.get("config")
                if not isinstance(incoming, dict):
                    return self._err(400, "config object required")
                cfg = onnx_cfg_mod.write_config(hub, incoming)
                onnx_cfg_mod.apply_config_to_env(cfg, hub)
                return self._json(
                    200,
                    {
                        "ok": True,
                        "config": cfg,
                        "models": onnx_cfg_mod.available_models(hub),
                        "diagnostics": onnx_cfg_mod.diagnostics(hub, cfg),
                    },
                )

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

            if parsed.path == "/api/content/start":
                if not hub:
                    return self._err(400, "hub query required")
                msg = (body.get("message") or "").strip()
                if not msg:
                    return self._err(400, "message required")
                session = content.start_session(
                    hub, msg, internet_research=bool(body.get("internetResearch", True))
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/content/chat":
                if not hub:
                    return self._err(400, "hub query required")
                msg = (body.get("message") or "").strip()
                if not msg:
                    return self._err(400, "message required")
                session = content.chat_turn(hub, msg)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/content/resume":
                if not hub:
                    return self._err(400, "hub query required")
                session = content.resume_qna(hub)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/content/approve":
                if not hub:
                    return self._err(400, "hub query required")
                session = content.approve_layout(
                    hub,
                    bool(body.get("approved", True)),
                    str(body.get("feedback") or ""),
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/content/reset":
                if not hub:
                    return self._err(400, "hub query required")
                session = content.reset_session(hub)
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/content/auto-respond/step":
                if not hub:
                    return self._err(400, "hub query required")
                preset = (body.get("preset") or "").strip().lower()
                if preset not in ("town_hub", "arena_trainers", "full_mainscene"):
                    return self._err(400, "preset must be town_hub, arena_trainers, or full_mainscene")
                result = content.auto_respond_step(
                    hub, preset, internet_research=bool(body.get("internetResearch", True))
                )
                return self._json(
                    200,
                    {
                        "ok": True,
                        "done": bool(result.get("done")),
                        "waiting": bool(result.get("waiting")),
                        "session": result.get("session"),
                    },
                )

            if parsed.path == "/api/content/auto-respond":
                if not hub:
                    return self._err(400, "hub query required")
                preset = (body.get("preset") or "").strip().lower()
                if preset not in ("town_hub", "arena_trainers", "full_mainscene"):
                    return self._err(400, "preset must be town_hub, arena_trainers, or full_mainscene")
                session = content.auto_respond_until_approval(
                    hub, preset, internet_research=bool(body.get("internetResearch", True))
                )
                return self._json(200, {"ok": True, "session": session})

            if parsed.path == "/api/build/cancel":
                if hub:
                    planner.cancel(hub)
                return self._json(200, {"ok": True})

            if parsed.path == "/api/wizard/pipeline/start":
                if not hub:
                    return self._err(400, "hub query required")
                presets = body.get("presets") if isinstance(body.get("presets"), dict) else None
                status = wizard_pipeline_mod.start_pipeline(
                    hub, presets, auto_approve=bool(body.get("autoApprove", True))
                )
                return self._json(200, {"ok": True, "pipeline": status})

            if parsed.path == "/api/wizard/pipeline/step":
                if not hub:
                    return self._err(400, "hub query required")
                result = wizard_pipeline_mod.pipeline_step(hub)
                return self._json(200, result)

            if parsed.path == "/api/wizard/pipeline/cancel":
                if not hub:
                    return self._err(400, "hub query required")
                status = wizard_pipeline_mod.cancel_pipeline(hub)
                return self._json(200, {"ok": True, "pipeline": status})

            if parsed.path == "/api/wizard/director/ensure":
                if not hub:
                    return self._err(400, "hub query required")
                result = wizard_director_mod.ensure_for_hub(hub)
                return self._json(200 if result.get("ok") else 503, result)

            if parsed.path == "/api/wizard/music/ensure":
                if not hub:
                    return self._err(400, "hub query required")
                result = wizard_music_mod.ensure_for_hub(hub)
                return self._json(200 if result.get("ok") else 503, result)

            if parsed.path == "/api/wizard/restart-build":
                if not hub:
                    return self._err(400, "hub query required")
                for t in wizard_registry_mod.TAB_DEFS:
                    wizard_registry_mod.reset_session(hub, t["id"])
                wizard_pipeline_mod.cancel_pipeline(hub)
                wizard_manifest_mod.refresh_manifest(hub)
                return self._json(200, {"ok": True})

            tab_id, action = self._parse_tab_path(parsed.path)
            if hub and tab_id and action:
                handled = self._handle_tab_post(hub, tab_id, action, body)
                if handled:
                    return handled

        except Exception as ex:
            return self._err(500, str(ex))

        return self._err(404, "not found")

    def _serve_static(self, path: str):
        rel = "index.html" if path in ("/", "") else path.lstrip("/")
        target = (DIST / rel).resolve()
        if not str(target).startswith(str(DIST.resolve())) or not target.is_file():
            target = DIST / "index.html"
            if not target.is_file():
                return self._err(503, "build-wizard dist~ missing — run npm run build")
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
