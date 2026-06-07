# MISSING_SPLINE

1. Open `CaveBuildFailingStages.json` — confirm `path` stage issues.
2. In `CaveBuildQualityStageFixer.FixPath`, call `TryBootstrapSplinePathFromMetadata`.
3. Adventure caves: if still < 4 knots, call `TryBootstrapMissingTrue3DShell`.
4. Re-grade; route probe should show path steps > 0.

Tag: `// [bot:rung:path:DATE]`
