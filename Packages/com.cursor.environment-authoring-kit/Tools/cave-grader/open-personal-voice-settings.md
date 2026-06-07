# Open Personal Voice settings (macOS 14–26)

Apple moved this — it is **not** under Privacy & Security.

Full narration workflow: [PERSONAL_VOICE_NARRATION.md](PERSONAL_VOICE_NARRATION.md)  
Helper / ladder reference: [VOICE_HELPERS.md](VOICE_HELPERS.md)

---

## Path

1. **System Settings** (Apple menu)
2. **Accessibility** (sidebar)
3. **Speech** → **Personal Voice**  
   — or on some builds, **Personal Voice** is directly under Accessibility

## What to check

- Your voice (**Jacob Adkins**) shows **Ready** (not still recording/processing).
- Section: **Allow applications to use your Personal Voice**
  - **Terminal** (or **Cursor**, **iTerm**) appears here only after you run the authorize script **from that same app** and click **Allow** on the popup.

You usually **cannot** add Terminal manually with a **+** button; the app must request access first.

## Trigger the allow prompt

In **Terminal** (recommended for recaps — Cursor’s shell often cannot capture Personal Voice):

```bash
cd ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
bash authorize-personal-voice.sh
```

Click **Allow** if macOS asks.

## Test

```bash
python3 prepare-narrator-voice.py --test
afplay ~/Desktop/PersonalVoice-Test.wav
```

Uses `narratorTestPhrase` from `~/Hub/Library/EnvironmentKit/DemoRecapApproved/ApprovedCards.json` unless you pass `--test-line "..."`.

You should see log lines for word-queue capture and, if the phrase contains `[[breath]]`, `breathCue: inhale/exhale inserted before 'grin'`.

## Producer recap (Terminal)

```bash
bash run-recap-terminal.sh ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp> --preview
```
