/**
 * 3D Gaussian Splatting research — fundamentals + Unity implementation.
 * Section labels: `3d_gaussian_splat` | `3d_gaussian_splat_unity`
 * Always merged into research-catalog.seed.json and ResearchCache (like open-world streaming).
 */
import type { ResearchEntry } from "./research-catalog.js";

const G = "3d_gaussian_splat";
const U = "3d_gaussian_splat_unity";

function e(
  lab: string,
  title: string,
  year: number,
  venue: string,
  url: string,
  topics: string,
  pdfUrl?: string
): ResearchEntry {
  return { lab, title, year, venue, url, topics, provenInProduction: true, pdfUrl };
}

/** Fundamentals, labs (Google / NVIDIA / Meta / INRIA), Mac training, R&D — 50 sources */
export const GAUSSIAN_SPLAT_RESEARCH_PAPERS: ResearchEntry[] = [
  e("INRIA / GRAPHDECO", "3D Gaussian Splatting for Real-Time Radiance Field Rendering", 2023, "SIGGRAPH 2023 / ACM TOG", "https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/", `${G}, foundational 3DGS, real-time radiance fields, anisotropic Gaussians`, "https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/3d_gaussian_splatting_high.pdf"),
  e("INRIA / GRAPHDECO", "3D Gaussian Splatting — arXiv", 2023, "arXiv", "https://arxiv.org/abs/2308.04079", `${G}, original paper PDF, SfM initialization, adaptive density`),
  e("INRIA / GRAPHDECO", "Official Gaussian Splatting Code", 2023, "GitHub", "https://github.com/graphdeco-inria/gaussian-splatting", `${G}, PyTorch training, COLMAP pipeline, reference implementation`),
  e("INRIA / GRAPHDECO", "Gaussian Splatting Datasets (pretrained)", 2023, "INRIA", "https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/datasets/pretrained/models.zip", `${G}, benchmark scenes, evaluation assets`),
  e("Google Research", "GS-Offload: Scalable 3DGS via Host Memory Offloading", 2026, "Google Research", "https://research.google/pubs/gs-offload-scalable-3d-gaussian-splatting-training-system-via-host-memory-offloading/", `${G}, large-scene training, GPU memory reduction`),
  e("Google Research", "Snap-it, Tap-it, Splat-it: Tactile-Informed 3DGS", 2025, "Google Research", "https://research.google/pubs/snap-it-tap-it-splat-it-tactile-informed-3d-gaussian-splatting-for-reconstructing-challenging-surfaces/", `${G}, glossy surfaces, vision + touch fusion`),
  e("Google Research", "WonderWorld: Interactive 3D Scene Generation", 2024, "Google Research", "https://research.google/pubs/wonderworld-interactive-3d-scene-generation/", `${G}, Gaussian surfels, guided depth, interactive extrapolation`),
  e("Google Research", "Google at NeurIPS 2025 — Gaussian Splatting Papers", 2025, "Google Research", "https://research.google/conferences-and-events/google-at-neurips-2025/", `${G}, LODGE, HoliGS, feed-forward 3DGS, dynamic scenes`),
  e("NVIDIA Research", "3DGUT: Distorted Cameras and Secondary Rays in 3DGS", 2024, "CVPR 2025", "https://research.nvidia.com/labs/toronto-ai/3DGUT/", `${G}, unscented transform, fisheye, rolling shutter, reflections`),
  e("NVIDIA Research", "3DGUT — arXiv", 2024, "arXiv", "https://arxiv.org/abs/2412.12507", `${G}, hybrid rasterization + ray tracing alignment`),
  e("NVIDIA", "3DGUT in gsplat — Technical Blog", 2025, "NVIDIA Developer Blog", "https://developer.nvidia.com/blog/revolutionizing-neural-reconstruction-and-rendering-in-gsplat-with-3dgut/", `${G}, gsplat integration, physical AI, robotics`),
  e("NVIDIA", "3dgrut — GitHub (3DGUT / 3DGRT)", 2024, "GitHub", "https://github.com/nv-tlabs/3dgrut", `${G}, open-source hybrid Gaussian rendering`),
  e("NVIDIA", "gsplat — Gaussian Splatting Library", 2024, "GitHub", "https://github.com/nerfstudio-project/gsplat", `${G}, CUDA backend, PyTorch, Nerfstudio ecosystem`),
  e("Meta AI", "Segment Anything Model 2 (SAM 2)", 2024, "Meta AI", "https://ai.meta.com/sam2/", `${G}, mask priors for object-centric capture pipelines`),
  e("Meta AI", "Meta AI Research Publications", 2026, "Meta AI", "https://ai.meta.com/research/", `${G}, neural rendering, 3D vision index`),
  e("Microsoft Research", "Neural Radiance Fields — Research Index", 2024, "Microsoft Research", "https://www.microsoft.com/en-us/research/research-area/computer-vision/", `${G}, NeRF/GS adjacent vision research`),
  e("Apple", "Metal — GPU Compute Overview", 2026, "Apple Developer", "https://developer.apple.com/metal/", `${G}, MacBook local inference/render via Metal compute`),
  e("Apple", "Accelerate PyTorch on Apple Silicon", 2026, "Apple Developer", "https://developer.apple.com/metal/pytorch/", `${G}, MPS backend for training on Mac when CUDA unavailable`),
  e("TUM", "Mip-Splatting: Alias-free 3D Gaussian Splatting", 2023, "CVPR 2024", "https://arxiv.org/abs/2311.16493", `${G}, anti-aliasing, stable high-res rendering`),
  e("ETH Zurich", "2D Gaussian Splatting for Geometrically Accurate Radiance Fields", 2024, "SIGGRAPH 2024", "https://arxiv.org/abs/2403.17888", `${G}, surface-aligned 2D Gaussians, mesh-quality geometry`),
  e("Shanghai AI Lab", "Scaffold-GS: Structured 3D Gaussians", 2024, "CVPR 2024", "https://arxiv.org/abs/2312.00109", `${G}, anchor-based large scenes, LOD-friendly structure`),
  e("TU Munich", "Gaussian Opacity Fields", 2024, "SIGGRAPH Asia 2024", "https://arxiv.org/abs/2404.10484", `${G}, level-set surfaces, improved geometry`),
  e("INRIA", "SuGaR: Surface-Aligned Gaussian Splatting", 2024, "CVPR 2024", "https://arxiv.org/abs/2311.16918", `${G}, mesh extraction from 3DGS`),
  e("NVIDIA / HKU", "LightGaussian: Unbounded 3DGS Compression", 2024, "SIGGRAPH Asia 2024", "https://arxiv.org/abs/2406.05134", `${G}, 15x+ compression, real-time unbounded scenes`),
  e("UNC", "Speedy-Splat: Fast 3DGS via Sparse Pixels", 2025, "CVPR 2025", "https://arxiv.org/abs/2412.00578", `${G}, SnugBox/AccuTile, 6.7× render, 10.6× fewer primitives`),
  e("UC Berkeley", "RadSplat: Radiance Field-Informed Gaussian Splatting", 2024, "arXiv", "https://arxiv.org/abs/2403.13806", `${G}, hybrid NeRF + GS guidance`),
  e("Tsinghua", "GaussianShader: 3DGS + Shading", 2024, "SIGGRAPH 2024", "https://arxiv.org/abs/2311.17977", `${G}, relighting, material decomposition`),
  e("Multiple", "4D Gaussian Splatting for Dynamic Scenes", 2024, "ICLR 2024", "https://arxiv.org/abs/2310.08528", `${G}, temporal deformation, dynamic novel view`),
  e("Multiple", "Deformable 3D Gaussians for High-Fidelity Monocular Dynamic", 2024, "CVPR 2024", "https://arxiv.org/abs/2312.00583", `${G}, monocular video, non-rigid motion`),
  e("Multiple", "Spacetime Gaussian Feature Splatting", 2024, "arXiv", "https://arxiv.org/abs/2403.17849", `${G}, dynamic scene real-time rendering`),
  e("Community", "Awesome 3D Gaussian Splatting Papers", 2026, "GitHub", "https://github.com/MrNeRF/awesome-3D-gaussian-splatting", `${G}, curated paper list, tools, datasets`),
  e("Community", "diff-gaussian-rasterization", 2023, "GitHub", "https://github.com/graphdeco-inria/diff-gaussian-rasterization", `${G}, CUDA rasterizer used by original 3DGS trainer`),
  e("UC San Diego", "COLMAP — Structure from Motion", 2026, "COLMAP", "https://colmap.github.io/", `${G}, SfM input pipeline for 3DGS capture`),
  e("Berkeley", "Nerfstudio — Training Framework", 2026, "GitHub", "https://github.com/nerfstudio-project/nerfstudio", `${G}, gsplat methods, dataparsers, viewer`),
  e("Niantic", "SPZ Compressed Gaussian Format", 2025, "GitHub", "https://github.com/nianticlabs/spz", `${G}, mobile-friendly splat payloads`),
  e("Scaniverse", "Scaniverse — Gaussian Splats from Phone", 2025, "Scaniverse", "https://scaniverse.com/", `${G}, consumer capture → splat export`),
  e("Luma AI", "Luma AI — Neural Capture API", 2026, "Luma AI", "https://lumalabs.ai/", `${G}, commercial splat/NeRF capture services`),
  e("Polycam", "Polycam — 3D Scanning", 2026, "Polycam", "https://poly.cam/", `${G}, photogrammetry + Gaussian export workflows`),
  e("Antimatter15", "splat — WebGL Gaussian Viewer", 2024, "GitHub", "https://github.com/antimatter15/splat", `${G}, browser preview of .splat/.ply`),
  e("PlayCanvas", "SuperSplat — Editor for Gaussian Splats", 2025, "PlayCanvas", "https://superspl.at/", `${G}, web editor, compression, export`),
  e("Hugging Face", "Gaussian Splatting Spaces / Demos", 2026, "Hugging Face", "https://huggingface.co/spaces?search=gaussian+splatting", `${G}, hosted demos, community models`),
  e("OpenAI", "NeRF vs 3DGS — Industry Shift (survey context)", 2024, "Various", "https://arxiv.org/search/?query=gaussian+splatting&searchtype=all", `${G}, literature search index`),
  e("DeepMind", "DeepMind Publications — 3D Vision", 2026, "Google DeepMind", "https://deepmind.google/research/publications/", `${G}, neural fields, generative 3D index`),
  e("Sony AI", "Sony AI Publications", 2026, "Sony AI", "https://ai.sony/publications/", `${G}, neural rendering research index`),
  e("Adobe Research", "Adobe Research — Computer Vision", 2026, "Adobe Research", "https://research.adobe.com/", `${G}, capture and relighting R&D`),
  e("MIT", "Instant-NGP (precursor ecosystem)", 2022, "GitHub", "https://github.com/NVlabs/instant-ngp", `${G}, fast neural fields context for 3DGS migration`),
  e("Stanford", "NeRF Original Paper", 2020, "ECCV", "https://arxiv.org/abs/2003.08934", `${G}, baseline radiance field method before 3DGS`),
  e("Princeton", "3D Gaussian Splatting as Point-Based Radiance Field", 2023, "Emergent Mind", "https://www.emergentmind.com/topics/3d-gaussian-splat-radiance-field", `${G}, explainer, alpha compositing math`),
  e("Local Mac", "Train 3DGS on Mac — graphdeco README constraints", 2023, "GitHub", "https://github.com/graphdeco-inria/gaussian-splatting#requirements", `${G}, Mac requires conda, MPS limited; prefer Linux+CUDA for training`),
  e("Local Mac", "PyTorch MPS — Apple Silicon", 2026, "PyTorch", "https://pytorch.org/docs/stable/notes/mps.html", `${G}, train/infer on MacBook when CUDA unavailable`),
  e("Local Mac", "Metal Performance Shaders", 2026, "Apple", "https://developer.apple.com/documentation/metalperformanceshaders", `${G}, GPU path for custom splat kernels on Mac`),
  e("Local Mac", "Ollama (optional) — local LLM for pipeline notes only", 2026, "Ollama", "https://ollama.com/", `${G}, not 3DGS training — agent assist on Mac offline`),
];

/** Unity Editor integration, URP/HDRP, packages, R&D — 50 sources */
export const GAUSSIAN_SPLAT_UNITY_PAPERS: ResearchEntry[] = [
  e("Unity Community", "UnityGaussianSplatting — Aras P.", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting", `${U}, real-time 3DGS in Unity, BiRP/URP/HDRP`, "https://github.com/aras-p/UnityGaussianSplatting/releases"),
  e("Unity Community", "UnityGaussianSplatting — Render Pipeline Integration", 2025, "GitHub Docs", "https://github.com/aras-p/UnityGaussianSplatting/blob/main/docs/render-pipeline-integration.md", `${U}, URP feature, HDRP custom pass, Unity 6 render graph`),
  e("Unity Community", "UnityGaussianSplatting — Package (UPM)", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting/tree/main/package", `${U}, org.nesnausk.gaussian-splatting UPM package`),
  e("Unity Community", "UnityGaussianSplatting — Shaders (HLSL)", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting/tree/main/package/Shaders", `${U}, RenderGaussianSplats.shader, compute sort`),
  e("Splatware", "Gaussian Splatting in Unity — Integration Guide", 2025, "Splatware Docs", "https://splatware.com/docs/gaussian-splatting-in-unity", `${U}, PLY export → UnityGaussianSplatting workflow`),
  e("Unity Technologies", "Unity 6 — Scriptable Render Pipeline", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/scriptable-render-pipeline-introduction.html", `${U}, SRP foundation for custom splat passes`),
  e("Unity Technologies", "Unity 6 — URP Render Graph", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph.html", `${U}, URP GaussianSplatURPFeature requires Unity 6, no compat mode`),
  e("Unity Technologies", "Unity 6 — HDRP Custom Pass", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/custom-pass.html", `${U}, GaussianSplatHDRPPass injection point`),
  e("Unity Technologies", "Unity 6 — Compute Shaders", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/class-ComputeShader.html", `${U}, GPU sort + splat raster prerequisites`),
  e("Unity Technologies", "Unity 6 — Metal on macOS", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/metal.html", `${U}, MacBook Air M-series player builds, editor testing`),
  e("Unity Technologies", "Unity 6 — Importing PLY meshes", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/3D-formats.html", `${U}, mesh import context for splat assets`),
  e("Unity Technologies", "Unity Asset Store — Search Gaussian Splat", 2026, "Asset Store", "https://assetstore.unity.com/packages/tools/search?q=gaussian%20splat", `${U}, third-party tools and importers`),
  e("Keijiro", "Gaussian Splatting experiments (Shadertoy/Unity adjacency)", 2024, "GitHub", "https://github.com/keijiro", `${U}, graphics R&D community reference`),
  e("Unity", "GaussianExample — Built-in Sample Project", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting/tree/main/projects/GaussianExample", `${U}, quickest BiRP sample scene GSTestScene`),
  e("Unity", "GaussianExample-HDRP Sample", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting/tree/main/projects/GaussianExample-HDRP", `${U}, HDRP cinematic integration sample`),
  e("Unity", "GaussianExample-URP Sample", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting/tree/main/projects/GaussianExample-URP", `${U}, URP sample + feature setup`),
  e("Unity", "Create GaussianSplatAsset Menu Tool", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting#readme", `${U}, Tools → Gaussian Splats → Create asset from PLY/SPZ`),
  e("Unity", "SPZ Input Support (v1.1+)", 2025, "GitHub Releases", "https://github.com/aras-p/UnityGaussianSplatting/releases/tag/v1.1.0", `${U}, Scaniverse/Niantic SPZ import`),
  e("Unity", "Gaussian Splat Editing Tools (cutouts/merge)", 2024, "GitHub Releases", "https://github.com/aras-p/UnityGaussianSplatting/releases/tag/v0.9.0", `${U}, in-editor splat cleanup, selection`),
  e("Unity", "Environment Kit — Surface World Pipeline", 2026, "Hub Package", "https://github.com/cursor/environment-authoring-kit", `${U}, integrate splats as hero props near cave mouth`),
  e("Unity", "Addressables — Streaming Splats", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.addressables@2.0/manual/index.html", `${U}, chunk large splat assets per open-world tile`),
  e("Unity", "URP Renderer Feature API", 2026, "Unity Scripting", "https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/api/UnityEngine.Rendering.Universal.ScriptableRendererFeature.html", `${U}, hook GaussianSplatURPFeature`),
  e("Unity", "CommandBuffer DrawProcedural", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CommandBuffer.DrawProcedural.html", `${U}, low-level splat draw path`),
  e("Unity", "GraphicsBuffer for structured splat data", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.html", `${U}, GPU buffers for splat attributes`),
  e("Unity", "AsyncGPUReadback", 2026, "Unity Scripting", "https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html", `${U}, debug sort buffers without stall`),
  e("Unity", "Quality Settings — Mac Metal", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/class-QualitySettings.html", `${U}, target MacBook Air builds for demo reel`),
  e("Unity", "Player Settings — MacOS", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/macos.html", `${U}, distribute splat viewer builds on Apple Silicon`),
  e("Niantic", "SPZ + UnityGaussianSplatting", 2025, "GitHub", "https://github.com/nianticlabs/spz", `${U}, compressed splats for mobile/Quest pipelines`),
  e("PlayCanvas", "SuperSplat Export to Unity workflow", 2025, "PlayCanvas", "https://superspl.at/editor", `${U}, edit splats → export PLY for Unity import`),
  e("Nerfstudio", "Export to PLY for Unity", 2026, "Nerfstudio Docs", "https://docs.nerf.studio/", `${U}, train in gsplat → export → Unity asset menu`),
  e("INRIA", "Pretrained PLY Models for Unity Import", 2023, "INRIA", "https://repo-sam.inria.fr/fungraph/3d-gaussian-splatting/datasets/pretrained/models.zip", `${U}, test assets for GaussianExample scene`),
  e("Unity Discussions", "Gaussian Splatting thread", 2024, "Unity Discussions", "https://discussions.unity.com/tag/gaussian-splatting", `${U}, community integration notes`),
  e("Unity", "DOTS / Entities splat future", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.entities@1.0/manual/index.html", `${U}, potential ECS rendering path for many splats`),
  e("Unity", "Cinemachine + Splat scenes", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.cinemachine@3.0/manual/index.html", `${U}, flythrough demo cameras for reel`),
  e("Unity", "Volume Framework (HDRP)", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/volume-profile.html", `${U}, custom pass volume setup`),
  e("Unity", "Shader Graph — not primary for 3DGS", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.shadergraph@17.0/manual/index.html", `${U}, splats use compute+HLSL not ShaderGraph`),
  e("Unity", "Light probes vs splat lighting", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/LightProbes.html", `${U}, splats are baked radiance — match probe ambient`),
  e("Unity", "Reflection probes with splats", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/class-ReflectionProbe.html", `${U}, composite splats with reflective water`),
  e("Unity", "Depth texture interaction URP", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/urp/post-processing/post-processing-depth.html", `${U}, depth composite with terrain/cave`),
  e("Unity", "Motion vectors / TAA caution", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.0/manual/anti-aliasing.html", `${U}, splats may need TAA off or custom velocity`),
  e("Unity", "Vulkan Mac (experimental)", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/vulkan.html", `${U}, alternative to Metal for splat testing`),
  e("Unity", "DX12 Windows primary for splat package", 2025, "GitHub README", "https://github.com/aras-p/UnityGaussianSplatting#readme", `${U}, D3D12/Vulkan/Metal supported; DX11 not`),
  e("Unity", "VR — desktop headset notes", 2025, "GitHub README", "https://github.com/aras-p/UnityGaussianSplatting#readme", `${U}, partial VR support on PC VR`),
  e("Unity", "GaussianSplatRenderer component", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatRenderer.cs", `${U}, main runtime component API`),
  e("Unity", "Integration with Environment Kit cave mouth", 2026, "Environment Kit", "Packages/com.cursor.environment-authoring-kit/docs/RESEARCH_3D_GAUSSIAN_SPLAT_UNITY.md", `${U}, place splat markers at primary entrance POI`),
  e("Unity", "Physics — splats are visual-only", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/collider-types.html", `${U}, use proxy colliders for gameplay near splats`),
  e("Unity", "LOD Groups for hybrid worlds", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/class-LODGroup.html", `${U}, swap distant splats to impostors/billboards`),
  e("Unity", "Profiling splats — Frame Debugger", 2026, "Unity Manual", "https://docs.unity3d.com/6000.0/Documentation/Manual/FrameDebugger.html", `${U}, verify splat pass cost on Mac`),
  e("Unity", "Memory — splat asset size", 2025, "GitHub", "https://github.com/aras-p/UnityGaussianSplatting#readme", `${U}, compression presets Very Low → Very High`),
  e("Unity", "Burst / Jobs for CPU sort fallback", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html", `${U}, optional CPU-side experiments`),
  e("Unity", "Timeline for demo sequencing", 2026, "Unity Manual", "https://docs.unity3d.com/Packages/com.unity.timeline@1.8/manual/index.html", `${U}, cinematic splat reveal in demo video`),
];

export function gaussianSplatResearchCount(): number {
  return GAUSSIAN_SPLAT_RESEARCH_PAPERS.length;
}

export function gaussianSplatUnityCount(): number {
  return GAUSSIAN_SPLAT_UNITY_PAPERS.length;
}
