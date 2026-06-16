# DTA Training Companion — Scientific & Design Foundations
Citations and research foundations applied inside the training dashboard for Deep Train Academy.

---

## 1. Model lineage & experiment versioning
- **Concept**: Experiment versioning, schema lineage tracking, and comparison of run statistics across training checkpoints.
- **Application**: Implemented in **Brain Lab** as the Model Lineage Graph Node Visualizer and comparison panel of Checkpoint A vs Checkpoint B side-by-side.

## 2. Interactive explainability & quality gates
- **Concept**: Interactive explainability views, monitoring charts, and tabular dataset validation filters beside active models.
- **Application**: Implemented in **Training Studio** with the Quality Gate Funnel and Approved/Held-Back sample verification grid, allowing users to verify model inputs prior to behavior cloning execution.

## 3. "System Model & User Model" (arXiv:2305.02469)
- **Concept**: Parallel rendering of the systemic statistical model (weights, hashes, ledger metrics) alongside the user perspective and qualitative intent ("train battle mode").
- **Application**: Implemented on **Home Lab** with dual viewports:
  - *System Model*: Displaying active ONNX adapter versions, ledger marks, and parameter bounds.
  - *User Model*: Reflecting player cohort details, user-defined goals, and qualitative feedback loops.

## 4. Disclosure dashboards for AI coaching
- **Concept**: Clear disclosure showing what physical or statistical states the AI infers about the user, separating objective facts from subjective coaching strategies.
- **Application**: Implemented in **Copilot Rail** as the Disclosure Mode toggle, splitting advice cards into **[OBJECTIVE EPISODE FACTS]** vs **[STRATEGIC COACHING PRINCIPLES]**.

## 5. AI dashboard trust principles
- **Concept**: 
  - *Confidence Visibility*: Expressing model advice as a percentage or scale with underlying reasoning.
  - *Decision Transparency*: Displaying raw logs and the observation sequence.
  - *Data Provenance*: Pinpointing the origin records (e.g. `episode_12a.jsonl`) that informed the state.
  - *Override Accessibility*: Simple button pathways to dismiss or veto recommendations.
  - *Failure State Dignity*: Graceful, constructive error state handling during low data density.
- **Application**: Utilized on every interactive element and advice card across the entire companion.
