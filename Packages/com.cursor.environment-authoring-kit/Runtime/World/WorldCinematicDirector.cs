using System.Collections;
using System.Collections.Generic;
using EnvironmentAuthoringKit.Cave;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>
    /// Procedural in-engine cutscene: orbit camera, subtitles, optional music sting.
    /// Triggered by <see cref="WorldCinematicTrigger"/> volumes (not pre-rendered video).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldCinematicDirector : MonoBehaviour
    {
        public static WorldCinematicDirector Instance { get; private set; }

        [SerializeField] float defaultDuration = 7f;
        [SerializeField] float orbitSpeedDegrees = 28f;
        [SerializeField] float orbitRadius = 7f;
        [SerializeField] float orbitHeight = 2.5f;

        readonly HashSet<string> _played = new();
        Coroutine _active;
        Transform _focus;
        ThirdPersonFollowCamera _camera;
        FollowModeSnapshot _camSnap;

        struct FollowModeSnapshot
        {
            public Transform target;
            public ThirdPersonFollowCamera.FollowMode mode;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool TryPlay(WorldCinematicTrigger trigger)
        {
            if (trigger == null || _active != null)
                return false;

            var id = string.IsNullOrEmpty(trigger.cinematicId)
                ? trigger.gameObject.name
                : trigger.cinematicId;
            return TryPlayById(id, trigger.playOnce, trigger.duration, trigger.biomeHint, trigger.transform);
        }

        public bool TryPlayById(
            string cinematicId,
            bool playOnce = true,
            float duration = 0f,
            WorldSurfaceBiomeId biomeHint = WorldSurfaceBiomeId.PlayKarst,
            Transform focus = null)
        {
            if (_active != null || string.IsNullOrWhiteSpace(cinematicId))
                return false;

            if (playOnce && _played.Contains(cinematicId))
                return false;

            _active = StartCoroutine(PlayRoutineById(cinematicId, playOnce, duration, biomeHint, focus));
            return true;
        }

        IEnumerator PlayRoutineById(
            string id,
            bool playOnce,
            float duration,
            WorldSurfaceBiomeId biomeHint,
            Transform focusTransform)
        {
            if (playOnce)
                _played.Add(id);

            var player = GameObject.FindGameObjectWithTag("Player");
            var focusPos = focusTransform != null ? focusTransform.position : transform.position;
            if (player != null)
                focusPos = Vector3.Lerp(focusPos, player.transform.position, 0.35f);

            EnsureFocus(focusPos);
            BindCinematicCamera();

            var lines = WorldCinematicLines.Resolve(id, biomeHint);
            var playDuration = Mathf.Max(3f, duration > 0f ? duration : defaultDuration);
            WorldCinematicSubtitleOverlay.Show(lines.Length > 0 ? lines[0] : id);
            var elapsed = 0f;
            while (elapsed < playDuration)
            {
                elapsed += Time.deltaTime;
                if (_focus != null)
                {
                    var yaw = elapsed * orbitSpeedDegrees;
                    var orbit = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, orbitHeight, -orbitRadius);
                    _focus.position = focusPos + orbit;
                    _focus.LookAt(focusPos + Vector3.up * 1.6f);
                }

                if (lines.Length > 1 && elapsed > playDuration * 0.45f)
                    WorldCinematicSubtitleOverlay.Show(lines[1]);

                yield return null;
            }

            WorldCinematicSubtitleOverlay.Hide();
            RestoreCamera();
            _active = null;
        }

        void EnsureFocus(Vector3 world)
        {
            if (_focus == null)
            {
                var go = new GameObject("CinematicFocus");
                go.hideFlags = HideFlags.HideAndDontSave;
                _focus = go.transform;
            }

            _focus.position = world + Vector3.up * orbitHeight;
            _focus.rotation = Quaternion.LookRotation(world - _focus.position, Vector3.up);
        }

        void BindCinematicCamera()
        {
            _camera = FindAnyObjectByType<ThirdPersonFollowCamera>();
            if (_camera == null)
                return;

            _camSnap = new FollowModeSnapshot
            {
                target = _camera.Target,
                mode = _camera.Mode,
            };
            _camera.SetFollowTarget(_focus, ThirdPersonFollowCamera.FollowMode.AutoTrackTarget);
        }

        void RestoreCamera()
        {
            if (_camera == null)
                return;

            _camera.SetFollowTarget(_camSnap.target, _camSnap.mode);
        }
    }

    /// <summary>Generated subtitle lines per trigger id / biome.</summary>
    public static class WorldCinematicLines
    {
        public static string[] Resolve(string cinematicId, WorldSurfaceBiomeId biome)
        {
            if (!string.IsNullOrEmpty(cinematicId))
            {
                return cinematicId switch
                {
                    "intro_guide" => new[]
                    {
                        "The karst world opens before you.",
                        "Find the guide — the peaks hide older secrets.",
                    },
                    "boss_portal" => new[]
                    {
                        "The hollow Titan groans.",
                        "Step through when you are ready for the boss stage.",
                    },
                    "biome_foothill" => new[]
                    {
                        "Foothills — green relief after the playa.",
                        "Herbalists and rangers trade along these slopes.",
                    },
                    "biome_peak" => new[]
                    {
                        "Peak stone — guards and ore traders watch the rim.",
                        "The horizon thins to mist beyond.",
                    },
                    "biome_annex" => new[]
                    {
                        "The labyrinth annex twists below the map.",
                        "Something dead still walks here.",
                    },
                    _ => new[] { cinematicId.Replace('_', ' '), "Press on." },
                };
            }

            return biome switch
            {
                WorldSurfaceBiomeId.FoothillGreen => new[] { "Foothill green.", "Copper and herbs scatter the trails." },
                WorldSurfaceBiomeId.PeakStone => new[] { "Peak stone.", "Cold ore and colder watchers." },
                WorldSurfaceBiomeId.HorizonMist => new[] { "Horizon mist.", "The world fades at the edge." },
                WorldSurfaceBiomeId.AnnexLabyrinth => new[] { "Annex labyrinth.", "Do not lose the thread back." },
                _ => new[] { "A new region.", "Explore carefully." },
            };
        }
    }
}
