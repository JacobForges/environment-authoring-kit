#!/usr/bin/env python3
"""Export Unity AssetPreview thumbnails for build-wizard prop concept cards."""
from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

_TOOLS = Path(__file__).resolve().parent
if str(_TOOLS) not in sys.path:
    sys.path.insert(0, str(_TOOLS))

from planner_asset_catalog import (  # noqa: E402
    export_kit_catalog_sync,
    kit_catalog_thumb_count,
    refresh_prop_cards_from_catalog,
)

from envkit_paths import atomic_write_text, planner_session_path  # noqa: E402

SESSION_REL = Path("Assets/EnvironmentKit/Generated/CaveBuildPlannerSession.json")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--hub", type=Path, required=True, help="Hub project root")
    parser.add_argument("--refresh-session", action="store_true", help="Update prop cards in planner session")
    args = parser.parse_args()
    hub = args.hub.expanduser().resolve()
    if not hub.is_dir():
        print(f"ERROR: hub not found: {hub}", file=sys.stderr)
        return 1

    ok, msg = export_kit_catalog_sync(hub)
    print(msg)
    if not ok:
        return 1

    count = kit_catalog_thumb_count(hub)
    print(f"Catalog thumbs on disk: {count}")

    if args.refresh_session:
        session_path = planner_session_path(hub)
        if session_path.is_file():
            doc = json.loads(session_path.read_text(encoding="utf-8"))
            if refresh_prop_cards_from_catalog(hub, doc):
                doc["conceptImageTs"] = int(datetime.now(timezone.utc).timestamp())
                doc.pop("kitCatalogExporting", None)
                doc["kitCatalogExportMessage"] = msg
                atomic_write_text(session_path, json.dumps(doc, indent=2) + "\n")
                print("Refreshed prop concept card thumbnails in planner session.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
