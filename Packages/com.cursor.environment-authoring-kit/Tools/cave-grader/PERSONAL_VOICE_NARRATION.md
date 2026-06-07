# Personal Voice narration (demo recap)

macOS **Personal Voice** (`say` + `mysay.dylib`) captures narration, then the **voice ladder** post-processes the WAV. Long scripts use a **word queue** (one capture per word, with safe 2-word pairs). All tuning lives in:

`~/Hub/Library/EnvironmentKit/DemoRecapApproved/ApprovedCards.json`

---

## Quick start

1. **Authorize** Personal Voice from the same app you will use for captures (usually **Terminal.app**, not Cursor’s shell):

   ```bash
   cd ~/Hub/Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
   bash authorize-personal-voice.sh
   ```

   See [open-personal-voice-settings.md](open-personal-voice-settings.md) if the voice is not **Ready** or Terminal is not listed.

2. **Test** the approved phrase (~90 words, ~20–35 min first run with word queue):

   ```bash
   python3 prepare-narrator-voice.py --test
   afplay ~/Desktop/PersonalVoice-Test.wav
   ```

3. **Producer recap** (full presentation + narration) — use Terminal so Personal Voice is visible:

   ```bash
   bash run-recap-terminal.sh ~/Hub/Library/EnvironmentKit/DemoCapture/<timestamp> --preview
   ```

---

## Capture order (`demo-recap-narrator.py`)

For each narration line, `speech_to_wav()` tries paths in this order:

| Order | Path | When |
|-------|------|------|
| 1 | **Portfolio word queue** | `narratorPortfolioMode` + `wordQueue.portfolioFirst` + word count ≤ `portfolioMaxWords` |
| 2 | **Emphasis segment capture** | `narrationEmphasisCapture: true` and phrase matches `narrationEmphasisMoments` — **off by default** when word queue is on (avoids batchy phrase sound) |
| 3 | **Word queue** | `narratorWordQueue` + `wordQueue.enabled` and length within `maxWords` / portfolio limit |
| 4 | **mysay** single pass | `say` + `mysay.dylib` → one WAV |
| 5 | **AVSpeech** | Swift fallback if Personal Voice capture fails |

After a successful capture, **`deliver_personal_voice_wav()`** runs the ladder, then **tail / breath** finishing (see below).

---

## Phrase queue (`voiceHelpers.wordQueue`)

**Default (approved):** `captureMode: "sentence"` — one Personal Voice capture per **punctuation phrase**, not per word.

| Behavior | Detail |
|----------|--------|
| **Sentence mode** | Split on `.` `!` `?` `…`, then em-dash `—`, then long clauses on `,` if over `maxWordsPerUnit` |
| **Word mode** | Set `captureMode: "word"` — legacy per-word + risky 2-word pairs |
| Pauses | After each phrase from its ending punctuation (`periodPauseMs`, `commaPauseMs`, …) |
| Join | `crossfadeMs` + **phrase release/attack** (`phraseReleaseMs`, `phraseAttackMs`, `boundaryDip`) — voice eases down before each pause, eases up into the next phrase |
| Pronunciation QC | Multiple `say` attempts + `sayAs` overrides |
| Watchdog | Per-phrase timeouts scaled by character count |
| Rate | ~**186 wpm** documentary pace (`sayRate`); tiny `perUnitStepWpm` drift — **not** 206+ word-boost |

**Runtime:** ~32 s per phrase (`estSecPerUnit`) — a ~90-word test paragraph is often **~8–15 minutes** (many short sentences), vs 20–35+ min word-by-word.

**Ladder:** `ttsSmooth` / `steadyVoice` skip only in **word** mode. Sentence merges can use the full tone ladder (lighter chorus).

Config block: `voiceHelpers.wordQueue` in `ApprovedCards.json`.

---

## Words only (no sighs / synthetic breath)

Set in `ApprovedCards.json`:

```json
"narratorWordsOnly": true,
"voiceHelpers": {
  "breathCue": { "enabled": false },
  "breathSmooth": { "enabled": false }
}
```

Capture and output are **spoken words only** — no `[[breath]]` inserts, no inhale/exhale WAV overlay. Remove `[[breath]]` from scripts; use normal punctuation instead.

Optional legacy breath cue (off by default): set `breathCue.enabled: true` and `narratorWordsOnly: false`.

---

## Tail finish

After the ladder, `finish_narration_tail()` may:

- Pad the final syllable (`tailPadMs`) — **no time-stretch** when `skipStretchAfterWordQueue: true`
- Optionally re-capture the last word (`forceLastWordQueue`) — **default false** for word queue (avoids deep/weird replacement voice)

Tune: `voiceHelpers.tailFinish`.

---

## Emphasis moments (optional)

`narrationEmphasisMoments` lists phrases that can get a dedicated excited capture (rate/pitch/gain). With word queue enabled, emphasis capture stays off unless:

```json
"narrationEmphasisCapture": true,
"narrationEmphasisWithWordQueue": true
```

Default in approved cards: emphasis capture **false** so the main paragraph stays word-by-word.

---

## Full-script producer recaps

| Key | Typical value | Meaning |
|-----|----------------|---------|
| `narrationMode` | `fullScript` | One narration track; on-screen captions are not read per beat |
| `cursorFullNarration` | `true` | Cursor director writes `DemoRecapFullNarration.json` |
| `narratorIgnoreCaptions` | `true` | Per-beat TTS disabled |
| `narrationSyncMode` | `speech_first` | Video holds follow speech length (no speech speedup) |

Compose: `compose-presentation-recap.py` + `run-producer-recap.py`.

---

## Voice ladder (default phases)

Phases run in order: **algorithm → character → tone → polish → auditor → finalizer**

| Phase | Helpers (approved default) |
|-------|----------------------------|
| algorithm | antiStutter, lowClean, speakerSafe, steadyVoice |
| character | character, casualTone, warmth, breathSmooth, vocalSpark |
| tone | naturalPitch, phraseFall, phraseDynamics, ttsSmooth |
| polish | deClick, autoEq25, midTreble, autoDynamics, spatialHarmony, deEss |
| auditor | Peak/RMS QC, trim rules |
| finalizer | loudnorm + limiter |

List defaults: `python3 personal-voice-ladder.py`  
Helper reference: [VOICE_HELPERS.md](VOICE_HELPERS.md)

---

## ApprovedCards narrator keys (cheat sheet)

| Key | Purpose |
|-----|---------|
| `narratorEngine` | `personal` (required for Jacob Adkins) |
| `narratorPersonalVoice` | Exact `say -v` name |
| `narratorWordQueue` / `narratorPortfolioMode` | Enable queue paths |
| `narratorTestPhrase` | Line used by `prepare-narrator-voice.py --test` |
| `narratorPreferMysay` | Prefer `mysay.dylib` over AVSpeech |
| `narratorMysayTimeoutSec` | Long single-pass timeout (180s+) |
| `voiceLadder` / `voiceHelpers` | Ladder phases and per-helper tuning |
| `narrationEmphasisMoments` | Optional excited phrases |
| `narratorPersonalSettings` | Legacy merge: `sayRate`, `delivery`, `personalHumanize` |

Portrait / intro / outro: same JSON file; copied into each capture by Unity (`apply-approved-cards.py`).

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|----------------|-----|
| Silent test WAV | Cursor shell cannot see Personal Voice | Run in **Terminal.app** or `bash run-recap-terminal.sh` |
| Frozen word N/90 | mysay hang | Watchdog on; shorten line; check `mysayTimeout*` |
| Robotic glue | ttsSmooth on word-by-word joins | Use `captureMode: "sentence"`; `skipOnWordQueue` only applies to **word** mode |
| Deep weird ending | Last-word re-capture + stretch | `forceLastWordQueue: false`, `lastWordStretchMs: 0` |
| No breath sound | Stripped marker / low level | Keep `[[breath]]` in script; raise `breathCue.level` / `peak` |
| Says “breath” | Old script | Use `[[breath]]` marker, not the word Breath |
| `Need timelapse PNGs` with `--capture` | CLI path | `run-producer-recap.py --capture <dir>` is supported |

Health check: `python3 recap-doctor.py <capture_folder>`

---

## Related files

| File | Role |
|------|------|
| `demo-recap-narrator.py` | Capture, word queue, deliver, sync |
| `personal-voice-helpers.py` | Helper registry + DSP |
| `personal-voice-ladder.py` | Phased ladder runner |
| `prepare-narrator-voice.py` | Setup, `--test`, `--list-helpers` |
| `run-producer-recap.py` | Producer pipeline entry |
| `run-recap-terminal.sh` | Opens Terminal.app for Personal Voice |
| `compose-presentation-recap.py` | Presentation compose + full narration |
| `apply-approved-cards.py` | Merge approved JSON into timeline |
| `recap-doctor.py` | Capture + voice sanity check |
| `requirements-recap.txt` | Optional Python deps (opencv, edge-tts, …) |
