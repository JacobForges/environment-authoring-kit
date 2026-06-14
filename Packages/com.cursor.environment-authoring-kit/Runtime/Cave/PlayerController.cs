using UnityEngine;
using UnityEngine.EventSystems;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Third-person walk/run controller for surface + cave play. Uses legacy Input Manager axes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] float walkSpeed = 4.6f;
        [SerializeField] float runSpeed = 7.2f;
        [SerializeField] float gravity = -18f;
        [SerializeField] float jumpSpeed = 6.5f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 2.4f;
        [SerializeField] bool invertY;

        [Header("State (CavePlayerMovementGuard / cinematics)")]
        public bool introActive;
        public bool dialogActive;
        public bool chatInputActive;
        public Transform cameraPivot;

        [Header("Combat")]
        [SerializeField] float defendDamageMultiplier = 0.45f;

        CharacterController _controller;
        PlayerCameraRig _cameraRig;
        float _verticalVelocity;
        bool _defending;

        public bool IsDefending => _defending;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _cameraRig = GetComponent<PlayerCameraRig>();
            if (HasPortfolioPlayerController())
                enabled = false;
        }

        void Start()
        {
            if (_cameraRig == null)
                _cameraRig = PlayerCameraRig.Ensure(transform);

            if (cameraPivot == null && _cameraRig != null)
                cameraPivot = _cameraRig.cameraPivot;

            UnlockMovement();
        }

        void Update()
        {
            if (_controller == null || !_controller.enabled)
                return;

            if (introActive || dialogActive || chatInputActive || IsKeyboardCapturedByUi())
                return;

            HandleRespawnHotkey();
            HandleLook();
            HandleMove();
        }

        static bool IsKeyboardCapturedByUi()
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null)
                return false;

            var selected = es.currentSelectedGameObject;
            if (selected.GetComponentInParent<UnityEngine.UI.InputField>() != null)
                return true;

            foreach (var component in selected.GetComponentsInParent<Component>(true))
            {
                if (component != null && component.GetType().Name == "TMP_InputField")
                    return true;
            }

            return false;
        }

        void HandleRespawnHotkey()
        {
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                return;
            if (!Input.GetKeyDown(KeyCode.R))
                return;

            if (CaveMainAreaRespawn.TryRespawnPlayer(transform, "Shift+R surface respawn"))
                UnlockMovement();
        }

        void HandleLook()
        {
            if (_cameraRig == null || chatInputActive || !Input.GetMouseButton(1))
                return;

            var yaw = Input.GetAxis("Mouse X") * mouseSensitivity;
            var pitch = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
            if (Mathf.Abs(yaw) > 0.0001f || Mathf.Abs(pitch) > 0.0001f)
                _cameraRig.ApplyLookInput(yaw, pitch);
        }

        void HandleMove()
        {
            if (World.PlayerSpawnGroundHold.IsActiveOn(transform))
                return;

            var grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            var h = Input.GetAxisRaw("Horizontal");
            var v = Input.GetAxisRaw("Vertical");
            var input = new Vector3(h, 0f, v);
            if (input.sqrMagnitude > 1f)
                input.Normalize();

            var forward = _cameraRig != null ? _cameraRig.GetMovementForward() : transform.forward;
            var right = _cameraRig != null ? _cameraRig.GetMovementRight() : transform.right;
            var move = forward * input.z + right * input.x;

            var speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? runSpeed
                : walkSpeed;
            _controller.Move(move * speed * Time.deltaTime);

            if (grounded && Input.GetButtonDown("Jump"))
                _verticalVelocity = jumpSpeed;

            _verticalVelocity += gravity * Time.deltaTime;
            _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
        }

        public void Defend(bool defending) => _defending = defending;

        public float ApplyDefendMultiplier(float damage) =>
            _defending ? damage * defendDamageMultiplier : damage;

        public void UnlockMovement()
        {
            introActive = false;
            dialogActive = false;
            if (_controller != null && !_controller.enabled)
                _controller.enabled = true;
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = false;
        }

        bool HasPortfolioPlayerController()
        {
            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb == this)
                    continue;

                var type = mb.GetType();
                if (type.Name == "PlayerController" && type != typeof(PlayerController))
                    return true;
            }

            return false;
        }
    }
}
