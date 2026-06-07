# Playable demo — acceptance criteria

**Goal:** A recruiter/portfolio **Play Mode** loop on top of a generated world — not commercial ship.

## Must pass (demoReady)

1. **Spawn** — Player at surface spawn; no fall-through; camera follows (`PlayerController` + `PlayerCameraRig`).
2. **Surface traverse** — Walk from spawn to cave portal without noclip (NavMesh or CharacterController on terrain/colliders).
3. **Portal enter** — Primary mouth / `PortalFive` transition into cave route.
4. **Cave traverse** — Walk route floor to **goal marker** (see G5).
5. **Combat** — At least one enemy damages player; player can damage/kill enemy via `CombatStats` (Assembly-CSharp).
6. **No cheat flags** — No debug teleport, god mode, or editor-only shortcuts required.

## Should (stretch)

- HUD: HP + simple objective text
- One pickup or minable block (`WorldItemInventory` hook)
- Hollow Titan visible on surface (landmark, not blocking demo path)

## Verify manually

1. Open `MainScene.unity`
2. Enter Play Mode
3. Complete steps 1–5 above
4. Set `HubGameProgress.json` → `"demoReady": true` and mark G1–G8 `"done"` (or ask agent to update after you confirm)

## Out of scope for demo

- XR device certification
- Full quest chain / inventory UI polish
- Grade ≥ 95 ship tier (separate EnvKit milestone)
