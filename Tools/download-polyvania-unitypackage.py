#!/usr/bin/env python3
"""Download Polyvania .unitypackage from itch.io (free, no API key)."""
from __future__ import annotations

import json
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path

GAME_URL = "https://emaceart.itch.io/polyvania"
OUT_DIR = Path(__file__).resolve().parents[1] / "Assets" / "_Import" / "Polyvania"
TARGET_SUBSTR = "unitypackage"


def fetch(url: str, data: bytes | None = None, headers: dict | None = None) -> bytes:
    req = urllib.request.Request(url, data=data, headers=headers or {})
    with urllib.request.urlopen(req, timeout=120) as resp:
        return resp.read()


def main() -> int:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    cookie_jar: dict[str, str] = {}

    def request(url: str, data: dict | None = None) -> bytes:
        headers = {
            "User-Agent": "Mozilla/5.0 Hub-Polyvania-Downloader",
            "Referer": GAME_URL,
        }
        if cookie_jar:
            headers["Cookie"] = "; ".join(f"{k}={v}" for k, v in cookie_jar.items())
        body = None
        if data is not None:
            body = urllib.parse.urlencode(data).encode("utf-8")
            headers["Content-Type"] = "application/x-www-form-urlencoded"
        req = urllib.request.Request(url, data=body, headers=headers)
        with urllib.request.urlopen(req, timeout=120) as resp:
            for key, val in resp.headers.items():
                if key.lower() == "set-cookie":
                    part = val.split(";", 1)[0]
                    if "=" in part:
                        k, v = part.split("=", 1)
                        cookie_jar[k] = v
            return resp.read()

    page = request(GAME_URL).decode("utf-8", errors="replace")
    csrf = re.search(r'name="csrf_token" value="([^"]+)"', page)
    if not csrf:
        print("Could not find csrf_token on game page", file=sys.stderr)
        return 1

    purchase_html = request(
        f"{GAME_URL}/purchase",
        {"csrf_token": csrf.group(1), "price": "0"},
    ).decode("utf-8", errors="replace")

    # itch embeds upload ids in download buttons after purchase
    upload_ids = sorted(set(re.findall(r"/file/(\d+)", purchase_html)))
    if not upload_ids:
        upload_ids = sorted(set(re.findall(r"data-upload_id=\"(\d+)\"", purchase_html)))

    if not upload_ids:
        print("No upload ids found — try manual download from itch", file=sys.stderr)
        return 1

    print(f"Found {len(upload_ids)} upload(s): {upload_ids}")

    for upload_id in upload_ids:
        meta_url = f"https://itch.io/api/1/{upload_id}/upload"
        try:
            meta = json.loads(request(meta_url).decode("utf-8"))
        except Exception as exc:
            print(f"skip {upload_id}: {exc}")
            continue

        filename = meta.get("filename") or meta.get("display_name") or f"upload-{upload_id}"
        print(f"  {upload_id}: {filename}")
        if TARGET_SUBSTR not in filename.lower():
            continue

        dl_url = f"https://emaceart.itch.io/polyvania/download/{upload_id}"
        print(f"Downloading {filename} …")
        data = request(dl_url)
        out = OUT_DIR / filename
        out.write_bytes(data)
        print(f"Saved {out} ({len(data) / (1024*1024):.1f} MB)")
        return 0

    print("No unitypackage upload found", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
