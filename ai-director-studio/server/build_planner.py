#!/usr/bin/env python3
"""Slim LLM bridge for AI Director Studio — Cursor API via planner-llm.ts."""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any, Callable

TOOLS = Path(__file__).resolve().parent
STUDIO_ROOT = TOOLS.parent
STUDIO_ENV_PATH = STUDIO_ROOT / ".env"
SERVER_ENV_PATH = TOOLS / ".env"

_DOTENV_FORCE_KEYS = frozenset(
    {"CURSOR_API_KEY", "CAVE_CURSOR_MODEL", "CAVE_AI_PROVIDER", "DIRECTOR_DATA_ROOT"}
)

AUTO_REPLY_MAX_ATTEMPTS = 4
AUTO_REPLY_DUPE_THRESHOLD = 0.82


def studio_env_path() -> Path:
    return STUDIO_ENV_PATH


def cursor_api_missing_message() -> str:
    return (
        f"CURSOR_API_KEY missing — set it in {STUDIO_ENV_PATH.resolve()} "
        "(copy from Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env if you use Hub)"
    )


def _cave_grader_env_paths() -> list[Path]:
    paths: list[Path] = []
    hub = os.environ.get("HUB_ROOT", "").strip()
    if hub:
        paths.append(
            Path(hub).expanduser()
            / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
        )
    sibling = (
        STUDIO_ROOT.parent
        / "Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/.env"
    )
    if sibling not in paths:
        paths.append(sibling)
    return [p for p in paths if p.is_file()]


def _read_env_value(path: Path, key: str) -> str:
    if not path.is_file():
        return ""
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        if k.strip() == key:
            return v.strip().strip('"').strip("'")
    return ""


def _upsert_env_value(path: Path, key: str, value: str) -> None:
    text = path.read_text(encoding="utf-8") if path.is_file() else ""
    line = f"{key}={value}"
    if re.search(rf"^{re.escape(key)}=", text, flags=re.M):
        text = re.sub(rf"^{re.escape(key)}=.*$", line, text, flags=re.M)
    else:
        if text and not text.endswith("\n"):
            text += "\n"
        text += line + "\n"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def seed_cursor_api_key_from_cave_grader() -> bool:
    """Copy CURSOR_API_KEY from Hub cave-grader/.env into studio .env when missing."""
    if cursor_api_configured():
        return False
    for cave_env in _cave_grader_env_paths():
        key = _read_env_value(cave_env, "CURSOR_API_KEY")
        if not key:
            continue
        _upsert_env_value(STUDIO_ENV_PATH, "CURSOR_API_KEY", key)
        model = _read_env_value(cave_env, "CAVE_CURSOR_MODEL")
        if model:
            _upsert_env_value(STUDIO_ENV_PATH, "CAVE_CURSOR_MODEL", model)
        hub = _read_env_value(cave_env, "HUB_ROOT")
        if hub:
            _upsert_env_value(STUDIO_ENV_PATH, "HUB_ROOT", hub)
        load_dotenv(None)
        return True
    return False


def _apply_env_file(path: Path) -> None:
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        k, v = k.strip(), v.strip().strip('"').strip("'")
        if not k:
            continue
        if k in _DOTENV_FORCE_KEYS:
            if v:
                os.environ[k] = v
        elif k not in os.environ:
            os.environ[k] = v


def load_dotenv(hub: Path | None = None) -> None:
    for candidate in (STUDIO_ENV_PATH, SERVER_ENV_PATH):
        if candidate.is_file():
            _apply_env_file(candidate)
    if not os.environ.get("CURSOR_API_KEY", "").strip():
        for cave_env in _cave_grader_env_paths():
            _apply_env_file(cave_env)
            if os.environ.get("CURSOR_API_KEY", "").strip():
                break
    if hub and not os.environ.get("STUDIO_ROOT"):
        os.environ["STUDIO_ROOT"] = str(Path(hub).expanduser())
    elif not os.environ.get("STUDIO_ROOT"):
        os.environ["STUDIO_ROOT"] = str(STUDIO_ROOT)


def cursor_api_configured(hub: Path | None = None) -> bool:
    load_dotenv(hub)
    return bool(os.environ.get("CURSOR_API_KEY", "").strip())


def _augment_path_env(base: dict[str, str] | None = None) -> dict[str, str]:
    env = dict(base if base is not None else os.environ)
    prefixes = [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        os.path.expanduser("~/.volta/bin"),
        str(STUDIO_ROOT / "node_modules" / ".bin"),
    ]
    current = env.get("PATH", "")
    seen: set[str] = set()
    merged: list[str] = []
    for p in prefixes + ([current] if current else []):
        if not p or p in seen:
            continue
        seen.add(p)
        merged.append(p)
    env["PATH"] = os.pathsep.join(merged)
    return env


def _planner_subprocess_env(hub: Path | None = None) -> dict[str, str]:
    load_dotenv(hub)
    env = _augment_path_env()
    from envkit_paths import ENV_VAR, ensure_planner_process_env, resolve_envkit_root

    root = resolve_envkit_root()
    env[ENV_VAR] = str(root)
    env["STANDALONE"] = "1"
    env["STUDIO_ROOT"] = str(STUDIO_ROOT)
    if hub is not None:
        env["STUDIO_ROOT"] = str(Path(hub).expanduser().resolve())
    tmp = ensure_planner_process_env()
    env["TMPDIR"] = str(tmp)
    env["TEMP"] = str(tmp)
    env["TMP"] = str(tmp)
    return env


def _resolve_node(env: dict[str, str] | None = None) -> str:
    env = _augment_path_env(env)
    found = shutil.which("node", path=env["PATH"])
    if found:
        return found
    for fixed in ("/usr/local/bin/node", "/opt/homebrew/bin/node"):
        if os.path.isfile(fixed) and os.access(fixed, os.X_OK):
            return fixed
    raise RuntimeError("Node.js not found — install from https://nodejs.org or brew install node")


def _tsx_argv(script: Path, *args: str | Path) -> list[str]:
    tsx_cli = STUDIO_ROOT / "node_modules" / "tsx" / "dist" / "cli.mjs"
    node = _resolve_node()
    tail = [str(a) for a in args]
    if tsx_cli.is_file():
        return [node, str(tsx_cli), str(script), *tail]
    npx = shutil.which("npx", path=_augment_path_env()["PATH"])
    if npx:
        return [npx, "--yes", "tsx", str(script), *tail]
    return [node, "--import", "tsx", str(script), *tail]


def _parse_llm_json(raw: str) -> dict[str, Any]:
    raw = raw.strip()
    if raw.startswith("```"):
        raw = re.sub(r"^```(?:json)?\s*", "", raw)
        raw = re.sub(r"\s*```$", "", raw)
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        match = re.search(r"\{[\s\S]*\}", raw)
        if match:
            return json.loads(match.group(0))
        raise


def _clear_streaming(doc: dict[str, Any]) -> None:
    doc.pop("streamingText", None)
    doc.pop("streamingRole", None)


def _planner_stream_display(raw: str) -> str:
    raw = (raw or "").strip()
    if not raw:
        return ""
    if not raw.startswith("{"):
        return raw
    try:
        parsed = _parse_llm_json(raw)
        msg = parsed.get("assistantMessage")
        if isinstance(msg, str) and msg.strip():
            return msg.strip()
    except Exception:
        pass
    match = re.search(r'"assistantMessage"\s*:\s*"((?:[^"\\]|\\.)*)', raw, re.DOTALL)
    if match:
        try:
            return json.loads(f'"{match.group(1)}"').strip()
        except json.JSONDecodeError:
            return match.group(1).replace("\\n", "\n").replace('\\"', '"').strip()
    return ""


def _sanitize_chat_content(content: str) -> str:
    text = (content or "").strip()
    if not text.startswith("{"):
        return content
    if '"checklist"' not in text and '"assistantMessage"' not in text:
        return content
    try:
        parsed = _parse_llm_json(text)
        msg = parsed.get("assistantMessage")
        if isinstance(msg, str) and msg.strip():
            return msg.strip()
    except Exception:
        pass
    if '"assistantMessage"' in text:
        extracted = _planner_stream_display(text)
        if extracted:
            return extracted
    return content


def _public_streaming(doc: dict[str, Any]) -> tuple[str | None, str | None]:
    role = doc.get("streamingRole")
    raw = doc.get("streamingText")
    if not raw:
        return None, role
    if role in ("assistant", "script"):
        return _planner_stream_display(str(raw)) or None, role
    return str(raw), role


def _set_streaming(doc: dict[str, Any], role: str, text: str, *, json_response: bool = False) -> None:
    doc["streamingRole"] = role
    display = (
        _planner_stream_display(text)
        if role in ("assistant", "script") and json_response
        else text
    )
    doc["streamingText"] = display


def _write_session(_hub: Path, _doc: dict[str, Any]) -> None:
    """No-op — director sessions live in project folders, not planner JSON."""


def _llm_prompt_once(script: Path, req_path: str, env: dict[str, str]) -> str:
    proc = subprocess.run(
        _tsx_argv(script, req_path),
        cwd=TOOLS,
        capture_output=True,
        text=True,
        timeout=300,
        env=env,
    )
    line = (proc.stdout or proc.stderr or "").strip().splitlines()[-1] if proc.stdout or proc.stderr else ""
    if not line:
        raise RuntimeError(proc.stderr or "AI planner returned no output")
    data = json.loads(line)
    if data.get("error"):
        raise RuntimeError(str(data["error"]))
    text = data.get("text", "")
    if not text:
        raise RuntimeError("AI planner returned empty text")
    return text


def _llm_stream_once(
    hub: Path,
    script: Path,
    req_path: str,
    env: dict[str, str],
    stream_doc: dict[str, Any] | None,
    stream_role: str,
    *,
    json_response: bool,
    stream_persist: Callable[[dict[str, Any]], None] | None = None,
) -> str:
    stream_env = dict(env)
    stream_env["PYTHONUNBUFFERED"] = "1"
    proc = subprocess.Popen(
        _tsx_argv(script, req_path),
        cwd=TOOLS,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        bufsize=1,
        env=stream_env,
    )
    accumulated = ""
    last_stream_write = 0.0

    def _persist() -> None:
        if stream_doc is None:
            return
        if stream_persist is not None:
            stream_persist(stream_doc)

    assert proc.stdout is not None
    for line in proc.stdout:
        line = line.strip()
        if not line:
            continue
        try:
            data = json.loads(line)
        except json.JSONDecodeError:
            continue
        if data.get("type") == "error":
            raise RuntimeError(str(data.get("error")))
        if data.get("type") == "delta":
            accumulated = str(data.get("accumulated") or accumulated + str(data.get("text") or ""))
            if stream_doc is not None:
                _set_streaming(stream_doc, stream_role, accumulated, json_response=json_response)
                import time as _time

                now = _time.time()
                if now - last_stream_write >= 0.3:
                    _persist()
                    last_stream_write = now
        elif data.get("type") == "done":
            accumulated = str(data.get("text") or accumulated)

    err = proc.stderr.read() if proc.stderr else ""
    code = proc.wait(timeout=300)
    if code != 0:
        raise RuntimeError(err or f"AI planner exited {code}")
    if not accumulated.strip():
        raise RuntimeError(err or "AI planner returned empty text")
    if stream_doc is not None:
        _clear_streaming(stream_doc)
        _persist()
    return accumulated.strip()


def _llm_messages(
    hub: Path,
    system: str,
    messages: list[dict[str, str]],
    *,
    stream_doc: dict[str, Any] | None = None,
    stream_role: str = "assistant",
    json_response: bool = True,
    stream_persist: Callable[[dict[str, Any]], None] | None = None,
) -> str:
    load_dotenv(hub)
    if not os.environ.get("CURSOR_API_KEY", "").strip():
        raise RuntimeError(cursor_api_missing_message())

    script = TOOLS / "planner-llm.ts"
    if not script.is_file():
        raise RuntimeError(f"planner-llm.ts not found at {script}")

    import tempfile

    use_stream = stream_doc is not None
    payload = {
        "hubRoot": str(hub),
        "system": system,
        "messages": messages,
        "mode": "agent" if use_stream else "prompt",
        "stream": use_stream,
        "plainText": not json_response,
    }
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as tmp:
        json.dump(payload, tmp)
        req_path = tmp.name

    try:
        env = _planner_subprocess_env(hub)
        if not use_stream:
            return _llm_prompt_once(script, req_path, env)

        if stream_doc is not None:
            stream_doc["cursorWorking"] = True
            _set_streaming(stream_doc, stream_role, "", json_response=json_response)
            if stream_persist is not None:
                stream_persist(stream_doc)

        try:
            return _llm_stream_once(
                hub,
                script,
                req_path,
                env,
                stream_doc,
                stream_role,
                json_response=json_response,
                stream_persist=stream_persist,
            )
        except Exception as stream_err:
            if stream_doc is not None:
                _clear_streaming(stream_doc)
            fallback = dict(payload)
            fallback["mode"] = "prompt"
            fallback["stream"] = False
            with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as fb:
                json.dump(fallback, fb)
                fb_path = fb.name
            try:
                return _llm_prompt_once(script, fb_path, env)
            except Exception:
                raise stream_err from None
            finally:
                try:
                    os.unlink(fb_path)
                except OSError:
                    pass
    finally:
        if stream_doc is not None:
            _clear_streaming(stream_doc)
            if stream_persist is not None:
                stream_persist(stream_doc)
        try:
            os.unlink(req_path)
        except OSError:
            pass


def _normalize_for_dedupe(text: str) -> str:
    t = re.sub(r"\s+", " ", (text or "").strip().lower())
    t = re.sub(r"[^\w\s]", "", t)
    return t


def _reply_similarity(a: str, b: str) -> float:
    from difflib import SequenceMatcher

    na, nb = _normalize_for_dedupe(a), _normalize_for_dedupe(b)
    if not na or not nb:
        return 0.0
    if na == nb:
        return 1.0
    shorter, longer = (na, nb) if len(na) <= len(nb) else (nb, na)
    if len(shorter) >= 24 and shorter in longer:
        return 0.96
    return SequenceMatcher(None, na, nb).ratio()


def _collect_prior_user_replies(doc: dict[str, Any]) -> list[str]:
    return [
        str(m.get("content") or "").strip()
        for m in doc.get("messages") or []
        if m.get("role") == "user" and str(m.get("content") or "").strip()
    ]


def _is_duplicate_auto_reply(
    candidate: str,
    doc: dict[str, Any],
    *,
    threshold: float = AUTO_REPLY_DUPE_THRESHOLD,
) -> tuple[bool, str]:
    cand = (candidate or "").strip()
    if len(cand) < 8:
        return True, "empty or too short"
    for i, prev in enumerate(_collect_prior_user_replies(doc)):
        sim = _reply_similarity(cand, prev)
        if sim >= threshold:
            return True, f"duplicate of prior user reply #{i + 1} (similarity {sim:.0%})"
    assistant = _last_assistant_message(doc)
    if assistant and _reply_similarity(cand, assistant) >= 0.78:
        return True, "echoes planner question"
    return False, ""


def _format_prior_replies_block(doc: dict[str, Any]) -> str:
    prior = _collect_prior_user_replies(doc)
    if not prior:
        return ""
    lines = ["\n## Prior user answers (do NOT repeat or lightly rephrase):"]
    for i, p in enumerate(prior[-6:], start=max(1, len(prior) - 5)):
        snippet = p.replace("\n", " ").strip()[:220]
        lines.append(f"{i}. {snippet}")
    return "\n".join(lines)


def _last_assistant_message(doc: dict[str, Any]) -> str:
    for m in reversed(doc.get("messages") or []):
        if m.get("role") == "assistant":
            return str(m.get("content") or "")
    return ""
