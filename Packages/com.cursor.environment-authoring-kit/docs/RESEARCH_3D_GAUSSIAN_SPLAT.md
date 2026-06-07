# Research synthesis — 3D Gaussian Splatting (fundamentals & Mac)

**Purpose:** Curated R&D for **3D Gaussian Splatting (3DGS)** — theory, major lab work (INRIA, Google Research, NVIDIA, Meta, Microsoft), training pipelines, and **running on a local MacBook** (MPS/Metal constraints vs CUDA).

**Cache category:** `3d_gaussian_splat` → `Assets/EnvironmentKit/ResearchCache/3d_gaussian_splat/`

**Catalog:** `Tools/cave-grader/gaussian-splat-research-catalog.ts` (52 sources, merged into `research-catalog.seed.json`)

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
| 1 | INRIA / GRAPHDECO — *3D Gaussian Splatting for Real-Time Radiance Field Rendering* (2023, SIGGRAPH 2023 / ACM TOG) | https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/ | foundational 3DGS; real-time radiance fields; anisotropic Gaussians |
| 2 | INRIA / GRAPHDECO — *3D Gaussian Splatting — arXiv* (2023, arXiv) | https://arxiv.org/abs/2308.04079 | original paper PDF; SfM initialization; adaptive density |
| 3 | INRIA / GRAPHDECO — *Official Gaussian Splatting Code* (2023, GitHub) | https://github.com/graphdeco-inria/gaussian-splatting | PyTorch training; COLMAP pipeline; reference implementation |
| 4 | INRIA / GRAPHDECO — *Gaussian Splatting Datasets (pretrained)* (2023, INRIA) | https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/datasets/pretrained/models.zip | benchmark scenes; evaluation assets |
| 5 | Google Research — *GS-Offload: Scalable 3DGS via Host Memory Offloading* (2026, Google Research) | https://research.google/pubs/gs-offload-scalable-3d-gaussian-splatting-training-system-via-host-memory-offloading/ | large-scene training; GPU memory reduction |
| 6 | Google Research — *Snap-it, Tap-it, Splat-it: Tactile-Informed 3DGS* (2025, Google Research) | https://research.google/pubs/snap-it-tap-it-splat-it-tactile-informed-3d-gaussian-splatting-for-reconstructing-challenging-surfaces/ | glossy surfaces; vision + touch fusion |
| 7 | Google Research — *WonderWorld: Interactive 3D Scene Generation* (2024, Google Research) | https://research.google/pubs/wonderworld-interactive-3d-scene-generation/ | Gaussian surfels; guided depth; interactive extrapolation |
| 8 | Google Research — *Google at NeurIPS 2025 — Gaussian Splatting Papers* (2025, Google Research) | https://research.google/conferences-and-events/google-at-neurips-2025/ | LODGE; HoliGS; feed-forward 3DGS; dynamic scenes |
| 9 | NVIDIA Research — *3DGUT: Distorted Cameras and Secondary Rays in 3DGS* (2024, CVPR 2025) | https://research.nvidia.com/labs/toronto-ai/3DGUT/ | unscented transform; fisheye; rolling shutter; reflections |
| 10 | NVIDIA Research — *3DGUT — arXiv* (2024, arXiv) | https://arxiv.org/abs/2412.12507 | hybrid rasterization + ray tracing alignment |
| 11 | NVIDIA — *3DGUT in gsplat — Technical Blog* (2025, NVIDIA Developer Blog) | https://developer.nvidia.com/blog/revolutionizing-neural-reconstruction-and-rendering-in-gsplat-with-3dgut/ | gsplat integration; physical AI; robotics |
| 12 | NVIDIA — *3dgrut — GitHub (3DGUT / 3DGRT)* (2024, GitHub) | https://github.com/nv-tlabs/3dgrut | open-source hybrid Gaussian rendering |

---

## Category B — Google / NVIDIA / Meta / industry labs

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
| 1 | NVIDIA — *gsplat — Gaussian Splatting Library* (2024, GitHub) | https://github.com/nerfstudio-project/gsplat | CUDA backend; PyTorch; Nerfstudio ecosystem |
| 2 | Meta AI — *Segment Anything Model 2 (SAM 2)* (2024, Meta AI) | https://ai.meta.com/sam2/ | mask priors for object-centric capture pipelines |
| 3 | Meta AI — *Meta AI Research Publications* (2026, Meta AI) | https://ai.meta.com/research/ | neural rendering; 3D vision index |
| 4 | Microsoft Research — *Neural Radiance Fields — Research Index* (2024, Microsoft Research) | https://www.microsoft.com/en-us/research/research-area/computer-vision/ | NeRF/GS adjacent vision research |
| 5 | Apple — *Metal — GPU Compute Overview* (2026, Apple Developer) | https://developer.apple.com/metal/ | MacBook local inference/render via Metal compute |
| 6 | Apple — *Accelerate PyTorch on Apple Silicon* (2026, Apple Developer) | https://developer.apple.com/metal/pytorch/ | MPS backend for training on Mac when CUDA unavailable |
| 7 | TUM — *Mip-Splatting: Alias-free 3D Gaussian Splatting* (2023, CVPR 2024) | https://arxiv.org/abs/2311.16493 | anti-aliasing; stable high-res rendering |
| 8 | ETH Zurich — *2D Gaussian Splatting for Geometrically Accurate Radiance Fields* (2024, SIGGRAPH 2024) | https://arxiv.org/abs/2403.17888 | surface-aligned 2D Gaussians; mesh-quality geometry |
| 9 | Shanghai AI Lab — *Scaffold-GS: Structured 3D Gaussians* (2024, CVPR 2024) | https://arxiv.org/abs/2312.00109 | anchor-based large scenes; LOD-friendly structure |
| 10 | TU Munich — *Gaussian Opacity Fields* (2024, SIGGRAPH Asia 2024) | https://arxiv.org/abs/2404.10484 | level-set surfaces; improved geometry |
| 11 | INRIA — *SuGaR: Surface-Aligned Gaussian Splatting* (2024, CVPR 2024) | https://arxiv.org/abs/2311.16918 | mesh extraction from 3DGS |
| 12 | NVIDIA / HKU — *LightGaussian: Unbounded 3DGS Compression* (2024, SIGGRAPH Asia 2024) | https://arxiv.org/abs/2406.05134 | 15x+ compression; real-time unbounded scenes |
| 13 | UNC — *Speedy-Splat: Fast 3DGS via Sparse Pixels* (2025, CVPR 2025) | https://arxiv.org/abs/2412.00578 | SnugBox/AccuTile; 6.7× render; 10.6× fewer primitives |
| 14 | UC Berkeley — *RadSplat: Radiance Field-Informed Gaussian Splatting* (2024, arXiv) | https://arxiv.org/abs/2403.13806 | hybrid NeRF + GS guidance |
| 15 | Tsinghua — *GaussianShader: 3DGS + Shading* (2024, SIGGRAPH 2024) | https://arxiv.org/abs/2311.17977 | relighting; material decomposition |
| 16 | Multiple — *4D Gaussian Splatting for Dynamic Scenes* (2024, ICLR 2024) | https://arxiv.org/abs/2310.08528 | temporal deformation; dynamic novel view |
| 17 | Multiple — *Deformable 3D Gaussians for High-Fidelity Monocular Dynamic* (2024, CVPR 2024) | https://arxiv.org/abs/2312.00583 | monocular video; non-rigid motion |
| 18 | Multiple — *Spacetime Gaussian Feature Splatting* (2024, arXiv) | https://arxiv.org/abs/2403.17849 | dynamic scene real-time rendering |
| 19 | Community — *Awesome 3D Gaussian Splatting Papers* (2026, GitHub) | https://github.com/MrNeRF/awesome-3D-gaussian-splatting | curated paper list; tools; datasets |
| 20 | Community — *diff-gaussian-rasterization* (2023, GitHub) | https://github.com/graphdeco-inria/diff-gaussian-rasterization | CUDA rasterizer used by original 3DGS trainer |
| 21 | UC San Diego — *COLMAP — Structure from Motion* (2026, COLMAP) | https://colmap.github.io/ | SfM input pipeline for 3DGS capture |
| 22 | Berkeley — *Nerfstudio — Training Framework* (2026, GitHub) | https://github.com/nerfstudio-project/nerfstudio | gsplat methods; dataparsers; viewer |
| 23 | Niantic — *SPZ Compressed Gaussian Format* (2025, GitHub) | https://github.com/nianticlabs/spz | mobile-friendly splat payloads |

---

## Category C — Compression, dynamic scenes, mesh export, Mac local

| # | Source | URL | Summary (for kit) |
|---|--------|-----|-------------------|
| 1 | Scaniverse — *Scaniverse — Gaussian Splats from Phone* (2025, Scaniverse) | https://scaniverse.com/ | consumer capture → splat export |
| 2 | Luma AI — *Luma AI — Neural Capture API* (2026, Luma AI) | https://lumalabs.ai/ | commercial splat/NeRF capture services |
| 3 | Polycam — *Polycam — 3D Scanning* (2026, Polycam) | https://poly.cam/ | photogrammetry + Gaussian export workflows |
| 4 | Antimatter15 — *splat — WebGL Gaussian Viewer* (2024, GitHub) | https://github.com/antimatter15/splat | browser preview of .splat/.ply |
| 5 | PlayCanvas — *SuperSplat — Editor for Gaussian Splats* (2025, PlayCanvas) | https://superspl.at/ | web editor; compression; export |
| 6 | Hugging Face — *Gaussian Splatting Spaces / Demos* (2026, Hugging Face) | https://huggingface.co/spaces?search=gaussian+splatting | hosted demos; community models |
| 7 | OpenAI — *NeRF vs 3DGS — Industry Shift (survey context)* (2024, Various) | https://arxiv.org/search/?query=gaussian+splatting&searchtype=all | literature search index |
| 8 | DeepMind — *DeepMind Publications — 3D Vision* (2026, Google DeepMind) | https://deepmind.google/research/publications/ | neural fields; generative 3D index |
| 9 | Sony AI — *Sony AI Publications* (2026, Sony AI) | https://ai.sony/publications/ | neural rendering research index |
| 10 | Adobe Research — *Adobe Research — Computer Vision* (2026, Adobe Research) | https://research.adobe.com/ | capture and relighting R&D |
| 11 | MIT — *Instant-NGP (precursor ecosystem)* (2022, GitHub) | https://github.com/NVlabs/instant-ngp | fast neural fields context for 3DGS migration |
| 12 | Stanford — *NeRF Original Paper* (2020, ECCV) | https://arxiv.org/abs/2003.08934 | baseline radiance field method before 3DGS |
| 13 | Princeton — *3D Gaussian Splatting as Point-Based Radiance Field* (2023, Emergent Mind) | https://www.emergentmind.com/topics/3d-gaussian-splat-radiance-field | explainer; alpha compositing math |
| 14 | Local Mac — *Train 3DGS on Mac — graphdeco README constraints* (2023, GitHub) | https://github.com/graphdeco-inria/gaussian-splatting#requirements | Mac requires conda; MPS limited; prefer Linux+CUDA for training |
| 15 | Local Mac — *PyTorch MPS — Apple Silicon* (2026, PyTorch) | https://pytorch.org/docs/stable/notes/mps.html | train/infer on MacBook when CUDA unavailable |
| 16 | Local Mac — *Metal Performance Shaders* (2026, Apple) | https://developer.apple.com/documentation/metalperformanceshaders | GPU path for custom splat kernels on Mac |
| 17 | Local Mac — *Ollama (optional) — local LLM for pipeline notes only* (2026, Ollama) | https://ollama.com/ | not 3DGS training — agent assist on Mac offline |

---

## Kit implications

- **Hero set-dressing:** High-fidelity photogrammetry splats at cave mouth / landmark POIs without replacing terrain pipeline.
- **Not a terrain replacement:** Keep LiDAR + Terrain for walkable collision; splats are **visual radiance fields**.
- **Demo reel:** Pre-bake splats, stream via Addressables if multi-chunk open world expands later.
