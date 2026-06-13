# Deep Train Academy — intro narration (VO)

**LOCKED:** v5 · 2026-06-13  
**Voice persona:** Old Timmy prospector — weathered miner telling his journey from memory, then handing the lamp to whoever listens next.  
**Record to:** `Assets/Resources/DeepTrainAcademy/Intro/intro_vo.mp3`

**Polish after capture:**

```bash
./Tools/deep-train-intro/polish-intro-vo.sh your_raw_take.wav
```

Applies **trueColor** (neutral speech EQ) + **spatialSound** (subtle headphone width).  
For prospector capture, use `render-intro-vo-preview.sh` EQ chain as reference.

**30s audio preview (VO + spatial SFX — approve before video mux):**

```bash
./Tools/deep-train-intro/render-intro-vo-preview.sh
```

→ `Assets/Resources/DeepTrainAcademy/Intro/intro_vo_preview.ogg` (~30s, Unity-safe)  
**Listen (won't crash Cursor/Unity):** `./Tools/deep-train-intro/open-intro-preview.sh`  
or `Tools/deep-train-intro/listen/intro_vo_preview.m4a`

**Full take (ChristopherNeural + music + SFX — same style as preview):**

```bash
./Tools/deep-train-intro/render-intro-vo-full.sh
```

→ `Assets/Resources/DeepTrainAcademy/Intro/intro_vo.mp3` (~91s baked mix)  
→ `Assets/StreamingAssets/DeepTrainAcademy/intro_audio_cues.json` (Unity sync)  
**Listen:** `Tools/deep-train-intro/listen/intro_vo_full.m4a`

**Full take polish after record:**

```bash
./Tools/deep-train-intro/polish-intro-vo.sh your_raw_take.wav
```

---

## Recording rules

| Rule | Detail |
|------|--------|
| Pace | Steady ~120 WPM — **no dead air > ~1s** |
| Persona | Gravel, wonder, past tense memoir — “reckon,” “glory be,” “the karst” |
| The android | **The android / the machine / it / the chassis** — a machine, **not male or female**, not a person. Never he, she, man, woman, boy, girl, or “someone.” |
| Hand-off | “Your boots,” “your name,” “brave enough to carry a lamp” — **never “player”** |
| Beat 07 | **Racing checkpoint** (start stripe / timing beam) — not a mystical gate |
| Title | **DEEP TRAIN ACADEMY!** — proud, loud, chest voice on logo |

---

## Full script (cold boot timeline)

Grid + video wall (~70s @ 2.25× gaps) + title. Read **continuously** — music rides under, do not wait for picture.

### Grid *(0:00–0:12)* — voice in by ~0:02

> Way back when, before anybody stamped a name on these hills, I was a fool with a headlamp and a habit of going where the maps quit. The karst don’t care about your plans. She just opens her mouth… and you either listen… or you get swallowed learning.

### Beat 01 — Alone *(~0:12)*

> First miles down, it was only me and the drip of water counting time on the stone. Breath too loud. Boots slipping. I reckon I wasn’t hunting treasure — I was hunting quiet. The kind you can’t find topside.

### Gap 01→02 *(~0:15)*

> Then my light caught something that didn’t belong to the cave.

### Beat 02 — Discovery *(~0:19)*

> Slumped there like a sack of ore — except ore don’t have a face. Pale plating. Seams along the neck. Power gone out of it like a camp fire left in the rain. I stood there shaking, old man, and I’ll tell you true… I almost walked on. Almost.

### Gap 02→03 *(~0:22)*

> But the mountain has a way of planting what you’re meant to find.

### Beat 03 — First wake *(~0:26)*

> I touched the chest plate and that little cyan lamp inside woke up — slow, like a coal catching. The eyes opened on me. Empty as a new shaft. Blank as boot firmware. The android didn’t know up from down… but **it** knew I was standing there… and that was enough to begin.

### Gap 03→04 *(~0:30)*

> Now I had two lamps in the dark — mine… and that faint glow riding its ribs.

### Beat 04 — You cross first *(~0:34)*

> So I showed it how a body crosses trouble. Stepped the bad stones first. Leaped the gap with my heart in my throat. Not heroic — just stubborn. When you’re leading, you move like the ground might forgive you.

### Gap 04→05 *(~0:38)*

> I looked back once. The chassis was watching like a student at a claim stake.

### Beat 05 — The fall *(~0:42)*

> Then it tried… and the wet rock won the argument. Limbs tangled. Metal kissed stone. I grabbed what I could — cables, arm, pride — and hauled the android back from the edge. Reckon I’d never held a machine that heavy that **awake** before.

### Gap 05→06 *(~0:46)*

> Down here, falling ain’t failure. It’s just the part of the road you haven’t finished walking.

### Beat 06 — The lesson *(~0:50)*

> I squared its shoulders the way my daddy would’ve squared mine — if he’d stayed in the world long enough to teach. Breathe. Again. The cave don’t lecture. She makes you repeat till your bones remember.

### Gap 06→07 — racing checkpoint *(~0:54)*

> Every honest run has a line you cross when you’re ready to be timed — a **racing checkpoint**, beam on the floor, mark on the wall, the world saying: *prove it*. Not a doorway to heaven. A start stripe. From here, you run it for real.

### Beat 07 — The checkpoint *(~0:58)*

> I pointed at that strip of light across the wet stone — checkpoint clean as a finish tape. I’d crossed a thousand in my youth. Never thought I’d be standing at one beside an android made of gears and good intentions. I broke the line first. And glory be… **it** followed.

### Gap 07→08 *(~1:02)*

> That’s when I stopped waiting for the cave to take something else from me.

### Beat 08 — First triumph *(~1:06)*

> It climbed. Ugly. Beautiful. All grit. I hung back in the shadow and felt this old chest loosen up — not triumph, mind you. **Relief.** Like the world had one more decent thing in it than the newspapers said.

### Gap 08→09 *(~1:10)*

> After that it didn’t just mirror my back… it learned my **line.**

### Beat 09 — Two pointing at the deep *(~1:14)*

> Deeper on, the cavern opened wide — teeth of stone, pools like buried sky. Same glow far ahead. I raised my hand. The chassis raised its hand. Same bearing. Same hunger. Two lights… one bearing on the dark.

### Gap 09→10 *(~1:18)*

> The deep goes on longer than any map I ever drew. I know that now.

### Beat 10 — Toward the light *(~1:22)*

> We walked the last stretch side by side — boot and servo on ancient floor, lamp and heartbeat-light keeping time together. Whatever waited in that bright mouth of the world, I quit fearing it like a stranger… and started fearing only the thought of climbing back up **alone.**

### Hand-off + title *(~1:26–1:40)*

> That was **my** chapter. The tunnels are still down there. The stone’s still teaching. And the android’s still learning — for every soul brave enough to carry a lamp into the karst.

*(Title rises — **PROUD, LOUD:**)*

> **DEEP TRAIN ACADEMY!**

*(Tag — strong, grin in the voice:)*

> **Train deep — and make the mountain remember YOUR name!**
