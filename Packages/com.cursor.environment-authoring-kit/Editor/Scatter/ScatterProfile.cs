using System;
using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Scatter
{
    [CreateAssetMenu(fileName = "ScatterProfile", menuName = "Environment Kit/Scatter Profile")]
    public sealed class ScatterProfile : ScriptableObject
    {
        [Serializable]
        public sealed class ScatterEntry
        {
            public GameObject prefab;
            [Min(0f)] public float weight = 1f;
            public Vector2 scaleRange = new(0.8f, 1.2f);
            public bool randomYRotation = true;
            public bool alignToSurfaceNormal = true;
        }

        public List<ScatterEntry> entries = new();
        [Min(0f)] public float densityPerSquareMeter = 0.08f;
        [Min(0.1f)] public float brushRadius = 4f;
        [Range(0f, 90f)] public float minSlope = 0f;
        [Range(0f, 90f)] public float maxSlope = 45f;
        public float minHeight = -10000f;
        public float maxHeight = 10000f;
        public LayerMask surfaceMask = ~0;
        public bool usePlaceholderPrimitives = true;

        public GameObject PickWeighted(System.Random rng)
        {
            var valid = new List<ScatterEntry>();
            var total = 0f;
            foreach (var entry in entries)
            {
                if (entry?.prefab == null)
                    continue;
                valid.Add(entry);
                total += Mathf.Max(0.01f, entry.weight);
            }

            if (valid.Count == 0)
                return null;

            var roll = (float)rng.NextDouble() * total;
            foreach (var entry in valid)
            {
                roll -= Mathf.Max(0.01f, entry.weight);
                if (roll <= 0f)
                    return entry.prefab;
            }

            return valid[valid.Count - 1].prefab;
        }

        public bool TryGetEntryForPrefab(GameObject prefab, out ScatterEntry entry)
        {
            foreach (var e in entries)
            {
                if (e.prefab == prefab)
                {
                    entry = e;
                    return true;
                }
            }

            entry = null;
            return false;
        }
    }
}
