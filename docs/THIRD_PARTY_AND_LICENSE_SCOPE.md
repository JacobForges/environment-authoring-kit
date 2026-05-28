# License scope and third-party components

**Repository accuracy:** [PUBLIC_REPO_SCOPE.md](PUBLIC_REPO_SCOPE.md)

## Your rights to the Environment Authoring Kit (original work)

The kit and this repo’s original documentation are **not** public domain and **not** CC0.

**Single license text:** [Packages/com.cursor.environment-authoring-kit/LICENSE.md](../Packages/com.cursor.environment-authoring-kit/LICENSE.md)  
**Hub pointer:** [LICENSE](../LICENSE) (same terms by reference)

### Summary (read the license file for the full legal text)

| Use | Allowed? |
|-----|----------|
| **Educational / personal non-commercial** learning, teaching, evaluation | **Yes — free** |
| **Commercial use** — selling access, paid products/services built on the kit, paid training whose primary materials are the kit, paid client work where the deliverable is substantially this tooling | **No — unless you have a separate commercial license or purchase from the copyright holder (Jacob)** |
| **Monetary gain / public commercial sale** without permission | **Requires approval or a commercial license from the author** |

The author may offer paid licenses, support, or sponsorship at their discretion. Contact the repository owner for commercial terms.

---

## What the kit license covers

- `Packages/com.cursor.environment-authoring-kit/` — C# (editor + runtime), `Tools/cave-grader` sources (**not** `node_modules`)
- Hub documentation: `README.md`, `REQUIREMENTS.md`, `docs/`, package `docs/` (kit-authored text only)

---

## What the kit license does **not** cover

You only have rights to other material under **that material’s own terms**:

| Component | Your rights come from |
|-----------|----------------------|
| **Unity Editor & runtime** | Unity subscription / [Unity Terms](https://unity.com/legal) |
| **UPM packages** (URP, XR, etc.) | Each package’s license in Package Manager |
| **`Assets/`** art, scenes, store packs (local on your machine) | Asset author / Asset Store license |
| **`Tools/cave-grader/node_modules/`** | Each npm package’s license |
| **`@cursor/sdk`** (when using Cursor provider) | [Cursor Terms of Service](https://cursor.com/terms-of-service) |
| **External LLM APIs** (Google, Anthropic, OpenAI, OpenRouter, local servers) | Those providers’ terms |
| **USGS / NOAA / FGS / FDEP geospatial data** | Government and state agency terms — see [RESEARCH_DATA_ATTRIBUTION.md](../Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_DATA_ATTRIBUTION.md) |

Redistributing a **full Unity project build** still requires compliance with Unity and every asset you included.

### Geospatial research data

The kit may **index or cache** derived PNGs and metadata locally (`ResearchCache/` — gitignored on public GitHub). That does **not** place agency data in the public domain. Retain provider credit per `RESEARCH_DATA_ATTRIBUTION.md` if you redistribute derived files.

---

## Practical notes

- Do not commit API keys (`.env`, EditorPrefs).
- Do not ship `node_modules` — run `npm install` in `Tools/cave-grader`.
- **Educational free ≠ commercial free.** Commercial use requires permission or a paid license from the author.
