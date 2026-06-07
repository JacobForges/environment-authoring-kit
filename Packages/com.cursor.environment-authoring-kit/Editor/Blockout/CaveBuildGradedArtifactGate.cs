#if UNITY_EDITOR
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// One graded task at a time — freeze other tracks until the active rung completes or fails reprompt.
    /// </summary>
    public static class CaveBuildGradedArtifactGate
    {
        public enum Track
        {
            None = 0,
            Terrain = 1,
            Mountain = 2,
            Prop = 3,
            HollowTitan = 4,
        }

        static Track _activeTrack = Track.None;
        static string _activeRungId = string.Empty;

        public static Track ActiveTrack => _activeTrack;
        public static string ActiveRungId => _activeRungId;

        public static void Reset() => Release(Track.None, string.Empty);

        public static bool TryBegin(Track track, string rungId)
        {
            if (string.IsNullOrEmpty(rungId))
                return false;

            if (_activeTrack != Track.None &&
                (_activeTrack != track || _activeRungId != rungId))
            {
                Debug.LogWarning(
                    $"[CaveBuild] Graded artifact gate busy — {_activeTrack}/{_activeRungId}; " +
                    $"cannot start {track}/{rungId}.");
                return false;
            }

            _activeTrack = track;
            _activeRungId = rungId;
            return true;
        }

        public static void Release(Track track, string rungId)
        {
            if (_activeTrack == track && (_activeRungId == rungId || string.IsNullOrEmpty(rungId)))
            {
                _activeTrack = Track.None;
                _activeRungId = string.Empty;
            }
        }

        public static void CompleteAndRelease(Track track, string rungId, int seed)
        {
            if (_activeTrack == track && _activeRungId == rungId)
            {
                CaveBuildPhaseContractRegistry.MarkRungComplete(rungId, seed);
                Release(track, rungId);
            }
        }
    }
}
#endif
