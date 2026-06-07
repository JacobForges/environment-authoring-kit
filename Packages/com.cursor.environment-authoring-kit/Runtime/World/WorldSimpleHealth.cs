using System;
using EnvironmentAuthoringKit;
using UnityEngine;

namespace EnvironmentAuthoringKit.World
{
    /// <summary>Fallback HP when Hub CombatStats is absent; fires <see cref="Died"/> once at zero.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldSimpleHealth : MonoBehaviour
    {
        public int maxHp = 36;
        public int currentHp = 36;
        public WorldSurfaceBiomeId lootBiome = WorldSurfaceBiomeId.PlayKarst;
        public int lootSeed;

        public event Action<WorldSimpleHealth> Died;

        bool _dead;

        void Awake()
        {
            if (lootSeed == 0)
                lootSeed = UnityObjectCompat.ReferenceId(this);
            if (currentHp <= 0)
                currentHp = maxHp;
        }

        public void TakeDamage(int amount)
        {
            if (_dead || amount <= 0)
                return;

            currentHp = Mathf.Max(0, currentHp - amount);
            if (currentHp <= 0)
                Die();
        }

        void Die()
        {
            if (_dead)
                return;
            _dead = true;
            Died?.Invoke(this);
        }
    }
}
