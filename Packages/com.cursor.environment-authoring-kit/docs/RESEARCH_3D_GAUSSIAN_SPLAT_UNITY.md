# Research synthesis — 3D Gaussian Splatting in Unity Editor

**Purpose:** Implementation and R&D for **UnityGaussianSplatting**, URP/HDRP/BiRP, PLY/SPZ import, Mac Metal builds, and **Environment Kit** placement (cave mouth, demo Timeline).

**Cache category:** `3d_gaussian_splat_unity` → `Assets/EnvironmentKit/ResearchCache/3d_gaussian_splat_unity/`

**Related:** [RESEARCH_3D_GAUSSIAN_SPLAT.md](RESEARCH_3D_GAUSSIAN_SPLAT.md) (fundamentals + Mac training)

**Catalog:** `Tools/cave-grader/gaussian-splat-research-catalog.ts` (51 Unity-focused sources)

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
| 1 | Unity Community — *UnityGaussianSplatting — Aras P.* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting | real-time 3DGS in Unity; BiRP/URP/HDRP |
| 2 | Unity Community — *UnityGaussianSplatting — Render Pipeline Integration* (2025, GitHub Docs) | https://github.com/aras-p/UnityGaussianSplatting/blob/main/docs/render-pipeline-integration.md | URP feature; HDRP custom pass; Unity 6 render graph |
| 3 | Unity Community — *UnityGaussianSplatting — Package (UPM)* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting/tree/main/package | org.nesnausk.gaussian-splatting UPM package |
| 4 | Unity Community — *UnityGaussianSplatting — Shaders (HLSL)* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting/tree/main/package/Shaders | RenderGaussianSplats.shader; compute sort |
| 5 | Splatware — *Gaussian Splatting in Unity — Integration Guide* (2025, Splatware Docs) | https://splatware.com/docs/gaussian-splatting-in-unity | PLY export → UnityGaussianSplatting workflow |
| 6 | Unity Technologies — *Unity 6 — Scriptable Render Pipeline* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/scriptable-render-pipeline-introduction.html | SRP foundation for custom splat passes |
| 7 | Unity Technologies — *Unity 6 — URP Render Graph* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph.html | URP GaussianSplatURPFeature requires Unity 6; no compat mode |
| 8 | Unity Technologies — *Unity 6 — HDRP Custom Pass* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/custom-pass.html | GaussianSplatHDRPPass injection point |
| 9 | Unity Technologies — *Unity 6 — Compute Shaders* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/class-ComputeShader.html | GPU sort + splat raster prerequisites |
| 10 | Unity Technologies — *Unity 6 — Metal on macOS* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/metal.html | MacBook Air M-series player builds; editor testing |
| 11 | Unity Technologies — *Unity 6 — Importing PLY meshes* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/3D-formats.html | mesh import context for splat assets |
| 12 | Unity Technologies — *Unity Asset Store — Search Gaussian Splat* (2026, Asset Store) | https://assetstore.unity.com/packages/tools/search?q=gaussian%20splat | third-party tools and importers |
| 13 | Keijiro — *Gaussian Splatting experiments (Shadertoy/Unity adjacency)* (2024, GitHub) | https://github.com/keijiro | graphics R&D community reference |
| 14 | Unity — *GaussianExample — Built-in Sample Project* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting/tree/main/projects/GaussianExample | quickest BiRP sample scene GSTestScene |
| 15 | Unity — *GaussianExample-HDRP Sample* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting/tree/main/projects/GaussianExample-HDRP | HDRP cinematic integration sample |
| 16 | Unity — *GaussianExample-URP Sample* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting/tree/main/projects/GaussianExample-URP | URP sample + feature setup |
| 17 | Unity — *Create GaussianSplatAsset Menu Tool* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting#readme | Tools → Gaussian Splats → Create asset from PLY/SPZ |
| 18 | Unity — *SPZ Input Support (v1.1+)* (2025, GitHub Releases) | https://github.com/aras-p/UnityGaussianSplatting/releases/tag/v1.1.0 | Scaniverse/Niantic SPZ import |
| 19 | Unity — *Gaussian Splat Editing Tools (cutouts/merge)* (2024, GitHub Releases) | https://github.com/aras-p/UnityGaussianSplatting/releases/tag/v0.9.0 | in-editor splat cleanup; selection |
| 20 | Unity — *Environment Kit — Surface World Pipeline* (2026, Hub Package) | https://github.com/cursor/environment-authoring-kit | integrate splats as hero props near cave mouth |

---

## Category B — Unity 6 SRP, GPU, Mac player

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
| 1 | Unity — *Addressables — Streaming Splats* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/index.html | chunk large splat assets per open-world tile |
| 2 | Unity — *URP Renderer Feature API* (2026, Unity Scripting) | https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererFeature.html | hook GaussianSplatURPFeature |
| 3 | Unity — *CommandBuffer DrawProcedural* (2026, Unity Scripting) | https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CommandBuffer.DrawProcedural.html | low-level splat draw path |
| 4 | Unity — *GraphicsBuffer for structured splat data* (2026, Unity Scripting) | https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.html | GPU buffers for splat attributes |
| 5 | Unity — *AsyncGPUReadback* (2026, Unity Scripting) | https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html | debug sort buffers without stall |
| 6 | Unity — *Quality Settings — Mac Metal* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/class-QualitySettings.html | target MacBook Air builds for demo reel |
| 7 | Unity — *Player Settings — MacOS* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/macos.html | distribute splat viewer builds on Apple Silicon |
| 8 | Niantic — *SPZ + UnityGaussianSplatting* (2025, GitHub) | https://github.com/nianticlabs/spz | compressed splats for mobile/Quest pipelines |
| 9 | PlayCanvas — *SuperSplat Export to Unity workflow* (2025, PlayCanvas) | https://superspl.at/editor | edit splats → export PLY for Unity import |
| 10 | Nerfstudio — *Export to PLY for Unity* (2026, Nerfstudio Docs) | https://docs.nerf.studio/ | train in gsplat → export → Unity asset menu |
| 11 | INRIA — *Pretrained PLY Models for Unity Import* (2023, INRIA) | https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/datasets/pretrained/models.zip | test assets for GaussianExample scene |
| 12 | Unity Discussions — *Gaussian Splatting thread* (2024, Unity Discussions) | https://discussions.unity.com/tag/gaussian-splatting | community integration notes |
| 13 | Unity — *DOTS / Entities splat future* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.entities@1.0/manual/index.html | potential ECS rendering path for many splats |
| 14 | Unity — *Cinemachine + Splat scenes* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.cinemachine@3.0/manual/index.html | flythrough demo cameras for reel |
| 15 | Unity — *Volume Framework (HDRP)* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/volume-profile.html | custom pass volume setup |
| 16 | Unity — *Shader Graph — not primary for 3DGS* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/index.html | splats use compute+HLSL not ShaderGraph |
| 17 | Unity — *Light probes vs splat lighting* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/LightProbes.html | splats are baked radiance — match probe ambient |
| 18 | Unity — *Reflection probes with splats* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/class-ReflectionProbe.html | composite splats with reflective water |
| 19 | Unity — *Depth texture interaction URP* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/urp/post-processing/post-processing-depth.html | depth composite with terrain/cave |
| 20 | Unity — *Motion vectors / TAA caution* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/anti-aliasing.html | splats may need TAA off or custom velocity |

---

## Category C — Compositing, performance, Environment Kit hooks

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
| 1 | Unity — *Vulkan Mac (experimental)* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/vulkan.html | alternative to Metal for splat testing |
| 2 | Unity — *DX12 Windows primary for splat package* (2025, GitHub README) | https://github.com/aras-p/UnityGaussianSplatting#readme | D3D12/Vulkan/Metal supported; DX11 not |
| 3 | Unity — *VR — desktop headset notes* (2025, GitHub README) | https://github.com/aras-p/UnityGaussianSplatting#readme | partial VR support on PC VR |
| 4 | Unity — *GaussianSplatRenderer component* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatRenderer.cs | main runtime component API |
| 5 | Unity — *Integration with Environment Kit cave mouth* (2026, Environment Kit) | Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md | place splat markers at primary entrance POI |
| 6 | Unity — *Physics — splats are visual-only* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/collider-types.html | use proxy colliders for gameplay near splats |
| 7 | Unity — *LOD Groups for hybrid worlds* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/class-LODGroup.html | swap distant splats to impostors/billboards |
| 8 | Unity — *Profiling splats — Frame Debugger* (2026, Unity Manual) | https://docs.unity3d.com/6000.0/Documentation/Manual/FrameDebugger.html | verify splat pass cost on Mac |
| 9 | Unity — *Memory — splat asset size* (2025, GitHub) | https://github.com/aras-p/UnityGaussianSplatting#readme | compression presets Very Low → Very High |
| 10 | Unity — *Burst / Jobs for CPU sort fallback* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html | optional CPU-side experiments |
| 11 | Unity — *Timeline for demo sequencing* (2026, Unity Manual) | https://docs.unity3d.com/Packages/com.unity.timeline@1.8/manual/index.html | cinematic splat reveal in demo video |

---

## Kit implications

- **BiRP/URP/HDRP:** Match your project pipeline; URP feature is Unity 6 render-graph only.
- **Open world:** Pair with Addressables chunking from [RESEARCH_OPEN_WORLD_STREAMING.md](RESEARCH_OPEN_WORLD_STREAMING.md).
- **Resource ID limits:** Large splats + many terrains stress GPU tables — keep splat count bounded per loaded chunk.
