namespace EnvironmentAuthoringKit.Editor.Generation
{
    /// <summary>
    /// Controls whether a build pass generates open-sky surface world, underground cave, or both.
    /// Existing cave-only behavior is preserved when scope is <see cref="CaveOnly"/>.
    /// </summary>
    public enum SurfaceBuildScope
    {
        /// <summary>Radial surface (trails, water, mountains, cave mouth markers) then full cave pipeline.</summary>
        FullWorld = 0,

        /// <summary>Surface only — does not destroy or rebuild UndergroundCaveSystem.</summary>
        SurfaceOnly = 1,

        /// <summary>Cave only — uses existing surface terrain + <see cref="SurfaceWorldPaths"/> cave opening markers.</summary>
        CaveOnly = 2,
    }
}
