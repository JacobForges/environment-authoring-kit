using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using Hub.Competition;
using UnityEngine;

/// <summary>Third-person movement, look, dialog locks, and legacy pickup hook.</summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 2f;
    public float gravity = -18f;
    public float jumpSpeed = 6.5f;

    [Header("Camera")]
    public Transform cameraPivot;

    [HideInInspector] public bool dialogActive;
    [HideInInspector] public bool introActive;
    [HideInInspector] public bool uiMenuOpen;
    [HideInInspector] public bool chatInputActive;

    public bool IsDefending { get; private set; }

    CharacterController _controller;
    float _pitch;
    float _verticalVelocity;
    readonly List<string> _legacyInventory = new();

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        ResolveCameraPivot();
    }

    void Start()
    {
        if (_controller == null)
            _controller = GetComponent<CharacterController>();

        PlayerCameraRig.Ensure(transform);
        ResolveCameraPivot();
        DisableConflictingEnvKitController();
        PlayableHumanoidScale.Apply(transform);

        var inv = GetComponent<PlayerInventory>();
        if (inv != null && inv.UsedSlots == 0)
            inv.SeedStarterKit();
    }

    void Update()
    {
        if (PlayerUiInputCoordinator.ShouldBlockGameplay()
            || dialogActive || introActive || uiMenuOpen
            || IsCaveWarpPlaying())
        {
            IsDefending = false;
            if (uiMenuOpen)
                _verticalVelocity = 0f;
            chatInputActive = PlayerUiInputCoordinator.ShouldBlockGameplay()
                              || CompetitionChatPanel.BlocksInput;
            return;
        }

        chatInputActive = false;

        IsDefending = Input.GetMouseButton(1);
        HandleLook();
        HandleMovement();
    }

    public void Defend(bool defending) => IsDefending = defending;

    public void SetIntroLock(bool locked) => introActive = locked;

    /// <summary>Clear menu/cinematic locks after spawn or portal transitions.</summary>
    public void UnlockMovement()
    {
        introActive = false;
        dialogActive = false;
        uiMenuOpen = false;
        chatInputActive = false;

        if (_controller != null && !_controller.enabled
            && !EnvironmentAuthoringKit.World.PlayerSpawnGroundHold.IsActiveOn(transform))
            _controller.enabled = true;

        PlayerControllerLock.ExitUiPointerMode();
    }

    public void PickupItem(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return;

        var inv = GetComponent<PlayerInventory>();
        if (inv != null && inv.TryAddByName(itemName))
        {
            Debug.Log($"[Player] Added {itemName} to inventory.");
            return;
        }

        _legacyInventory.Add(itemName);
        Debug.Log($"[Player] Picked up {itemName} (legacy list). Total: {_legacyInventory.Count}");
    }

    void ResolveCameraPivot()
    {
        if (cameraPivot != null)
            return;

        var rig = GetComponent<PlayerCameraRig>();
        if (rig != null && rig.cameraPivot != null)
        {
            cameraPivot = rig.cameraPivot;
            return;
        }

        var pivot = transform.Find(PlayerCameraRig.PivotName);
        if (pivot != null)
            cameraPivot = pivot;
    }

    void HandleLook()
    {
        if (!Input.GetMouseButton(1))
            return;

        if (chatInputActive || CompetitionChatPanel.BlocksInput)
            return;

        var yaw = Input.GetAxis("Mouse X") * lookSensitivity;
        var pitchDelta = Input.GetAxis("Mouse Y") * lookSensitivity;
        if (Mathf.Abs(yaw) < 0.0001f && Mathf.Abs(pitchDelta) < 0.0001f)
            return;

        var rig = GetComponent<PlayerCameraRig>();
        if (rig != null)
        {
            rig.ApplyLookInput(yaw, -pitchDelta);
            return;
        }

        ResolveCameraPivot();
        if (cameraPivot == null)
            return;

        transform.Rotate(0f, yaw, 0f);
        _pitch = Mathf.Clamp(_pitch - pitchDelta, -80f, 80f);
        cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void HandleMovement()
    {
        if (_controller == null)
            _controller = GetComponent<CharacterController>();

        if (_controller == null || !_controller.enabled
            || chatInputActive || CompetitionChatPanel.BlocksInput)
            return;

        if (EnvironmentAuthoringKit.World.PlayerSpawnGroundHold.IsActiveOn(transform))
            return;

        var grounded = _controller.isGrounded;
        if (grounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        var moveX = Input.GetAxis("Horizontal");
        var moveZ = Input.GetAxis("Vertical");
        if (Mathf.Abs(moveX) >= 0.01f || Mathf.Abs(moveZ) >= 0.01f)
        {
            var rig = GetComponent<PlayerCameraRig>();
            var forward = rig != null ? rig.GetMovementForward() : transform.forward;
            var right = rig != null ? rig.GetMovementRight() : transform.right;
            var move = right * moveX + forward * moveZ;
            move = Vector3.ClampMagnitude(move, 1f) * moveSpeed;
            _controller.Move(move * Time.deltaTime);
        }

        if (grounded && Input.GetButtonDown("Jump"))
            _verticalVelocity = jumpSpeed;

        _verticalVelocity += gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    static bool IsCaveWarpPlaying()
    {
        var warp = Object.FindAnyObjectByType<CaveWarpTransition>();
        return warp != null && warp.IsPlaying;
    }

    void DisableConflictingEnvKitController()
    {
        var envKit = GetComponent<EnvironmentAuthoringKit.Cave.PlayerController>();
        if (envKit != null)
            envKit.enabled = false;
    }
}
