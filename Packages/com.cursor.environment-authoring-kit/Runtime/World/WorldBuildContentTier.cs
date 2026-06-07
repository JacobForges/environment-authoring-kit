namespace EnvironmentAuthoringKit.World
{
    /// <summary>Plan v4 — how much world content to spawn for this build.</summary>
    public enum WorldBuildContentTier
    {
        /// <summary>Guide NPC only; reduced loot; no boss stage load.</summary>
        Light = 0,

        /// <summary>~45% loot tables; tree proxy; minimal NPCs.</summary>
        Standard = 1,

        /// <summary>Full NPC layout, enemies, landmarks, loot tables.</summary>
        Aaa = 2,
    }
}
