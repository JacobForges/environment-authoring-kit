#!/usr/bin/env python3
"""
Standalone ONNX tester for competition models (no Unity).

  cd /Users/jacob/Hub
  .venv-competition-onnx/bin/python Tools/competition-models/test_platform.py
  .venv-competition-onnx/bin/python Tools/competition-models/test_platform.py --all
  .venv-competition-onnx/bin/python Tools/competition-models/test_platform.py --chat "hold position"
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
TRAIN = ROOT / "train"
TRAINED = ROOT / "trained"
sys.path.insert(0, str(TRAIN))

from text_encoder import encode_text  # noqa: E402

INTENTS = [
    "cmd_hold", "cmd_follow", "cmd_rush", "cmd_attack", "cmd_defend", "cmd_skill", "cmd_cancel",
    "greet", "status", "strategy", "encourage", "taunt", "learned", "hurt", "victory",
    "stuck", "social_local", "unknown",
]

ARCHETYPES = ("sprinter", "steady", "cautious", "explorer", "jumper", "safe", "bold", "balanced")

ACTION_V2 = [
    "move_x", "move_z", "turn", "jump", "attack", "defend",
    "skill1", "skill2", "skill3", "target_cycle", "bark_trigger", "bark_category",
]

ACTION_V1 = ["move_x", "move_z", "turn", "jump_logit"]


def encode_setup_demo() -> np.ndarray:
    """Demo chip picks: Medium, Tall, Lush, Gold, Ranger, Steady, Normal, Jump, Balanced, Safe, Checkpoints, Same."""
    f = np.zeros(100, dtype=np.float32)
    f[1] = 1.0      # build medium
    f[3 + 2] = 1.0  # tall
    f[6 + 1] = 1.0  # lush
    f[9 + 1] = 1.0  # gold
    f[13 + 1] = 1.0 # ranger
    f[17 + 1] = 1.0 # steady race
    f[20 + 1] = 1.0 # normal jump
    f[23 + 1] = 1.0 # jump when stuck
    f[26 + 1] = 1.0 # balanced risk
    f[29 + 1] = 1.0 # checkpoints focus
    f[32 + 2] = 1.0 # same pace after fall
    return f.reshape(1, 100)


def load_session(name: str, path: Path):
    import onnxruntime as ort

    if not path.is_file():
        raise FileNotFoundError(f"Missing {path}\nRun trainers in Tools/competition-models/train/")
    return ort.InferenceSession(str(path), providers=["CPUExecutionProvider"]), name


def softmax(x: np.ndarray) -> np.ndarray:
    x = x - np.max(x)
    e = np.exp(x)
    return e / (e.sum() + 1e-9)


def test_setup(sess) -> None:
    print("\n=== competition_setup.onnx ===")
    feat = encode_setup_demo()
    av, z, arch = sess.run(["avatar", "z", "archetype"], {"features": feat})
    av, z, arch = av[0], z[0], arch[0]
    probs = softmax(arch)
    best = int(probs.argmax())
    print("avatar[12]:", np.round(av, 3).tolist())
    print("z[24]:     ", np.round(z, 3).tolist())
    print("archetype: ", ARCHETYPES[best], f"({probs[best]*100:.1f}%)")


def test_chat(sess, text: str) -> None:
    print(f"\n=== competition_chat_intent.onnx ===\ninput: {text!r}")
    feat = encode_text(text).reshape(1, 64)
    logits = sess.run(["intent_logits"], {"text_features": feat})[0][0]
    probs = softmax(logits)
    order = np.argsort(probs)[::-1][:5]
    for i in order:
        tag = " [COMMAND]" if INTENTS[i].startswith("cmd_") else ""
        print(f"  {INTENTS[i]:16} {probs[i]*100:5.1f}%{tag}")


def _sigmoid(v: float) -> float:
    return float(1.0 / (1.0 + np.exp(-v)))


def _apply_activations(actions: np.ndarray, layout: list[str]) -> dict:
    logits = {
        "jump_logit", "jump", "attack", "defend", "skill1", "skill2", "skill3",
        "target_cycle", "bark_trigger",
    }
    out = {}
    for i, name in enumerate(layout):
        v = float(actions[i])
        if name in logits or name.endswith("_logit"):
            out[name] = round(_sigmoid(v), 3)
        elif name == "bark_category":
            out[name] = round(float(np.clip(v, -1, 1)), 3)
        else:
            out[name] = round(float(np.tanh(v)), 3)
    return out


def test_gameplay_v2(sess, seed: int = 0) -> None:
    print("\n=== competition_gameplay.onnx (v2) ===")
    rng = np.random.default_rng(seed)
    state = rng.uniform(-0.5, 0.5, 96).astype(np.float32)
    state[52:76] = np.tanh(rng.normal(0, 0.5, 24))
    state[90] = 1.0
    actions = sess.run(["actions"], {"state": state.reshape(1, 96)})[0][0]
    print("state sample (locomotion):", np.round(state[0:8], 3).tolist())
    print("state sample (z):", np.round(state[52:58], 3).tolist(), "…")
    applied = _apply_activations(actions, ACTION_V2)
    for k, v in applied.items():
        print(f"  {k:16} raw={actions[ACTION_V2.index(k)]:+.3f}  → {v}")


def test_policy_v1(sess, seed: int = 1) -> None:
    print("\n=== competition_policy.onnx (v1 legacy) ===")
    rng = np.random.default_rng(seed)
    state = rng.uniform(-1, 1, 42).astype(np.float32)
    actions = sess.run(["actions"], {"state": state.reshape(1, 42)})[0][0]
    applied = _apply_activations(actions, ACTION_V1)
    for k, v in applied.items():
        print(f"  {k:16} {v}")


def run_validate() -> bool:
    print("\n=== validate (onnxruntime + onnx checker) ===")
    r = ROOT / "validate_models.py"
    import subprocess

    proc = subprocess.run([sys.executable, str(r)], capture_output=True, text=True)
    print(proc.stdout or proc.stderr)
    return proc.returncode == 0


def interactive_loop(sessions: dict) -> None:
    print("\nInteractive mode — commands:")
    print("  setup | gameplay | policy | chat <text> | validate | quit")
    while True:
        try:
            line = input("\ncomp-test> ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            break
        if not line:
            continue
        if line in ("q", "quit", "exit"):
            break
        if line == "setup" and "setup" in sessions:
            test_setup(sessions["setup"])
        elif line == "gameplay" and "gameplay" in sessions:
            test_gameplay_v2(sessions["gameplay"])
        elif line == "policy" and "policy" in sessions:
            test_policy_v1(sessions["policy"])
        elif line.startswith("chat "):
            if "chat" not in sessions:
                print("chat model not loaded")
                continue
            test_chat(sessions["chat"], line[5:].strip())
        elif line == "chat":
            if "chat" not in sessions:
                print("chat model not loaded")
                continue
            t = input("text> ").strip()
            test_chat(sessions["chat"], t)
        elif line == "validate":
            run_validate()
        elif line == "all":
            run_all(sessions)
        else:
            print("unknown — try setup, gameplay, chat hold, validate, quit")


def run_all(sessions: dict) -> None:
    if "setup" in sessions:
        test_setup(sessions["setup"])
    if "chat" in sessions:
        for phrase in ("hello", "hold", "how is your hp", "rush now", "what did you learn"):
            test_chat(sessions["chat"], phrase)
    if "gameplay" in sessions:
        test_gameplay_v2(sessions["gameplay"])
    if "policy" in sessions:
        test_policy_v1(sessions["policy"])


def main() -> int:
    ap = argparse.ArgumentParser(description="Test competition ONNX models")
    ap.add_argument("--all", action="store_true", help="Run all model smoke tests")
    ap.add_argument("--validate", action="store_true", help="Run validate_models.py only")
    ap.add_argument("--chat", type=str, default="", help="Classify one chat line")
    ap.add_argument("--interactive", "-i", action="store_true", help="REPL (default if no flags)")
    args = ap.parse_args()

    try:
        import onnxruntime  # noqa: F401
    except ImportError:
        print("Install: .venv-competition-onnx/bin/pip install onnxruntime onnx numpy", file=sys.stderr)
        return 1

    if args.validate:
        return 0 if run_validate() else 1

    sessions = {}
    mapping = {
        "setup": TRAINED / "competition_setup.onnx",
        "gameplay": TRAINED / "competition_gameplay.onnx",
        "policy": TRAINED / "competition_policy.onnx",
        "chat": TRAINED / "competition_chat_intent.onnx",
    }
    for key, path in mapping.items():
        if path.is_file():
            sessions[key], _ = load_session(key, path)

    if not sessions:
        print(f"No models in {TRAINED}", file=sys.stderr)
        return 1

    print("Loaded:", ", ".join(sessions.keys()))
    manifest = TRAINED / "validate_report.json"
    if manifest.is_file():
        print("Last validate:", manifest.read_text(encoding="utf-8")[:200], "…")

    if args.chat:
        if "chat" not in sessions:
            print("chat model missing", file=sys.stderr)
            return 1
        test_chat(sessions["chat"], args.chat)
        return 0

    if args.all:
        run_all(sessions)
        return 0

    if len(sys.argv) == 1 or args.interactive:
        run_all(sessions)
        interactive_loop(sessions)
        return 0

    ap.print_help()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
