using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Scatter
{
    static class ScatterUtility
    {
        public static bool SlopeOk(Vector3 normal, ScatterProfile profile)
        {
            var slope = Vector3.Angle(Vector3.up, normal);
            return slope >= profile.minSlope && slope <= profile.maxSlope;
        }

        public static bool HeightOk(Vector3 position, ScatterProfile profile)
        {
            return position.y >= profile.minHeight && position.y <= profile.maxHeight;
        }
    }
}
