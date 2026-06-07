using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Player-sized capsule or prefab avatar for route playtests.</summary>
    [DisallowMultipleComponent]
    public sealed class CavePlaytestBotAvatar : MonoBehaviour
    {
        public const string AvatarObjectName = "CavePlaytestBot_Avatar";

        [SerializeField] Color bodyColor = new(0.2f, 0.75f, 0.9f, 0.9f);
        [SerializeField] bool showDebugMesh = true;
        [SerializeField] GameObject visualPrefab;

        CharacterController _controller;
        CavePlaytestBotController _botController;
        Renderer _renderer;
        GameObject _visualInstance;

        public CharacterController Controller => _controller;
        public CavePlaytestBotController BotController => _botController;
        public float Height { get; private set; }
        public float Radius { get; private set; }

        public static CavePlaytestBotAvatar Ensure(
            Transform caveRoot,
            bool visible = true,
            GameObject prefabOverride = null)
        {
            if (caveRoot == null)
                return null;

            HumanoidDimensions.Resolve(out var height, out var radius, out var stepOffset);

            var existing = caveRoot.Find(AvatarObjectName);
            GameObject go;
            CavePlaytestBotAvatar avatar;
            if (existing != null)
            {
                go = existing.gameObject;
                avatar = go.GetComponent<CavePlaytestBotAvatar>();
                if (avatar == null)
                    avatar = go.AddComponent<CavePlaytestBotAvatar>();
            }
            else
            {
                go = new GameObject(AvatarObjectName);
                go.transform.SetParent(caveRoot, false);
                avatar = go.AddComponent<CavePlaytestBotAvatar>();
            }

            if (prefabOverride != null)
                avatar.visualPrefab = prefabOverride;

            if (go.GetComponent<CavePlaytestBotMarker>() == null)
                go.AddComponent<CavePlaytestBotMarker>();

            avatar.ConfigureBody(height, radius, stepOffset, visible);
            return avatar;
        }

        void ConfigureBody(float height, float radius, float stepOffset, bool visible)
        {
            Height = height;
            Radius = radius;

            EnsureVisualRoot(height, radius, visible);
            EnsureController(height, radius, stepOffset);
            _botController = GetComponent<CavePlaytestBotController>();
            if (_botController == null)
                _botController = gameObject.AddComponent<CavePlaytestBotController>();

            var layer = LayerMask.NameToLayer("Ignore Raycast");
            if (layer >= 0)
                gameObject.layer = layer;
        }

        void EnsureVisualRoot(float height, float radius, bool visible)
        {
            if (visualPrefab != null)
            {
                if (_visualInstance == null || _visualInstance.name != visualPrefab.name + "_Instance")
                {
                    if (_visualInstance != null)
                        Destroy(_visualInstance);

                    _visualInstance = Instantiate(visualPrefab, transform);
                    _visualInstance.name = visualPrefab.name + "_Instance";
                    _visualInstance.transform.localPosition = Vector3.zero;
                    _visualInstance.transform.localRotation = Quaternion.identity;
                }

                _renderer = GetComponent<Renderer>();
                if (_renderer != null)
                    _renderer.enabled = false;

                var anim = _visualInstance.GetComponentInChildren<Animator>();
                if (anim != null && anim.gameObject != _visualInstance)
                    anim.applyRootMotion = false;

                return;
            }

            if (_visualInstance != null)
            {
                Destroy(_visualInstance);
                _visualInstance = null;
            }

            if (GetComponent<MeshFilter>() == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                var mf = gameObject.AddComponent<MeshFilter>();
                var mr = gameObject.AddComponent<MeshRenderer>();
                mf.sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
            }

            transform.localScale = HumanoidDimensions.CapsuleLocalScale(height, radius);
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
            {
                _renderer.enabled = visible && showDebugMesh;
                if (visible && showDebugMesh)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    mat.color = bodyColor;
                    if (mat.HasProperty("_Smoothness"))
                        mat.SetFloat("_Smoothness", 0.35f);
                    _renderer.sharedMaterial = mat;
                }
            }
        }

        void EnsureController(float height, float radius, float stepOffset)
        {
            _controller = GetComponent<CharacterController>();
            if (_controller == null)
                _controller = gameObject.AddComponent<CharacterController>();

            _controller.height = height;
            _controller.radius = radius;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
            _controller.stepOffset = stepOffset;
            _controller.skinWidth = 0.05f;
        }

        public void TeleportTo(Vector3 worldPosition)
        {
            if (_controller != null)
                _controller.enabled = false;
            transform.position = worldPosition;
            if (_controller != null)
                _controller.enabled = true;
        }

        public void MoveToward(Vector3 worldTarget, float speed)
        {
            if (_botController != null)
            {
                _botController.walkSpeed = speed;
                _botController.runSpeed = speed * 1.65f;
                _botController.MoveTowardGoal(worldTarget, allowRun: true);
                return;
            }

            if (_controller == null)
            {
                var delta = worldTarget - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.02f)
                    transform.position += delta.normalized * (speed * Time.deltaTime);
                return;
            }

            var move = worldTarget - transform.position;
            move.y = 0f;
            if (move.sqrMagnitude > 0.02f)
                _controller.Move(move.normalized * (speed * Time.deltaTime));
        }
    }
}
