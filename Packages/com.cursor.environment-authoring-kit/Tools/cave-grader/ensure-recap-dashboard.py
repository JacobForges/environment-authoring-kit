#!/usr/bin/env python3
"""Start or refresh recap dashboard API + static UI — no manual server management.

Unity, bash, and agents call this instead of hand-starting recap-dashboard-server.py.

  python3 ensure-recap-dashboard.py /path/to/DemoCapture/<ts> [--open|--no-open]

Behavior:
  - Kills stale API on RECAP_DASHBOARD_PORT when review routes are missing or script is older
  - Ensures recap-dashboard/dist exists (runs npm build once if missing)
  - Starts recap-dashboard-server.py detached with pid file
  - Registers compose gate waiting for the capture folder
  - Opens browser to review UI (macOS) unless --no-open
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
_DASHBOARD = _TOOLS / "recap-dashboard"
_SERVER = _TOOLS / "recap-dashboard-server.py"
from envkit_paths import server_runtime_dir  # noqa: E402

_RUNTIME = server_runtime_dir()
_PID_FILE = _RUNTIME / "recap-dashboard-server.pid"
_LOG_FILE = _RUNTIME / "recap-dashboard-server.log"


def _port() -> int:
    return int(os.environ.get("RECAP_DASHBOARD_PORT", "8765"))


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
    return code == 200 and '"ok"' in body


def api_has_review_routes(port: int) -> bool:
    code, body = _http_get(f"http://127.0.0.1:{port}/api/approved/assets")
    return code == 200 and "approvedDir" in body and "unknown api route" not in body


def api_has_narration_review_route(port: int) -> bool:
    code, body = _http_get(f"http://127.0.0.1:{port}/api/capture/narration/review?path=/")
    if code == 404 and "unknown api route" in body:
        return False
    return code in (200, 404)


def api_has_compose_start_route(port: int) -> bool:
    """Detect stale recap server from before Proceed spawned compose directly."""
    code, body = _http_post(
        f"http://127.0.0.1:{port}/api/capture/compose/start",
        {"capture": "/nonexistent-path-for-probe"},
        timeout=1.5,
    )
    if code == 404 and "unknown route" in body:
        return False
    return code in (200, 400, 404)


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
    pids: list[int] = []
    for line in (out.stdout or "").splitlines():
        line = line.strip()
        if line.isdigit():
            pids.append(int(line))
    return pids


def read_pid_file() -> int | None:
    try:
        raw = _PID_FILE.read_text(encoding="utf-8").strip()
        return int(raw) if raw.isdigit() else None
    except OSError:
        return None


def write_pid_file(pid: int) -> None:
    _PID_FILE.parent.mkdir(parents=True, exist_ok=True)
    _PID_FILE.write_text(f"{pid}\n", encoding="utf-8")


def kill_pid(pid: int) -> None:
    try:
        os.kill(pid, signal.SIGTERM)
        time.sleep(0.25)
        os.kill(pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    except PermissionError:
        subprocess.run(["kill", "-9", str(pid)], check=False)


def stop_api(port: int) -> None:
    pid = read_pid_file()
    if pid:
        kill_pid(pid)
    for p in pids_on_port(port):
        kill_pid(p)
    # Shell kill works when launched from Unity/Terminal (full user session).
    subprocess.run(
        ["/bin/bash", "-c", f"lsof -ti :{port} | xargs kill -9 2>/dev/null || true"],
        check=False,
    )
    time.sleep(0.5)
    try:
        _PID_FILE.unlink(missing_ok=True)
    except OSError:
        pass


def dist_needs_rebuild() -> bool:
    dist_index = _DASHBOARD / "dist" / "index.html"
    if not dist_index.is_file():
        return True
    src_app = _DASHBOARD / "src" / "App.jsx"
    if src_app.is_file() and src_app.stat().st_mtime > dist_index.stat().st_mtime:
        return True
    src_css = _DASHBOARD / "src" / "index.css"
    if src_css.is_file() and src_css.stat().st_mtime > dist_index.stat().st_mtime:
        return True
    return False


def ensure_dist_built() -> None:
    if not (_DASHBOARD / "package.json").is_file():
        return
    if not dist_needs_rebuild():
        return
    npm = subprocess.run(["which", "npm"], capture_output=True, text=True).stdout.strip()
    if not npm:
        print("recap-dashboard/dist stale/missing and npm not found", file=sys.stderr)
        return
    print("Building recap-dashboard/dist…", flush=True)
    subprocess.run(["npm", "install", "--silent"], cwd=str(_DASHBOARD), check=False)
    subprocess.run(["npm", "run", "build"], cwd=str(_DASHBOARD), check=False)


def pick_port(preferred: int) -> int:
    if api_healthy(preferred) and api_has_review_routes(preferred):
        return preferred
    if not pids_on_port(preferred):
        return preferred
    for alt in range(preferred + 1, preferred + 6):
        if not pids_on_port(alt) and not api_healthy(alt):
            return alt
    return preferred


def start_server(capture: Path, port: int) -> int:
    _LOG_FILE.parent.mkdir(parents=True, exist_ok=True)
    log_fd = open(_LOG_FILE, "a", encoding="utf-8")
    cmd = [
        _python(),
        str(_SERVER),
        "--capture",
        str(capture),
        "--port",
        str(port),
    ]
    proc = subprocess.Popen(
        cmd,
        cwd=str(_TOOLS),
        stdout=log_fd,
        stderr=subprocess.STDOUT,
        start_new_session=True,
    )
    write_pid_file(proc.pid)
    port_file = _PID_FILE.parent / "recap-dashboard-port.txt"
    port_file.write_text(f"{port}\n", encoding="utf-8")
    try:
        from envkit_paths import resolve_envkit_root

        alt = resolve_envkit_root() / "recap-dashboard-port.txt"
        alt.parent.mkdir(parents=True, exist_ok=True)
        alt.write_text(f"{port}\n", encoding="utf-8")
    except Exception:
        pass
    print(f"Started recap-dashboard-server.py pid={proc.pid} port={port} log={_LOG_FILE}", flush=True)
    return port


def wait_ready(port: int, timeout_sec: float = 12.0) -> bool:
    deadline = time.time() + timeout_sec
    while time.time() < deadline:
        if api_healthy(port) and api_has_review_routes(port):
            return True
        time.sleep(0.25)
    return api_healthy(port) and api_has_review_routes(port)


def register_gate_waiting(capture: Path, port: int) -> None:
    enc = urllib.parse.quote(str(capture.resolve()))
    url = f"http://127.0.0.1:{port}/api/capture/compose/gate?path={enc}"
    _http_post(url, {"waiting": True})


def dashboard_url(capture: Path, port: int) -> str:
    enc = urllib.parse.quote(str(capture.resolve()))
    return f"http://127.0.0.1:{port}/?capture={enc}"


def open_browser(url: str) -> None:
    if sys.platform != "darwin":
        return
    subprocess.run(["/usr/bin/open", url], check=False)


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


def resolve_capture(path: Path | None) -> Path | None:
    if path is not None:
        capture = path.expanduser().resolve()
        return capture if capture.is_dir() else None
    return latest_capture()


def resolve_port() -> int:
    port_file = _PID_FILE.parent / "recap-dashboard-port.txt"
    if port_file.is_file():
        try:
            return int(port_file.read_text(encoding="utf-8").strip())
        except ValueError:
            pass
    return _port()


def server_script_newer_than_running(port: int) -> bool:
    """Restart when recap-dashboard-server.py changed since the pid file was written."""
    if not _SERVER.is_file():
        return False
    pid = read_pid_file()
    if not pid:
        return False
    try:
        pid_mtime = _PID_FILE.stat().st_mtime if _PID_FILE.is_file() else 0
        return _SERVER.stat().st_mtime > pid_mtime + 1.0
    except OSError:
        return False


def ensure(capture: Path | None, *, open_ui: bool) -> int:
    port = pick_port(resolve_port())
    resolved = resolve_capture(capture)
    if resolved is None:
        print("No capture folder found under DemoCapture/ (need timelapse/).", file=sys.stderr)
        return 1
    capture = resolved

    ensure_dist_built()

    needs_restart = (
        not api_healthy(port)
        or not api_has_review_routes(port)
        or not api_has_compose_start_route(port)
        or not api_has_narration_review_route(port)
        or server_script_newer_than_running(port)
    )
    if needs_restart:
        print(f"Ensuring recap API on :{port}…", flush=True)
        stop_api(port)
        port = start_server(capture, port)
        if not wait_ready(port):
            print(
                f"recap-dashboard-server did not become ready on :{port}. See {_LOG_FILE}",
                file=sys.stderr,
            )
            return 1
    else:
        print(f"Recap API already ready on :{port}", flush=True)

    register_gate_waiting(capture, port)
    url = dashboard_url(capture, port)
    print(f"Recap dashboard UI: {url}", flush=True)
    if open_ui:
        open_browser(url)
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("capture", type=Path, nargs="?", help="DemoCapture run folder")
    ap.add_argument("--open", action="store_true", help="Open browser (default on macOS)")
    ap.add_argument("--no-open", action="store_true", help="Do not open browser")
    args = ap.parse_args()

    open_ui = not args.no_open
    if sys.platform == "darwin" and not args.open and not args.no_open:
        open_ui = True
    return ensure(args.capture, open_ui=open_ui)


if __name__ == "__main__":
    raise SystemExit(main())
