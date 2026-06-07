using System.Reflection;
using UnityEngine;

namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>
    /// Kit-side <see cref="ICaveSpawnedEnemy"/> when Hub NpcEnemy does not implement the interface directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveSpawnedEnemyAdapter : MonoBehaviour, ICaveSpawnedEnemy
    {
        public void Configure(int seed)
        {
            var direct = GetComponents<MonoBehaviour>();
            foreach (var mb in direct)
            {
                if (mb == null || mb == this)
                    continue;
                if (mb is ICaveSpawnedEnemy spawned)
                {
                    spawned.Configure(seed);
                    return;
                }
            }

            foreach (var mb in GetComponents<MonoBehaviour>())
            {
                if (mb == null)
                    continue;
                var method = mb.GetType().GetMethod(
                    "Configure",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);
                if (method != null)
                {
                    method.Invoke(mb, new object[] { seed });
                    return;
                }
            }
        }
    }
}
