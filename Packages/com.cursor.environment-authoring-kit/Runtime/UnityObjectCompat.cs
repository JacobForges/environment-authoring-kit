using UnityEngine;

namespace EnvironmentAuthoringKit
{
    /// <summary>Unity 6+ object lookup / stable ids without deprecated APIs.</summary>
    public static class UnityObjectCompat
    {
        public static T FindAny<T>() where T : Object => Object.FindAnyObjectByType<T>();

        /// <summary>Stable int for spawn seeds and caches (replaces GetInstanceID).</summary>
        public static int ReferenceId(Object obj)
        {
            if (obj == null)
                return 0;
            return obj.GetEntityId().GetHashCode();
        }
    }
}
