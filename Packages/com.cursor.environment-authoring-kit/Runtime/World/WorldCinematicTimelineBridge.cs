using System;
using System.Reflection;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Plays Unity Timeline on a trigger when PlayableDirector is present (optional package).</summary>
    public static class WorldCinematicTimelineBridge
    {
        static Type _directorType;
        static MethodInfo _playMethod;

        public static bool TryPlayOn(WorldCinematicTrigger trigger)
        {
            if (trigger == null)
                return false;

            Resolve();
            if (_directorType == null || _playMethod == null)
                return false;

            var director = trigger.GetComponent(_directorType);
            if (director == null)
                return false;

            _playMethod.Invoke(director, null);
            return true;
        }

        static void Resolve()
        {
            if (_directorType != null)
                return;

            _directorType = Type.GetType("UnityEngine.Playables.PlayableDirector, Unity.Timeline");
            _playMethod = _directorType?.GetMethod("Play", Type.EmptyTypes);
        }
    }
}
