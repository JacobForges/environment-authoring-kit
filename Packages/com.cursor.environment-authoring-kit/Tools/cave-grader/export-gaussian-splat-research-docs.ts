#!/usr/bin/env npx tsx
/** Regenerates docs/RESEARCH_3D_GAUSSIAN_SPLAT*.md from gaussian-splat-research-catalog.ts */
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import type { ResearchEntry } from "./research-catalog.js";
import {
  GAUSSIAN_SPLAT_RESEARCH_PAPERS,
  GAUSSIAN_SPLAT_UNITY_PAPERS,
  gaussianSplatResearchCount,
  gaussianSplatUnityCount,
} from "./gaussian-splat-research-catalog.js";

const dir = dirname(fileURLToPath(import.meta.url));
const docsDir = join(dir, "../../docs");

function summaryFromTopics(topics: string): string {
  const parts = topics.split(",").map((s) => s.trim());
  return parts.filter((p) => !p.startsWith("3d_gaussian_splat")).join("; ") || topics;
}

function tableRows(papers: ResearchEntry[]): string {
  return papers
    .map((p, i) => {
      const summary = summaryFromTopics(p.topics);
      const src = `${p.lab} — *${p.title}* (${p.year}, ${p.venue})`;
      return `| ${i + 1} | ${src} | ${p.url} | ${summary} |`;
    })
    .join("\n");
}

function writeFundamentalsDoc(): void {
  const n = gaussianSplatResearchCount();
  const body = `# Research synthesis — 3D Gaussian Splatting (fundamentals & Mac)

**Purpose:** Curated R&D for **3D Gaussian Splatting (3DGS)** — theory, major lab work (INRIA, Google Research, NVIDIA, Meta, Microsoft), training pipelines, and **running on a local MacBook** (MPS/Metal constraints vs CUDA).

**Cache category:** \`3d_gaussian_splat\` → \`Assets/EnvironmentKit/ResearchCache/3d_gaussian_splat/\`

**Catalog:** \`Tools/cave-grader/gaussian-splat-research-catalog.ts\` (${n} sources, merged into \`research-catalog.seed.json\`)

**Last updated:** 2026-05-29

---

## Quick Mac workflow

1. **Train (heavy):** Prefer Linux + NVIDIA CUDA for full graphdeco training; Mac conda works but MPS is limited for custom CUDA rasterizers.
2. **Train (light) / view:** Use **Nerfstudio + gsplat** or cloud GPU, export **PLY/SPZ**.
3. **View on Mac:** **SuperSplat** (browser) or **UnityGaussianSplatting** in Unity 6 with **Metal** player/editor.
4. **Integrate in Hub:** See [RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md](RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md).

---

## Category A — Foundational 3DGS (INRIA & core papers)

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
${tableRows(GAUSSIAN_SPLAT_RESEARCH_PAPERS.slice(0, 12))}

---

## Category B — Google / NVIDIA / Meta / industry labs

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
${tableRows(GAUSSIAN_SPLAT_RESEARCH_PAPERS.slice(12, 35))}

---

## Category C — Compression, dynamic scenes, mesh export, Mac local

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
${tableRows(GAUSSIAN_SPLAT_RESEARCH_PAPERS.slice(35))}

---

## Kit implications

- **Hero set-dressing:** High-fidelity photogrammetry splats at cave mouth / landmark POIs without replacing terrain pipeline.
- **Not a terrain replacement:** Keep LiDAR + Terrain for walkable collision; splats are **visual radiance fields**.
- **Demo reel:** Pre-bake splats, stream via Addressables if multi-chunk open world expands later.
`;
  writeFileSync(join(docsDir, "RESEARCH_3D_GAUSSIAN_SPLAT.md"), body, "utf8");
}

function writeUnityDoc(): void {
  const n = gaussianSplatUnityCount();
  const body = `# Research synthesis — 3D Gaussian Splatting in Unity Editor

**Purpose:** Implementation and R&D for **UnityGaussianSplatting**, URP/HDRP/BiRP, PLY/SPZ import, Mac Metal builds, and **Environment Kit** placement (cave mouth, demo Timeline).

**Cache category:** \`3d_gaussian_splat_unity\` → \`Assets/EnvironmentKit/ResearchCache/3d_gaussian_splat_unity/\`

**Related:** [RESEARCH_3D_GAUSSIAN_SPLAT.md](RESEARCH_3D_GAUSSIAN_SPLAT.md) (fundamentals + Mac training)

**Catalog:** \`Tools/cave-grader/gaussian-splat-research-catalog.ts\` (${n} Unity-focused sources)

**Last updated:** 2026-05-29

---

## Recommended Unity integration path

1. Add **aras-p/UnityGaussianSplatting** UPM package (Unity **6**, URP needs render graph, no compat mode).
2. **Tools → Gaussian Splats → Create Gaussian Splat asset** from exported PLY or SPZ.
3. Add **GaussianSplatRenderer** to a child of environment root; align with cave entrance marker.
4. Use **proxy colliders** — splats do not provide gameplay collision.
5. Profile on **Mac Metal** with Frame Debugger before demo recording.

---

## Category A — UnityGaussianSplatting package & samples

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
${tableRows(GAUSSIAN_SPLAT_UNITY_PAPERS.slice(0, 20))}

---

## Category B — Unity 6 SRP, GPU, Mac player

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
${tableRows(GAUSSIAN_SPLAT_UNITY_PAPERS.slice(20, 40))}

---

## Category C — Compositing, performance, Environment Kit hooks

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
${tableRows(GAUSSIAN_SPLAT_UNITY_PAPERS.slice(40))}

---

## Kit implications

- **BiRP/URP/HDRP:** Match your project pipeline; URP feature is Unity 6 render-graph only.
- **Open world:** Pair with Addressables chunking from [RESEARCH_OPEN_WORLD_STREAMING.md](RESEARCH_OPEN_WORLD_STREAMING.md).
- **Resource ID limits:** Large splats + many terrains stress GPU tables — keep splat count bounded per loaded chunk.
`;
  writeFileSync(join(docsDir, "RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md"), body, "utf8");
}

writeFundamentalsDoc();
writeUnityDoc();
console.log(`Wrote ${docsDir}/RESEARCH_3D_GAUSSIAN_SPLAT.md (${gaussianSplatResearchCount()} sources)`);
console.log(`Wrote ${docsDir}/RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md (${gaussianSplatUnityCount()} sources)`);
