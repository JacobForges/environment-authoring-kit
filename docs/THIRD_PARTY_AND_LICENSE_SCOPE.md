# License scope and third-party components

**Repository accuracy:** [PUBLIC_REPO_SCOPE.md](PUBLIC_REPO_SCOPE.md) lists what is actually committed on GitHub.

## What you may do with covered original work

**Use it however you like.** The original kit code and project documentation in this repo are dedicated to the **public domain** under **[CC0 1.0](../LICENSE)** (or equivalent waiver where CC0 is not recognized).

That means, for the covered parts only:

- Any purpose (personal, commercial, research, etc.)
- Modify, merge, and redistribute
- No attribution required (credit appreciated but not required)
- No permission needed from the author

## What CC0 covers

- `Packages/com.cursor.environment-authoring-kit/` — Environment Authoring Kit C# (editor + runtime)
- `Packages/com.cursor.environment-authoring-kit/Tools/cave-grader/` — TypeScript, shell scripts, prompt templates (**not** `node_modules`)
- Documentation: `README.md`, `REQUIREMENTS.md`, `docs/`, package `docs/` (including `RESEARCH_DATA_ATTRIBUTION.md` as **kit-authored** credit text — not a license on USGS/NOAA data itself)

**Note:** The UPM package also ships [LICENSE.md](../Packages/com.cursor.environment-authoring-kit/LICENSE.md) (educational free / commercial license) for package distribution — separate from root CC0.

Affirmer: see `LICENSE` (update the name if the legal rights holder is different).

## What CC0 does **not** cover

You only have rights to other material under **that material’s own terms**. This project also contains:

| Component | Your rights come from |
|-----------|----------------------|
| **Unity Editor & runtime** | Unity subscription / [Unity Terms](https://unity.com/legal) |
| **UPM packages** (URP, XR, etc.) | Each package’s license in Package Manager |
| **`Assets/`** (game art, scenes, store packs) | Asset author / Asset Store license |
| **Unity-bundled native libs** (FBX, SketchUp API, etc.) | Those vendors |
| **`Tools/cave-grader/node_modules/`** | Each npm package’s license |
| **`@cursor/sdk`** | [Cursor Terms of Service](https://cursor.com/terms-of-service) |
| **External research PDFs / websites** cited in prompts | Publisher / site terms |
| **USGS / NOAA / FGS / FDEP geospatial data** (LiDAR DEM, aquifer GIS) | U.S. Government and state agency terms — see [RESEARCH_DATA_ATTRIBUTION.md](../Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md) |

Redistributing a **full Unity project build** still requires compliance with Unity and every asset you included.

### Geospatial research data (not CC0)

The kit **indexes and may cache small derived PNGs** (county hillshades) and **metadata** pointing to public datasets. That does **not** place USGS, NOAA, or Florida agency data in the public domain. If you redistribute hillshade PNGs or bulk DEM subsets, retain provider credit per `RESEARCH_DATA_ATTRIBUTION.md`.

## Practical notes

- Do not commit API keys (`.env`, EditorPrefs).
- Shipping `node_modules` is unnecessary — run `npm install` in `Tools/cave-grader`.
- CC0 applies to **what the affirmer owns** in this repo; it does not grant rights to third-party content you did not create.
