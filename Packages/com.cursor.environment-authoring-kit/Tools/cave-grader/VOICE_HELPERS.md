# Personal Voice helpers

Named post-processing steps after macOS **Personal Voice** capture (`say` + `mysay`).  
Configure in `~/Hub/Library/EnvironmentKit/DemoRecapApproved/ApprovedCards.json`.

**Narration workflow (word queue, breath cue, producer recaps):** [PERSONAL_VOICE_NARRATION.md](PERSONAL_VOICE_NARRATION.md)

---

## Quick config

```json
"voiceLadder": {
  "enabled": true,
  "phases": {
    "algorithm": ["antiStutter", "lowClean", "speakerSafe", "steadyVoice"],
    "character": ["character", "casualTone", "warmth", "breathSmooth", "vocalSpark"],
    "tone": ["naturalPitch", "phraseFall", "phraseDynamics", "ttsSmooth"],
    "polish": ["deClick", "autoEq25", "midTreble", "autoDynamics", "spatialHarmony", "deEss"],
    "auditor": true,
    "finalizer": ["finalizer"]
  }
},
"voiceHelpers": {
  "preset": "ladderDocumentary",
  "sayRate": 190
}
```

Flat chain (no ladder):

```json
"voiceHelpers": {
  "preset": "custom",
  "chain": ["antiStutter", "character", "deEss", "warmth"],
  "sayRate": 185
}
```

---

## Voice ladder phases

| Phase | Role |
|-------|------|
| **algorithm** | De-stutter, tame sub/chest (`lowClean`, `speakerSafe`), optional level ride (`steadyVoice`) |
| **character** | Body, warmth, plosive smoothing, spark |
| **tone** | Contour pitch (`naturalPitch`), phrase fall/dynamics, optional `ttsSmooth` |
| **polish** | deClick, **autoEq25**, mids/treble, dynamics, **spatialHarmony**, deEss |
| **auditor** | Peak/RMS/tail QC; trim disabled when tail finish needs full ending |
| **finalizer** | loudnorm (I=-16) + limiter |

List default phase map:

```bash
python3 personal-voice-ladder.py
```

**Word-queue note:** When `narratorUsedWordQueue` is set after a word-queue merge, helpers with `skipOnWordQueue: true` (default for `ttsSmooth`, `steadyVoice`) are skipped so joins stay natural.

---

## Presets

| Preset | Behavior |
|--------|----------|
| **ladderDocumentary** | Uses `voiceLadder.phases` (recommended) |
| **human** / **humanCharacter** | Flat: antiStutter → character → deEss |
| **documentary** | Flat + warmth |
| **raw** | No post-processing |
| **pitchAutotune** | humanSmooth + pitchAutotune — easy to sound robotic |

---

## Helpers (implemented)

| ID | What it does |
|----|----------------|
| **antiStutter** | Envelope leveling, micro-gap fill, light compression |
| **lowClean** | High-pass + sub/chest tame (deep Personal Voice lows) |
| **speakerSafe** | Extra low cut for spatial / bass-heavy speakers |
| **steadyVoice** | Light level-ride + mid clarity (`skipOnWordQueue` recommended) |
| **character** | Presence, mids, gentle dynamics |
| **casualTone** | Warm clarity — less stiff delivery |
| **warmth** | Low-mid body + soft shelf |
| **breathSmooth** | Gentle compressors on plosives / breath spikes |
| **vocalSpark** | Forward mids + capped treble lift |
| **voiceFlair** | Extra punch (often disabled in approved cards) |
| **naturalPitch** | Contour smoothing via autotune module (high dry mix) |
| **phraseFall** | Gentle energy dip at phrase ends (skipped during per-word capture only) |
| **phraseDynamics** | Light phrase-to-phrase gain motion |
| **ttsSmooth** | Soft clip + chorus glue — **skip on word queue** |
| **naturalPacing** | Phrase atempo heuristics — **off** in approved cards (can slur) |
| **deClick** | FFmpeg declick |
| **autoEq20** / **autoEq25** | Log-spaced speech EQ vs target curve (25 = more mid resolution) |
| **midTreble** | Documentary mids forward, treble capped |
| **autoDynamics** | Light expansion |
| **spatial3d** | Legacy Haas / width |
| **spatialHarmony** | Bass mono, M/S width, Haas, elevation (`style: soundcore`) |
| **deEss** | Sibilance tame |
| **auditor** | QC + safe fixes |
| **finalizer** | Broadcast loudness + limiter |
| **humanSmooth** | Legacy envelope smooth (flat presets) |
| **pitchAutotune** | PYIN correction — use low strength |
| **roomTone** | Subtle echo — optional |
| **raw** | Passthrough |

### Post-capture (not ladder helpers)

| Feature | Config block | Role |
|---------|--------------|------|
| **Phrase queue** | `voiceHelpers.wordQueue` | Default `captureMode: "sentence"` — one capture per punctuation phrase; `word` for legacy per-word — [PERSONAL_VOICE_NARRATION.md](PERSONAL_VOICE_NARRATION.md) |
| **Breath cue** | `voiceHelpers.breathCue` | Synthetic inhale/exhale at `[[breath]]` — not spoken |
| **Tail finish** | `voiceHelpers.tailFinish` | Final pad; optional last-word re-capture |

---

## Commands

```bash
cd ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader

python3 personal-voice-helpers.py
python3 personal-voice-ladder.py
python3 prepare-narrator-voice.py --list-helpers
python3 prepare-narrator-voice.py --test
python3 tune-personal-voice.py
bash authorize-personal-voice.sh
bash test-personal-voice.sh
```

---

## Planned (not built)

- **noiseGate** — floor between sentences  
- **voiceCloneMatch** — timbre match to reference WAV  
- **loudnessMatch** — EBU target LUFS  
- **transcriptAlign** — stretch video to word boundaries from SRT  

---

## Source files

| File | Role |
|------|------|
| `personal-voice-helpers.py` | Registry + `HELPER_APPLY` |
| `personal-voice-ladder.py` | Phase runner |
| `personal-voice-autotune.py` | `naturalPitch` / pitchAutotune |
| `personal-voice-fluent.py` | Legacy humanSmooth path |
| `personal-voice-speak.swift` | AVSpeech + authorize |
| `mysay.dylib` / `mysay.c` | Capture `say` audio to file |
| `demo-recap-narrator.py` | Capture + deliver + word queue |
