#!/usr/bin/env python3
"""Render ~30s intro audio preview — natural VO + spatial SFX (video integration later)."""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
VENV_PY = SCRIPT_DIR / ".venv-intro-vo" / "bin" / "python"


def ensure_deps() -> None:
    py = str(VENV_PY if VENV_PY.is_file() else sys.executable)
    subprocess.run([py, "-m", "pip", "install", "-q", "edge-tts>=6.1"], check=False)


def main() -> int:
    ensure_deps()
    py = str(VENV_PY if VENV_PY.is_file() else sys.executable)
    return subprocess.call([py, str(SCRIPT_DIR / "compose-intro-preview.py")])


if __name__ == "__main__":
    raise SystemExit(main())
