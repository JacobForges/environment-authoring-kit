#!/usr/bin/env python3
"""Start or refresh AI Director API + static UI.

  python3 ensure-ai-director.py /path/to/DemoCapture/<ts> [--open|--no-open]
"""
from __future__ import annotations

import argparse
import json
import os
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
_DIRECTOR = _TOOLS / "ai-director"
_SERVER = _TOOLS / "ai-director-server.py"

import build_planner as _planner  # noqa: E402
from envkit_paths import server_runtime_dir  # noqa: E402

_RUNTIME = server_runtime_dir()
_PID_FILE = _RUNTIME / "ai-director-server.pid"
_LOG_FILE = _RUNTIME / "ai-director-server.log"
_PORT_FILE = _PID_FILE.parent / "ai-director-port.txt"
DEFAULT_PORT = 8767


def _port() -> int:
    return int(os.environ.get("AI_DIRECTOR_PORT", str(DEFAULT_PORT)))


def _python() -> str:
    for candidate in (
        "/opt/homebrew/bin/python3",
        "/usr/local/bin/python3",
        "/usr/bin/python3",
        sys.executable,
    ):
        if candidate and Path(candidate).is_file():
            return candidate
    return "python3"


def _http_get(url: str, timeout: float = 2.0) -> tuple[int, str]:
    try:
        req = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", errors="replace")
    except Exception:
        return 0, ""


def _http_post(url: str, body: dict, timeout: float = 3.0) -> tuple[int, str]:
    data = json.dumps(body).encode("utf-8")
    try:
        req = urllib.request.Request(
            url,
            data=data,
            method="POST",
            headers={"Content-Type": "application/json"},
        )
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            return resp.status, resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", errors="replace")
    except Exception as exc:
        return 0, str(exc)


def api_healthy(port: int) -> bool:
    code, body = _http_get(f"http://127.0.0.1:{port}/api/health")
    return code == 200 and '"ok"' in body and "ai-director" in body


def api_has_routes(port: int) -> bool:
    code, body = _http_get(f"http://127.0.0.1:{port}/api/director/health")
    return code == 200 and "ai-director" in body


def pids_on_port(port: int) -> list[int]:
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


def kill_pid(pid: int) -> None:
    try:
        os.kill(pid, signal.SIGTERM)
        time.sleep(0.2)
        os.kill(pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


def stop_api(port: int) -> None:
    if _PID_FILE.is_file():
        try:
            kill_pid(int(_PID_FILE.read_text().strip()))
        except (ValueError, OSError):
            pass
    for p in pids_on_port(port):
        kill_pid(p)
    _PID_FILE.unlink(missing_ok=True)


def dist_needs_rebuild() -> bool:
    dist_index = _DIRECTOR / "dist" / "index.html"
    if not dist_index.is_file():
        return True
    for rel in ("App.jsx", "index.css"):
        src = _DIRECTOR / "src" / rel
        if src.is_file() and src.stat().st_mtime > dist_index.stat().st_mtime:
            return True
    return False


def ensure_dist_built() -> None:
    if not (_DIRECTOR / "package.json").is_file():
        return
    if not dist_needs_rebuild():
        return
    npm = subprocess.run(["which", "npm"], capture_output=True, text=True).stdout.strip()
    if not npm:
        print("ai-director/dist stale/missing and npm not found", file=sys.stderr)
        return
    print("Building ai-director/dist…", flush=True)
    subprocess.run(["npm", "install", "--silent"], cwd=str(_DIRECTOR), check=False)
    subprocess.run(["npm", "run", "build"], cwd=str(_DIRECTOR), check=False)


def start_server(capture: Path, port: int) -> None:
    _LOG_FILE.parent.mkdir(parents=True, exist_ok=True)
    log_fd = open(_LOG_FILE, "a", encoding="utf-8")
    env = _planner._planner_subprocess_env()
    proc = subprocess.Popen(
        [_python(), str(_SERVER), "--capture", str(capture), "--port", str(port)],
        cwd=str(_TOOLS),
        stdout=log_fd,
        stderr=subprocess.STDOUT,
        start_new_session=True,
        env=env,
    )
    _PID_FILE.write_text(f"{proc.pid}\n", encoding="utf-8")
    _PORT_FILE.write_text(f"{port}\n", encoding="utf-8")
    try:
        from envkit_paths import resolve_envkit_root

        alt = resolve_envkit_root() / "ai-director-port.txt"
        alt.parent.mkdir(parents=True, exist_ok=True)
        alt.write_text(f"{port}\n", encoding="utf-8")
    except Exception:
        pass
    print(f"Started ai-director-server.py pid={proc.pid} port={port}", flush=True)


def wait_ready(port: int, timeout_sec: float = 15.0) -> bool:
    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        if api_healthy(port) and api_has_routes(port):
            return True
        time.sleep(0.25)
    return api_healthy(port)


def register_gate(capture: Path, port: int) -> None:
    enc = urllib.parse.quote(str(capture.resolve()))
    _http_post(f"http://127.0.0.1:{port}/api/capture/compose/gate?path={enc}", {"waiting": True})
    _http_post(f"http://127.0.0.1:{port}/api/director/session/start", {"capture": str(capture)})


def director_url(capture: Path, port: int) -> str:
    enc = urllib.parse.quote(str(capture.resolve()))
    return f"http://127.0.0.1:{port}/?capture={enc}"


def latest_capture() -> Path | None:
    try:
        from envkit_paths import resolve_envkit_root
    except ImportError:
        return None
    root = resolve_envkit_root() / "DemoCapture"
    if not root.is_dir():
        return None
    runs = sorted(
        (p for p in root.iterdir() if p.is_dir() and (p / "timelapse").is_dir()),
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )
    return runs[0] if runs else None


def ensure(capture: Path | None, *, open_ui: bool) -> int:
    port = _port()
    resolved = capture.expanduser().resolve() if capture else latest_capture()
    if resolved is None or not resolved.is_dir():
        print("No capture folder found.", file=sys.stderr)
        return 1

    ensure_dist_built()
    if not api_healthy(port) or not api_has_routes(port):
        stop_api(port)
        start_server(resolved, port)
        if not wait_ready(port):
            print(f"AI Director API not ready on :{port}. See {_LOG_FILE}", file=sys.stderr)
            return 1
    else:
        print(f"AI Director API already ready on :{port}", flush=True)

    register_gate(resolved, port)
    url = director_url(resolved, port)
    print(f"AI Director: {url}", flush=True)
    if open_ui and sys.platform == "darwin":
        subprocess.run(["/usr/bin/open", url], check=False)
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("capture", type=Path, nargs="?")
    ap.add_argument("--open", action="store_true")
    ap.add_argument("--no-open", action="store_true")
    args = ap.parse_args()
    open_ui = args.open or (sys.platform == "darwin" and not args.no_open)
    return ensure(args.capture, open_ui=open_ui)


if __name__ == "__main__":
    raise SystemExit(main())
