using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>Tracks active minable cave geometry for gameplay (quests, drops, analytics).</summary>
    [DisallowMultipleComponent]
    public sealed class CaveMiningRegistry : MonoBehaviour
    {
        public static CaveMiningRegistry Instance { get; private set; }

        readonly List<MinableRock> _active = new();

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Register(MinableRock rock)
        {
            if (rock != null && !_active.Contains(rock))
                _active.Add(rock);
        }

        public void Unregister(MinableRock rock) => _active.Remove(rock);

        public void NotifyMined(MinableRock rock, Vector3 worldPos)
        {
            Unregister(rock);
            // Hook: spawn pickups, update quests, achievements.
        }
    }
}
