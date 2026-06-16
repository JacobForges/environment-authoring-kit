using System;
using System.IO;
using UnityEngine;

namespace DTA.Hub.AutoImport
{
    /// <summary>
    /// Reference copy — canonical implementation lives in Hub at
    /// Assets/Scripts/Competition/Transfer/CompetitionCheckpointAutoImport.cs
    /// </summary>
    public class CompetitionCheckpointAutoImport : MonoBehaviour
    {
        [Header("Agent Manifest Settings")]
        [Tooltip("The unique Agent UUID matching the DTA Training Companion.")]
        public string activeAgentId = "usr_agent_scout_01";

        [Header("Local Asset Paths relative to streaming or persistent assets")]
        [Tooltip("Direct asset target for game models. Will reload into Sentis runtime.")]
        public string targetModelSubPath = "Competition/Agents/{0}/models/gameplay_adapter.onnx";

        private string syncFolderPath;
        private string pendingImportPath;
        private string targetOnnxPath;

        [System.Serializable]
        public class PendingImportManifest
        {
            public int checkpointVersion;
            public string agentId;
            public string sha256;
            public string trainUtc;
            public string focusActivity;
            public float trainAccuracy;
            public string onnxFileName;
        }

        private void Start()
        {
            InitializePaths();
            CheckAndImportCheckpoint();
        }

        private void InitializePaths()
        {
            // Resolve OS environment paths
#if UNITY_STANDALONE_WIN
            string baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            syncFolderPath = Path.Combine(baseFolder, "DeepTrainAcademy", "checkpoints", activeAgentId);
#elif UNITY_STANDALONE_OSX
            string baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
            syncFolderPath = Path.Combine(baseFolder, "DeepTrainAcademy", "checkpoints", activeAgentId);
#else
            // Fallback to persistent data path for other platforms (e.g., Linux, mobile test)
            syncFolderPath = Path.Combine(Application.persistentDataPath, "DeepTrainAcademy", "checkpoints", activeAgentId);
#endif

            pendingImportPath = Path.Combine(syncFolderPath, "pending_import.json");
            
            // Unity editor target vs standalone build path
#if UNITY_EDITOR
            targetOnnxPath = Path.Combine(Application.dataPath, string.Format(targetModelSubPath, activeAgentId));
#else
            targetOnnxPath = Path.Combine(Application.persistentDataPath, "Agents", activeAgentId, "gameplay_adapter.onnx");
#endif

            Debug.Log($"[DTA Hub Auto-Importer] Checking sync directory: '{syncFolderPath}'");
            Debug.Log($"[DTA Hub Auto-Importer] Target model destination: '{targetOnnxPath}'");
        }

        public void CheckAndImportCheckpoint()
        {
            if (!File.Exists(pendingImportPath))
            {
                Debug.Log("[DTA Hub Auto-Importer] No pending_import.json found. System model is synchronized.");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(pendingImportPath);
                PendingImportManifest manifest = JsonUtility.FromJson<PendingImportManifest>(jsonContent);

                if (manifest == null)
                {
                    Debug.LogError("[DTA Hub Auto-Importer] Failed to parse pending_import.json. Manifest is empty or corrupt.");
                    return;
                }

                if (manifest.agentId != activeAgentId)
                {
                    Debug.LogWarning($"[DTA Hub Auto-Importer] Pending code agent ID '{manifest.agentId}' does not match active game agent '{activeAgentId}'. Skipping sync.");
                    return;
                }

                string sourceOnnxPath = Path.Combine(syncFolderPath, manifest.onnxFileName ?? "gameplay_adapter.onnx");

                if (!File.Exists(sourceOnnxPath))
                {
                    Debug.LogError($"[DTA Hub Auto-Importer] ONNX File not found at source path: '{sourceOnnxPath}' despite manifest instructions.");
                    return;
                }

                // Ensure parent directory exists in Target destination
                string targetDir = Path.GetDirectoryName(targetOnnxPath);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Atomic-imitated file write
                File.Copy(sourceOnnxPath, targetOnnxPath, true);
                Debug.Log($"[DTA Hub Auto-Importer] SUCCESS: Core brain imported! Version {manifest.checkpointVersion} ({manifest.focusActivity}) is now live in Sentis runtime.");

                // Show screen toast if a UI Canvas helper exists (Console fallback)
                Debug.Log($"[DTA] Companion checkpoint v{manifest.checkpointVersion} loaded successfully.");
                
                // Clear the pending import flag so we do not import repeatedly on every reboot
                File.Delete(pendingImportPath);
                Debug.Log("[DTA Hub Auto-Importer] Cleared pending_import.json flag. Ready for the next companion compile job.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DTA Hub Auto-Importer] CRITICAL Sync Failure: {ex.Message}");
            }
        }
    }
}
