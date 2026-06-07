using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.Serialization;

namespace EnvironmentAuthoringKit.World
{
    public sealed class WorldCinematicPlayableAsset : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] string cinematicId = string.Empty;
        [FormerlySerializedAs("duration")]
        [SerializeField] float durationSeconds = 6f;

        public override double duration => durationSeconds;

        public string CinematicId => cinematicId;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<WorldCinematicPlayableBehaviour>.Create(graph);
            playable.GetBehaviour().cinematicId = cinematicId;
            return playable;
        }
    }
}
