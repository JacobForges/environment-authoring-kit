---
name: environment-kit-scene-camera
description: >-
  Owns Environment Authoring Kit Scene view camera during live builds. Use when
  changing zoom, framing, cinematic follow, demo recording shots, or any
  CaveBuildLiveSceneFeedback / SceneView camera behavior in Hub.
---

# Environment Kit Scene Camera

## Single owner

All Scene view camera actions go through:

`Packages/com.cursor.environment-authoring-kit/Editor/Blockout/CaveBuildSceneCameraDirector.cs`

Do not add ad-hoc `SceneView.Frame`, `SceneView.LookAt`, or zoom constants in other files.

`CaveBuildLiveSceneFeedback.cs` only dispatches beats and draws the LIVE BUILD banner.

## Shot grammar (cinema crew)

| Beat | Shot role | Intent |
|------|-----------|--------|
| SessionOpen, PhaseChange, BuildArea | EstablishingWide | Show the whole world — never tight on one tile |
| TerrainTilePlaced, PipelineStep | MasterCoverage | Hold wide; reframe every 8th tile, not every tile |
| PropPlaced | MediumCoverage | Context around placement, still readable at distance |
| DemoRecording | EstablishingWide | Stable documentary wide shot — no tile punch-in, no orbit |

## Rules

1. **Coverage hierarchy:** `TryResolvePlacedTerrainWorkBounds` (all placed tiles) frames the shot; live focus only biases pivot via `FocusPivotBlend` — never collapse to one tile.
2. **Hold windows:** `ShotProfileFor(role).HoldSeconds` and `ReframeEveryNthEvent` (terrain tiles: every 8th) — no jitter on every placement.
3. **Cinematic vs documentary:** cinematic = slow orbit + `LookAt` on terrain pivot; documentary = padded `Frame(bounds)`.
4. **Demo recording:** `PushDemoRecordingSession` forces EstablishingWide + `ApplyDocumentaryFrame` (padded `Frame(bounds)`); no cinematic orbit/dolly; `ApplyDemoCaptureFrame()` runs before each Scene PNG capture.
5. **Zoom slider:** `liveSceneCameraZoomOut` maps to `ZoomCoverageScale()` (0.75–5.0× orbit distance; `LookAt` size stays coverage-based so pull-back is visible) — single application on distance/padding, not stacked on view size.
6. **Settings:** `CaveBuildCursorSettings.showLiveScenePlacement` and `cinematicSceneCamera`; respect `EnvironmentKitHardwareBudget.DisableLiveSceneFraming`.

## Changing behavior

1. Edit shot profiles in `ShotProfileFor(ShotRole)` — not scattered magic numbers.
2. Add a new `CameraBeat` only if a new build event needs distinct coverage.
3. Route callers through `CaveBuildSceneCameraDirector.TryRequest` or the typed helpers (`RequestTerrainTile`, etc.).

## Do not

- Tighten framing on every terrain tile
- Claim camera is fixed without describing which shot role changed
- Switch workspace or run git operations when asked only for camera work
