# Publishing the Environment Authoring Kit

Checklist for publishing **`com.cursor.environment-authoring-kit`** to GitHub, OpenUPM, or a private registry.

## Before you tag a release

1. **Version** — bump `package.json` and add an entry to [CHANGELOG.md](../CHANGELOG.md).
2. **Compile** — open a Unity 6000 + URP project; zero console errors from the package.
3. **Smoke build** — **Build Complete Cave** to **120/120** on a scene with Ground + `PortalFive` + 9 terrains.
4. **Visual** — spot-check all terrain tiles for vegetation; cave has blocks or full shell (not ramp-only).
5. **Docs** — [README.md](../README.md), [REQUIREMENTS.md](REQUIREMENTS.md), and [docs/README.md](README.md) match menus and contracts.

## What to ship in the repo

| Include | Exclude |
|---------|---------|
| `Editor/`, `Runtime/`, `docs/`, `Tools/cave-grader/` sources | `Tools/cave-grader/node_modules/` |
| `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE.md` (educational free / commercial by agreement) | `Assets/EnvironmentKit/Generated/` (consumer project artifacts) |
| `Tools/cave-grader/package.json`, `.env.example` | `.env` with API keys |
| Optional: sample `Assets/EnvironmentKit/ResearchCache/` index (no huge binaries) | Personal absolute paths in docs |

## Unity package layout

UPM expects:

```
com.cursor.environment-authoring-kit/
├── package.json
├── README.md
├── CHANGELOG.md
├── Editor/
├── Runtime/
└── ...
```

Consumers add:

```json
"com.cursor.environment-authoring-kit": "https://github.com/YOU/YOUR_REPO.git?path=Packages/com.cursor.environment-authoring-kit#v0.2.0"
```

Or copy the folder into `Packages/`.

## Consumer project expectations

Document in your release notes:

- Unity **6000.0+**, **URP 17+**
- `Assets/EnvironmentKit/` for Generated + ResearchCache (or document retargeting)
- Optional: Node **18+** for `Tools/cave-grader`
- Florida research data — link [RESEARCH_DATA_ATTRIBUTION.md](RESEARCH_DATA_ATTRIBUTION.md)

## License bundle

- Kit **code/docs**: [LICENSE.md](../LICENSE.md) — educational use free; commercial license required for paid distribution.
- **Geospatial cache**: third-party terms — attribution required.
- **Unity / npm / Cursor SDK**: third-party terms apply.

See consuming repo `docs/THIRD_PARTY_AND_LICENSE_SCOPE.md` when shipping a game + kit together.
