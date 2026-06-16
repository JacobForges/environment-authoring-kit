# Model Market — Brain Marketplace

## Purpose

Download and manage **ONNX gameplay adapter** brains for your active agent. Up to **3 persistent slots** map to on-disk folders the companion and Unity both understand.

## Catalog sources

| Source | Description |
|--------|-------------|
| **bundled** | Ships with the game (`StreamingAssets/DTACompanion/models-catalog.json`) |
| **github** | `catalog/github-models.json` or `DTA_MODEL_CATALOG_URL` env |
| **huggingface** | Live search via HF API (optional `huggingFaceToken` for private repos) |
| **url** | Paste any direct `.onnx` resolve URL |

Click **Refresh catalog** to re-fetch.

## Using slots

1. Open **Model Market**
2. Select **Slot 1, 2, or 3** (highlighted card)
3. Click **Download → slot N** on any entry
4. Slot shows installed name + catalog id

Install path:

```
~/Library/Application Support/DeepTrainAcademy/model_slots/slot_{N}/gameplay_adapter.onnx
```

Also copies to agent checkpoint folder and Unity Competition path when `agentId` is set.

## Deploy to game

After installing to a slot:

1. Go to **Deploy** page, or
2. Install with active `agentId` — writes `pending_import.json`

Unity `CompetitionCheckpointAutoImport` imports on next game launch.

## Custom URLs

Entries marked **editable** accept a Hugging Face or GitHub raw URL:

```
https://huggingface.co/org/repo/resolve/main/gameplay_adapter.onnx
```

## Publishing new bundled brains

Edit `Assets/StreamingAssets/DTACompanion/models-catalog.json` in the Hub repo, rebuild the game, or add rows to `dta-companion/catalog/github-models.json`.
