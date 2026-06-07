using UnityEngine;
using UnityEngine.Playables;

namespace EnvironmentAuthoringKit.World
{
    public sealed class WorldCinematicPlayableBehaviour : PlayableBehaviour
    {
        public string cinematicId = string.Empty;
        bool _played;

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            if (_played)
                return;
            _played = true;

            if (WorldCinematicDirector.Instance == null)
            {
                var go = new GameObject("WorldCinematicDirector");
                go.AddComponent<WorldCinematicDirector>();
            }

            WorldCinematicDirector.Instance.TryPlayById(cinematicId);
        }
    }
}
