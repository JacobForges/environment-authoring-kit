## Rung: trail_walkability

Ensure trail splines exist and `SurfaceRouteProbeRunner` passes.

- Regenerate trails via `SurfaceWorldGenerator` with `SurfaceIncludeTrails`.
- Fix route probe issues (steep segments, gaps at cave mouth).
- Export: `CaveBuildSurfaceRouteProbe.json`.
