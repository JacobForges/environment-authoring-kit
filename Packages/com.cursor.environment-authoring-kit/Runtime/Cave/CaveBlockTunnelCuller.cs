using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Enables block renderers/colliders only within a radius of the player (Minecraft-style view distance).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveBlockTunnelCuller : MonoBehaviour
    {
        [Header("Block visibility")]
        [Tooltip("When off, every block stays visible (recommended). Turn on only for very large caves on low-end hardware.")]
        public bool distanceCullingEnabled;

        [Tooltip("Render cave blocks within this distance of the player.")]
        public float renderRadius = 72f;

        [Tooltip("Keep physics colliders within this distance (usually smaller than render).")]
        public float collisionRadius = 48f;

        [Tooltip("How often to refresh visibility (seconds).")]
        public float refreshInterval = 0.18f;

        [Header("Target")]
        public Transform playerOverride;

        struct BlockEntry
        {
            public Transform transform;
            public Renderer renderer;
            public Collider collider;
            public bool renderOn;
            public bool colliderOn;
        }

        readonly List<BlockEntry> _blocks = new();
        Transform _target;
        float _nextRefresh;
        float _renderRadiusSq;
        float _collisionRadiusSq;

        void OnEnable()
        {
            _renderRadiusSq = renderRadius * renderRadius;
            _collisionRadiusSq = collisionRadius * collisionRadius;
            CacheBlocks();

            // In the Editor (not playing), never hide blocks — the cave is underground while the
            // player object is usually still at the surface, which made the whole tunnel invisible.
            if (!Application.isPlaying)
            {
                RestoreAllBlocks();
                return;
            }

            ResolveTarget();
            RefreshNow();
        }

        void OnDisable() => RestoreAllBlocks();

        void Update()
        {
            if (!Application.isPlaying || !distanceCullingEnabled)
                return;

            if (Time.time < _nextRefresh)
                return;

            _nextRefresh = Time.time + refreshInterval;
            if (_target == null)
                ResolveTarget();

            if (_target == null)
                return;

            RefreshVisibility(_target.position);
        }

        public void RebuildBlockCache()
        {
            CacheBlocks();
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (!Application.isPlaying || !distanceCullingEnabled)
            {
                RestoreAllBlocks();
                return;
            }

            if (_target == null)
                ResolveTarget();

            if (_target == null)
            {
                RestoreAllBlocks();
                return;
            }

            _renderRadiusSq = renderRadius * renderRadius;
            _collisionRadiusSq = collisionRadius * collisionRadius;
            RefreshVisibility(_target.position);
        }

        public void RestoreAllBlocks()
        {
            for (var i = 0; i < _blocks.Count; i++)
            {
                var entry = _blocks[i];
                if (entry.renderer != null)
                    entry.renderer.enabled = ShouldShowRenderer(entry.renderer.gameObject);
                if (entry.collider != null)
                    entry.collider.enabled = true;
                entry.renderOn = entry.renderer != null && entry.renderer.enabled;
                entry.colliderOn = true;
                _blocks[i] = entry;
            }
        }

        static bool ShouldShowRenderer(GameObject blockObject) =>
            blockObject != null && blockObject.name.StartsWith("CaveBlock_Minable");

        void CacheBlocks()
        {
            _blocks.Clear();
            var tunnelRoot = CaveGeometryPaths.FindBlockTunnel(transform);
            if (tunnelRoot == null)
                return;

            var marked = tunnelRoot.GetComponentsInChildren<CaveTunnelBlock>(true);
            if (marked.Length > 0)
            {
                foreach (var block in marked)
                {
                    if (block == null)
                        continue;

                    block.CacheReferences();
                    AddBlockEntry(block.transform, block.blockRenderer, block.blockCollider);
                }

                return;
            }

            foreach (var renderer in tunnelRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.gameObject.name.StartsWith("CaveBlock_"))
                    continue;

                AddBlockEntry(renderer.transform, renderer, renderer.GetComponent<Collider>());
            }
        }

        void AddBlockEntry(Transform blockTransform, Renderer renderer, Collider collider)
        {
            _blocks.Add(new BlockEntry
            {
                transform = blockTransform,
                renderer = renderer,
                collider = collider,
                renderOn = true,
                colliderOn = true
            });
        }

        void ResolveTarget()
        {
            if (playerOverride != null)
            {
                _target = playerOverride;
                return;
            }

            var controller = Object.FindAnyObjectByType<CharacterController>();
            if (controller != null)
            {
                _target = controller.transform;
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                _target = player.transform;
        }

        void RefreshVisibility(Vector3 worldPosition)
        {
            for (var i = 0; i < _blocks.Count; i++)
            {
                var entry = _blocks[i];
                if (entry.transform == null)
                    continue;

                var distSq = (entry.transform.position - worldPosition).sqrMagnitude;
                var showRender = distSq <= _renderRadiusSq;
                var showCollider = distSq <= _collisionRadiusSq;

                if (entry.renderer != null && showRender != entry.renderOn)
                {
                    entry.renderer.enabled = showRender && ShouldShowRenderer(entry.renderer.gameObject);
                    entry.renderOn = entry.renderer.enabled;
                }

                if (entry.collider != null && showCollider != entry.colliderOn)
                {
                    entry.collider.enabled = showCollider;
                    entry.colliderOn = showCollider;
                }

                _blocks[i] = entry;
            }
        }
    }

    /// <summary>Shared name with editor block tunnel root (runtime cannot reference editor types).</summary>
    public static class CaveBlockTunnelCullerDefaults
    {
        public const string BlockTunnelRootName = "BlockTunnel";
    }
}
