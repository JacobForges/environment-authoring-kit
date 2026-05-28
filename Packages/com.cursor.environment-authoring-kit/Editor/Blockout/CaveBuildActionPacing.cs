using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EnvironmentAuthoringKit.Editor.Blockout
{
    /// <summary>
    /// Editor work queue — one queued action per editor frame when possible.
    /// Timing uses ReadyAt / cooldown timestamps only (never Thread.Sleep on the main thread).
    /// </summary>
    static class CaveBuildActionPacing
    {
        public const float LightDelaySeconds = 0.05f;
        public const float DefaultDelaySeconds = LightDelaySeconds * 3f;
        public const float DelaySeconds = DefaultDelaySeconds;
        public const float HeavyDelaySeconds = LightDelaySeconds * 12f;
        public const float CooldownLightSeconds = LightDelaySeconds;
        public const float CooldownNormalSeconds = LightDelaySeconds * 3f;
        public const float CooldownHeavySeconds = LightDelaySeconds * 8f;
        public const int DefaultBatchSize = 5;
        public const int MaxQueueDepth = 4096;

        public enum ActionWeight
        {
            Light,
            Normal,
            Heavy,
        }

        struct PendingAction
        {
            public Action Action;
            public string Label;
            public double ReadyAt;
            public ActionWeight Weight;
        }

        static readonly Queue<PendingAction> Queue = new();
        static readonly List<PendingAction> ArmedBatch = new(DefaultBatchSize);
        static bool _polling;
        static bool _actionRunning;
        static bool _heavyRunning;
        static bool _runArmedHooked;
        static int _batchRunIndex;
        static double _globalEarliestRunAt;
        static double _lastBatchCompletedAt;
        static double _batchArmedAt;
        static ActionWeight _lastCompletedWeight = ActionWeight.Light;
        static bool _insideQueueInvoke;

        const double QueueStallWatchdogSeconds = 3.0;
        const double BatchRunWatchdogSeconds = 12.0;

        public static bool IsInsideQueueInvoke => _insideQueueInvoke;

        public static int QueuedCount => Queue.Count;

        /// <summary>True while a batch is armed or running (false during the queued callback body).</summary>
        public static bool IsExecuting =>
            !_insideQueueInvoke && (_actionRunning || _heavyRunning || _runArmedHooked);

        public static bool HasQueuedWork => Queue.Count > 0;

        /// <summary>Executing or still has items waiting — use to avoid starting duplicate top-level builds.</summary>
        public static bool IsBusy => IsExecuting || HasQueuedWork;

        public static bool IsHeavyRunning => !_insideQueueInvoke && _heavyRunning;

        /// <summary>Clears queued editor work and polling hooks — use when the editor freezes during a cave build.</summary>
        public static void EmergencyAbortAll()
        {
            Queue.Clear();
            ArmedBatch.Clear();
            _batchRunIndex = 0;
            _actionRunning = false;
            _heavyRunning = false;
            _runArmedHooked = false;
            _batchArmedAt = 0;
            _insideQueueInvoke = false;
            _globalEarliestRunAt = 0;
            _lastBatchCompletedAt = 0;
            _polling = false;
            EditorApplication.update -= Poll;
            EditorApplication.update -= RunArmedBatchOnce;
            CaveBuildTsxProcessRunner.CancelActive("emergency abort");
        }

        public static void Schedule(Action action, string label = null) =>
            Enqueue(action, label, ActionWeight.Normal);

        public static void ScheduleLight(Action action, string label = null) =>
            Enqueue(action, label, ActionWeight.Light);

        public static void ScheduleHeavy(Action action, string label = null) =>
            Enqueue(action, label, ActionWeight.Heavy);

        /// <summary>
        /// Schedule heavy work after the current queue callback returns (avoids "Heavy work busy" re-queue storms).
        /// </summary>
        public static void ScheduleLightChain(Action action, string label = null)
        {
            if (action == null)
                return;

            if (!_insideQueueInvoke && !_heavyRunning && !_actionRunning)
            {
                ScheduleLight(action, label);
                return;
            }

            EditorApplication.delayCall += () => ScheduleLight(action, label);
        }

        /// <summary>Runs on the next editor frame — no queue cooldown (keeps terrain sculpt responsive).</summary>
        public static void ScheduleNextEditorFrame(Action action)
        {
            if (action == null)
                return;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    CaveBuildPipelineDomains.LogCaveWarning(
                        $"Editor frame step failed: {ex.Message}");
                }
                finally
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }
            };
        }

        public static void ScheduleHeavyChain(Action action, string label = null)
        {
            if (action == null)
                return;

            if (!_insideQueueInvoke && !_heavyRunning && !_actionRunning)
            {
                ScheduleHeavy(action, label);
                return;
            }

            EditorApplication.delayCall += () => ScheduleHeavy(action, label);
        }

        /// <summary>
        /// Standard kit entry point: queue work with load-scaled delay before run (non-blocking).
        /// </summary>
        public static void ScheduleBuildStep(
            Action action,
            string label,
            ActionWeight weight = ActionWeight.Heavy)
        {
            if (action == null)
                return;

            if (weight == ActionWeight.Heavy)
                ScheduleHeavy(action, label);
            else if (weight == ActionWeight.Light)
                ScheduleLight(action, label);
            else
                Schedule(action, label);
        }

        static int BatchSize
        {
            get
            {
                if (CaveBuildEditorResponsiveness.IsLongBuildActive)
                    return 1;

                var configured = CaveBuildCursorSettings.ResolveQueuePacing().batchSize;
                return CaveBuildPipelineScope.CaveOnlyContinuation
                    ? 1
                    : configured;
            }
        }

        static float BaseSeconds(ActionWeight weight, bool cooldown)
        {
            var cfg = CaveBuildCursorSettings.ResolveQueuePacing();
            if (cooldown)
            {
                return weight switch
                {
                    ActionWeight.Light => cfg.lightBase * cfg.cooldownLightMult,
                    ActionWeight.Heavy => cfg.lightBase * cfg.cooldownHeavyMult,
                    _ => cfg.lightBase * cfg.cooldownNormalMult,
                };
            }

            return weight switch
            {
                ActionWeight.Light => cfg.lightBase,
                ActionWeight.Heavy => cfg.lightBase * cfg.heavyPreMult,
                _ => cfg.lightBase * cfg.normalPreMult,
            };
        }

        static float ComputeLoadMultiplier(ActionWeight weight)
        {
            var cfg = CaveBuildCursorSettings.ResolveQueuePacing();
            var backlog = Queue.Count + ArmedBatch.Count + (_actionRunning ? 1 : 0) + (_runArmedHooked ? 1 : 0);
            var mult = 1f + Mathf.Min(backlog, 8) * cfg.loadStepPerQueuedItem;

            if (_heavyRunning)
                mult += cfg.loadBoostHeavyActive;
            if (_lastCompletedWeight == ActionWeight.Heavy)
                mult += cfg.loadBoostAfterHeavy * 0.5f;
            if (EditorApplication.isCompiling)
                mult += cfg.loadBoostCompiling;
            if (weight == ActionWeight.Heavy)
                mult += cfg.loadBoostHeavyScheduled;

            return Mathf.Clamp(mult, 1f, cfg.maxLoadMultiplier);
        }

        public static float GetTimerSeconds(ActionWeight weight, bool cooldown = false) =>
            (cooldown ? BaseSeconds(weight, true) : BaseSeconds(weight, false)) * ComputeLoadMultiplier(weight);

        static float ResolvePreDelay(ActionWeight weight) => GetTimerSeconds(weight, cooldown: false);

        static float ResolveCooldown(ActionWeight weight) => GetTimerSeconds(weight, cooldown: true);

        /// <summary>
        /// Non-blocking yield — schedules cooldown for the next queue item and repaints the editor.
        /// Never blocks with Thread.Sleep (that freezes the Unity UI).
        /// </summary>
        public static void SleepTimers(ActionWeight weight, string label = null)
        {
            YieldToEditor(weight, label);
        }

        public static void ApplyCooldownTimers(ActionWeight weight)
        {
            var cooldown = ResolveCooldown(weight);
            _globalEarliestRunAt = Math.Max(
                _globalEarliestRunAt,
                EditorApplication.timeSinceStartup + cooldown);
            _lastCompletedWeight = weight;
        }

        /// <summary>Minimum wait before the next queued action runs (non-blocking).</summary>
        public static void PostponeNextRun(double seconds)
        {
            if (seconds <= 0)
                return;
            _globalEarliestRunAt = Math.Max(
                _globalEarliestRunAt,
                EditorApplication.timeSinceStartup + seconds);
        }

        /// <summary>Clears backlog cooldown so the next pipeline step can run on the next editor tick.</summary>
        public static void PreparePipelineChainKickoff()
        {
            _globalEarliestRunAt = EditorApplication.timeSinceStartup;
            _lastCompletedWeight = ActionWeight.Light;
        }

        /// <summary>First step after starting a queued pipeline — no pre-cooldown, minimal delay.</summary>
        public static void SchedulePipelineFirstStep(
            Action action,
            string label,
            ActionWeight weight = ActionWeight.Light)
        {
            if (action == null)
                return;
            PreparePipelineChainKickoff();
            Enqueue(action, label, weight, immediate: true);
        }

        /// <summary>Runs before other queued work (cave validate after long surface terrain queue).</summary>
        public static void SchedulePriorityFirstStep(
            Action action,
            string label,
            ActionWeight weight = ActionWeight.Light)
        {
            if (action == null)
                return;

            PreparePipelineChainKickoff();
            var readyAt = EditorApplication.timeSinceStartup;
            var item = new PendingAction
            {
                Action = action,
                Label = label,
                ReadyAt = readyAt,
                Weight = weight,
            };

            var rest = new List<PendingAction>(Queue.Count);
            while (Queue.Count > 0)
                rest.Add(Queue.Dequeue());

            Queue.Enqueue(item);
            foreach (var pending in rest)
                Queue.Enqueue(pending);

            CaveBuildEditorLog.LogQueueStep(label, $"priority jump (depth was {rest.Count + 1})");
            EnsurePolling();
        }

        /// <summary>Cooldown for follow-up work without blocking the current frame.</summary>
        public static void SleepAfterStep(ActionWeight weight, string label = null)
        {
            ApplyCooldownTimers(weight);
            if (!string.IsNullOrEmpty(label))
            {
                CaveBuildEditorLog.LogQueueStep(label, $"cooldown {ResolveCooldown(weight):F2}s ({weight})");
            }
        }

        /// <summary>Lets Unity process pending UI / import work between heavy substeps.</summary>
        public static void YieldToEditor(ActionWeight weight = ActionWeight.Light, string label = null)
        {
            ApplyCooldownTimers(weight);
            EditorApplication.QueuePlayerLoopUpdate();
            if (!string.IsNullOrEmpty(label))
            {
                CaveBuildEditorLog.LogQueueStep(label, $"yield ({weight})");
            }
        }

        static ActionWeight Heavier(ActionWeight a, ActionWeight b)
        {
            if (a == ActionWeight.Heavy || b == ActionWeight.Heavy)
                return ActionWeight.Heavy;
            if (a == ActionWeight.Normal || b == ActionWeight.Normal)
                return ActionWeight.Normal;
            return ActionWeight.Light;
        }

        static void Enqueue(Action action, string label, ActionWeight weight, bool immediate = false)
        {
            if (action == null)
                return;

            if (Queue.Count >= MaxQueueDepth)
            {
                CaveBuildPipelineDomains.LogCaveWarning(
                    $"Editor queue full ({MaxQueueDepth}) — dropped '{label ?? "action"}'. " +
                    "Wait for current cave build to finish.");
                return;
            }

            if (weight == ActionWeight.Heavy && (_heavyRunning || _actionRunning) && !_insideQueueInvoke)
            {
                var wait = ResolvePreDelay(ActionWeight.Heavy);
                CaveBuildEditorLog.LogCaveWarning(
                    $"Heavy work busy — re-queued '{label ?? "heavy action"}' (~{wait:F2}s).");
            }

            var preDelay = immediate ? 0f : ResolvePreDelay(weight);
            var readyAt = EditorApplication.timeSinceStartup + preDelay;
            readyAt = immediate ? EditorApplication.timeSinceStartup : Math.Max(readyAt, _globalEarliestRunAt);

            var wasEmpty = Queue.Count == 0;
            Queue.Enqueue(new PendingAction
            {
                Action = action,
                Label = label,
                ReadyAt = readyAt,
                Weight = weight,
            });

            if (wasEmpty)
                _lastBatchCompletedAt = EditorApplication.timeSinceStartup;

            if (Queue.Count > 1)
            {
                CaveBuildEditorLog.LogQueueStep(
                    label ?? "action",
                    $"queued ({weight}, depth={Queue.Count}, load×{ComputeLoadMultiplier(weight):F2})");
            }

            EnsurePolling();
        }

        static void EnsurePolling()
        {
            if (_polling)
                return;

            _polling = true;
            EditorApplication.update += Poll;
        }

        /// <summary>
        /// First item must be ready (timers). Heavy always runs alone; light/normal may batch up to batchSize.
        /// </summary>
        static bool TryCollectBatch(double now, List<PendingAction> batch)
        {
            batch.Clear();
            if (Queue.Count == 0)
                return false;

            if (now < _globalEarliestRunAt || now < Queue.Peek().ReadyAt)
                return false;

            batch.Add(Queue.Dequeue());
            if (batch[0].Weight == ActionWeight.Heavy)
                return true;

            var limit = BatchSize;
            while (batch.Count < limit && Queue.Count > 0)
            {
                if (Queue.Peek().Weight == ActionWeight.Heavy)
                    break;
                if (now < Queue.Peek().ReadyAt)
                    break;

                batch.Add(Queue.Dequeue());
            }

            return batch.Count > 0;
        }

        static void Poll()
        {
            if (_runArmedHooked)
            {
                var armedFor = EditorApplication.timeSinceStartup - _batchArmedAt;
                if (_batchArmedAt > 0 && armedFor > BatchRunWatchdogSeconds)
                {
                    CaveBuildPipelineDomains.LogCaveWarning(
                        $"Editor queue batch stuck {armedFor:F1}s — forcing finish (validate uses delayCall; check Console for exceptions).");
                    FinishBatchRun(ActionWeight.Light);
                }

                return;
            }

            if (Queue.Count == 0 && ArmedBatch.Count == 0)
            {
                StopPolling();
                return;
            }

            if (_actionRunning)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (Queue.Count > 0 && !IsExecuting)
            {
                var stalledFor = now - _lastBatchCompletedAt;
                if (_lastBatchCompletedAt > 0 && stalledFor > QueueStallWatchdogSeconds)
                {
                    var headLabel = Queue.Peek().Label ?? "action";
                    PreparePipelineChainKickoff();
                    var buffer = new List<PendingAction>(Queue.Count);
                    while (Queue.Count > 0)
                        buffer.Add(Queue.Dequeue());
                    var head = buffer[0];
                    head.ReadyAt = now;
                    buffer[0] = head;
                    foreach (var pending in buffer)
                        Queue.Enqueue(pending);

                    CaveBuildPipelineDomains.LogCaveWarning(
                        $"Editor queue stalled {stalledFor:F1}s — forced ready on '{headLabel}' " +
                        $"(depth={buffer.Count}). Validate running now if this was a validate step.");
                    _lastBatchCompletedAt = now;
                }
            }

            if (!TryCollectBatch(now, ArmedBatch))
                return;

            _actionRunning = true;
            _batchRunIndex = 0;
            _runArmedHooked = true;
            _batchArmedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += RunArmedBatchOnce;
        }

        static void RunArmedBatchOnce()
        {
            if (ArmedBatch.Count == 0)
            {
                FinishBatchRun(ActionWeight.Light);
                return;
            }

            if (_batchRunIndex >= ArmedBatch.Count)
            {
                var batchWeight = ActionWeight.Light;
                for (var i = 0; i < ArmedBatch.Count; i++)
                    batchWeight = Heavier(batchWeight, ArmedBatch[i].Weight);
                FinishBatchRun(batchWeight);
                return;
            }

            var item = ArmedBatch[_batchRunIndex];
            _batchRunIndex++;

            if (_batchRunIndex > 1)
                ApplyCooldownTimers(ActionWeight.Light);

            if (!string.IsNullOrEmpty(item.Label))
            {
                CaveBuildEditorLog.LogQueueStep(
                    item.Label,
                    $"run [{_batchRunIndex}/{ArmedBatch.Count}] load×{ComputeLoadMultiplier(item.Weight):F2} {item.Weight}");
            }

            _heavyRunning = item.Weight == ActionWeight.Heavy;
            _insideQueueInvoke = true;
            try
            {
                item.Action?.Invoke();
            }
            catch (Exception ex)
            {
                CaveBuildPipelineDomains.LogCaveWarning(
                    $"Queue action failed ({_batchRunIndex}/{ArmedBatch.Count}): {ex.Message}");
            }
            finally
            {
                _insideQueueInvoke = false;
                _heavyRunning = false;
            }

            if (_batchRunIndex < ArmedBatch.Count)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }

            CaveBuildProgressUI.ClearIfShown();

            var completedWeight = ActionWeight.Light;
            for (var i = 0; i < ArmedBatch.Count; i++)
                completedWeight = Heavier(completedWeight, ArmedBatch[i].Weight);
            FinishBatchRun(completedWeight);
        }

        static void FinishBatchRun(ActionWeight batchWeight)
        {
            EditorApplication.update -= RunArmedBatchOnce;
            _runArmedHooked = false;
            _batchArmedAt = 0;
            ArmedBatch.Clear();
            _batchRunIndex = 0;
            _actionRunning = false;
            _heavyRunning = false;
            _lastCompletedWeight = batchWeight;
            _lastBatchCompletedAt = EditorApplication.timeSinceStartup;

            _globalEarliestRunAt = EditorApplication.timeSinceStartup + ResolveCooldown(batchWeight);

            CaveBuildDeferredAssetRefresh.Flush();
            CaveBuildEditorResponsiveness.OnQueueStepCompleted();
            CaveBuildLiveSceneFlushUtility.FlushWorldView();
            EnsurePolling();
        }

        static void StopPolling()
        {
            if (IsExecuting || HasQueuedWork)
                return;

            _polling = false;
            EditorApplication.update -= Poll;
        }
    }
}
