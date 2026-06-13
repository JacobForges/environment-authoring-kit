using System.Collections;
using EnvironmentAuthoringKit.Cave;
using Hub.Competition;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

public class PersistentPlayer : MonoBehaviour
{
    public static PersistentPlayer instance;

    [Serializable]
    public class BagSlotSave
    {
        public string displayName;
        public int stackCount;
    }

    [Serializable]
    public class PlayerMemory
    {
        public int level = 1;
        public string playerName = "Jacob";
        public Vector3 lastPosition;
        public int health = 100;
        public string lastScene = "MainScene";
        public BagSlotSave[] bagSlots = System.Array.Empty<BagSlotSave>();
    }

    [Header("UI References (Assign in Inspector)")]
    public Button saveButton, loadButton, resetButton, quitButton;
    public TMP_Dropdown slotDropdown;
    public GameObject savePromptPanel;
    public GameObject pauseMenuCanvas;
    public Camera playerCamera;
    public Camera pauseMenuCamera;

    [Header("Save Slot")]
    public int saveSlot = 0;

    public PlayerMemory memory = new PlayerMemory();

    /// <summary>Scene placement from the editor — used when no PlayerSpawn marker exists.</summary>
    Vector3 _sceneEditorSpawnPosition;
    bool _capturedSceneSpawn;

    private string SaveDir => Application.persistentDataPath;
    private const string FILE_PREFIX = "player_save_";
    private bool skipQuitSave = false;

    // --- LIFECYCLE ---

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        _sceneEditorSpawnPosition = transform.position;
        _capturedSceneSpawn = true;
        BindUI();
        PlayerCameraRig.Ensure(transform);
        if (playerCamera != null)
        {
            var rig = GetComponent<PlayerCameraRig>();
            if (rig != null)
                rig.playerCamera = playerCamera;
            rig?.AutoWire();
        }

        EnforceSingleAudioListener();
        GameplaySpawnGrounding.EnsurePlayableCollision(transform);
        GameplaySpawnGrounding.EnsureSpawnHold(transform);
        StartCoroutine(LoadAndSnapRoutine());
    }

    IEnumerator LoadAndSnapRoutine()
    {
        var load = LoadAsync();
        while (!load.IsCompleted)
            yield return null;

        GameplaySpawnGrounding.EnsurePlayableCollision(transform);
        yield return GameplaySpawnGrounding.PlaceAboveGroundWhenReady(transform);
    }

    void Update()
    {
        if (!PlayerUiInputCoordinator.ShouldBlockGameplay()
            && Input.GetKeyDown(KeyCode.R)
            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            Debug.Log("[PersistentPlayer] Shift+R reset+delete");
            DeleteSaveFile();
            ResetPlayerToSpawn();
        }

        // Esc pause menu is handled by PlayerUiInputCoordinator + GameplayPauseMenu.
    }

    static void EnforceSingleAudioListener()
    {
        var listeners = FindObjectsByType<AudioListener>();
        if (listeners.Length <= 1)
            return;

        AudioListener primary = null;
        if (instance != null && instance.playerCamera != null)
            primary = instance.playerCamera.GetComponent<AudioListener>();

        if (primary == null)
        {
            foreach (var listener in listeners)
            {
                if (!listener.isActiveAndEnabled)
                    continue;
                var cam = listener.GetComponent<Camera>();
                if (cam != null && cam.enabled)
                {
                    primary = listener;
                    break;
                }
            }
        }

        primary ??= listeners[0];

        foreach (var listener in listeners)
            listener.enabled = listener == primary;
    }

    void OnApplicationQuit()
    {
        if (!skipQuitSave)
            SaveData();
    }

    // --- SAVE/LOAD SYSTEM ---

    private string GetSavePath(int slot) => Path.Combine(SaveDir, $"{FILE_PREFIX}{slot}.dat");
    public void SaveAndQuit()
    {
        SaveData();
        HubApplicationQuit.GracefulQuitSkipLocalSave();
    }

    public void QuitWithoutSaving()
    {
        HubApplicationQuit.GracefulQuitSkipLocalSave();
    }
    public void SaveData()
    {
        memory.lastPosition = transform.position;
        memory.lastScene = SceneManager.GetActiveScene().name;
        CaptureInventoryToMemory();
        string json = JsonUtility.ToJson(memory, true);
        File.WriteAllText(GetSavePath(saveSlot), Encrypt(json));
        Debug.Log($"[PersistentPlayer] Saved slot {saveSlot}: {GetSavePath(saveSlot)}");
    }

    public async Task SaveAsync()
    {
        memory.lastPosition = transform.position;
        memory.lastScene = SceneManager.GetActiveScene().name;
        CaptureInventoryToMemory();
        string json = JsonUtility.ToJson(memory, true);
        await Task.Run(() => File.WriteAllText(GetSavePath(saveSlot), Encrypt(json)));
        Debug.Log($"[PersistentPlayer] (Async) Saved slot {saveSlot}: {GetSavePath(saveSlot)}");
    }

    public void LoadData()
    {
        string path = GetSavePath(saveSlot);
        if (!File.Exists(path))
        {
            Debug.Log("[PersistentPlayer] No save file; using defaults.");
            return;
        }
        string json = Decrypt(File.ReadAllText(path));
        memory = JsonUtility.FromJson<PlayerMemory>(json);
        ApplyData();
        Debug.Log($"[PersistentPlayer] Loaded slot {saveSlot}: {path}");
    }

    public async Task LoadAsync()
    {
        string path = GetSavePath(saveSlot);
        if (!File.Exists(path))
        {
            Debug.Log("[PersistentPlayer] No save file (async); using defaults.");
            return;
        }
        string json = await Task.Run(() => Decrypt(File.ReadAllText(path)));
        memory = JsonUtility.FromJson<PlayerMemory>(json);
        ApplyData();
        Debug.Log($"[PersistentPlayer] (Async) Loaded slot {saveSlot}: {path}");
    }

    public void DeleteSaveFile()
    {
        string path = GetSavePath(saveSlot);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[PersistentPlayer] Deleted save at {path}");
            memory = new PlayerMemory();
            skipQuitSave = true;
        }
        else
        {
            Debug.Log("[PersistentPlayer] No save file to delete.");
        }
    }

    private string Encrypt(string input) => XorEncryptDecrypt(input, 42);
    private string Decrypt(string input) => XorEncryptDecrypt(input, 42);

    private string XorEncryptDecrypt(string text, byte key)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] ^= key;
        return Encoding.UTF8.GetString(buffer);
    }

    // --- PLAYER STATE LOGIC ---

    private void ApplyData()
    {
        var pos = memory.lastPosition;
        if (IsObviouslyBadSpawnPosition(pos) || IsSavedPositionUnderCaveSystem(pos))
            MoveToSpawn();
        else
            transform.position = pos;

        RestoreInventoryFromMemory();
    }

    void CaptureInventoryToMemory()
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv?.bag == null || inv.bag.Length == 0)
        {
            memory.bagSlots = System.Array.Empty<BagSlotSave>();
            return;
        }

        var list = new System.Collections.Generic.List<BagSlotSave>(inv.bag.Length);
        foreach (var slot in inv.bag)
        {
            if (slot == null || string.IsNullOrEmpty(slot.displayName))
                continue;
            list.Add(new BagSlotSave
            {
                displayName = slot.displayName,
                stackCount = Mathf.Max(1, slot.stackCount),
            });
        }

        memory.bagSlots = list.ToArray();
    }

    void RestoreInventoryFromMemory()
    {
        var inv = GetComponent<PlayerInventory>();
        if (inv == null || memory.bagSlots == null || memory.bagSlots.Length == 0)
            return;

        for (var i = 0; i < inv.bag.Length; i++)
            inv.bag[i] = null;

        foreach (var saved in memory.bagSlots)
        {
            if (saved == null || string.IsNullOrEmpty(saved.displayName))
                continue;
            var item = InventoryItem.FromName(saved.displayName);
            item.stackCount = Mathf.Max(1, saved.stackCount);
            inv.TryAdd(item);
        }
    }

    public void ResetPlayerToSpawn()
    {
        memory = new PlayerMemory();
        MoveToSpawn();
        memory.lastPosition = transform.position;
        memory.lastScene = SceneManager.GetActiveScene().name;
        SaveData();
        skipQuitSave = true;
        Debug.Log("[PersistentPlayer] Reset to spawn & wiped save.");
    }

    static bool IsObviouslyBadSpawnPosition(Vector3 pos) =>
        pos.sqrMagnitude < 0.01f || pos.y < -25f || pos.y > 2000f;

    static bool IsUnderCaveSystem(Transform t)
    {
        if (t == null)
            return false;

        var lava = GameObject.Find(CaveEntranceTeleport.CaveSystemObjectName);
        return lava != null && t.IsChildOf(lava.transform);
    }

    /// <summary>Old broken builds saved the player underground — respawn on the surface instead.</summary>
    static bool IsSavedPositionUnderCaveSystem(Vector3 worldPos)
    {
        var lava = GameObject.Find(CaveEntranceTeleport.CaveSystemObjectName);
        if (lava == null)
            return false;

        var local = lava.transform.InverseTransformPoint(worldPos);
        return local.y < CaveGeometryPaths.UndergroundDepthMeters * 0.35f;
    }

    Transform ResolvePlayerSpawnMarker() => CaveMainAreaRespawn.ResolveSurfaceSpawn();

    void MoveToSpawn()
    {
        var marker = ResolvePlayerSpawnMarker();
        var start = marker != null
            ? marker.position
            : (_capturedSceneSpawn ? _sceneEditorSpawnPosition : transform.position);

        if (marker == null)
            Debug.LogWarning(
                "[PersistentPlayer] No PlayerSpawn tag or PlayerSpawnPoint object — using your character's scene position.",
                this);

        SnapPlayerToGround("Respawn", start);
        UnlockPlayerMovement();
    }

    static void UnlockPlayerMovement()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        var pc = player.GetComponent<PlayerController>();
        if (pc != null)
            pc.SetIntroLock(false);

        CavePlayerMovementGuard.UnlockMovement(player.transform);
    }

    void SnapPlayerToGround(string reason, Vector3? nearOverride = null)
    {
        GameplaySpawnGrounding.EnsurePlayableCollision(transform);
        var before = transform.position;

        if (nearOverride.HasValue)
        {
            var marker = nearOverride.Value;
            var markerTransform = ResolvePlayerSpawnMarker();
            transform.SetPositionAndRotation(
                new Vector3(marker.x, transform.position.y, marker.z),
                markerTransform != null ? markerTransform.rotation : transform.rotation);
        }

        GameplaySpawnGrounding.PlaceAboveGround(transform);
        GameplaySpawnGrounding.SnapHard(transform, 4);

        if ((transform.position - before).sqrMagnitude > 0.25f)
            Debug.Log($"[PersistentPlayer] Ground snap ({reason}) → {transform.position}", this);
    }

    public void HardResetAndReloadScene()
    {
        DeleteSaveFile();
        SceneManager.LoadScene("MainScene");
    }

    // --- UI BINDING ---

    private void BindUI()
    {
        saveButton?.onClick.AddListener(() => _ = SaveAsync());
        loadButton?.onClick.AddListener(() => _ = LoadAsync());
        resetButton?.onClick.AddListener(ResetPlayerToSpawn);
        
        quitButton?.onClick.AddListener(PromptSaveBeforeQuit);
        slotDropdown?.onValueChanged.AddListener(idx => { saveSlot = idx; Debug.Log($"[PersistentPlayer] Slot: {idx}"); });
    }



    public void PromptSaveBeforeQuit()
    {
        if (savePromptPanel != null)
            savePromptPanel.SetActive(true);
        if (pauseMenuCanvas != null)
            pauseMenuCanvas.SetActive(false); // hides menu, shows prompt
    }

    public void CancelQuitPrompt()
    {
        if (savePromptPanel != null)
            savePromptPanel.SetActive(false);
        if (pauseMenuCanvas != null)
            pauseMenuCanvas.SetActive(true); // optional: return user to pause menu
    }

}