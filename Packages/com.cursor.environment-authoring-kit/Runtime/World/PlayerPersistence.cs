using System;
using System.IO;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Attach to the player root — saves card + inventory on quit and optional checkpoints.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerPersistence : MonoBehaviour
    {
        const string SaveFolder = "Saves";

        [SerializeField] PlayerCardData card = new();

        public PlayerCardData Card => card;

        public static string SavePathForPlayer(string playerId)
        {
            var safe = string.IsNullOrWhiteSpace(playerId) ? "default" : playerId.Trim();
            return Path.Combine(Application.persistentDataPath, SaveFolder, safe + ".json");
        }

        void Awake()
        {
            if (card == null)
                card = new PlayerCardData();
            LoadIfPresent();
            SyncGrapplingHookFromInventory();
        }

        void OnApplicationQuit() => Save();
        void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
        }

        public void Save()
        {
            if (card == null)
                return;

            try
            {
                var path = SavePathForPlayer(card.playerId);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonUtility.ToJson(card, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerPersistence] Save failed: {ex.Message}", this);
            }
        }

        public bool LoadIfPresent()
        {
            if (card == null)
                card = new PlayerCardData();

            var path = SavePathForPlayer(card.playerId);
            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                JsonUtility.FromJsonOverwrite(json, card);
                SyncGrapplingHookFromInventory();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayerPersistence] Load failed: {ex.Message}", this);
                return false;
            }
        }

        public void Checkpoint() => Save();

        void SyncGrapplingHookFromInventory()
        {
            if (!PlayerGrapplingHookController.HasHookInInventory(card))
                return;

            var controller = GetComponent<PlayerGrapplingHookController>();
            if (controller == null)
                controller = gameObject.AddComponent<PlayerGrapplingHookController>();
            controller.SetEquipped(true);
        }
    }
}
