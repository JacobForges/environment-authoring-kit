#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// When Hub Unity is already open, recap avatar renders via request file (no batchmode compile/OOM).
    /// Request: {dataRoot}/.recap-unity/recap-bot-avatar-request.json
    /// </summary>
    [InitializeOnLoad]
    public static class CaveBuildRecapBotAvatarLiveBridge
    {
        const string RequestName = "recap-bot-avatar-request.json";
        const string DoneName = "recap-bot-avatar-done.json";
        const string ProcessingName = "recap-bot-avatar-request.processing";

        static double _nextPoll;

        static CaveBuildRecapBotAvatarLiveBridge()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlaying)
                return;
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.35;

            var scratch = Path.Combine(EnvironmentKitDataRoot.ResolveRoot(), ".recap-unity");
            var requestPath = Path.Combine(scratch, RequestName);
            if (!File.Exists(requestPath))
                return;

            var processingPath = Path.Combine(scratch, ProcessingName);
            try
            {
                // exFAT/external volumes: atomic File.Move often fails — copy then delete.
                File.WriteAllText(processingPath, File.ReadAllText(requestPath));
                File.Delete(requestPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RecapBotAvatar] Live request lock failed: {ex.Message}");
                return;
            }

            LiveAvatarRequest req;
            try
            {
                req = JsonUtility.FromJson<LiveAvatarRequest>(File.ReadAllText(processingPath));
            }
            catch (Exception ex)
            {
                WriteDone(scratch, "", false, "Bad request JSON: " + ex.Message, 0);
                TryDelete(processingPath);
                return;
            }

            if (req == null || string.IsNullOrWhiteSpace(req.envelopeJson) || string.IsNullOrWhiteSpace(req.frameDir))
            {
                WriteDone(scratch, req?.id ?? "", false, "Request missing envelopeJson or frameDir", 0);
                TryDelete(processingPath);
                return;
            }

            var width = req.width > 64 ? req.width : 240;
            var offset = Math.Max(0, req.frameOffset);
            if (!string.IsNullOrWhiteSpace(req.skinPath) && File.Exists(req.skinPath))
                Environment.SetEnvironmentVariable("RECAP_BOT_SKIN_PATH", req.skinPath);
            Debug.Log(
                $"[RecapBotAvatar] Live Editor render id={req.id} offset={offset} " +
                $"skin={(string.IsNullOrWhiteSpace(req.skinPath) ? "default" : req.skinPath)}…");

            var ok = false;
            var message = "";
            var frames = 0;
            try
            {
                ok = CaveBuildRecapBotAvatarRecorder.RenderEnvelopeFile(
                    req.envelopeJson, req.frameDir, width, offset);
                message = ok
                    ? $"[RecapBotAvatar] Live OK offset {offset} armPolicy=v{CaveBuildRecapBotAvatarRecorder.HostArmPolicyVersion}"
                    : "[RecapBotAvatar] Live render returned false";
                if (ok && File.Exists(req.envelopeJson))
                {
                    var env = JsonUtility.FromJson<EnvelopePayload>(File.ReadAllText(req.envelopeJson));
                    frames = env?.envelope?.Length ?? 0;
                }
            }
            catch (Exception ex)
            {
                message = "[RecapBotAvatar] Live exception: " + ex.Message;
                Debug.LogError(message);
            }

            WriteDone(scratch, req.id, ok, message, frames);
            TryDelete(processingPath);
        }

        static void WriteDone(string scratch, string id, bool ok, string message, int frameCount)
        {
            Directory.CreateDirectory(scratch);
            var done = new LiveAvatarDone
            {
                id = id ?? "",
                ok = ok,
                message = message ?? "",
                frameCount = frameCount,
                utc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(Path.Combine(scratch, DoneName), JsonUtility.ToJson(done, true));
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }

        [Serializable]
        sealed class LiveAvatarRequest
        {
            public string id;
            public string envelopeJson;
            public string frameDir;
            public int frameOffset;
            public int width = 240;
            public string skinPath;
        }

        [Serializable]
        sealed class LiveAvatarDone
        {
            public string id;
            public bool ok;
            public string message;
            public int frameCount;
            public string utc;
        }

        [Serializable]
        sealed class EnvelopePayload
        {
            public float[] envelope;
            public int fps = 15;
        }
    }
}
#endif
