# Agent Studio — Cosmetics Editor & Store

## Purpose

Customize your squadmate agent's look **outside the game**. Cosmetics are lab-side flair that sync to `agent_appearance.json` in the checkpoint folder for future Unity import.

## Slots

| Slot | Examples |
|------|----------|
| **helmet** | Rail guard, signal dome |
| **visor** | HUD lens, scan band |
| **chassis** | Hull plating (default: Slate Chassis) |
| **trail** | Motion streak, particle color |
| **emblem** | Squad badge |
| **voice** | Announcer / comms pack |

## Agent Editor tab

1. Open **Agent Studio → Agent Editor**
2. Select a **slot tab**
3. Click an **owned** item to equip
4. **Unequip** clears the slot (chassis reverts to default)

Preview panel shows layered silhouette colors per equipped item.

## Cosmetic Store tab

1. Filter by slot or rarity (Common → Legendary)
2. Prices use **gold** and **gems** from your player profile (lab currency)
3. **Purchase** adds to inventory and deducts currency
4. Return to Editor to equip

Catalog seed: `catalog/cosmetics.json` (16 items).

## Persistence

```
~/Library/Application Support/DeepTrainAcademy/cosmetics.json
~/Library/Application Support/DeepTrainAcademy/checkpoints/{agentId}/agent_appearance.json
```

## API

See [API_REFERENCE.md](API_REFERENCE.md) — `/api/cosmetics/*` routes.

## Unity integration (optional)

`agent_appearance.json` is written on equip/save. Unity can read this on import to apply visuals in a future gameplay pass.
