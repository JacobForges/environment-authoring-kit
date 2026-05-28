/**
 * Injected into every agent prompt — stable contract (hardcoded prompts v2).
 */
export const MANDATORY_BUILD_RULES_MD = `## MANDATORY — every pass (never skip)

1. **Read JSON on disk first** — Open \`CaveBuildGeneratedJsonManifest.json\`, then the reports listed in the hardcoded contract (quality, ladder, probes, terrain ladder, research plan). Do not invent scores or probe results.

2. **Land-mass references only** — Hillshade/elevation data is for **bare-earth relief**. Ignore map legends, scale bars, watermarks, attribution, borders, and **map icons**. Never drive generation from \`ResearchCache/images/*/ref-*.png\` pixels. No bathymetry or inundation for cave geometry.

3. **Layout variety** — Each seed must differ in tunnel/chamber/surface parameters. Do not clone the previous seed's spline, platform ring, or grid story.

4. **One rung / one fix** — Address only the active ladder rung. Smallest correct diff in Hub C# or scene. No full "Build Complete Cave" from the agent.

5. **Bot order (editor, paced)** — Surface trails → cave mouth → underground route. Read \`CaveBuildPhaseBotReport.json\` and route probe JSON after bots run. Never skip surface validation for cave-only tweaks.

6. **Additive surface** — Preserve Ground anchor and center land disk. LiDAR/DEM is **creative guide only** (procedural FBM + ≤28% structural bias), not a heightmap photocopy. Neighbor tiles: seam stitch only when main guide stamp is done (no per-tile DEM re-stamp loop).

7. **Surface height** — No stacked Perlin strata; no full-heightmap sync in one frame. Read \`SurfaceTerrainSculptAgentPrompt.md\` before sculpt edits.

8. **compile_gate** — Fix only \`verifiedOnDisk: true\` CS errors.

9. **Unity must stay responsive** — No blocking node scripts during active builds; no 9-tile smooth + DEM in one editor frame.`;
