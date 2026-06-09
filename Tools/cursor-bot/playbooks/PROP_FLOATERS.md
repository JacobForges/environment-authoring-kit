# PROP_FLOATERS

1. Layout audit: `propFloaterCount` > 0 or `blocksCaveQueue` from floaters.
2. Run `SurfacePropGroundLock.Apply` after vegetation pass.
3. Outliers: `CaveBuildPropSnapRepair` for remaining floaters above tolerance.
4. Re-run layout audit before cave queue proceeds.

Tag: `// [bot:rung:prop_floaters:DATE]`
