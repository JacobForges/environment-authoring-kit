# MOUTH_DEPTH_50M

1. Read `cave_mouth_seal` and `ground_placement` scores.
2. Apply `CaveGroundPlacementUtility.TrySnapMouthToSurfaceDepthOnly` — **XZ locked**, depth only.
3. Do not terrain-sculpt mouth in meat loop unless rubric explicitly says terrain_carve.
4. Re-grade — placement error should drop below 1m.

Tag: `// [bot:rung:cave_mouth_seal:DATE]`
