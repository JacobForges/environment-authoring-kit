namespace EnvironmentAuthoringKit.Cave
{
    /// <summary>How a cave mob behaves toward the player after spawn.</summary>
    public enum CaveMobAggression
    {
        /// <summary>Chases and attacks on sight.</summary>
        Aggressive = 0,
        /// <summary>Attacks when damaged or when player is very close.</summary>
        Defensive = 1,
        /// <summary>Idle until damaged; then fights back.</summary>
        Passive = 2,
    }
}
