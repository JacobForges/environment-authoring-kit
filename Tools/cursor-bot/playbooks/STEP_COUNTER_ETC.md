# STEP_COUNTER_ETC

1. Hub shows current > planned (e.g. 3355/1941) or ETC ~00:00 incorrectly.
2. Call `CaveBuildStepCounter.SyncLiveTotals()` from scheduled queue depth.
3. `CaveBuildPlannedStepBudget.ConfigureForRequest` from active planner session flags.
4. Reject stale paced checkpoints that inflate current step count.

Tag: `// [bot:rung:step_counter:DATE]`
