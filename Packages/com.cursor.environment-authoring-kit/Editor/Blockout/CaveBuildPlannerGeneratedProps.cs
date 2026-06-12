#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Hub-owned procedural props for planner concept cards + world scatter (no third-party kit).
    /// Output: Assets/EnvironmentKit/Generated/PlannerProps/Prefabs/**
    /// </summary>
    public static class CaveBuildPlannerGeneratedProps
    {
        public const string Root = "Assets/EnvironmentKit/Generated/PlannerProps";
        public const string PrefabRoot = Root + "/Prefabs";
        public const string MeshRoot = Root + "/Meshes";
        public const string MaterialRoot = Root + "/Materials";
        public const string TextureRoot = Root + "/Textures";
        public const string RequestRel = "Assets/EnvironmentKit/Generated/planner-generated-props.request";
        public const string DoneRel = "Assets/EnvironmentKit/Generated/planner-generated-props.done.json";
        public const string CardMeshRequestRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.request.json";
        public const string CardMeshDoneRel =
            "Assets/EnvironmentKit/Generated/planner-concept-card-mesh.done.json";

        [MenuItem("Window/Environment Kit/Generate original planner props")]
        public static void GenerateMenu()
        {
            var count = GenerateDefaultSet(out var msg);
            EditorUtility.DisplayDialog("Planner props", msg, "OK");
            Debug.Log($"[PlannerProps] {msg} ({count} prefab(s))");
        }

        /// <summary>Batchmode: EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.GeneratePlannerProps</summary>
        public static void GenerateForBatch()
        {
            GenerateDefaultSet(out var msg);
            Debug.Log($"[PlannerProps] {msg}");
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }

        public static int GenerateDefaultSet(out string message)
        {
            EnsureFolders();
            var n = 0;
            n += GenerateGrassVariants(4);
            n += GenerateRockVariants(4);
            n += GenerateBushVariants(3);
            n += GenerateTreeVariants(3);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            message = $"Generated {n} original prop prefab(s) under {PrefabRoot}.";
            return n;
        }

        [Serializable]
        public sealed class AiColor
        {
            public float r = -1f;
            public float g = -1f;
            public float b = -1f;
        }

        [Serializable]
        public sealed class AiMeshSpec
        {
            public string kind;
            public int variant = -1;
            public float height;
            public float canopyScale = 1f;
            public float rockRadius = 1f;
            public float roughness = 0.35f;
            public float normalStrength = 1f;
            public AiColor trunkColor;
            public AiColor leafColor;
            public AiColor albedoColor;
            public string styleNotes;
        }

        [Serializable]
        public sealed class CardMeshRequest
        {
            public string cardId;
            public string categoryKey;
            public string label;
            public int seed;
            public int regenIndex;
            public AiMeshSpec aiSpec;
        }

        /// <summary>Generate one Hub-owned prop prefab for a concept approval card.</summary>
        public static bool TryGenerateForCard(
            string cardId,
            string categoryKey,
            string label,
            int seed,
            int regenIndex,
            AiMeshSpec aiSpec,
            out string prefabRel,
            out string thumbRel,
            out string message)
        {
            prefabRel = null;
            thumbRel = null;
            message = null;
            EnsureFolders();
            var effectiveSeed = unchecked(seed + Mathf.Max(0, regenIndex) * 104729);
            var kind = !string.IsNullOrWhiteSpace(aiSpec?.kind)
                ? aiSpec.kind.ToLowerInvariant()
                : ResolvePropKind(categoryKey, label);
            var variant = aiSpec != null && aiSpec.variant >= 0
                ? Mathf.Abs(aiSpec.variant) % 4
                : Mathf.Abs(effectiveSeed) % 4;
            var slug = Slug(cardId, label);
            const bool replace = true;
            var ok = kind switch
            {
                "grass" => GenerateGrassOne(slug, variant, effectiveSeed, replace, aiSpec),
                "rock" => GenerateRockOne(slug, variant, effectiveSeed, replace, aiSpec),
                "tree" => GenerateTreeOne(slug, variant, effectiveSeed, replace, aiSpec),
                "orb" => GenerateOrbOne(slug, variant, effectiveSeed, replace, aiSpec),
                _ => GenerateBushOne(slug, variant, effectiveSeed, replace, aiSpec),
            };
            if (!ok)
            {
                message = $"Failed to build {kind} mesh for {label}.";
                return false;
            }

            prefabRel = $"{PrefabRoot}/{kind}/{slug}.prefab";
            AttachPropAutomation(prefabRel, kind);
            thumbRel = CaveBuildPlannerKitCatalogExporter.ExportThumbnailForPrefab(prefabRel);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var variation = regenIndex > 0 ? $" (variation {regenIndex + 1})" : string.Empty;
            if (aiSpec != null && !string.IsNullOrWhiteSpace(aiSpec.styleNotes))
                message = $"Hyper-realistic {kind} for {label}{variation} — {aiSpec.styleNotes.Trim()}";
            else if (aiSpec != null)
                message = $"AI {kind} mesh for {label}{variation}.";
            else
                message = $"Generated original {kind} mesh for {label}{variation}.";
            return File.Exists(Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), prefabRel));
        }

        static void ResolveColor(AiColor c, float dr, float dg, float db, out float r, out float g, out float b)
        {
            if (c != null && c.r >= 0f && c.g >= 0f && c.b >= 0f)
            {
                r = Mathf.Clamp01(c.r);
                g = Mathf.Clamp01(c.g);
                b = Mathf.Clamp01(c.b);
                return;
            }

            r = dr;
            g = dg;
            b = db;
        }

        static string ResolvePropKind(string categoryKey, string label)
        {
            var cat = (categoryKey ?? string.Empty).ToLowerInvariant();
            var lab = (label ?? string.Empty).ToLowerInvariant();
            if (cat.Contains("grass") || lab.Contains("grass"))
                return "grass";
            if (cat.Contains("rock") || lab.Contains("rock") || lab.Contains("stone"))
                return "rock";
            if (cat.Contains("tree") || lab.Contains("tree"))
                return "tree";
            if (lab.Contains("orb") || lab.Contains("crystal") || lab.Contains("gem") || lab.Contains("shard")
                || lab.Contains("pickup") || lab.Contains("loot") || lab.Contains("relic") || lab.Contains("token")
                || cat.Contains("collectible"))
                return "orb";
            return "bush";
        }

        static string Slug(string cardId, string label)
        {
            // One stable prefab name per card — strip stacked Gen_ from repeated regen.
            var raw = string.IsNullOrWhiteSpace(label) ? cardId : label;
            while (raw.StartsWith("Gen_", StringComparison.Ordinal))
                raw = raw.Substring(4);
            var slug = System.Text.RegularExpressions.Regex.Replace(raw, @"[^a-zA-Z0-9]+", "_")
                .Trim('_');
            if (slug.Length > 48)
                slug = slug.Substring(0, 48);
            return string.IsNullOrEmpty(slug) ? "Gen_Prop" : $"Gen_{slug}";
        }

        static bool GenerateGrassOne(string slug, int variant, int seed, bool replace, AiMeshSpec ai)
        {
            ResolveColor(ai?.albedoColor, 0.30f + variant * 0.04f, 0.58f, 0.24f, out var r, out var g, out var b);
            var height = ai != null && ai.height > 0.1f ? ai.height * 0.45f : 0.85f + variant * 0.1f;
            var rough = ai != null && ai.roughness > 0.01f ? ai.roughness : 0.42f;
            var normal = ai != null && ai.normalStrength > 0.01f ? ai.normalStrength : 1f;
            var albedo = SaveNoiseTexture($"{slug}_albedo.png", r, g, b, 0.14f, seed, 128);
            var bump = SaveNormalTexture($"{slug}_normal.png", normal, seed + 31);
            return SavePrefab($"grass/{slug}.prefab", SaveMesh($"{slug}_mesh.asset", CreateGrassClumpMesh(height), replace),
                SaveLitMaterial($"MAT_{slug}.mat", albedo, bump, rough, replace),
                Vector3.one);
        }

        static bool GenerateRockOne(string slug, int variant, int seed, bool replace, AiMeshSpec ai)
        {
            ResolveColor(ai?.albedoColor, 0.38f + variant * 0.02f, 0.36f, 0.32f, out var r, out var g, out var b);
            var radius = ai != null && ai.rockRadius > 0.1f ? ai.rockRadius * 0.55f : 0.5f + variant * 0.12f;
            var rough = ai != null && ai.roughness > 0.01f ? ai.roughness : 0.72f;
            var normal = ai != null && ai.normalStrength > 0.01f ? ai.normalStrength : 1.15f;
            var albedo = SaveNoiseTexture($"{slug}_albedo.png", r, g, b, 0.18f, seed, 128);
            var bump = SaveNormalTexture($"{slug}_normal.png", normal, seed + 53);
            return SavePrefab($"rock/{slug}.prefab",
                SaveMesh($"{slug}_mesh.asset", CreateRockMesh(radius, seed), replace),
                SaveLitMaterial($"MAT_{slug}.mat", albedo, bump, rough, replace),
                Vector3.one);
        }

        static bool GenerateBushOne(string slug, int variant, int seed, bool replace, AiMeshSpec ai)
        {
            ResolveColor(ai?.albedoColor, 0.16f, 0.40f, 0.18f, out var r, out var g, out var b);
            var mat = SaveLitMaterial($"MAT_{slug}.mat",
                SaveNoiseTexture($"{slug}_albedo.png", r, g, b, 0.1f, seed), 0.2f, replace);
            var root = new GameObject(slug);
            try
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.transform.SetParent(root.transform, false);
                puff.transform.localScale = Vector3.one * (0.65f + variant * 0.12f);
                UnityEngine.Object.DestroyImmediate(puff.GetComponent<Collider>());
                puff.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return SavePrefabInstance($"bush/{slug}.prefab", root);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static bool GenerateOrbOne(string slug, int variant, int seed, bool replace, AiMeshSpec ai)
        {
            ResolveColor(ai?.albedoColor, 0.15f + variant * 0.12f, 0.55f + variant * 0.08f, 0.95f, out var r, out var g, out var b);
            var radius = ai != null && ai.rockRadius > 0.08f ? ai.rockRadius * 0.42f : 0.32f + variant * 0.05f;
            var rough = ai != null && ai.roughness > 0.01f ? ai.roughness * 0.35f : 0.12f;
            var albedo = SaveNoiseTexture($"{slug}_albedo.png", r, g, b, 0.06f, seed, 128);
            var bump = SaveNormalTexture($"{slug}_normal.png", 0.85f, seed + 19);
            var mat = SaveLitMaterial($"MAT_{slug}.mat", albedo, bump, rough, replace);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(r * 0.55f, g * 0.65f, b * 0.85f));
            }

            var root = new GameObject(slug);
            try
            {
                var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = "Orb";
                orb.transform.SetParent(root.transform, false);
                orb.transform.localScale = Vector3.one * radius * 2f;
                UnityEngine.Object.DestroyImmediate(orb.GetComponent<Collider>());
                orb.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return SavePrefabInstance($"orb/{slug}.prefab", root);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static bool GenerateTreeOne(string slug, int variant, int seed, bool replace, AiMeshSpec ai)
        {
            ResolveColor(ai?.trunkColor, 0.34f, 0.24f, 0.14f, out var tr, out var tg, out var tb);
            ResolveColor(ai?.leafColor, 0.10f, 0.42f, 0.16f, out var lr, out var lg, out var lb);
            var trunkH = ai != null && ai.height > 0.1f ? ai.height * 0.35f : 0.8f + variant * 0.15f;
            var canopyScale = ai != null && ai.canopyScale > 0.1f ? ai.canopyScale : 1.0f + variant * 0.18f;
            var trunkMat = SaveLitMaterial($"MAT_{slug}_trunk.mat",
                SaveNoiseTexture($"{slug}_trunk.png", tr, tg, tb, 0.08f, seed), 0.1f, replace);
            var leafMat = SaveLitMaterial($"MAT_{slug}_leaf.mat",
                SaveNoiseTexture($"{slug}_leaf.png", lr, lg, lb, 0.1f, seed + 17), 0.15f, replace);
            var root = new GameObject(slug);
            try
            {
                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.transform.SetParent(root.transform, false);
                trunk.transform.localScale = new Vector3(0.2f, trunkH, 0.2f);
                trunk.transform.localPosition = new Vector3(0f, trunk.transform.localScale.y, 0f);
                UnityEngine.Object.DestroyImmediate(trunk.GetComponent<Collider>());
                trunk.GetComponent<MeshRenderer>().sharedMaterial = trunkMat;

                var canopyGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                canopyGo.transform.SetParent(root.transform, false);
                canopyGo.transform.localScale = Vector3.one * canopyScale;
                canopyGo.transform.localPosition = new Vector3(0f, 1.2f + trunkH + canopyScale * 0.35f, 0f);
                UnityEngine.Object.DestroyImmediate(canopyGo.GetComponent<Collider>());
                canopyGo.GetComponent<MeshRenderer>().sharedMaterial = leafMat;
                return SavePrefabInstance($"tree/{slug}.prefab", root);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void EnsureFolders()
        {
            foreach (var rel in new[] { Root, PrefabRoot, MeshRoot, MaterialRoot, TextureRoot,
                         $"{PrefabRoot}/grass", $"{PrefabRoot}/rock", $"{PrefabRoot}/bush", $"{PrefabRoot}/tree",
                         $"{PrefabRoot}/orb" })
            {
                var abs = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), rel);
                if (!Directory.Exists(abs))
                    Directory.CreateDirectory(abs);
            }
        }

        static int GenerateGrassVariants(int count)
        {
            var hues = new[] { (0.32f, 0.62f, 0.28f), (0.28f, 0.55f, 0.22f), (0.38f, 0.68f, 0.32f), (0.22f, 0.48f, 0.18f) };
            var n = 0;
            for (var i = 0; i < count; i++)
            {
                var (r, g, b) = hues[i % hues.Length];
                var tex = SaveNoiseTexture($"Gen_Grass_Albedo_{i + 1:00}.png", r, g, b, 0.12f);
                var mat = SaveLitMaterial($"MAT_Gen_Grass_{i + 1:00}.mat", tex, smoothness: 0.15f);
                var mesh = SaveMesh($"Gen_Grass_Cross_{i + 1:00}.asset", CreateGrassCrossMesh(0.9f + i * 0.08f));
                if (SavePrefab($"grass/Gen_Grass_Clump_{i + 1:00}.prefab", mesh, mat, Vector3.one))
                    n++;
            }

            return n;
        }

        static int GenerateRockVariants(int count)
        {
            var palettes = new[] {
                (0.42f, 0.40f, 0.36f), (0.35f, 0.38f, 0.32f), (0.48f, 0.44f, 0.40f), (0.30f, 0.34f, 0.30f)
            };
            var n = 0;
            for (var i = 0; i < count; i++)
            {
                var (r, g, b) = palettes[i % palettes.Length];
                var tex = SaveNoiseTexture($"Gen_Rock_Albedo_{i + 1:00}.png", r, g, b, 0.18f);
                var mat = SaveLitMaterial($"MAT_Gen_Rock_{i + 1:00}.mat", tex, smoothness: 0.25f);
                var mesh = SaveMesh($"Gen_Rock_Lump_{i + 1:00}.asset", CreateRockMesh(0.55f + i * 0.12f, 42 + i * 17));
                if (SavePrefab($"rock/Gen_Rock_Flat_{i + 1:00}.prefab", mesh, mat, Vector3.one))
                    n++;
            }

            return n;
        }

        static int GenerateBushVariants(int count)
        {
            var n = 0;
            for (var i = 0; i < count; i++)
            {
                var tex = SaveNoiseTexture($"Gen_Bush_Albedo_{i + 1:00}.png", 0.18f, 0.42f, 0.20f, 0.1f);
                var mat = SaveLitMaterial($"MAT_Gen_Bush_{i + 1:00}.mat", tex, smoothness: 0.2f);
                var root = new GameObject($"Gen_Bush_{i + 1:00}");
                try
                {
                    var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    puff.name = "Puff";
                    puff.transform.SetParent(root.transform, false);
                    puff.transform.localScale = Vector3.one * (0.7f + i * 0.1f);
                    UnityEngine.Object.DestroyImmediate(puff.GetComponent<Collider>());
                    puff.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    if (SavePrefabInstance($"bush/Gen_Bush_{i + 1:00}.prefab", root))
                        n++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            return n;
        }

        static int GenerateTreeVariants(int count)
        {
            var n = 0;
            for (var i = 0; i < count; i++)
            {
                var trunkTex = SaveNoiseTexture($"Gen_Tree_Trunk_{i + 1:00}.png", 0.36f, 0.26f, 0.16f, 0.08f);
                var leafTex = SaveNoiseTexture($"Gen_Tree_Leaf_{i + 1:00}.png", 0.12f, 0.45f, 0.18f, 0.1f);
                var trunkMat = SaveLitMaterial($"MAT_Gen_Tree_Trunk_{i + 1:00}.mat", trunkTex, smoothness: 0.1f);
                var leafMat = SaveLitMaterial($"MAT_Gen_Tree_Leaf_{i + 1:00}.mat", leafTex, smoothness: 0.15f);

                var root = new GameObject($"Gen_Tree_{i + 1:00}");
                try
                {
                    var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    trunk.name = "Trunk";
                    trunk.transform.SetParent(root.transform, false);
                    trunk.transform.localScale = new Vector3(0.22f, 0.9f + i * 0.15f, 0.22f);
                    trunk.transform.localPosition = new Vector3(0f, trunk.transform.localScale.y, 0f);
                    UnityEngine.Object.DestroyImmediate(trunk.GetComponent<Collider>());
                    trunk.GetComponent<MeshRenderer>().sharedMaterial = trunkMat;

                    var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    canopy.name = "Canopy";
                    canopy.transform.SetParent(root.transform, false);
                    canopy.transform.localScale = Vector3.one * (1.1f + i * 0.2f);
                    canopy.transform.localPosition = new Vector3(0f, 2.0f + i * 0.2f, 0f);
                    UnityEngine.Object.DestroyImmediate(canopy.GetComponent<Collider>());
                    canopy.GetComponent<MeshRenderer>().sharedMaterial = leafMat;

                    if (SavePrefabInstance($"tree/Gen_Tree_{i + 1:00}.prefab", root))
                        n++;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            return n;
        }

        static Mesh CreateGrassCrossMesh(float height)
        {
            var halfW = height * 0.35f;
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            var uvs = new System.Collections.Generic.List<Vector2>();

            for (var plane = 0; plane < 3; plane++)
            {
                var angle = plane * 60f * Mathf.Deg2Rad;
                var right = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var up = Vector3.up;
                var baseIdx = verts.Count;
                verts.Add(-right * halfW);
                verts.Add(right * halfW);
                verts.Add(right * halfW + up * height);
                verts.Add(-right * halfW + up * height);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                tris.Add(baseIdx);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx);
                tris.Add(baseIdx + 3);
                tris.Add(baseIdx + 2);
            }

            var mesh = new Mesh { name = "GenGrassCross" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateRockMesh(float radius, int seed)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                var mesh = UnityEngine.Object.Instantiate(sphere.GetComponent<MeshFilter>().sharedMesh);
                mesh.name = "GenRock";
                var verts = mesh.vertices;
                var rng = new System.Random(seed);
                for (var i = 0; i < verts.Length; i++)
                {
                    var v = verts[i];
                    var n = v.normalized;
                    var squash = 0.55f + (float)rng.NextDouble() * 0.25f;
                    verts[i] = new Vector3(v.x * radius, v.y * radius * squash, v.z * radius);
                }

                mesh.vertices = verts;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sphere);
            }
        }

        static Mesh CreateGrassClumpMesh(float height)
        {
            var meshes = new System.Collections.Generic.List<Mesh> { CreateGrassCrossMesh(height) };
            var merged = CreateGrassCrossMesh(height * 0.92f);
            meshes.Add(merged);
            return CreateGrassCrossMesh(height * 1.05f);
        }

        static void AttachPropAutomation(string prefabRel, string kind)
        {
            var path = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), prefabRel);
            if (!File.Exists(path))
                return;
            var root = PrefabUtility.LoadPrefabContents(prefabRel);
            try
            {
                if (root.GetComponent<EnvironmentAuthoringKit.Runtime.PlannerPropWindMotion>() == null)
                    root.AddComponent<EnvironmentAuthoringKit.Runtime.PlannerPropWindMotion>();

                if (root.transform.Find("Rig_Root") == null)
                {
                    var rig = new GameObject("Rig_Root");
                    rig.transform.SetParent(root.transform, false);
                    var bone = new GameObject("Bone_Wind");
                    bone.transform.SetParent(rig.transform, false);
                    var toReparent = new System.Collections.Generic.List<Transform>();
                    foreach (Transform child in root.transform)
                    {
                        if (child.name == "Rig_Root")
                            continue;
                        toReparent.Add(child);
                    }

                    foreach (var child in toReparent)
                        child.SetParent(bone.transform, true);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabRel);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Texture2D SaveNoiseTexture(
            string fileName, float r, float g, float b, float jitter, int seed = 0, int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(unchecked(fileName.GetHashCode() + seed));
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var j = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(r + j),
                        Mathf.Clamp01(g + j),
                        Mathf.Clamp01(b + j),
                        1f);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            var rel = $"{TextureRoot}/{fileName}";
            var abs = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), rel);
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(rel);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
        }

        static Texture2D SaveNormalTexture(string fileName, float strength, int seed)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var rng = new System.Random(unchecked(fileName.GetHashCode() + seed));
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var n = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.35f * strength;
                    var ny = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.35f * strength;
                    pixels[y * size + x] = new Color(0.5f + n, 0.5f + ny, 1f, 1f);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            var rel = $"{TextureRoot}/{fileName}";
            var abs = Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), rel);
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(rel);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
        }

        static Material SaveLitMaterial(
            string fileName,
            Texture2D albedo,
            Texture2D normal,
            float roughness,
            bool replace = false)
        {
            var smoothness = Mathf.Clamp01(1f - roughness);
            var rel = $"{MaterialRoot}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(rel);
            if (existing != null)
            {
                if (replace)
                {
                    if (albedo != null && existing.HasProperty("_BaseMap"))
                        existing.SetTexture("_BaseMap", albedo);
                    if (normal != null && existing.HasProperty("_BumpMap"))
                    {
                        existing.SetTexture("_BumpMap", normal);
                        existing.EnableKeyword("_NORMALMAP");
                    }

                    if (existing.HasProperty("_Smoothness"))
                        existing.SetFloat("_Smoothness", smoothness);
                    EditorUtility.SetDirty(existing);
                }

                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            if (albedo != null)
                mat.SetTexture("_BaseMap", albedo);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, rel);
            return mat;
        }

        static Material SaveLitMaterial(string fileName, Texture2D albedo, float smoothness, bool replace = false)
        {
            var rel = $"{MaterialRoot}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(rel);
            if (existing != null)
            {
                if (replace)
                {
                    if (albedo != null)
                    {
                        if (existing.HasProperty("_BaseMap"))
                            existing.SetTexture("_BaseMap", albedo);
                        else if (existing.HasProperty("_MainTex"))
                            existing.SetTexture("_MainTex", albedo);
                    }

                    if (existing.HasProperty("_Smoothness"))
                        existing.SetFloat("_Smoothness", smoothness);
                    EditorUtility.SetDirty(existing);
                }

                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            if (albedo != null)
                mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, rel);
            return mat;
        }

        static Mesh SaveMesh(string fileName, Mesh mesh, bool replace = false)
        {
            var rel = $"{MeshRoot}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(rel);
            if (existing != null && replace)
            {
                existing.Clear();
                existing.vertices = mesh.vertices;
                existing.triangles = mesh.triangles;
                existing.uv = mesh.uv;
                existing.RecalculateNormals();
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                return existing;
            }

            if (existing != null)
                return existing;
            AssetDatabase.CreateAsset(mesh, rel);
            return mesh;
        }

        static bool SavePrefab(string subPath, Mesh mesh, Material mat, Vector3 scale)
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(subPath));
            try
            {
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = mesh;
                mr.sharedMaterial = mat;
                go.transform.localScale = scale;
                return SavePrefabInstance(subPath, go);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static bool SavePrefabInstance(string subPath, GameObject tempRoot)
        {
            var rel = $"{PrefabRoot}/{subPath}";
            var dir = Path.GetDirectoryName(Path.Combine(CaveBuildCursorSettings.ResolveHubRoot(), rel));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(tempRoot, rel);
            return prefab != null;
        }
    }
}
#endif
