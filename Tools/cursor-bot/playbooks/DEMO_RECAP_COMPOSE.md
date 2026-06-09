# DEMO_RECAP_COMPOSE

1. Timelapse frames exist but no MP4 — check `DemoCapture/` under data root (Lexar or local).
2. Call `CaveBuildDemoAutoRecorder.TryComposeOrphanedCaptureIfIdle` when gate idle.
3. Ensure playthrough MP4 in `uploads/playthroughs/` before final compose.
4. Release compose hold: `ReleaseComposeHoldAndFinalize()` after post-build playthrough.

Tag: `// [bot:rung:demo_recap:DATE]`
