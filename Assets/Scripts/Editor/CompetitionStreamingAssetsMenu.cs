#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hub.Editor
{
    static class CompetitionStreamingAssetsMenu
    {
        const string SyncScript = "Tools/competition-models/sync-to-streaming-assets.sh";
        const string WhisperBundleScript = "Tools/competition-voice/bundle-whisper-for-unity.sh";
        const string GameplayTrainer = "Tools/competition-models/train/gameplay_trainer.py";
        const string EpisodeBcTrainer = "Tools/competition-models/train/episode_bc_trainer.py";

        [MenuItem("Hub/Competition/Retrain Gameplay ONNX (synthetic BC)")]
        public static void RetrainGameplay()
        {
            var hub = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(hub))
                return;

            var trainer = Path.Combine(hub, GameplayTrainer);
            if (!File.Exists(trainer))
            {
                EditorUtility.DisplayDialog("Competition", "Missing " + GameplayTrainer, "OK");
                return;
            }

            var venvPython = Path.Combine(hub, "Tools/competition-models/.venv-competition-onnx/bin/python3");
            var python = File.Exists(venvPython) ? venvPython : "python3";
            var psi = new ProcessStartInfo(python, trainer)
            {
                WorkingDirectory = hub,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            var stdout = p?.StandardOutput.ReadToEnd() ?? string.Empty;
            var stderr = p?.StandardError.ReadToEnd() ?? string.Empty;
            p?.WaitForExit();
            if (p != null && p.ExitCode != 0)
            {
                UnityEngine.Debug.LogError("[Competition] Gameplay retrain failed:\n" + stderr);
                EditorUtility.DisplayDialog("Competition", "Gameplay retrain failed — see Console.", "OK");
                return;
            }

            UnityEngine.Debug.Log("[Competition] Gameplay retrain:\n" + stdout.Trim());
            Sync();
        }

        [MenuItem("Hub/Competition/Retrain Gameplay From Episodes (JSONL BC)")]
        public static void RetrainFromEpisodes()
        {
            var hub = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(hub))
                return;

            var trainer = Path.Combine(hub, EpisodeBcTrainer);
            if (!File.Exists(trainer))
            {
                EditorUtility.DisplayDialog(
                    "Competition",
                    "Missing " + EpisodeBcTrainer + " — run synthetic retrain or add episode BC trainer.",
                    "OK");
                return;
            }

            var venvPython = Path.Combine(hub, "Tools/competition-models/.venv-competition-onnx/bin/python3");
            var python = File.Exists(venvPython) ? venvPython : "python3";
            var psi = new ProcessStartInfo(python, trainer)
            {
                WorkingDirectory = hub,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            var stdout = p?.StandardOutput.ReadToEnd() ?? string.Empty;
            var stderr = p?.StandardError.ReadToEnd() ?? string.Empty;
            p?.WaitForExit();
            if (p != null && p.ExitCode != 0)
            {
                UnityEngine.Debug.LogError("[Competition] Episode BC retrain failed:\n" + stderr);
                EditorUtility.DisplayDialog("Competition", "Episode BC retrain failed — see Console.", "OK");
                return;
            }

            UnityEngine.Debug.Log("[Competition] Episode BC retrain:\n" + stdout.Trim());
            Sync();
        }

        [MenuItem("Hub/Competition/Bundle Whisper for Standalone")]
        public static void BundleWhisper()
        {
            var hub = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(hub))
                return;

            var script = Path.Combine(hub, WhisperBundleScript);
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("Competition", "Missing " + WhisperBundleScript, "OK");
                return;
            }

            var psi = new ProcessStartInfo("/bin/bash", script)
            {
                WorkingDirectory = hub,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            var stdout = p?.StandardOutput.ReadToEnd() ?? string.Empty;
            var stderr = p?.StandardError.ReadToEnd() ?? string.Empty;
            p?.WaitForExit();
            if (p != null && p.ExitCode != 0)
            {
                UnityEngine.Debug.LogError("[Competition] Whisper bundle failed:\n" + stderr);
                EditorUtility.DisplayDialog(
                    "Competition",
                    "Whisper bundle failed — see Console (cmake + git required on Mac).",
                    "OK");
                return;
            }

            AssetDatabase.Refresh();
            UnityEngine.Debug.Log("[Competition] Whisper bundle:\n" + stdout.Trim());
            EditorUtility.DisplayDialog(
                "Competition",
                "Whisper model + CLI copied to StreamingAssets.\nInclude in Mac .app / Windows .exe build.",
                "OK");
        }

        [MenuItem("Hub/Competition/Sync Models to Resources")]
        public static void Sync()
        {
            var hub = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(hub))
                return;

            var script = Path.Combine(hub, SyncScript);
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("Competition", "Missing " + SyncScript, "OK");
                return;
            }

            var psi = new ProcessStartInfo("/bin/bash", script)
            {
                WorkingDirectory = hub,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            var stdout = p?.StandardOutput.ReadToEnd() ?? string.Empty;
            p?.WaitForExit();
            ImportAllOnnxModels();
            UnityEngine.Debug.Log("[Competition] " + stdout.Trim());
        }

        [MenuItem("Hub/Competition/Reimport Model Assets")]
        public static void ReimportModels() => ImportAllOnnxModels();

        static void ImportAllOnnxModels()
        {
            var modelsDir = Path.Combine(Application.dataPath, "Resources", "Competition", "Models");
            if (!Directory.Exists(modelsDir))
            {
                UnityEngine.Debug.LogWarning("[Competition] Missing " + modelsDir);
                return;
            }

            var count = 0;
            foreach (var file in Directory.GetFiles(modelsDir, "*.onnx"))
            {
                var assetPath = "Assets/Resources/Competition/Models/" + Path.GetFileName(file);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnityEngine.Debug.Log($"[Competition] Reimported {count} ONNX → ModelAsset under Resources/Competition/Models");
        }
    }
}
#endif
