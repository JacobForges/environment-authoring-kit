using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>Places upright wall modules along tunnel segments (vertical faces, not stretched horizontal).</summary>
    internal static class CaveVerticalWallPlacer
    {
        const float SliceSpacing = 2.4f;
        const float ModuleHeightGuess = 3.8f;

        public static int PlaceTunnelWalls(
            Transform module,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            float halfWidth,
            float wallHeight,
            float segmentLength,
            ref int pieceCount)
        {
            var slices = Math.Max(1, (int)Math.Ceiling(segmentLength / SliceSpacing));
            var placed = 0;
            var scaleY = Mathf.Max(0.85f, wallHeight / ModuleHeightGuess);
            var wallScale = new Vector3(1.05f, scaleY, 1.02f);
            var halfLen = segmentLength * 0.48f;

            for (var i = 0; i < slices; i++)
            {
                var t = (i + 0.5f) / slices;
                var z = Mathf.Lerp(-halfLen, halfLen, t);

                placed += PlaceVerticalWall(module, catalog, rng, new Vector3(-halfWidth, wallHeight * 0.5f, z),
                    Quaternion.LookRotation(Vector3.right, Vector3.up), wallScale, "Wall_V_L", ref pieceCount);
                placed += PlaceVerticalWall(module, catalog, rng, new Vector3(halfWidth, wallHeight * 0.5f, z),
                    Quaternion.LookRotation(Vector3.left, Vector3.up), wallScale, "Wall_V_R", ref pieceCount);
            }

            var capScale = new Vector3(halfWidth * 2f / 5f, scaleY, 1.05f);
            placed += PlaceVerticalWall(module, catalog, rng, new Vector3(0f, wallHeight * 0.5f, -halfLen),
                Quaternion.LookRotation(Vector3.forward, Vector3.up), capScale, "Wall_Cap_A", ref pieceCount);
            placed += PlaceVerticalWall(module, catalog, rng, new Vector3(0f, wallHeight * 0.5f, halfLen),
                Quaternion.LookRotation(Vector3.back, Vector3.up), capScale, "Wall_Cap_B", ref pieceCount);

            return placed;
        }

        static int PlaceVerticalWall(
            Transform module,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 scale,
            string label,
            ref int pieceCount)
        {
            var before = pieceCount;
            PlaceWallSlice(module, catalog, rng, localPos, localRot, scale, ref pieceCount);
            return pieceCount > before ? 1 : 0;
        }

        public static void PlaceWallSlice(
            Transform module,
            LavaTubePrefabCatalog catalog,
            System.Random rng,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 scale,
            ref int pieceCount)
        {
            if (CavePrefabScatter.PlaceModule(module, catalog.Pick(catalog.Walls, rng), localPos, localRot, scale,
                    "Wall", false))
                pieceCount++;
        }
    }
}
