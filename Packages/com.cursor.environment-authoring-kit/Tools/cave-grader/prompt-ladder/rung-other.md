## Rung: other (remaining critical stages)

**Goal:** Address the highest-scoring failing stage in compact JSON context — one focused change set.

### Approach

- Read failing stage `id`, `issues`, and `fixes` from context.
- Prefer `CaveBuildQualityStageFixer` patterns: targeted in-place fix, not full regen.
- If multiple stages fail, only fix what matches the listed failing stage ids in context.
