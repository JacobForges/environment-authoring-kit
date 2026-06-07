# CC0 characters & animations (primary source)

**Mixamo is optional.** This folder is filled by the agent — no Adobe login.

## Source

| Pack | License | Contents |
|------|---------|----------|
| [Quaternius Ultimate Animated Character Pack](https://opengameart.org/content/animated-characters-pack) | CC0 | 52 rigged FBX characters, each with locomotion animations |
| [Kenney Animated Characters 2](https://opengameart.org/content/animated-characters-2) | CC0 | 1 rig + idle/jump/run clips |

## No Blender required

CC0 zips include `.blend` sources; Hub uses **FBX/OBJ only**. After download, run `node strip-blend-sources.mjs` (or Unity menu **Strip Blender Sources**) so the Editor never calls Blender.

## Download again (agent / Terminal)

```bash
cd /Users/jacob/Hub/PlanV4-AssetReview
node download-cc0-characters.mjs
node link-cc0-slots.mjs
node download-cc0-items.mjs
node strip-blend-sources.mjs
```

## Unity (automatic)

On **FullWorld** build end (or menu **Window → Environment Kit → World → Import CC0 Characters & Items**):

- Humanoid rig + prefabs for all character slots
- Item prefabs for all manifest item IDs in `Items/`
- NPCs/enemies spawn as prefabs; loot uses item meshes (not spheres)

## Unity paths

- Per-slot characters: `CC0Imports/Characters/<slot>.fbx`
- Per-item meshes: `CC0Imports/Items/<id>.obj` (81 items)
- Prefabs: `CC0Imports/Prefabs/Characters/`, `Prefabs/Items/`
- Slot map: `CC0_CHARACTER_MAPPING.md`

## Audio, water, lava, cinematics (CC0)

```bash
node download-cc0-media.mjs
```

- **Music:** `Audio/` — explore + battle + theme (CC0, OpenGameArt)
- **Lava texture:** `Textures/lava_seamless.png` — wired to cave lava material
- **Surface water:** blue URP / Ignite water on `Water_*` during build
- **Cinematics:** world trigger volumes → camera orbit + subtitles (in-engine, not AI video)

Menu: **Window → Environment Kit → World → Apply FX + Music + Cinematics**

## Credit (optional)

Quaternius · Kenney.nl · OpenGameArt audio — not required for CC0.
