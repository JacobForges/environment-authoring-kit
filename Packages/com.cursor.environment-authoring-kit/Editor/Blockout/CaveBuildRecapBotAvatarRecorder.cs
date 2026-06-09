#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Batchmode lip-sync avatar for recap overlay — Kenney male cyborg (half-human / half-AI).
    /// Unity -batchmode -executeMethod EnvironmentAuthoringKit.Editor.EnvironmentKitBatch.RenderRecapBotAvatar -quit
    /// Env: RECAP_BOT_ENVELOPE_JSON, RECAP_BOT_FRAME_DIR, optional RECAP_BOT_WIDTH (default 240).
    /// </summary>
    public static class CaveBuildRecapBotAvatarRecorder
    {
        /// <summary>Bump when host arm policy changes — log proves live Unity picked up scripts.</summary>
        public const int HostArmPolicyVersion = 14;
        const string ModelPath =
            "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Model/characterMedium.fbx";
        const string DefaultSkinPath =
            "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Skins/cyborgMaleA.png";
        const string IdlePath =
            "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Animations/idle.fbx";

        const int DefaultWidth = 320;
        static readonly Color EmeraldGlow = new(0.09f, 0.92f, 0.62f, 1f);

        public static void RenderFromEnvironment()
        {
            var envelopeJson = Environment.GetEnvironmentVariable("RECAP_BOT_ENVELOPE_JSON");
            var frameDir = Environment.GetEnvironmentVariable("RECAP_BOT_FRAME_DIR");
            var width = DefaultWidth;
            if (int.TryParse(Environment.GetEnvironmentVariable("RECAP_BOT_WIDTH"), out var w) && w > 64)
                width = w;

            if (string.IsNullOrWhiteSpace(envelopeJson) || string.IsNullOrWhiteSpace(frameDir))
            {
                Debug.LogError("[RecapBotAvatar] RECAP_BOT_ENVELOPE_JSON and RECAP_BOT_FRAME_DIR required.");
                EditorApplication.Exit(2);
                return;
            }

            if (!File.Exists(envelopeJson))
            {
                Debug.LogError("[RecapBotAvatar] Envelope JSON missing: " + envelopeJson);
                EditorApplication.Exit(2);
                return;
            }

            EnvelopeData data;
            try
            {
                data = JsonUtility.FromJson<EnvelopeData>(File.ReadAllText(envelopeJson));
            }
            catch (Exception ex)
            {
                Debug.LogError("[RecapBotAvatar] Bad envelope JSON: " + ex.Message);
                EditorApplication.Exit(2);
                return;
            }

            if (data?.envelope == null || data.envelope.Length == 0)
            {
                Debug.LogError("[RecapBotAvatar] Envelope empty.");
                EditorApplication.Exit(2);
                return;
            }

            Directory.CreateDirectory(frameDir);
            var offset = ReadFrameOffset();
            var ok = RenderFrameSequence(data, frameDir, width, offset);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Live Editor path (no batchmode) — used when Hub is already open.</summary>
        public static bool RenderEnvelopeFile(string envelopeJson, string frameDir, int width, int frameOffset)
        {
            if (!File.Exists(envelopeJson))
            {
                Debug.LogError("[RecapBotAvatar] Envelope JSON missing: " + envelopeJson);
                return false;
            }

            EnvelopeData data;
            try
            {
                data = JsonUtility.FromJson<EnvelopeData>(File.ReadAllText(envelopeJson));
            }
            catch (Exception ex)
            {
                Debug.LogError("[RecapBotAvatar] Bad envelope JSON: " + ex.Message);
                return false;
            }

            if (data?.envelope == null || data.envelope.Length == 0)
            {
                Debug.LogError("[RecapBotAvatar] Envelope empty.");
                return false;
            }

            Directory.CreateDirectory(frameDir);
            return RenderFrameSequence(data, frameDir, width, frameOffset);
        }

        static int ReadFrameOffset()
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("RECAP_BOT_FRAME_OFFSET"), out var off) && off >= 0)
                return off;
            return 0;
        }

        static bool RenderFrameSequence(EnvelopeData data, string frameDir, int width, int frameOffset)
        {
            EnsureKenneyAssetsImported();
            var skinPath = ResolveSkinPath();
            var modelPrefab = LoadModelPrefab(ModelPath);
            var skin = LoadSkinTexture(skinPath);
            var idleClip = LoadAnimationClip(IdlePath) ?? CreateIdleStubClip();
            if (modelPrefab == null || skin == null)
            {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(IdlePath)
                    .Select(a => a != null ? a.GetType().Name + ":" + a.name : "null");
                Debug.LogError(
                    "[RecapBotAvatar] Missing Kenney cyborg assets — " +
                    $"model={(modelPrefab != null)} skin={(skin != null)} " +
                    $"idleSubAssets=[{string.Join(", ", subAssets)}] " +
                    $"paths: {ModelPath}, {skinPath}, {IdlePath}");
                return false;
            }

            if (idleClip.name == "RecapIdleStub")
                Debug.LogWarning("[RecapBotAvatar] idle.fbx has no clips — using bind-pose stub (reimport Animations/idle.fbx).");

            var stage = new GameObject("RecapBotStage");
            RenderTexture rt = null;
            Camera camera = null;
            try
            {
                var bot = UnityEngine.Object.Instantiate(modelPrefab, stage.transform);
                bot.name = "JacobAdkinsCyborgBot";
                bot.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                // Face the viewer (camera sits on +Z looking toward origin).
                bot.transform.localRotation = Quaternion.Euler(-4f, -8f, 0f);
                bot.transform.localScale = Vector3.one * 1.05f;
                DisableStaticMeshes(bot);
                ApplyCyborgSkin(bot, skin);
                var head = FindHeadBone(bot.transform);
                var jaw = FindJawBone(bot.transform);
                var leftArm = FindBone(bot.transform, "armLeft", "Arm_L", "LeftArm", "upper_arm.L");
                var rightArm = FindBone(bot.transform, "armRight", "Arm_R", "RightArm", "upper_arm.R");
                var leftFore = FindBone(bot.transform, "forearmLeft", "ForeArm_L", "LeftForeArm", "forearm.L");
                var rightFore = FindBone(bot.transform, "forearmRight", "ForeArm_R", "RightForeArm", "forearm.R");
                var leftHand = FindBone(bot.transform, "handLeft", "Hand_L", "LeftHand", "hand.L");
                var rightHand = FindBone(bot.transform, "handRight", "Hand_R", "RightHand", "hand.R");
                if (rightArm == null || leftArm == null)
                    Debug.LogWarning("[RecapBotAvatar] Arm bones missing — host point gestures disabled.");

                var camGo = new GameObject("RecapBotCamera");
                camGo.transform.SetParent(stage.transform, false);
                camGo.transform.localPosition = new Vector3(0f, 0.88f, 2.35f);
                camGo.transform.localRotation = Quaternion.Euler(2f, 180f, 0f);
                camera = camGo.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 0.92f;
                camera.allowMSAA = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 12f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.allowHDR = false;

                // Floating presenter — transparent backdrop, studio rim only (no floor/stage).
                var keyGo = new GameObject("RecapBotKeyLight");
                keyGo.transform.SetParent(stage.transform, false);
                keyGo.transform.rotation = Quaternion.Euler(28f, -18f, 0f);
                var key = keyGo.AddComponent<Light>();
                key.type = LightType.Directional;
                key.intensity = 1.05f;
                key.color = new Color(0.94f, 0.98f, 1f);

                var rimGo = new GameObject("RecapBotRimLight");
                rimGo.transform.SetParent(stage.transform, false);
                rimGo.transform.rotation = Quaternion.Euler(-8f, 158f, 0f);
                var rim = rimGo.AddComponent<Light>();
                rim.type = LightType.Directional;
                rim.intensity = 0.38f;
                rim.color = EmeraldGlow;

                var fillGo = new GameObject("RecapBotFillLight");
                fillGo.transform.SetParent(stage.transform, false);
                fillGo.transform.rotation = Quaternion.Euler(6f, 192f, 0f);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional;
                fill.intensity = 0.34f;
                fill.color = new Color(0.88f, 0.94f, 1f);

                var accentGo = new GameObject("RecapBotAccentLight");
                accentGo.transform.SetParent(stage.transform, false);
                accentGo.transform.localPosition = new Vector3(0.35f, 1.35f, 1.6f);
                var accent = accentGo.AddComponent<Light>();
                accent.type = LightType.Point;
                accent.range = 4f;
                accent.intensity = 0.22f;
                accent.color = EmeraldGlow * 0.85f;

                rt = new RenderTexture(width, width, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear,
                };
                camera.targetTexture = rt;

                var idleLen = Mathf.Max(0.05f, idleClip.length);
                var frameCount = data.envelope.Length;

                var captureTex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                var armBindPose = CaptureBindPose(
                    leftArm, rightArm, leftFore, rightFore, leftHand, rightHand);
                LogArmBindDiagnostics(leftArm, rightArm, leftFore, rightFore, leftHand, rightHand);
                Debug.Log($"[RecapBotAvatar] Host arm policy v{HostArmPolicyVersion} (neutral sides, point milestone only)");
                AnimationMode.StartAnimationMode();
                try
                {
                    for (var fi = 0; fi < frameCount; fi++)
                    {
                        var amp = Mathf.Clamp01(data.envelope[fi]);
                        var globalFi = frameOffset + fi;
                        var phase = globalFi / (float)Mathf.Max(1, frameCount + frameOffset);
                        var bob = Mathf.Sin(phase * Mathf.PI * 2f) * 0.012f;
                        bot.transform.localPosition = new Vector3(0f, 0.02f + bob, 0f);

                        ApplyPresenterFace(head, jaw, amp, phase);
                        ApplyGameShowHostArms(
                            leftArm, rightArm, leftFore, rightFore, leftHand, rightHand,
                            globalFi, data.fps > 0 ? data.fps : 15f, amp, data.pointFrames,
                            armBindPose);

                        var path = Path.Combine(frameDir, $"f_{globalFi:D4}.png");
                        if (!CaptureFrame(camera, rt, captureTex, path))
                            return false;
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                    UnityEngine.Object.DestroyImmediate(captureTex);
                }

                Debug.Log($"[RecapBotAvatar] Kenney male cyborg ({skinPath}) — {frameCount} frames @ offset {frameOffset} → {frameDir}");
                return true;
            }
            finally
            {
                if (camera != null)
                    camera.targetTexture = null;
                if (rt != null && RenderTexture.active == rt)
                    RenderTexture.active = null;
                if (rt != null)
                    UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(stage);
            }
        }

        static void EnsureKenneyAssetsImported()
        {
            EnsureKenneyModelImporter(IdlePath, importAnimation: true);
            EnsureKenneyModelImporter(ModelPath, importAnimation: false);
            if (File.Exists(Path.GetFullPath(DefaultSkinPath)))
                AssetDatabase.ImportAsset(DefaultSkinPath, ImportAssetOptions.ForceSynchronousImport);
        }

        static void EnsureKenneyModelImporter(string assetPath, bool importAnimation)
        {
            if (!File.Exists(Path.GetFullPath(assetPath)))
                return;

            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
                return;

            var needsReimport = importer.importAnimation != importAnimation ||
                                importer.animationType != ModelImporterAnimationType.Generic;
            if (!needsReimport)
                return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = importAnimation;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            importer.SaveAndReimport();
        }

        static GameObject LoadModelPrefab(string assetPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
                return prefab;
            return AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
        }

        static string ResolveSkinPath()
        {
            var overridePath = Environment.GetEnvironmentVariable("RECAP_BOT_SKIN_PATH");
            if (string.IsNullOrWhiteSpace(overridePath) || !File.Exists(Path.GetFullPath(overridePath)))
            {
                var skinSidecar = Path.Combine(
                    EnvironmentKitDataRoot.ResolveRoot(),
                    ".recap-unity",
                    "recap-bot-skin-path.txt");
                if (File.Exists(skinSidecar))
                {
                    overridePath = File.ReadAllText(skinSidecar).Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(overridePath) &&
                File.Exists(Path.GetFullPath(overridePath)))
                return overridePath.Replace('\\', '/');

            const string generatedSkin =
                "Assets/EnvironmentKit/Generated/RecapBotHostSkin.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(generatedSkin) != null)
                return generatedSkin;

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultSkinPath) != null)
                return DefaultSkinPath;

            const string femaleFallback =
                "Assets/EnvironmentKit/CC0Imports/kenney-animated-characters-2/Skins/cyborgFemaleA.png";
            return femaleFallback;
        }

        static AnimationClip LoadAnimationClip(string assetPath)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__", StringComparison.Ordinal))
                .ToList();
            return clips.FirstOrDefault(c =>
                       c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? clips.FirstOrDefault();
        }

        static AnimationClip CreateIdleStubClip()
        {
            var clip = new AnimationClip { name = "RecapIdleStub", frameRate = 30f };
            clip.wrapMode = WrapMode.Loop;
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 2f, 0f));
            return clip;
        }

        static void DisableStaticMeshes(GameObject bot)
        {
            foreach (var mf in bot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.GetComponent<SkinnedMeshRenderer>() != null)
                    continue;
                UnityEngine.Object.DestroyImmediate(mf.gameObject);
            }
        }

        static void ApplyCyborgSkin(GameObject bot, Texture2D skin)
        {
            skin.anisoLevel = 8;
            skin.filterMode = FilterMode.Trilinear;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            foreach (var renderer in bot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mat = new Material(shader);
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", skin);
                else
                    mat.mainTexture = skin;

                // Jacob's Bot — warm friendly host, emerald cyborg accent.
                var tint = new Color(1f, 0.97f, 0.92f, 1f);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", tint);
                else
                    mat.color = tint;

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", EmeraldGlow * 0.18f);
                }

                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0.72f);
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", 0.12f);
                if (mat.HasProperty("_SpecularHighlights"))
                    mat.SetFloat("_SpecularHighlights", 1f);
                if (mat.HasProperty("_EnvironmentReflections"))
                    mat.SetFloat("_EnvironmentReflections", 0.35f);

                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static Texture2D LoadSkinTexture(string skinPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(skinPath);
            if (asset != null)
                return asset;

            var full = Path.GetFullPath(skinPath);
            if (!File.Exists(full))
                return null;

            var bytes = File.ReadAllBytes(full);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!tex.LoadImage(bytes))
                return null;

            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            return tex;
        }

        static Transform FindHeadBone(Transform root)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var name in new[] { "Head", "head", "mixamorig:Head", "neck" })
            {
                var hit = transforms.FirstOrDefault(t =>
                    t.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return hit;
            }

            return transforms.FirstOrDefault(t =>
                       t.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? root;
        }

        static Transform FindJawBone(Transform root)
        {
            return FindBone(root, "Jaw", "jaw", "lowerJaw", "mixamorig:Jaw", "chin");
        }

        static Transform FindBone(Transform root, params string[] names)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var name in names)
            {
                var hit = transforms.FirstOrDefault(t =>
                    t.name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return hit;
            }

            foreach (var name in names)
            {
                var token = name.Replace("_", "").Replace(".", "");
                var hit = transforms.FirstOrDefault(t =>
                    t.name.Replace("_", "").Replace(".", "")
                        .IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null)
                    return hit;
            }

            return null;
        }

        static void ApplyPresenterFace(Transform head, Transform jawBone, float amp, float phase)
        {
            if (head == null)
                return;

            var sampledRot = head.localRotation;
            var talkAmp = Mathf.Clamp01(Mathf.Pow(amp, 0.52f));
            // Friendly Jacob's Bot host — chin up, warm smile baseline.
            var smile = Quaternion.Euler(-26f + talkAmp * 7f, 4f, 4f);
            var talk = Quaternion.Euler(talkAmp * 10f, Mathf.Sin(phase * 28f) * talkAmp * 2.5f, 0f);
            var breathe = Quaternion.Euler(0f, 0f, Mathf.Sin(phase * Mathf.PI * 2f * 0.2f) * 0.35f);
            head.localRotation = sampledRot * smile * talk * breathe;

            if (jawBone == null)
                return;

            var sampledJaw = jawBone.localRotation;
            var jawOpen = Mathf.Clamp01(Mathf.Pow(amp, 0.45f)) * 24f;
            jawBone.localRotation = sampledJaw * Quaternion.Euler(jawOpen, 0f, 0f);
        }

        static bool IsMilestonePointFrame(int globalFi, int[] pointFrames, float fps)
        {
            if (pointFrames == null || pointFrames.Length == 0)
                return false;
            var hold = Mathf.Max(12, Mathf.RoundToInt(fps * 1.4f));
            foreach (var pf in pointFrames)
            {
                if (globalFi >= pf && globalFi < pf + hold)
                    return true;
            }

            return false;
        }

        static void ApplyHostNeutralArms(
            Transform leftArm,
            Transform rightArm,
            Transform leftFore,
            Transform rightFore,
            Transform leftHand,
            Transform rightHand,
            float talkSway)
        {
            // Kenney bind ships a hip-hand stance — drop arms and hide hands on REST/talk only.
            ApplyBoneDelta(leftArm, Quaternion.Euler(12f + talkSway, -2f, 36f));
            ApplyBoneDelta(rightArm, Quaternion.Euler(14f - talkSway, 4f, 46f));
            ApplyBoneDelta(leftFore, Quaternion.Euler(-6f, 0f, -4f));
            ApplyBoneDelta(rightFore, Quaternion.Euler(-8f, 0f, 0f));
            SetHandBoneScale(leftHand, 0f);
            SetHandBoneScale(rightHand, 0f);
        }

        static void SetHandBoneScale(Transform hand, float scale)
        {
            if (hand == null)
                return;
            hand.localScale = scale < 0.01f ? Vector3.zero : Vector3.one;
        }

        static void ApplyHostNaturalLeftArm(Transform leftArm, Transform leftFore, float talkSway)
        {
            ApplyBoneDelta(leftArm, Quaternion.Euler(12f + talkSway, -2f, 36f));
            ApplyBoneDelta(leftFore, Quaternion.Euler(-6f, 0f, -4f));
        }

        static void ApplyGameShowHostArms(
            Transform leftArm,
            Transform rightArm,
            Transform leftFore,
            Transform rightFore,
            Transform leftHand,
            Transform rightHand,
            int globalFi,
            float fps,
            float amp,
            int[] pointFrames,
            BindPoseSnapshot armBindPose)
        {
            var t = globalFi / Mathf.Max(1f, fps);
            var milestonePoint = IsMilestonePointFrame(globalFi, pointFrames, fps);
            var talkSway = Mathf.Sin(t * 3.1f) * amp * 1.5f;

            RestoreBindPose(armBindPose);

            if (milestonePoint)
            {
                SetHandBoneScale(leftHand, 0f);
                SetHandBoneScale(rightHand, 1f);
                ApplyHostNaturalLeftArm(leftArm, leftFore, talkSway);
                // Point up and toward the recap (screen upper-left from host corner).
                ApplyBoneDelta(rightArm, Quaternion.Euler(-74f, 42f, -64f));
                ApplyBoneDelta(rightFore, Quaternion.Euler(22f, -14f, -48f));
                ApplyBoneDelta(rightHand, Quaternion.Euler(-10f, -8f, -6f));
                return;
            }

            ApplyHostNeutralArms(
                leftArm, rightArm, leftFore, rightFore, leftHand, rightHand, talkSway);
        }

        static void ApplyBoneDelta(Transform bone, Quaternion delta)
        {
            if (bone == null)
                return;
            bone.localRotation = bone.localRotation * delta;
        }

        static void LogArmBindDiagnostics(
            Transform leftArm,
            Transform rightArm,
            Transform leftFore,
            Transform rightFore,
            Transform leftHand,
            Transform rightHand)
        {
            var lines = new List<string>();
            foreach (var bone in new[] { leftArm, rightArm, leftFore, rightFore, leftHand, rightHand })
            {
                if (bone == null)
                {
                    lines.Add("missing");
                    continue;
                }

                var e = bone.localRotation.eulerAngles;
                lines.Add($"{bone.name}: X={e.x:F1} Y={e.y:F1} Z={e.z:F1}");
            }

            var msg = "[RecapBotAvatar] Kenney arm bind — " + string.Join(" | ", lines);
            Debug.Log(msg);
            try
            {
                var scratch = Path.Combine(EnvironmentKitDataRoot.ResolveRoot(), ".recap-unity");
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "kenney-arm-bind.txt"), msg + "\n");
            }
            catch
            {
                // best-effort diagnostics only
            }
        }

        sealed class BindPoseSnapshot
        {
            public readonly Dictionary<Transform, Quaternion> LocalRotations = new();
            public readonly Dictionary<Transform, Vector3> LocalScales = new();
        }

        static BindPoseSnapshot CaptureBindPose(params Transform[] bones)
        {
            var snap = new BindPoseSnapshot();
            foreach (var bone in bones)
            {
                if (bone == null)
                    continue;
                snap.LocalRotations[bone] = bone.localRotation;
                snap.LocalScales[bone] = bone.localScale;
            }

            return snap;
        }

        static void RestoreBindPose(BindPoseSnapshot snap)
        {
            if (snap?.LocalRotations == null)
                return;
            foreach (var kv in snap.LocalRotations)
            {
                if (kv.Key != null)
                    kv.Key.localRotation = kv.Value;
            }

            if (snap.LocalScales == null)
                return;
            foreach (var kv in snap.LocalScales)
            {
                if (kv.Key != null)
                    kv.Key.localScale = kv.Value;
            }
        }

        static void ZeroTransparentRgb(Texture2D tex)
        {
            var pixels = tex.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 0.08f)
                    pixels[i] = Color.clear;
            }

            tex.SetPixels(pixels);
        }

        static void DefringeDarkHalos(Texture2D tex)
        {
            var pixels = tex.GetPixels();
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                var lum = p.r + p.g + p.b;
                if (p.a < 0.28f && lum < 0.22f)
                    pixels[i] = Color.clear;
            }

            tex.SetPixels(pixels);
        }

        /// <summary>Kenney FBX ships a full-width shadow disc — strip it for floating presenter.</summary>
        static void StripFullWidthGroundBands(Texture2D tex)
        {
            var w = tex.width;
            var h = tex.height;
            var pixels = tex.GetPixels();
            for (var y = 0; y < h; y++)
            {
                var opaque = 0;
                for (var x = 0; x < w; x++)
                {
                    if (pixels[y * w + x].a > 0.12f)
                        opaque++;
                }

                if (opaque < w * 0.72f)
                    continue;

                for (var x = 0; x < w; x++)
                    pixels[y * w + x] = Color.clear;
            }

            tex.SetPixels(pixels);
        }

        static bool CaptureFrame(Camera camera, RenderTexture rt, Texture2D tex, string path)
        {
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            try
            {
                GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
                camera.Render();
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                ZeroTransparentRgb(tex);
                StripFullWidthGroundBands(tex);
                DefringeDarkHalos(tex);
                ZeroTransparentRgb(tex);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[RecapBotAvatar] Capture failed: " + ex.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
        }

        [Serializable]
        sealed class EnvelopeData
        {
            public float[] envelope;
            public int fps = 15;
            public int[] pointFrames;
        }
    }
}
#endif
