# Help Me Run Environment Kit

This guide is intentionally split into two tones:

- **Casual quick start** (fast, plain-English)
- **Professional runbook** (repeatable/demo-safe)

---

## Casual Quick Start

If you just want to run it without babysitting:

1. Open **Window -> Environment Kit -> Hub**.
2. In Build tab, use **Full AAA Rebuild + Recording** (inside the AAA foldout).
3. Let it run. You should not need to click through pipeline popups.
4. Watch progress in Hub:
   - Pipeline step
   - Main action / Sub-action
   - Live status and activity feed
5. For demo video output:
   - In **Scene view & playtest -> Demo recording**, verify ffmpeg status.
   - If ffmpeg is missing, click **Install ffmpeg helper** and follow the commands.
6. After build:
   - use **Reveal last demo output**
   - recap artifacts are in `Library/EnvironmentKit/DemoCapture/<timestamp>/`

---

## Professional Runbook

### 1) Preconditions

- Unity 6 project compiles.
- Ground anchor is valid (not modular grid blockout when running FullWorld).
- Hub is open before build start.
- If you need a stitched MP4 recap, ffmpeg is installed and detected in Hub.

### 2) Recommended operator flow

1. Open **Environment Kit Hub**.
2. Confirm generation style preset.
3. Expand AAA foldout and click:
   - **Full AAA Rebuild + Recording**
4. Do not interrupt pipeline unless blocked.
5. Monitor:
   - step counter
   - main/sub action
   - live status markdown
6. On completion or block:
   - review completion banner in Hub
   - inspect generated readout and demo recap artifacts

### 3) Non-modal behavior policy

- Build start and failure/readout paths are now non-modal during normal Hub-driven flows.
- Blocking conditions are reported through Hub banner + logs instead of modal popups.

### 4) ffmpeg setup (for recap MP4)

Hub includes:

- **Verify ffmpeg now**
- **Install ffmpeg helper**

Manual commands (macOS):

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
brew install ffmpeg
```

Reopen Unity or force script/domain refresh, then verify ffmpeg status in Hub.

### 5) Output locations

- Build live status:
  - `Library/EnvironmentKit/CaveBuildLiveRunStatus.md`
- Demo recap:
  - `Library/EnvironmentKit/DemoCapture/<timestamp>/`
  - includes `DemoRecap.md` and (when ffmpeg exists) `DemoRecap.mp4` or producer `DemoRecapPresentation.mp4`

### 6) Personal Voice narration (producer recaps)

Approved voice + ladder settings:

`Library/EnvironmentKit/DemoRecapApproved/ApprovedCards.json`

Documentation (in the package):

- `Tools/cave-grader/PERSONAL_VOICE_NARRATION.md` — word queue, breath cue, troubleshooting
- `Tools/cave-grader/DEMO_RECAP_PIPELINE.md` — Unity menus + `run-producer-recap.py`
- `Tools/cave-grader/open-personal-voice-settings.md` — macOS permissions

Run narration tests from **Terminal.app** (not Cursor’s shell). Quick test:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
python3 prepare-narrator-voice.py --test
```

