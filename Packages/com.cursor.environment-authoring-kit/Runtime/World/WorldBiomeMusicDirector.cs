using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Cross-fades CC0 explore loops by nearest biome marker.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldBiomeMusicDirector : MonoBehaviour
    {
        [SerializeField] AudioClip playKarstClip;
        [SerializeField] AudioClip foothillClip;
        [SerializeField] AudioClip peakClip;
        [SerializeField] AudioClip horizonClip;
        [SerializeField] AudioClip annexClip;
        [SerializeField] AudioClip bossClip;
        [SerializeField] float crossfadeSeconds = 2.5f;
        [SerializeField] float pollInterval = 1.2f;

        AudioSource _a;
        AudioSource _b;
        bool _useA = true;
        float _nextPoll;
        WorldSurfaceBiomeId _current = (WorldSurfaceBiomeId)(-1);

        void Awake()
        {
            _a = gameObject.AddComponent<AudioSource>();
            _b = gameObject.AddComponent<AudioSource>();
            foreach (var src in new[] { _a, _b })
            {
                src.loop = true;
                src.volume = 0.35f;
                src.spatialBlend = 0f;
                src.playOnAwake = false;
            }

            TryPlay(WorldSurfaceBiomeId.PlayKarst, immediate: true);
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPoll)
                return;
            _nextPoll = Time.unscaledTime + pollInterval;

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
                return;

            var biome = WorldBiomeProbe.ResolveAt(player.transform.position);
            if (biome == _current)
                return;

            TryPlay(biome, immediate: false);
        }

        public void AssignClips(
            AudioClip play,
            AudioClip foothill,
            AudioClip peak,
            AudioClip horizon,
            AudioClip annex,
            AudioClip boss)
        {
            playKarstClip = play;
            foothillClip = foothill;
            peakClip = peak;
            horizonClip = horizon;
            annexClip = annex;
            bossClip = boss;
        }

        void TryPlay(WorldSurfaceBiomeId biome, bool immediate)
        {
            var clip = biome switch
            {
                WorldSurfaceBiomeId.FoothillGreen => foothillClip ?? playKarstClip,
                WorldSurfaceBiomeId.PeakStone => peakClip ?? playKarstClip,
                WorldSurfaceBiomeId.HorizonMist => horizonClip ?? playKarstClip,
                WorldSurfaceBiomeId.AnnexLabyrinth => annexClip ?? playKarstClip,
                WorldSurfaceBiomeId.BossThreshold => bossClip ?? peakClip ?? playKarstClip,
                _ => playKarstClip,
            };

            if (clip == null)
                return;

            _current = biome;
            var next = _useA ? _b : _a;
            var prev = _useA ? _a : _b;
            next.clip = clip;
            next.volume = immediate ? 0.35f : 0f;
            next.Play();
            if (immediate)
            {
                prev.Stop();
                _useA = !_useA;
                return;
            }

            StopAllCoroutines();
            StartCoroutine(Crossfade(prev, next));
            _useA = !_useA;
        }

        System.Collections.IEnumerator Crossfade(AudioSource from, AudioSource to)
        {
            var t = 0f;
            var fromStart = from.volume;
            while (t < crossfadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                var k = t / crossfadeSeconds;
                from.volume = Mathf.Lerp(fromStart, 0f, k);
                to.volume = Mathf.Lerp(0f, 0.35f, k);
                yield return null;
            }

            from.Stop();
            to.volume = 0.35f;
        }
    }
}
