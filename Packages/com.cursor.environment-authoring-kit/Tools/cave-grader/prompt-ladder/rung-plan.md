# Ladder rung: plan (pre-build — after research)

**Do not edit kit C# in this pass.** Written plan only, tied to pre-build readiness stages.

## Checklist

- [ ] Read `CaveBuildPreBuildLadderReport.json` — every stage with `passed: false` or score &lt; 92.
- [ ] Read `CaveBuildPreBuildWorkflowContext.json` checklist.
- [ ] Read `CaveBuildResearch.json` and fetched papers from research phase.
- [ ] Write a **numbered plan table** (minimum 5 rows):

| Step | Pre-build stage id | Research source | Kit file(s) / Unity menu | Expected readiness change |
|------|-------------------|-----------------|--------------------------|---------------------------|

- [ ] End with: "Next Unity phase: **compile_gate** — zero CS errors before readiness ladder fixes."

Output the plan in your assistant response. Unity advances to **compile_gate** automatically.

**Last line (required):** `[CaveCursor:phase-complete] workflow=pre_build rung=plan reason=done`
