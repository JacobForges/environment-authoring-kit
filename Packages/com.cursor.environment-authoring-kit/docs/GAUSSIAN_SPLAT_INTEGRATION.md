# 3D Gaussian Splat — Environment Kit integration

**Status:** Optional, **off by default**. One hero anchor at the **primary cave mouth** — does not replace terrain or cave collision.

**Research:** [RESEARCH_3D_GAUSSIAN_SPLAT.md](RESEARCH_3D_GAUSSIAN_SPLAT.md), [RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md](RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md)

---

## Safeguards (same philosophy as terrain pacing)

| Guard | Behavior |
|-------|----------|
| **Off by default** | `enableGaussianSplatHeroAtCaveMouth` on `CaveBuildCursorSettings` |
| **MacBook / demo preset** | Turns splats **off** and blocks GPU budget (`AllowGaussianSplats = false`) |
| **One hero only** | `MaxActiveGaussianSplats = 1` on Default budget |
| **No edit-mode draw on laptop budget** | `gaussianSplatRenderInEditMode` forced off when `ConserveGpuMemory` |
| **Deferred placement** | Runs **next editor frame** after surface finalize (no extra hitch in same step) |
| **Proxy collider** | Trigger box for gameplay; splats remain **visual-only** |
| **Optional package** | No hard dependency — reflection attaches `GaussianSplatRenderer` when installed |

---

## Enable (recommended path)

1. **Default hardware budget** (not MacBook Air / demo preset).
2. Install **UnityGaussianSplatting** (Unity 6):  
   Package Manager → Add from git URL:  
   `https://github.com/aras-p/UnityGaussianSplatting.git?path=/package`
3. **Tools → Gaussian Splats → Create Gaussian Splat asset** from a **small** PLY/SPZ (use **Very Low** compression for Mac).
4. Open **CaveBuildCursorSettings** (Hub → Settings):
   - Enable **Gaussian Splat Hero At Cave Mouth**
   - Set **Gaussian Splat Asset Path** (e.g. `Assets/EnvironmentKit/GaussianSplats/hero.asset`)
   - Leave **Render In Edit Mode** off while building terrain
5. Run **Build Surface** or **Build Complete Cave** — hero slot appears under `SurfaceWorld/GaussianSplats/GaussianSplatHero_PrimaryMouth`.

**Manual:** `Window → Environment Kit → Cave Build → Advanced → Place Gaussian Splat Hero (Cave Mouth)`

---

## MacBook / demo builds

Use **MacBook Air GPU budget** or **Demo recording preset** — splats are disabled automatically so you keep responsiveness for FullWorld terrain/cave builds.

For a reel shot: build with budget off, enable hero + assign asset, **record Game view** (not Scene view), single splat only.

---

## Scene hierarchy

```
SurfaceWorld/
  GaussianSplats/
    GaussianSplatHero_PrimaryMouth   ← GaussianSplatHeroSlot (+ optional GaussianSplatRenderer)
```

Aligned to `CaveOpening_Primary` (`SurfaceCaveOpeningMarker.isPrimaryEntrance`).

---

## Troubleshooting

| Issue | Mitigation |
|-------|------------|
| `Resource ID out of range in SetResource` | Too many GPU resources — keep **one** splat, close Scene view during build, use MacBook preset for terrain |
| No renderer attached | Install package; menu **Install Gaussian Splat Package Help** |
| Hitch on place | Expected if edit-mode rendering on — disable **Render In Edit Mode** |
| Walk-through splat | Intended — add gameplay colliders on terrain/cave, not on splat |
