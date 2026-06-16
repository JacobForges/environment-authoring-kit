Deep Train Academy — DTA Training Companion (standalone)

This folder is a separate lab app that ships beside DeepTrainAcademy.app / .exe.
It is NOT embedded inside the game window.

HOW TO USE
1. Play the game normally (train your agent, collect samples).
2. Double-click:
   • macOS: Start-DTA-Companion.command
   • Windows: Start-DTA-Companion.bat
3. Your browser opens the training lab (coach, model market, agent studio, deploy).

AGENT STUDIO
• Agent Editor — equip cosmetics (helmet, visor, chassis, trail, emblem, voice) on your active agent.
• Cosmetic Store — buy items with in-lab gold/gems; appearance syncs to your checkpoint folder for the game.

LEGAL
© 2026 JacobForges. Deep Train Academy™ and DTA Training Companion™ are trademarks. See LICENSE in this folder.

REQUIREMENTS
• Node.js on your PATH (https://nodejs.org) — one-time install.
• Optional: your own Gemini API key in Settings (not Cursor, not the developer's keys).

MODEL FLEXIBILITY
• Up to 3 saved model slots in Model Market.
• Bundled ONNX brains ship with the game.
• Paste a Hugging Face or GitHub raw .onnx URL for custom weights.

SYNC WITH GAME
Deploy writes to:
  macOS: ~/Library/Application Support/DeepTrainAcademy/checkpoints/{agentId}/
  Windows: %LOCALAPPDATA%\DeepTrainAcademy\checkpoints\{agentId}\

The game auto-imports on next launch.

DEV (Jacob): from Hub repo run  cd dta-companion && npm run dev  → http://localhost:3000
