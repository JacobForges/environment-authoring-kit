using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    public enum BlockoutPrimitiveKind
    {
        Cube,
        Ramp,
        Cylinder,
        Plane,
        Wall
    }

    static class BlockoutSettings
    {
        public static BlockoutPrimitiveKind SelectedPrimitive = BlockoutPrimitiveKind.Cube;
        public static Color CubeColor = new(0.55f, 0.55f, 0.55f);
        public static Color RampColor = new(0.5f, 0.52f, 0.58f);
        public static Color CylinderColor = new(0.45f, 0.5f, 0.55f);
        public static Color PlaneColor = new(0.42f, 0.44f, 0.46f);
        public static Color WallColor = new(0.7f, 0.25f, 0.25f);

        public static Color CaveWallColor = new(0.22f, 0.2f, 0.18f);
        public static Color CaveFloorColor = new(0.28f, 0.24f, 0.2f);
        public static Color CaveRockColor = new(0.35f, 0.32f, 0.28f);
        public static Color StalactiteColor = new(0.45f, 0.42f, 0.38f);
    }
}
