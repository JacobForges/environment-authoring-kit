#!/usr/bin/env python3
"""Start build-wizard API + open browser."""
from __future__ import annotations

import argparse
import json
import os
import signal
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
_SERVER = _TOOLS / "build-wizard-server.py"

import build_planner as _planner  # noqa: E402
from envkit_paths import server_runtime_dir  # noqa: E402

_DASHBOARD = _TOOLS / "build-wizard~"
_RUNTIME = server_runtime_dir()
_PID = _RUNTIME / "build-wizard-server.pid"
_LOG = _RUNTIME / "build-wizard-server.log"
_PORT = int(os.environ.get("BUILD_WIZARD_PORT", "8766"))


def _default_hub() -> str:
    """Hub repo root when ensure-build-wizard.py lives under Packages/.../Tools/cave-grader."""
    candidate = _TOOLS.parent.parent.parent.parent
    if (candidate / "Assets").is_dir() and (candidate / "ProjectSettings").is_dir():
        return str(candidate.resolve())
    return ""


def _health() -> dict | None:
    try:
        with urllib.request.urlopen(f"http://127.0.0.1:{_PORT}/api/health", timeout=2) as r:
            if r.status != 200:
                return None
            return json.loads(r.read().decode("utf-8"))
    except Exception:
        return None


def _http_ok() -> bool:
    h = _health()
    return h is not None and h.get("ok") is True


def _planner_ready() -> bool:
    h = _health()
    return h is not None and h.get("mode") == "ai-planner"


def _pids_on_port(port: int) -> list[int]:
    try:
        out = subprocess.run(
            ["lsof", "-ti", f":{port}"],
            capture_output=True,
            text=True,
            check=False,
            timeout=5,
        )
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return []
    return [int(x) for x in (out.stdout or "").splitlines() if x.strip().isdigit()]


def _stop_wrong_server_on_port() -> None:
    """Free BUILD_WIZARD_PORT if AI Director (or another app) is bound there."""
    h = _health()
    if h is not None and h.get("mode") == "ai-planner":
        return
    for pid in _pids_on_port(_PORT):
        try:
            os.kill(pid, signal.SIGTERM)
        except OSError:
            pass
    time.sleep(0.25)


def _stop_stale_server() -> None:
    if not _PID.is_file():
        return
    try:
        pid = int(_PID.read_text(encoding="utf-8").strip())
        os.kill(pid, signal.SIGTERM)
    except (OSError, ValueError):
        pass
    try:
        _PID.unlink(missing_ok=True)
    except OSError:
        pass


def _ensure_dist():
    dist = _DASHBOARD / "dist~" / "index.html"
    if dist.is_file():
        return
    subprocess.run(["npm", "install"], cwd=_DASHBOARD, check=False)
    subprocess.run(["npm", "run", "build"], cwd=_DASHBOARD, check=False)


def _start_server():
    _PID.parent.mkdir(parents=True, exist_ok=True)
    log = open(_LOG, "a", encoding="utf-8")
    env = _planner._planner_subprocess_env()
    for prefix in ("/usr/local/bin", "/opt/homebrew/bin"):
        if os.path.isdir(prefix):
            env["PATH"] = prefix + os.pathsep + env.get("PATH", "")
    proc = subprocess.Popen(
        [sys.executable, str(_SERVER)],
        cwd=_TOOLS,
        stdout=log,
        stderr=log,
        start_new_session=True,
        env=env,
    )
    _PID.write_text(str(proc.pid), encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--open", action="store_true", default=True)
    ap.add_argument("--no-open", action="store_true")
    ap.add_argument(
        "--hub",
        default="",
        help="Unity project root for ?hub= (auto-detected from package path when omitted)",
    )
    ap.add_argument("--restart", action="store_true", help="Stop stale server and start fresh")
    args = ap.parse_args()

    _ensure_dist()
    if args.restart or (_http_ok() and not _planner_ready()):
        _stop_stale_server()
        _stop_wrong_server_on_port()
        time.sleep(0.3)
    elif not _planner_ready():
        _stop_wrong_server_on_port()
    if not _planner_ready():
        _start_server()
        for _ in range(40):
            if _planner_ready():
                break
            time.sleep(0.25)

    hub = (args.hub or os.environ.get("HUB_ROOT", "") or _default_hub()).strip()
    url = f"http://127.0.0.1:{_PORT}/"
    if hub:
        url += f"?hub={urllib.parse.quote(hub, safe='')}"
    else:
        print(
            "Warning: could not detect Hub project — open with ?hub=/path/to/your/Unity/project",
            file=sys.stderr,
        )

    if args.open and not args.no_open:
        subprocess.run(["open", url], check=False)

    print(url)


if __name__ == "__main__":
    main()
