# POST_BUILD_PLAYTHROUGH

1. Build ends at `CaveBuildPostBuildFinalizeGate` — do not finalize timelapse early.
2. Human enters Play Mode and records gameplay; bot fixes gate code only.
3. After playthrough: scene save → prefab export → `DemoRecapPresentation.mp4` compose.
4. `CaveBuildPauseController.PostBuildPlaythroughActive` must clear before compose.

Tag: `// [bot:rung:post_build_playthrough:DATE]`
