#!/usr/bin/env python3
"""Train competition_chat_intent.onnx — player text → command + social intents."""
from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
TRAIN = Path(__file__).resolve().parent
sys.path.insert(0, str(TRAIN))
sys.path.insert(0, str(ROOT))

from text_encoder import FEATURE_DIM, encode_text  # noqa: E402

INTENTS = [
  # Commands (owner → agent)
  "cmd_hold",
  "cmd_follow",
  "cmd_rush",
  "cmd_attack",
  "cmd_defend",
  "cmd_skill",
  "cmd_cancel",
  # Chat / query
  "greet",
  "status",
  "strategy",
  "encourage",
  "taunt",
  "learned",
  "hurt",
  "victory",
  "stuck",
  "social_local",
  "unknown",
]

# Paraphrase seeds per intent (synthetic training corpus).
PHRASES: dict[str, list[str]] = {
  "cmd_hold": [
    "hold", "stop", "stay", "wait here", "don't move", "freeze", "halt",
    "stay put", "plant here", "stop moving",
  ],
  "cmd_follow": [
    "follow me", "come with me", "stay on me", "stick with me",
    "behind me", "trail me", "come here",
  ],
  "cmd_rush": [
    "rush", "go go go", "sprint", "hurry", "full speed", "run fast",
    "push forward", "speed run",
  ],
  "cmd_attack": [
    "attack", "fight", "engage", "hit them", "strike", "kill it",
    "take them down", "melee",
  ],
  "cmd_defend": [
    "defend", "block", "guard", "shield up", "protect yourself",
    "parry", "brace",
  ],
  "cmd_skill": [
    "use skill", "skill 1", "skill 2", "ability", "cast", "use your power",
    "q skill", "hotbar 1",
  ],
  "cmd_cancel": [
    "cancel", "never mind", "stop that", "abort", "clear order", "stand down",
    "forget it", "release",
  ],
  "greet": [
    "hi", "hello", "hey", "yo", "good morning", "what's up", "howdy",
    "sup", "hey bot", "hi there", "hello agent",
  ],
  "status": [
    "how are you", "how are you?", "hp?", "health", "status report", "stamina", "how hurt",
    "what's your hp", "are you ok", "condition", "status",
  ],
  "strategy": [
    "what's the plan", "strategy", "which way", "next checkpoint", "route?",
    "where should we go", "plan", "tactics",
  ],
  "encourage": [
    "you got this", "nice", "good job", "keep going", "well done", "let's go",
    "cheer up", "we can do it",
  ],
  "taunt": [
    "trash talk", "you're slow", "bet you can't", "weak", "loser", "too slow bot",
    "catch up", "roast",
  ],
  "learned": [
    "what did you learn", "memory", "remember anything", "knowledge", "what do you know",
    "recall", "lessons", "facts",
  ],
  "hurt": [
    "ouch", "you're hurt", "low hp", "dying", "critical", "that looked painful",
    "you took damage",
  ],
  "victory": [
    "we won", "goal", "finished", "victory", "nice run", "checkpoint cleared",
    "made it", "success",
  ],
  "stuck": [
    "stuck", "can't move", "help stuck", "wedged", "snagged", "path blocked",
    "you're stuck",
  ],
  "social_local": [
    "over here", "psst", "nearby", "to the agent beside me", "tell them hi",
    "whisper", "local chat", "say to nearby",
  ],
  "unknown": [
    "asdf qwerty", "blah blah", "random words", "???", "hmm", "idk",
    "something something", "lorem ipsum",
  ],
}

NOISE_SUFFIXES = ["", "!", "?", " please", " now", "...", " ok"]


def augment(phrase: str, rng: np.random.Generator) -> str:
  s = phrase
  if rng.random() < 0.3:
    s = s + rng.choice(NOISE_SUFFIXES)
  if rng.random() < 0.15:
    s = s.replace(" ", "  ")
  if rng.random() < 0.1:
    i = rng.integers(0, max(1, len(s)))
    s = s[:i] + s[i].swapcase() if i < len(s) else s
  return s


CANARY = [
  ("hi", "greet"),
  ("hello", "greet"),
  ("how are you", "status"),
  ("how are you?", "status"),
  ("hold", "cmd_hold"),
  ("@ hello team", "social_local"),
  ("rush now", "cmd_rush"),
  ("what did you learn", "learned"),
]


def synthesize(per_intent: int = 900, noise_rows: int = 0) -> tuple[np.ndarray, np.ndarray]:
  xs, ys = [], []
  rng = np.random.default_rng(2026)
  for idx, name in enumerate(INTENTS):
    phrases = PHRASES.get(name, [name])
    for _ in range(per_intent):
      p = augment(rng.choice(phrases), rng)
      xs.append(encode_text(p))
      ys.append(idx)
  for text, name in CANARY:
    for _ in range(200):
      xs.append(encode_text(text))
      ys.append(INTENTS.index(name))
  return np.stack(xs), np.array(ys, dtype=np.int64)


def train_and_export(out_dir: Path, epochs: int = 160) -> dict:
  try:
    import torch
    import torch.nn as nn
  except ImportError as e:
    raise SystemExit("pip install torch") from e

  x, y = synthesize()
  n = len(x)
  perm = np.random.default_rng(3).permutation(n)
  split = int(n * 0.88)
  tr, va = perm[:split], perm[split:]

  class IntentNet(nn.Module):
    def __init__(self, n_cls: int):
      super().__init__()
      self.net = nn.Sequential(
        nn.Linear(FEATURE_DIM, 128),
        nn.ReLU(),
        nn.Linear(128, 128),
        nn.ReLU(),
        nn.Linear(128, n_cls),
      )

    def forward(self, t):
      return self.net(t)

  n_cls = len(INTENTS)
  device = torch.device("cpu")
  model = IntentNet(n_cls).to(device)
  opt = torch.optim.Adam(model.parameters(), lr=8e-4, weight_decay=1e-5)
  loss_fn = nn.CrossEntropyLoss()

  xt = torch.from_numpy(x[tr]).to(device)
  yt = torch.from_numpy(y[tr]).to(device)

  for _ in range(epochs):
    model.train()
    opt.zero_grad()
    loss = loss_fn(model(xt), yt)
    loss.backward()
    opt.step()

  model.eval()
  with torch.no_grad():
    xv = torch.from_numpy(x[va]).to(device)
    yv = torch.from_numpy(y[va]).to(device)
    logits = model(xv)
    val_loss = loss_fn(logits, yv).item()
    pred = logits.argmax(1).cpu().numpy()
    acc = float((pred == yv.cpu().numpy()).mean())

  canary_ok = {}
  model.eval()
  with torch.no_grad():
    for text, name in CANARY:
      feat = torch.from_numpy(encode_text(text).reshape(1, -1)).to(device)
      pred_i = int(model(feat).argmax(1).item())
      canary_ok[text] = {"want": name, "got": INTENTS[pred_i], "ok": INTENTS[pred_i] == name}

  out_dir.mkdir(parents=True, exist_ok=True)
  onnx_path = out_dir / "competition_chat_intent.onnx"
  dummy = torch.zeros(1, FEATURE_DIM)
  torch.onnx.export(
    model,
    dummy,
    str(onnx_path),
    input_names=["text_features"],
    output_names=["intent_logits"],
    dynamic_axes={"text_features": {0: "batch"}, "intent_logits": {0: "batch"}},
    opset_version=13,
  )

  metrics = {
    "model": "competition_chat_intent",
    "intents": INTENTS,
    "featureDim": FEATURE_DIM,
    "trainRows": int(split),
    "valRows": int(n - split),
    "valLoss": round(val_loss, 4),
    "valAccuracy": round(acc, 4),
    "epochs": epochs,
    "note": "Keyword encoder + synthetic paraphrases.",
    "canary": canary_ok,
  }
  (out_dir / "chat_intent_metrics.json").write_text(json.dumps(metrics, indent=2) + "\n", encoding="utf-8")

  manifest = {
    "version": 1,
    "file": "competition_chat_intent.onnx",
    "inputs": {"text_features": [1, FEATURE_DIM]},
    "outputs": {"intent_logits": [1, n_cls]},
    "intents": {str(i): name for i, name in enumerate(INTENTS)},
    "commandIntents": [n for n in INTENTS if n.startswith("cmd_")],
    "encode": "train/text_encoder.py encode_text (keyword slots + stable hash)",
  }
  (out_dir / "chat_intent_manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
  return metrics


if __name__ == "__main__":
  trained = ROOT / "trained"
  m = train_and_export(trained)
  print(json.dumps(m, indent=2))
  print(f"Wrote {trained / 'competition_chat_intent.onnx'}")
