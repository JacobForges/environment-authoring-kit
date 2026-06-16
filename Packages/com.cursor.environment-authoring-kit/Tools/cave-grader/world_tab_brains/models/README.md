# Wizard tab brain models (Python only)

Files here use the **`.tabbrain`** extension (not `.onnx`) so Unity does **not** run them through `ONNXModelImporter`.

These are **sklearn TF-IDF + logistic regression** models exported with **string tensor** inputs via skl2onnx. They are consumed by the build wizard server through **onnxruntime** (`world_tab_brains/rule_validators.py`), not Unity Sentis.

Recompile:

```bash
cd Packages/com.cursor.environment-authoring-kit/Tools/cave-grader
python3 world_tab_brains/compile_tab_brains_onnx.py
```

## License / Copyright

- Tooling is covered by `../../LICENSE_TOOL.md` (JacobForges non-commercial tooling terms).
- Copyright (c) JacobForges.
- Game/project code and content remain proprietary and protected.
