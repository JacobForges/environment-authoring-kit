# Pre-build rung: prior_cave_state

**Goal:** No stale onion/layered cave under ground anchor before rebuild.

## Fix

- [ ] **Window → Environment Kit → Remove Cave Layered Shells** if old cave exists.
- [ ] Delete legacy `CaveSystem` / layered shells under ground anchor.
- [ ] Do not run **Build Complete Cave** until pre-build gate passes.

User runs full build after pre-build workflow completes.
