using System;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    [Serializable]
    public struct CavePathKnot
    {
        public Vector3 Position;
        public float RadiusX;
        public float RadiusY;
        public bool IsChamber;

        public CavePathKnot(Vector3 position, float radiusX, float radiusY, bool isChamber = false)
        {
            Position = position;
            RadiusX = radiusX;
            RadiusY = radiusY;
            IsChamber = isChamber;
        }
    }
}
