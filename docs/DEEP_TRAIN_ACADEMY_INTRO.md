# Deep Train Academy — cold-boot intro (90s)

**Game:** Deep Train Academy · **Studio:** JacobForges  
**Plays:** Cold boot, before Security Guard login · **Runtime:** `DeepTrainAcademyIntro.cs`

## Locked structure (hybrid)

| Time | Layer |
|------|--------|
| 0:00–0:12 | Unity procedural grid — “signal waking up” |
| 0:12–1:18 | `intro.mp4` (66s) **or** Ken Burns keyframes **or** black |
| 1:18–1:30 | Unity title + fine print fade-in |
| Audio | Unity — music, VO, SFX (MP4 silent) |

## Story beats (10 keyframes → 66s video)

**Theme:** Human teaches a naive android/cyborg companion to get smarter — your way.

| # | File | Beat | AI state |
|---|------|------|----------|
| 1 | `intro_01.png` | Human alone in cave | — |
| 2 | `intro_02.png` | Discovers dormant android | Offline |
| 3 | `intro_03.png` | First wake / boot | Naive, confused |
| 4 | `intro_04.png` | Human demonstrates path | Watching |
| 5 | `intro_05.png` | AI fails / stumbles | Clumsy |
| 6 | `intro_06.png` | Human teaches posture | Learning |
| 7 | `intro_07.png` | Checkpoint — try again | Trying |
| 8 | `intro_08.png` | AI succeeds | Brighter glow |
| 9 | `intro_09.png` | Mimicry training | Copying |
| 10 | `intro_10.png` | Growth — side by side | Confident partner |

Full prompts: `docs/DEEP_TRAIN_ACADEMY_INTRO_KEYFRAMES.md`

**Stylized transition:** beat 6→7 = depth push through cave wall + brief teal bloom (~0.4s).

## Production path

1. Hyper-real PNG keyframes in `Assets/Resources/DeepTrainAcademy/Intro/`  
2. Unity cinematic scenes matched to keyframes  
3. Unity Recorder @ 1440p60 → grade in Resolve  
4. Export ship file → `Assets/StreamingAssets/DeepTrainAcademy/intro.mp4`  
5. Record VO (`docs/DEEP_TRAIN_ACADEMY_INTRO_VO.md`) + commission music  
6. Drop SFX clips (see `AUDIO_README.txt`)

## Export spec

| | |
|--|--|
| Master | 2560×1440 @ 60 fps |
| Ship | 1920×1080 @ 60 fps H.264 |
| Bitrate | ~12–14 Mbps (~100–150 MB) |
| Audio in MP4 | None |

## Audio files (Resources)

| File | When |
|------|------|
| `intro_music` | Full intro |
| `intro_vo` | Full intro (your voice) |
| `sfx_ambient_bed` | Loop 12s–78s |
| `sfx_power_on` | ~12s |
| `sfx_checkpoint` | ~45s |
| `sfx_ui_chime` | ~58s |
| `sfx_footstep` | Staggered 12s–78s |

## Skip rules

- **First launch:** no skip  
- **Later launches:** any key / mouse after 3s  
- Pref: `DeepTrainAcademy.IntroSeen.v1`  
- Skip fades audio 0.4s before login  

## Fine print (on-screen, whole intro)

`© 2026 JacobForges. Deep Train Academy™. All rights reserved. Third party assets used.`

+ music credit line when `intro_music` is present.  
Private full list: `docs/DEEP_TRAIN_ACADEMY_INTRO_ASSETS_PRIVATE.md`

## Fallback order

1. `StreamingAssets/DeepTrainAcademy/intro.mp4` → VideoPlayer (**built — 66s, 1080p60**)
2. Else PNG keyframes → Ken Burns (dev)
3. Else black + title

## Unity menu

**Hub → Deep Train Academy → Intro**

- **Build intro.mp4 From Keyframes** — reassemble after PNG changes  
- **Verify Intro Assets** — keyframes, video, music, VO  
- **Open Keyframes Folder** / **Open intro.mp4 Location**

## Next (your side)

1. **Record VO** — script in `docs/DEEP_TRAIN_ACADEMY_INTRO_VO.md` → `intro_vo.ogg`  
2. **Commission music** → `intro_music.ogg`  
3. **SFX pack** — see `AUDIO_README.txt`  
4. **Optional:** in-engine Recorder export → replace `intro.mp4` for true motion

## Boot flow

Intro (~90s) → Security Guard login → Tester welcome briefing → Character creator → Main menu
