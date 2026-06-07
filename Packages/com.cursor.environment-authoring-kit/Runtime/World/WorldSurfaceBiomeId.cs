namespace EnvironmentAuthoringKit.World
{
    /// <summary>FullWorld surface biomes — play, mixed transition ring, and ten concept presets.</summary>
    public enum WorldSurfaceBiomeId
    {
        PlayKarst = 0,

        // Legacy ring biomes (81-tile docs) — superseded by MixedTransition + ConceptPreset00–09.
        FoothillGreen = 1,
        PeakStone = 2,
        HorizonMist = 3,

        AnnexLabyrinth = 4,
        BossThreshold = 5,

        /// <summary>~72-tile blended ring (Chebyshev 2–4) surrounding play.</summary>
        MixedTransition = 6,

        ConceptPreset00 = 7,
        ConceptPreset01 = 8,
        ConceptPreset02 = 9,
        ConceptPreset03 = 10,
        ConceptPreset04 = 11,
        ConceptPreset05 = 12,
        ConceptPreset06 = 13,
        ConceptPreset07 = 14,
        ConceptPreset08 = 15,
        ConceptPreset09 = 16,
    }
}
