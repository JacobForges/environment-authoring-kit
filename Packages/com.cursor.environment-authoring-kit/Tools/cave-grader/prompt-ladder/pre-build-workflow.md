## Pre-build Cursor workflow (before cave geometry)

Unity runs this **before** `Build Complete Cave` when the weighted readiness ladder fails:

| Order | Phase | Cursor rung | What happens |
|-------|--------|-------------|----------------|
| 1 | Research | `research` | Papers + scene context — **plan table only** (no C#) |
| 2 | Plan | `plan` | Tie fixes to `CaveBuildPreBuildLadderReport.json` stages |
| 3 | Compile gate | `compile_gate` | Zero CS errors in package (retries up to 3×) |
| 4 | Readiness ladder | `package_tooling`, `scene_ground`, … | Up to 3 failing pre-build rungs |
| 5 | Proceed | (user) | Re-run pre-build gate or **Build Complete Cave Level** |

**Do not generate cave meshes in this workflow.** After `proceed_build`, the user builds the cave in Unity.

**Failure memory:** `CaveBuildAgentMemory.json` — do not repeat listed fingerprints.
