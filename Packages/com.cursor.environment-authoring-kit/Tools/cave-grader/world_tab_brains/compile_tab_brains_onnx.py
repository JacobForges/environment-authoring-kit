#!/usr/bin/env python3
"""Train and export one ONNX classifier per wizard tab.

Input:
  world_tab_brains/qa_datasets_out/qa_cases_<tab>.jsonl

Output:
  world_tab_brains/models/<tab>_brain.tabbrain
  world_tab_brains/models/<tab>_brain.manifest.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any, Iterable

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline
from sklearn.model_selection import train_test_split
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import StringTensorType

from world_tab_brains.model_paths import BRAIN_MODEL_EXT, brain_model_filename


def _read_jsonl(path: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line:
            continue
        rows.append(json.loads(line))
    return rows


def _text_features(rec: dict[str, Any]) -> str:
    return " | ".join(
        [
            f"tab:{rec.get('tabId', '')}",
            f"checklist:{rec.get('checklistId', '')}",
            f"q:{rec.get('assistantQuestion', '')}",
            f"a:{rec.get('decision', '')}",
        ]
    )


def _train_pipeline(X: Iterable[str], y: Iterable[int]) -> Pipeline:
    return Pipeline(
        steps=[
            (
                "vec",
                TfidfVectorizer(
                    max_features=2**16,
                    ngram_range=(1, 2),
                    norm="l2",
                    lowercase=True,
                ),
            ),
            (
                "clf",
                LogisticRegression(
                    max_iter=1200,
                    class_weight="balanced",
                    solver="liblinear",
                    random_state=42,
                ),
            ),
        ]
    ).fit(list(X), list(y))


def _score(model: Pipeline, X: list[str], y: list[int]) -> float:
    if not X:
        return 0.0
    return float(model.score(X, y))


def compile_tab(tab_id: str, src: Path, out_dir: Path) -> dict[str, Any]:
    rows = _read_jsonl(src)
    if not rows:
        raise RuntimeError(f"No rows in {src}")
    X = [_text_features(r) for r in rows]
    y = [1 if bool(r.get("expectedPass")) else 0 for r in rows]

    # Stratified shuffled split for a fairer held-out score.
    # (Index-based splitting can bias results when JSONL is grouped by item/variant.)
    if len(set(y)) < 2 or len(X) < 4:
        split = max(1, min(int(len(X) * 0.85), len(X) - 1))
        X_train, X_test = X[:split], X[split:]
        y_train, y_test = y[:split], y[split:]
    else:
        X_train, X_test, y_train, y_test = train_test_split(
            X,
            y,
            test_size=0.15,
            random_state=42,
            stratify=y,
            shuffle=True,
        )

    model = _train_pipeline(X_train, y_train)
    train_acc = _score(model, X_train, y_train)
    test_acc = _score(model, X_test, y_test)

    onnx_model = convert_sklearn(
        model,
        initial_types=[("input_text", StringTensorType([None, 1]))],
        target_opset=17,
    )
    out_dir.mkdir(parents=True, exist_ok=True)
    model_path = out_dir / brain_model_filename(tab_id)
    model_path.write_bytes(onnx_model.SerializeToString())

    manifest = {
        "tabId": tab_id,
        "modelPath": str(model_path),
        "sourceDataset": str(src),
        "inputSchema": {
            "input_text": "string tensor [N,1] with tab/checklist/question/decision concatenated text"
        },
        "labels": {"0": "reopen", "1": "pass"},
        "metrics": {
            "records": len(rows),
            "trainRecords": len(X_train),
            "testRecords": len(X_test),
            "trainAccuracy": train_acc,
            "testAccuracy": test_acc,
        },
        "version": 1,
    }
    manifest_path = out_dir / f"{tab_id}_brain.manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--datasets-dir",
        default="world_tab_brains/qa_datasets_out",
        help="Directory containing qa_cases_<tab>.jsonl files (relative to cave-grader).",
    )
    ap.add_argument(
        "--out-dir",
        default="world_tab_brains/models",
        help="Output directory for ONNX + manifest files (relative to cave-grader).",
    )
    args = ap.parse_args()

    cave_grader = Path(__file__).resolve().parent.parent
    datasets = cave_grader / args.datasets_dir
    out_dir = cave_grader / args.out_dir

    tabs = [
        "terrain",
        "surface-content",
        "caves",
        "mazes",
        "interior-content",
        "atmosphere",
        "music",
        "video",
    ]
    summary: dict[str, Any] = {"compiledTabs": []}
    for tab_id in tabs:
        src = datasets / f"qa_cases_{tab_id}.jsonl"
        if not src.is_file():
            raise FileNotFoundError(f"Missing dataset for {tab_id}: {src}")
        manifest = compile_tab(tab_id, src, out_dir)
        summary["compiledTabs"].append(
            {
                "tabId": tab_id,
                "modelPath": manifest["modelPath"],
                "testAccuracy": manifest["metrics"]["testAccuracy"],
            }
        )
        print(f"Compiled {tab_id}: test_acc={manifest['metrics']['testAccuracy']:.3f}")

    (out_dir / "compile_summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote summary: {out_dir / 'compile_summary.json'}")


if __name__ == "__main__":
    main()

