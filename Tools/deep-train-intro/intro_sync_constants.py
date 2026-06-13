#!/usr/bin/env python3
"""Shared intro sync knobs — keep Python render + Unity playback aligned."""

# Slower beat playback = longer hold on each still (Unity VideoPlayer.playbackSpeed on beats).
BEAT_PLAYBACK_SPEED = 0.45

# Narration wall clock matches video wall clock (grid narr @ 0, beat narr @ beat start).
NARRATION_VIDEO_DELAY_S = 0.0

GRID_S = 12.0
TITLE_HOLD_S = 12.0

# Beat lines get more wall time than gaps in the audio plan.
BEAT_SLOT_TIME_BIAS = 1.42
