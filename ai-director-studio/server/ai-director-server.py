#!/usr/bin/env python3
"""AI Director API — studio UI for recap compose, segments, voice persona, chat.

Reuses recap-dashboard-server compose pipeline; serves ai-director static UI.

  python3 ai-director-server.py [--capture /path/to/DemoCapture/ts] [--port 8767]
  bash start-ai-director.sh [capture_path]
"""
from __future__ import annotations

import argparse
import cgi
import importlib.util
import json
import mimetypes
import os
import sys
import tempfile
from http.server import ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, unquote, urlparse

_TOOLS = Path(__file__).resolve().parent
_STUDIO = _TOOLS.parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

os.environ.setdefault("STANDALONE", "1")

import build_planner as planner  # noqa: E402
import director_session as director  # noqa: E402
import ingest_media  # noqa: E402
import music_director_session as music_director  # noqa: E402
import music_instrumental  # noqa: E402
import music_ingest  # noqa: E402
import music_produce  # noqa: E402
import music_project  # noqa: E402
from director_paths import create_project, list_projects, resolve_project, studio_repo_root  # noqa: E402

planner.load_dotenv(None)

DIRECTOR_DIR = _STUDIO / "ai-director"
DEFAULT_PORT = 8767

_spec = importlib.util.spec_from_file_location("recap_srv", _TOOLS / "recap-dashboard-server.py")
recap_srv = importlib.util.module_from_spec(_spec)
assert _spec.loader
_spec.loader.exec_module(recap_srv)

recap_srv.DASHBOARD_DIR = DIRECTOR_DIR


class DirectorHandler(recap_srv.Handler):
    """Extends recap API with /api/director/* and /api/project/* routes."""

    def _parse_multipart(self) -> cgi.FieldStorage:
        env = {
            "REQUEST_METHOD": "POST",
            "CONTENT_TYPE": self.headers.get("Content-Type", ""),
            "CONTENT_LENGTH": self.headers.get("Content-Length", "0"),
        }
        return cgi.FieldStorage(fp=self.rfile, headers=self.headers, environ=env)

    def _handle_project_routes_get(self, path: str) -> bool:
        if path == "/api/project/list":
            projects = []
            for p in list_projects():
                frames = len(list((p / "timelapse").glob("tl_*.png"))) if (p / "timelapse").is_dir() else 0
                meta = ingest_media.read_media_ingest(p)
                mdoc = music_project.read_music_project(p)
                projects.append(
                    {
                        "path": str(p),
                        "name": p.name,
                        "frames": frames,
                        "mediaIngested": frames > 0,
                        "hasVideo": bool(meta.get("hasVideo")) if meta else False,
                        "hasImages": bool(meta.get("hasImages")) if meta else False,
                        "durationSec": meta.get("durationSec") or meta.get("estimatedDurationSec") if meta else None,
                        "hasSession": (p / "DirectorSession.json").is_file()
                        or (p / "MusicDirectorSession.json").is_file(),
                        "projectKind": mdoc.get("projectKind") if mdoc else "video",
                    }
                )
            self._json(200, {"projects": projects, "dataRoot": str(recap_srv.resolve_envkit_root())})
            return True
        if path == "/api/project/status":
            qs = parse_qs(urlparse(self.path).query)
            raw = qs.get("path", [None])[0] or qs.get("project", [None])[0]
            project = resolve_project(raw)
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            meta = ingest_media.read_media_ingest(project)
            frames = ingest_media.frame_count(project)
            self._json(
                200,
                {
                    "ok": True,
                    "project": str(project),
                    "name": project.name,
                    "frameCount": frames,
                    "mediaIngested": frames > 0,
                    "hasVideo": bool(meta.get("hasVideo")) if meta else False,
                    "hasImages": bool(meta.get("hasImages")) if meta else False,
                    "durationSec": meta.get("durationSec") or meta.get("estimatedDurationSec") if meta else None,
                    "source": meta.get("source") if meta else None,
                    "hasSession": (project / "DirectorSession.json").is_file(),
                    "mediaIngest": meta,
                },
            )
            return True
        return False

    def _handle_project_routes_post(self, path: str, body: dict) -> bool:
        if path == "/api/project/create":
            name = str(body.get("name") or body.get("title") or "project")
            project = create_project(name)
            ingest_media.ensure_timeline(project, title=name)
            self._json(200, {"ok": True, "project": str(project), "name": project.name})
            return True
        return False

    def _handle_project_ingest_multipart(self) -> None:
        form = self._parse_multipart()
        project_raw = form.getvalue("project") or form.getvalue("capture") or ""
        title = form.getvalue("title") or ""
        if not project_raw:
            return self._json(400, {"error": "project path required"})
        project = Path(str(project_raw)).expanduser().resolve()
        if not project.is_dir():
            return self._json(404, {"error": "project not found"})

        saved_paths: list[Path] = []
        tmp_dir = Path(tempfile.mkdtemp(prefix="aidirector-ingest-"))
        try:
            if isinstance(form["files"], list):
                file_items = form["files"]
            elif form.get("files"):
                file_items = [form["files"]]
            else:
                file_items = [item for key, item in form.items() if key not in ("project", "capture", "title")]

            for item in file_items:
                if not getattr(item, "file", None) or not getattr(item, "filename", None):
                    continue
                dest = tmp_dir / Path(item.filename).name
                dest.write_bytes(item.file.read())
                saved_paths.append(dest)

            if not saved_paths:
                return self._json(400, {"error": "no files uploaded"})

            result = ingest_media.ingest_files(project, saved_paths, title=str(title or project.name))
            return self._json(200, {"ok": True, **result})
        except Exception as exc:
            return self._json(500, {"error": str(exc)})
        finally:
            for p in saved_paths:
                p.unlink(missing_ok=True)
            try:
                tmp_dir.rmdir()
            except OSError:
                pass

    def _handle_music_routes_get(self, path: str) -> bool:
        qs = parse_qs(urlparse(self.path).query)
        if path == "/api/music/status":
            raw = qs.get("path", [None])[0] or qs.get("project", [None])[0]
            project = resolve_project(raw)
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            st = music_project.music_status(project)
            st["sunoConfigured"] = music_instrumental.suno_configured()
            if isinstance(st.get("status"), dict):
                st["status"]["sunoConfigured"] = music_instrumental.suno_configured()
            self._json(200, st)
            return True
        if path == "/api/music/file":
            raw = qs.get("path", [None])[0]
            name = qs.get("name", [None])[0]
            project = resolve_project(raw)
            if not project or not name or ".." in name or "/" in name:
                self._json(400, {"error": "path and name required"})
                return True
            fpath = music_project.music_dir(project) / name
            if not fpath.is_file():
                self._json(404, {"error": "file not found"})
                return True
            data = fpath.read_bytes()
            ctype = mimetypes.guess_type(str(fpath))[0] or "application/octet-stream"
            self._binary(200, data, ctype)
            return True
        if path == "/api/music/session":
            raw = qs.get("path", [None])[0] or qs.get("project", [None])[0]
            project = resolve_project(raw)
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            try:
                self._json(200, music_director.get_session(project))
            except FileNotFoundError as exc:
                self._json(404, {"error": str(exc)})
            return True
        return False

    def _handle_music_routes_post(self, path: str, body: dict) -> bool:
        if path == "/api/music/project/create":
            project, doc = music_project.create_music_project(
                str(body.get("name") or "track"),
                key=str(body.get("key") or "auto"),
                scale=str(body.get("scale") or "minor"),
                bpm=int(body["bpm"]) if body.get("bpm") else None,
                instrumental_prompt=str(body.get("instrumentalPrompt") or ""),
            )
            self._json(200, {"ok": True, "project": str(project), "name": project.name, "meta": doc})
            return True
        if path == "/api/music/instrumental/generate":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            result = music_instrumental.generate_instrumental(project, body.get("prompt"))
            code = 200 if result.get("ok") else 400
            self._json(code, result)
            return True
        if path == "/api/music/lyrics/save":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            try:
                result = music_ingest.save_lyrics(
                    project,
                    str(body.get("lyrics") or ""),
                    fmt=str(body.get("format") or "lines"),
                    bpm=int(body["bpm"]) if body.get("bpm") else None,
                )
                self._json(200, result)
            except ValueError as exc:
                self._json(400, {"error": str(exc)})
            return True
        if path == "/api/music/produce":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            result = music_produce.produce_async(project)
            self._json(200, result)
            return True
        if path == "/api/music/to-music-video":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            try:
                result = music_produce.create_music_video_project(project)
                code = 200 if result.get("ok") else 400
                if result.get("ok"):
                    vp = Path(result["videoProject"])
                    try:
                        result["session"] = director.get_session(vp)
                    except FileNotFoundError:
                        pass
                self._json(code, result)
            except Exception as exc:
                self._json(500, {"error": str(exc)})
            return True
        if path == "/api/music/session/start":
            name = str(body.get("name") or "track")
            project = resolve_project(body.get("project") or body.get("path"))
            internet_research = bool(body.get("internetResearch", True))
            try:
                if project:
                    sess = music_director.start_session(
                        project, name=name, internet_research=internet_research
                    )
                else:
                    sess = music_director.start_session(
                        None, name=name, internet_research=internet_research
                    )
                proj_path = sess.get("project") or str(project or "")
                self._json(200, {**sess, "project": proj_path})
            except FileNotFoundError as exc:
                self._json(404, {"error": str(exc)})
            return True
        if path == "/api/music/chat":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            msg = body.get("message")
            bootstrap = bool(body.get("bootstrap"))
            try:
                self._json(200, music_director.chat_turn(project, msg, bootstrap=bootstrap))
            except Exception as exc:
                self._json(500, {"error": str(exc)})
            return True
        if path == "/api/music/responder/run-until-done":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            preset = str(body.get("preset") or "bedroom-trap")
            self._json(200, music_director.responder_run_until_done(project, preset))
            return True
        if path == "/api/music/enter-production":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            step = str(body.get("step") or "instrumental")
            try:
                self._json(200, music_director.enter_production(project, step))
            except RuntimeError as exc:
                self._json(400, {"error": str(exc)})
            return True
        if path == "/api/music/production-step":
            project = resolve_project(body.get("project") or body.get("path"))
            if not project:
                self._json(404, {"error": "project not found"})
                return True
            step = str(body.get("step") or "instrumental")
            try:
                self._json(200, music_director.set_production_step(project, step))
            except FileNotFoundError as exc:
                self._json(404, {"error": str(exc)})
            return True
        return False

    def _handle_music_multipart(self, path: str) -> None:
        form = self._parse_multipart()
        project_raw = form.getvalue("project") or form.getvalue("path") or ""
        project = resolve_project(str(project_raw) if project_raw else None)
        if not project:
            return self._json(404, {"error": "project not found"})
        tmp_dir = Path(tempfile.mkdtemp(prefix="aidirector-music-"))
        try:
            if path == "/api/music/instrumental/upload":
                item = form.get("file") or form.get("instrumental")
                if not item or not getattr(item, "file", None):
                    return self._json(400, {"error": "file required"})
                dest = tmp_dir / Path(item.filename or "instrumental.mp3").name
                dest.write_bytes(item.file.read())
                result = music_instrumental.save_instrumental_upload(project, dest)
                return self._json(200, result)
            if path == "/api/music/record/upload":
                vocal = form.get("vocal")
                perf = form.get("performance")
                out: dict[str, Any] = {"ok": True}
                if vocal and getattr(vocal, "file", None):
                    suffix = Path(vocal.filename or "vocal.webm").suffix or ".webm"
                    vdest = tmp_dir / f"vocal{suffix}"
                    vdest.write_bytes(vocal.file.read())
                    out.update(music_ingest.save_vocal_take(project, vdest, suffix=suffix))
                if perf and getattr(perf, "file", None):
                    pdest = tmp_dir / Path(perf.filename or "performance.webm").name
                    pdest.write_bytes(perf.file.read())
                    out.update(music_ingest.save_performance_video(project, pdest))
                if not out.get("vocalRaw") and not out.get("performance"):
                    return self._json(400, {"error": "vocal or performance required"})
                return self._json(200, out)
            return self._json(404, {"error": "unknown music upload route"})
        except Exception as exc:
            return self._json(500, {"error": str(exc)})
        finally:
            import shutil as _shutil

            _shutil.rmtree(tmp_dir, ignore_errors=True)

    def _resolve_cap_director(self, qs: dict, body: dict | None = None) -> Path | None:
        body = body or {}
        raw = body.get("capture") or qs.get("path", [None])[0] or (str(self.capture) if self.capture else None)
        return recap_srv.resolve_capture(raw)

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        path = unquote(parsed.path)
        qs = parse_qs(parsed.query)

        if path.startswith("/api/project/"):
            if self._handle_project_routes_get(path):
                return
            return self._json(404, {"error": "unknown project route", "path": path})

        if path.startswith("/api/music/"):
            if self._handle_music_routes_get(path):
                return
            return self._json(404, {"error": "unknown music route", "path": path})

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
                        "cursorEnvFile": str(planner.studio_env_path().resolve()),
                        "cursorModel": os.environ.get("CAVE_CURSOR_MODEL", "composer-2.5"),
                        "sunoConfigured": music_instrumental.suno_configured(),
                        "musicRoutes": True,
                        "musicChecklistItems": len(music_director.DEFAULT_CHECKLIST),
                    },
                )
            if not cap and path not in ("/api/director/health",):
                return self._json(404, {"error": "capture not found"})
            if path == "/api/director/session":
                try:
                    return self._json(200, director.get_session(cap))
                except FileNotFoundError as exc:
                    return self._json(404, {"error": str(exc)})
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
        path = unquote(parsed.path)

        if path == "/api/project/ingest":
            ctype = self.headers.get("Content-Type", "")
            if "multipart/form-data" in ctype:
                return self._handle_project_ingest_multipart()
            try:
                body = self._read_json()
            except ValueError as exc:
                return self._json(400, {"error": str(exc)})
            paths = [Path(p) for p in body.get("paths") or []]
            project = resolve_project(body.get("project") or body.get("capture"))
            if not project:
                return self._json(404, {"error": "project not found"})
            try:
                result = ingest_media.ingest_files(project, paths, title=str(body.get("title") or project.name))
                return self._json(200, {"ok": True, **result})
            except Exception as exc:
                return self._json(500, {"error": str(exc)})

        if path.startswith("/api/music/"):
            ctype = self.headers.get("Content-Type", "")
            if "multipart/form-data" in ctype:
                return self._handle_music_multipart(path)
            try:
                body = self._read_json()
            except ValueError as exc:
                return self._json(400, {"error": str(exc)})
            if self._handle_music_routes_post(path, body):
                return
            return self._json(404, {"error": "unknown music route", "path": path})

        if path.startswith("/api/project/"):
            try:
                body = self._read_json()
            except ValueError as exc:
                return self._json(400, {"error": str(exc)})
            if self._handle_project_routes_post(path, body):
                return
            return self._json(404, {"error": "unknown project route", "path": path})

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
            except ValueError as exc:
                return self._json(400, {"error": str(exc)})
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
    print("One command (server + UI): bash start.sh [project_path]")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
