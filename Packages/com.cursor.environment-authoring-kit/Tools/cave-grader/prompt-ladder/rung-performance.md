## Rung: performance

**Goal:** XR-friendly draw calls / colliders without removing gameplay features.

### Fix in kit code

- `LavaTubeCavePostProcess.ApplyPhysicsAndLod`, `CaveBlockTunnelRuntimeSetup`, cull distances.
- Reduce duplicate colliders and disabled renderers left from purges.
